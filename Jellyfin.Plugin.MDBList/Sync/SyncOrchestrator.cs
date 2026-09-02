using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Library;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Single-flight coordination plus the periodic full-run and cheap
/// activity-gated pull -- port of sync_orchestrator.py.
///
/// Kodi needed a *file* lock because "Sync now" ran in a separate OS
/// process (RunScript) from the service, so a plain in-process lock there
/// couldn't see it. Jellyfin plugins are one process/one DI container, so
/// this collapses to a single <see cref="SemaphoreSlim"/> -- a live push and
/// a scheduled pull both go through <see cref="TryLock"/> so neither can
/// race the other's writes to the same item.
///
/// Watched, ratings, and collection sync are all wired in. Collection is
/// push-only -- see <see cref="CollectionSync"/> -- so it has no place in
/// CheckActivityAsync's pull-gating logic below, only in the full run.
/// </summary>
public sealed class SyncOrchestrator : IDisposable
{
    private static readonly string[] WatchedActivityKeys = ["watched_at", "season_watched_at", "episode_watched_at"];
    private static readonly string[] RatingActivityKeys = ["rated_at"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly OAuthService _oauthService;
    private readonly MDBListApiClient _apiClient;
    private readonly SyncStateStore _stateStore;
    private readonly WatchedSync _watchedSync;
    private readonly RatingsSync _ratingsSync;
    private readonly CollectionSync _collectionSync;
    private readonly ILogger<SyncOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncOrchestrator"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="watchedSync">Instance of the <see cref="WatchedSync"/>.</param>
    /// <param name="ratingsSync">Instance of the <see cref="RatingsSync"/>.</param>
    /// <param name="collectionSync">Instance of the <see cref="CollectionSync"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{SyncOrchestrator}"/> interface.</param>
    public SyncOrchestrator(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        OAuthService oauthService,
        MDBListApiClient apiClient,
        SyncStateStore stateStore,
        WatchedSync watchedSync,
        RatingsSync ratingsSync,
        CollectionSync collectionSync,
        ILogger<SyncOrchestrator> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _oauthService = oauthService;
        _apiClient = apiClient;
        _stateStore = stateStore;
        _watchedSync = watchedSync;
        _ratingsSync = ratingsSync;
        _collectionSync = collectionSync;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire the sync lock without blocking.
    /// </summary>
    /// <returns>
    /// A disposable that releases the lock when acquired; null if a sync is
    /// already in progress. Dispose the non-null result when done -- it
    /// holds the lock for the caller's whole operation, not just the check.
    /// </returns>
    public IDisposable? TryLock()
    {
        return _gate.Wait(0) ? new Releaser(_gate) : null;
    }

    /// <summary>
    /// Full run: rebuilds the library snapshot unconditionally and does
    /// push-then-pull for every enabled category. The expensive path --
    /// covers pushing anything the live listener missed (e.g. while
    /// Jellyfin was down) and acts as a periodic full reconciliation. Safe
    /// to call from multiple trigger points; overlapping calls are skipped
    /// rather than queued.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if a run actually executed.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        using var handle = TryLock();
        if (handle is null)
        {
            _logger.LogDebug("MDBList Sync: run already in progress, skipping");
            return false;
        }

        var user = ResolveLinkedUser();
        if (user is null)
        {
            return false;
        }

        var accessToken = await _oauthService.EnsureValidTokenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        try
        {
            // Snapshot build and server_time fetch live inside this try: a
            // failed query must abort the whole run rather than let
            // diff-based reconciliation treat an incomplete snapshot as "the
            // library is empty" and push bulk removals.
            var snapshot = LibrarySnapshot.Build(_libraryManager, _userDataManager, user);
            var activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);

            var watchedPush = await _watchedSync.PushAsync(user.Id, accessToken, snapshot, cancellationToken).ConfigureAwait(false);
            var watchedPull = await _watchedSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                .ConfigureAwait(false);
            var ratingsPush = await _ratingsSync.PushAsync(user.Id, accessToken, snapshot, cancellationToken).ConfigureAwait(false);
            var ratingsPull = await _ratingsSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                .ConfigureAwait(false);
            var collectionPush = await _collectionSync.PushAsync(user.Id, accessToken, snapshot, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "MDBList Sync: run complete - watched push +{WAdd}/-{WRemove} pull {WApplied} ({WMode}), "
                    + "ratings push +{RAdd}/-{RRemove} pull {RApplied} ({RMode}), collection push +{CAdd}/-{CRemove}",
                watchedPush.PushedAdd,
                watchedPush.PushedRemove,
                watchedPull.PulledApplied,
                watchedPull.Mode,
                ratingsPush.PushedAdd,
                ratingsPush.PushedRemove,
                ratingsPull.PulledApplied,
                ratingsPull.Mode,
                collectionPush.PushedAdd,
                collectionPush.PushedRemove);

            return true;
        }
        catch (MDBListApiException ex)
        {
            _logger.LogError(ex, "MDBList Sync: run failed");
            return false;
        }
    }

    /// <summary>
    /// Cheap poll: checks /sync/last_activities (a single lightweight GET)
    /// and only pays for a library snapshot rebuild + pull when a relevant
    /// bucket actually advanced since the last check.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if a pull actually ran.</returns>
    public async Task<bool> CheckActivityAsync(CancellationToken cancellationToken)
    {
        using var handle = TryLock();
        if (handle is null)
        {
            _logger.LogDebug("MDBList Sync: activity check skipped, a run is already in progress");
            return false;
        }

        var user = ResolveLinkedUser();
        if (user is null)
        {
            return false;
        }

        var accessToken = await _oauthService.EnsureValidTokenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        LastActivities activities;
        try
        {
            activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (MDBListApiException ex)
        {
            _logger.LogDebug(ex, "MDBList Sync: activity check failed");
            return false;
        }

        var seen = await _stateStore.GetLastActivitiesSeenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var current = ToDictionary(activities);

        // journal_at covers removals (it doesn't say which category) --
        // confirmed against api.mdblist's removal endpoints, which only
        // clear per-item state and separately bump journal_at. Without
        // checking it too, an unwatch/unrate never trips this gate.
        var journalAdvanced = AnyBucketAdvanced(seen, current, "journal_at");
        var watchedChanged = journalAdvanced || AnyBucketAdvanced(seen, current, WatchedActivityKeys);
        var ratingsChanged = journalAdvanced || AnyBucketAdvanced(seen, current, RatingActivityKeys);

        if (!watchedChanged && !ratingsChanged)
        {
            // Advance the watermark only when there's nothing to follow up
            // on. If a pull below fails, the watermark must stay put so
            // this gets retried on the next check instead of silently
            // marked "seen" -- see the matching comment further down.
            await _stateStore.SetLastActivitiesSeenAsync(user.Id, current, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("MDBList Sync: activity check found nothing new");
            return false;
        }

        var snapshot = LibrarySnapshot.Build(_libraryManager, _userDataManager, user);

        try
        {
            if (watchedChanged)
            {
                var watchedPull = await _watchedSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "MDBList Sync: activity-triggered watched pull applied {Applied} ({Mode})",
                    watchedPull.PulledApplied,
                    watchedPull.Mode);
            }

            if (ratingsChanged)
            {
                var ratingsPull = await _ratingsSync.PullAsync(user.Id, accessToken, user, snapshot, activities.ServerTime, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "MDBList Sync: activity-triggered ratings pull applied {Applied} ({Mode})",
                    ratingsPull.PulledApplied,
                    ratingsPull.Mode);
            }
        }
        catch (MDBListApiException ex)
        {
            _logger.LogError(ex, "MDBList Sync: activity-triggered pull failed");
            return false;
        }

        // Only reached on success, so a failed pull above leaves the
        // watermark where it was.
        await _stateStore.SetLastActivitiesSeenAsync(user.Id, current, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    private User? ResolveLinkedUser()
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault();
        if (linkedUserConfig is null)
        {
            return null;
        }

        return _userManager.GetUserById(linkedUserConfig.JellyfinUserId);
    }

    private static bool AnyBucketAdvanced(Dictionary<string, string> seen, Dictionary<string, string> current, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (current.TryGetValue(key, out var currentValue)
                && !string.IsNullOrEmpty(currentValue)
                && (!seen.TryGetValue(key, out var seenValue) || seenValue != currentValue))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string> ToDictionary(LastActivities activities)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(dict, "watchlisted_at", activities.WatchlistedAt);
        AddIfPresent(dict, "watched_at", activities.WatchedAt);
        AddIfPresent(dict, "season_watched_at", activities.SeasonWatchedAt);
        AddIfPresent(dict, "episode_watched_at", activities.EpisodeWatchedAt);
        AddIfPresent(dict, "rated_at", activities.RatedAt);
        AddIfPresent(dict, "journal_at", activities.JournalAt);
        AddIfPresent(dict, "collected_at", activities.CollectedAt);
        AddIfPresent(dict, "dropped_at", activities.DroppedAt);
        AddIfPresent(dict, "server_time", activities.ServerTime);
        return dict;
    }

    private static void AddIfPresent(Dictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
        }
    }
}
