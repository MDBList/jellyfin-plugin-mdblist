using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Watched-status two-way sync -- port of watched_sync.py.
///
/// Push: membership diff, plus a value-changed check on WatchedAt so a
/// rewatch that only updates LastPlayedDate (Played unchanged) is still
/// re-pushed by the full diff, not just by the live single-item push from
/// <see cref="PushSingleAsync"/>.
///
/// Pull: real last-write-wins conflict resolution using UTC timestamps on
/// both sides (Jellyfin's LastPlayedDate is already Kind=Utc -- confirmed
/// in Phase 3 -- so unlike the Kodi addon, no naive-local-time conversion is
/// needed here). Remote wins an exact tie in both directions.
/// </summary>
public class WatchedSync
{
    private const SyncCategory Category = SyncCategory.Watched;
    private const string Endpoint = "/sync/watched";
    private const string RemoveEndpoint = "/sync/watched/remove";
    private const string FieldName = "watched_at";
    private const string JournalCategory = "watched";
    private const int JournalPageSize = 1000;

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly SyncStateStore _stateStore;
    private readonly MDBListApiClient _apiClient;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<WatchedSync> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{WatchedSync}"/> interface.</param>
    public WatchedSync(
        SyncPayloadBuilder payloadBuilder,
        SyncStateStore stateStore,
        MDBListApiClient apiClient,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        ILogger<WatchedSync> logger)
    {
        _payloadBuilder = payloadBuilder;
        _stateStore = stateStore;
        _apiClient = apiClient;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>
    /// Full membership diff against the whole snapshot.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="snapshot">The current library snapshot.</param>
    /// <param name="allowRemovals">See <see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed/skipped.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, bool allowRemovals, CancellationToken cancellationToken)
    {
        var current = CurrentWatchedItems(snapshot);

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, items, GetWatchedAtValue, cancellationToken),
            items => _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, items, cancellationToken),
            valueChanged: (known, item) => known.WatchedAt != item.WatchedAt,
            cancellationToken,
            allowRemovals).ConfigureAwait(false);
    }

    /// <summary>
    /// Immediate push for one item, triggered by a live <c>UserDataSaved</c>
    /// notification (Jellyfin's native watched toggle, not just our own
    /// pull-applied writes -- those are filtered out by the caller before
    /// this is reached).
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="record">The single item's current state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What happened: not mappable, no-op, added, or removed.</returns>
    public async Task<PushOutcome> PushSingleAsync(Guid userId, string accessToken, SnapshotItem record, CancellationToken cancellationToken)
    {
        var key = CanonicalKey(record);
        if (key is null)
        {
            return PushOutcome.NotMappable;
        }

        var known = await _stateStore.GetKnownItemsAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        known.TryGetValue(key, out var knownItem);

        if (record.Played)
        {
            var item = BuildKnownItem(record);
            if (knownItem is not null && knownItem.WatchedAt == item.WatchedAt)
            {
                return PushOutcome.NoOp;
            }

            await _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, [item], GetWatchedAtValue, cancellationToken).ConfigureAwait(false);
            return PushOutcome.Added;
        }

        if (knownItem is null)
        {
            return PushOutcome.NoOp;
        }

        await _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, [knownItem], cancellationToken).ConfigureAwait(false);
        return PushOutcome.Removed;
    }

    /// <summary>
    /// First sync for this user (no known items yet): a full pull with no
    /// removal reconcile, run BEFORE the first push so it records what MDBList
    /// already has. Without it the first push re-sends the whole local
    /// history, and every LastPlayedDate that differs from MDBList's stored
    /// timestamp is written as a fresh watch (which e.g. un-drops shows).
    /// Anything watched only locally just hasn't been pushed yet -- not
    /// unwatched remotely -- hence no removal reconcile.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="user">The resolved Jellyfin user, for writing user data.</param>
    /// <param name="snapshot">The current library snapshot, to match remote entries against.</param>
    /// <param name="serverTime">See <see cref="PullAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were actually changed.</returns>
    public async Task<PullResult> SeedPullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MDBList Sync: watched pull for user {UserName} seeding first sync - running full pull", user.Username);
        return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, trusted: false, cancellationToken, seed: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls remote watched-status changes into Jellyfin -- an incremental
    /// journal read if a cursor exists, otherwise (or if the cursor is
    /// outside the 30-day journal retention window) a full reconciliation.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="user">The resolved Jellyfin user, for writing user data.</param>
    /// <param name="snapshot">The current library snapshot, to match remote entries against.</param>
    /// <param name="serverTime">
    /// /sync/last_activities' own server_time -- a safety-margined
    /// timestamp meant to be persisted as the next watermark, rather than
    /// the device's own clock, which can drift and under-cover the next
    /// incremental window.
    /// </param>
    /// <param name="trusted">
    /// Forwarded to the full-reconcile removal guard -- see
    /// <see cref="ShouldHoldPullRemovals"/> and removal_safety_pattern.md's
    /// Trusted Runs section. Defaults to false so a call site that forgets
    /// to think about it stays safe; only the caller's own trusted-removal
    /// signal (the same one gating push) should pass true.
    /// <see cref="PullIncrementalAsync"/> doesn't need this: it applies
    /// explicit per-item journal events, not a "known minus current-read"
    /// diff, so it isn't the failure mode this pattern guards against.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were actually changed, and which mode ran.</returns>
    public async Task<PullResult> PullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, bool trusted, CancellationToken cancellationToken)
    {
        var since = await _stateStore.GetSyncedAtAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(since))
        {
            _logger.LogDebug("MDBList Sync: watched pull for user {UserName} has no cursor - running full pull", user.Username);
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, trusted, cancellationToken).ConfigureAwait(false);
        }

        if (trusted && await _stateStore.GetFullReconcilePendingAsync(userId, Category, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("MDBList Sync: watched pull for user {UserName} has held removals from an earlier run - running full pull", user.Username);
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, trusted, cancellationToken).ConfigureAwait(false);
        }

        var journal = await _apiClient.FetchJournalAsync(accessToken, since, JournalPageSize, cancellationToken).ConfigureAwait(false);
        if (journal.RequiresFullSync)
        {
            _logger.LogDebug(
                "MDBList Sync: watched pull cursor {Since} for user {UserName} is outside journal retention - running full pull",
                since,
                user.Username);
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, trusted, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "MDBList Sync: watched pull cursor {Since} for user {UserName} - running incremental pull ({Count} journal entries)",
            since,
            user.Username,
            journal.Entries.Count);
        return await PullIncrementalAsync(userId, user, journal.Entries, snapshot, serverTime, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PullResult> PullFullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, bool trusted, CancellationToken cancellationToken, bool seed = false)
    {
        var changes = new PulledStateChanges();
        try
        {
            return await PullFullCoreAsync(userId, accessToken, user, snapshot, serverTime, trusted, seed, changes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Also on a later item's failure: what earlier items already
            // changed in Jellyfin must still be recorded (see ApplyWatched).
            await PersistPulledStateAsync(userId, changes, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<PullResult> PullFullCoreAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, bool trusted, bool seed, PulledStateChanges changes, CancellationToken cancellationToken)
    {
        // extended=null (full, not ids_only): ids_only only exposes a
        // movie's tmdb id (and an episode's parent show's tmdb id). A local
        // item identified only by imdb/tvdb carries no tmdb id at all, so it
        // could never be matched below with that alone -- full mode gives
        // every provider id.
        var data = await _apiClient.FetchSyncItemsAsync(accessToken, Endpoint, mediatype: null, since: null, extended: null, JournalPageSize, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "MDBList Sync: watched full pull for user {UserName} fetched {MovieCount} movies, {EpisodeCount} episodes from MDBList",
            user.Username,
            data.Movies.Count,
            data.Episodes.Count);

        var applied = 0;
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in data.Movies)
        {
            var ids = entry.Movie?.Ids;
            if (ids is null || ids.IsEmpty)
            {
                continue;
            }

            var (appliedOk, key) = ApplyMovieEntry(user, snapshot, ids, "active", entry.LastWatchedAt, changes);
            if (key is not null)
            {
                matchedKeys.Add(key);
            }

            if (appliedOk)
            {
                applied++;
            }
        }

        foreach (var entry in data.Episodes)
        {
            var showIds = entry.Episode?.Show?.Ids;
            if (showIds is null || showIds.IsEmpty)
            {
                continue;
            }

            var (appliedOk, key) = ApplyEpisodeEntry(user, snapshot, showIds, entry.Episode?.Season, entry.Episode?.Number, entry.Episode?.Ids, "active", entry.LastWatchedAt, changes);
            if (key is not null)
            {
                matchedKeys.Add(key);
            }

            if (appliedOk)
            {
                applied++;
            }
        }

        // The full list above is authoritative: anything locally watched but
        // not in it was unwatched remotely -- the fallback for when the
        // journal's 30-day retention window has lapsed, so there's no
        // incremental removal feed to rely on instead.
        //
        // This is the same "known minus current-read = remove" shape
        // DiffAndReconcileAsync guards against on push, just mirrored to the
        // opposite direction: a successful-but-degraded /sync/watched
        // response would otherwise read as "everything was unwatched
        // remotely" and wipe local state. Same three guards (trust,
        // empty-vs-threshold, magnitude), same constants -- see
        // removal_safety_pattern.md.
        var locallyWatched = new List<(SnapshotItem Record, string Key)>();
        foreach (var movie in snapshot.Movies)
        {
            var key = movie.Played ? ItemKeys.CanonicalMovieKey(movie.Ids) : null;
            if (key is not null)
            {
                locallyWatched.Add((movie, key));
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            var key = episode.Played ? ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber) : null;
            if (key is not null)
            {
                locallyWatched.Add((episode, key));
            }
        }

        // A seed pull (see SeedPullAsync) never unwatches: anything watched
        // only locally hasn't been pushed yet.
        if (seed)
        {
            locallyWatched.Clear();
        }

        var candidateRemovals = locallyWatched.Where(w => !matchedKeys.Contains(w.Key)).ToList();
        var holdRemovals = candidateRemovals.Count > 0
            && ShouldHoldPullRemovals(data.Movies.Count + data.Episodes.Count, candidateRemovals.Count, locallyWatched.Count, trusted);

        if (candidateRemovals.Count > 0 && !holdRemovals)
        {
            // The removal timestamp is the server-provided watermark, not
            // "now": if the item was genuinely rewatched between when the
            // server generated this snapshot and now, its local timestamp
            // needs to be newer than server_time (not a later client-side
            // "now") to correctly win the conflict-resolution check in
            // ApplyWatched.
            var removalAt = serverTime ?? NowIso();
            foreach (var (record, _) in candidateRemovals)
            {
                if (ApplyWatched(user, record, "removed", removalAt, changes))
                {
                    applied++;
                }
            }
        }

        await PersistPulledStateAsync(userId, changes, cancellationToken).ConfigureAwait(false);

        // The watermark advances even when removals are held: the adds above
        // are applied, and later untrusted runs can follow the journal
        // incrementally. Leaving it unset made every activity-gated pull
        // re-run this full pull (and hold again) until a trusted run came
        // along. The held removals aren't dropped -- the pending flag makes
        // the next trusted run (see PullAsync) redo this full reconcile.
        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        await _stateStore.SetFullReconcilePendingAsync(userId, Category, holdRemovals, cancellationToken).ConfigureAwait(false);

        return holdRemovals
            ? new PullResult { PulledApplied = applied, Mode = "full", SkippedRemove = candidateRemovals.Count }
            : new PullResult { PulledApplied = applied, Mode = "full" };
    }

    /// <summary>
    /// Same shape as <see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>'s
    /// removal guard, applied to the pull-direction full reconcile: held
    /// when the trigger isn't trusted for removals, when a totally-empty
    /// remote read sits next to a known-watched baseline bigger than the
    /// threshold, or when the removal batch itself is larger than
    /// max(<see cref="SyncPayloadBuilder.RemovalMinBatch"/>, knownCount *
    /// <see cref="SyncPayloadBuilder.RemovalMaxFraction"/>). The empty-read
    /// check is tied to the threshold rather than an absolute veto, same
    /// reasoning as DiffAndReconcileAsync -- a user whose whole watched
    /// library is smaller than the threshold must still be able to clear it
    /// completely on a trusted run.
    /// </summary>
    private bool ShouldHoldPullRemovals(int remoteCount, int candidateCount, int knownCount, bool trusted)
    {
        var threshold = Math.Max(SyncPayloadBuilder.RemovalMinBatch, (int)(knownCount * SyncPayloadBuilder.RemovalMaxFraction));

        if (!trusted)
        {
            _logger.LogDebug(
                "MDBList Sync: watched pull removal held ({Count} items) - this trigger doesn't allow removals",
                candidateCount);
            return true;
        }

        if (remoteCount == 0 && knownCount > threshold)
        {
            _logger.LogWarning(
                "MDBList Sync: watched pull removal held - remote full list came back empty while {KnownCount} items are locally "
                    + "watched (threshold {Threshold}); treating as an unreliable read rather than a real removal",
                knownCount,
                threshold);
            return true;
        }

        if (candidateCount > threshold)
        {
            _logger.LogWarning(
                "MDBList Sync: watched pull removal held - {Count} of {KnownCount} locally watched items would be unwatched "
                    + "(threshold {Threshold}); remote read may be incomplete",
                candidateCount,
                knownCount,
                threshold);
            return true;
        }

        return false;
    }

    private async Task<PullResult> PullIncrementalAsync(
        Guid userId,
        User user,
        IReadOnlyCollection<JournalEntry> entries,
        LibrarySnapshot snapshot,
        string? serverTime,
        CancellationToken cancellationToken)
    {
        var changes = new PulledStateChanges();
        try
        {
            return await PullIncrementalCoreAsync(userId, user, entries, snapshot, serverTime, changes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Also on a later item's failure: what earlier items already
            // changed in Jellyfin must still be recorded (see ApplyWatched).
            await PersistPulledStateAsync(userId, changes, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<PullResult> PullIncrementalCoreAsync(
        Guid userId,
        User user,
        IReadOnlyCollection<JournalEntry> entries,
        LibrarySnapshot snapshot,
        string? serverTime,
        PulledStateChanges changes,
        CancellationToken cancellationToken)
    {
        var applied = 0;
        var skippedType = 0;

        foreach (var entry in entries)
        {
            if (entry.Category != JournalCategory || entry.Ids is null)
            {
                continue;
            }

            // value_at is the actual watched timestamp and is what
            // conflict resolution must compare against -- but it's only
            // ever set on add/active rows; a removal row has no "value" to
            // speak of and only carries action_at (confirmed against
            // api.mdblist's _remove_movies/_remove_shows/etc., which write
            // the journal row with action_at but no value_at at all).
            // Falling back to action_at there keeps last-write-wins working
            // for removals instead of silently skipping the conflict check.
            var remoteAt = entry.ValueAt ?? entry.ActionAt;

            if (entry.ItemType == "movie")
            {
                var (appliedOk, _) = ApplyMovieEntry(user, snapshot, entry.Ids, entry.Status, remoteAt, changes);
                if (appliedOk)
                {
                    applied++;
                }
            }
            else if (entry.ItemType == "episode")
            {
                var (appliedOk, _) = ApplyEpisodeEntry(user, snapshot, entry.Ids, entry.Season, entry.Episode, entry.EpisodeIds, entry.Status, remoteAt, changes);
                if (appliedOk)
                {
                    applied++;
                }
            }
            else
            {
                // show/season-level rows have no directly writable Jellyfin field; skipped
                skippedType++;
            }
        }

        if (skippedType > 0)
        {
            _logger.LogDebug(
                "MDBList Sync: watched incremental pull for user {UserName} skipped {Count} journal entries with unhandled item type",
                user.Username,
                skippedType);
        }

        await PersistPulledStateAsync(userId, changes, cancellationToken).ConfigureAwait(false);
        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        return new PullResult { PulledApplied = applied, Mode = "incremental" };
    }

    private (bool Applied, string? Key) ApplyMovieEntry(User user, LibrarySnapshot snapshot, MediaIds ids, string? status, string? remoteAt, PulledStateChanges changes)
    {
        var match = snapshot.FindMovie(ids);
        if (match is null)
        {
            return (false, null);
        }

        return (ApplyWatched(user, match, status, remoteAt, changes), ItemKeys.CanonicalMovieKey(match.Ids));
    }

    private (bool Applied, string? Key) ApplyEpisodeEntry(User user, LibrarySnapshot snapshot, MediaIds showIds, int? season, int? episode, MediaIds? episodeIds, string? status, string? remoteAt, PulledStateChanges changes)
    {
        var match = snapshot.FindEpisode(showIds, season, episode, episodeIds);
        if (match is null)
        {
            _logger.LogDebug(
                "MDBList Sync: watched pull found no local match for show tmdb={Tmdb} imdb={Imdb} tvdb={Tvdb} S{Season}E{Episode} episodeTmdb={EpisodeTmdb} episodeTvdb={EpisodeTvdb}",
                showIds.Tmdb,
                showIds.Imdb,
                showIds.Tvdb,
                season,
                episode,
                episodeIds?.Tmdb,
                episodeIds?.Tvdb);
            return (false, null);
        }

        var key = ItemKeys.CanonicalEpisodeKey(match.Ids, match.Season, match.EpisodeNumber);
        return (ApplyWatched(user, match, status, remoteAt, changes), key);
    }

    /// <summary>
    /// Last-write-wins using Jellyfin's LastPlayedDate vs the remote
    /// timestamp -- the one sync category where Jellyfin tracks a
    /// comparable local timestamp, so real conflict resolution (not just
    /// remote-wins) applies. An exact tie resolves the same way in both
    /// branches below -- remote wins -- one consistent rule rather than
    /// local winning on removal but losing on activation.
    /// </summary>
    private bool ApplyWatched(User user, SnapshotItem record, string? status, string? remoteAt, PulledStateChanges changes)
    {
        // This pull may already have changed the item (watched, then unwatched,
        // in one batch): compare against that, not the pre-pull snapshot.
        var (localPlayCount, localTs) = changes.Current.TryGetValue(record.ItemId, out var current)
            ? current
            : (record.PlayCount, record.LastPlayedDate);
        var remoteTs = ParseTimestamp(remoteAt);
        var removed = status == "removed";
        var key = CanonicalKey(record);

        if (!ShouldApplyRemoteWatched(removed, localPlayCount, localTs, remoteTs))
        {
            if (removed && localPlayCount <= 0 && key is not null)
            {
                // Already unwatched here (e.g. by an earlier, interrupted pull):
                // still record it as synced
                changes.Removed.Add(key);
                changes.Upserts.Remove(key);
            }

            return false;
        }

        // Record what this change makes MDBList and Jellyfin agree on, for the
        // known-items state: without it the next push diffs a pulled watch as a
        // new local one and pushes it back -- undoing a later remote unwatch.
        if (removed)
        {
            SetWatched(user, record.ItemId, played: false, playCount: 0, lastPlayedDate: null);
            changes.Current[record.ItemId] = (0, localTs);
            if (key is not null)
            {
                changes.Removed.Add(key);
                changes.Upserts.Remove(key);
            }
        }
        else if (IsAlreadyWatchedAt(localPlayCount, localTs, remoteTs))
        {
            // Already exactly this in Jellyfin: skip the write, or every full
            // pull rewrites the user data of the whole watched library
            if (key is not null)
            {
                changes.Upserts[key] = BuildKnownItem(record, localTs);
                changes.Removed.Remove(key);
            }

            return false;
        }
        else
        {
            var savedLastPlayed = SetWatched(user, record.ItemId, played: true, playCount: Math.Max(localPlayCount, 1), lastPlayedDate: remoteTs ?? localTs);
            changes.Current[record.ItemId] = (Math.Max(localPlayCount, 1), savedLastPlayed);
            if (key is not null)
            {
                // Built from what Jellyfin now reports, the same way the next
                // push reads the library, so that push sees nothing new to send.
                changes.Upserts[key] = BuildKnownItem(record, savedLastPlayed);
                changes.Removed.Remove(key);
            }
        }

        return true;
    }

    /// <summary>
    /// The conflict-resolution decision at the heart of <see cref="ApplyWatched"/>,
    /// pulled out as a pure function so the matrix (local newer / remote
    /// newer / exact tie / missing timestamps, crossed with add vs remove)
    /// is unit-testable without a live <c>IUserDataManager</c>. An exact tie
    /// resolves the same way in both branches -- remote wins -- rather than
    /// local winning on removal but losing on activation.
    /// </summary>
    /// <param name="removed">Whether the remote row is a removal.</param>
    /// <param name="localPlayCount">The local item's current play count.</param>
    /// <param name="localTs">The local item's <c>LastPlayedDate</c>, if any.</param>
    /// <param name="remoteTs">The remote row's effective timestamp, if any.</param>
    /// <returns>True if the remote state should be applied locally.</returns>
    internal static bool ShouldApplyRemoteWatched(bool removed, int localPlayCount, DateTime? localTs, DateTime? remoteTs)
    {
        if (removed)
        {
            if (localPlayCount <= 0)
            {
                return false;
            }

            return !(localTs.HasValue && remoteTs.HasValue && localTs > remoteTs);
        }

        return !(localPlayCount > 0 && localTs.HasValue && remoteTs.HasValue && localTs > remoteTs);
    }

    /// <summary>
    /// Whether a remote watch is already exactly what Jellyfin has -- watched,
    /// with the same LastPlayedDate to the second (MDBList's precision) -- so
    /// applying it would be a no-op write.
    /// </summary>
    /// <param name="localPlayCount">The local item's current play count.</param>
    /// <param name="localTs">The local item's <c>LastPlayedDate</c>, if any.</param>
    /// <param name="remoteTs">The remote row's timestamp, if any.</param>
    /// <returns>True if nothing would change locally.</returns>
    internal static bool IsAlreadyWatchedAt(int localPlayCount, DateTime? localTs, DateTime? remoteTs)
    {
        return localPlayCount > 0
            && localTs.HasValue
            && remoteTs.HasValue
            && localTs.Value.Ticks / TimeSpan.TicksPerSecond == remoteTs.Value.Ticks / TimeSpan.TicksPerSecond;
    }

    /// <returns>The item's LastPlayedDate as Jellyfin stored it, read back after saving.</returns>
    private DateTime? SetWatched(User user, Guid itemId, bool played, int playCount, DateTime? lastPlayedDate)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return lastPlayedDate;
        }

        var userData = _userDataManager.GetUserData(user, item) ?? new UserItemData { Key = item.GetUserDataKeys().First() };
        userData.Played = played;
        userData.PlayCount = playCount;

        // Only overwrite when we have a real value -- on removal this
        // leaves it untouched, matching Jellyfin's own "mark unplayed"
        // behavior rather than forcing an empty/invalid date onto the item.
        if (lastPlayedDate.HasValue)
        {
            userData.LastPlayedDate = lastPlayedDate;
        }

        _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
        return _userDataManager.GetUserData(user, item)?.LastPlayedDate ?? userData.LastPlayedDate;
    }

    private async Task PersistPulledStateAsync(Guid userId, PulledStateChanges changes, CancellationToken cancellationToken)
    {
        if (changes.Upserts.Count == 0 && changes.Removed.Count == 0)
        {
            return;
        }

        await _stateStore.MergeKnownItemsAsync(userId, Category, changes.Upserts, changes.Removed.ToList(), cancellationToken).ConfigureAwait(false);
        changes.Upserts.Clear();
        changes.Removed.Clear();
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static string NowIso()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string? CanonicalKey(SnapshotItem record)
    {
        return record.Type == "movie"
            ? ItemKeys.CanonicalMovieKey(record.Ids)
            : ItemKeys.CanonicalEpisodeKey(record.Ids, record.Season, record.EpisodeNumber);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record)
    {
        return BuildKnownItem(record, record.LastPlayedDate);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record, DateTime? lastPlayedDate)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            EpisodeIds = record.EpisodeIds,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            WatchedAt = lastPlayedDate?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private static JsonNode? GetWatchedAtValue(KnownSyncItem item)
    {
        return item.WatchedAt is null ? null : JsonValue.Create(item.WatchedAt);
    }

    private static Dictionary<string, KnownSyncItem> CurrentWatchedItems(LibrarySnapshot snapshot)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        foreach (var movie in snapshot.Movies)
        {
            if (!movie.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                items[key] = BuildKnownItem(movie);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            if (!episode.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                items[key] = BuildKnownItem(episode);
            }
        }

        return items;
    }

    /// <summary>
    /// Known-items changes from one pull run: items it made watched in Jellyfin
    /// (upserts) and unwatched (removed).
    /// </summary>
    private sealed class PulledStateChanges
    {
        public Dictionary<string, KnownSyncItem> Upserts { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Removed { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets each item's play count and last-played date as this pull left it,
        /// for later entries in the same batch.
        /// </summary>
        public Dictionary<Guid, (int PlayCount, DateTime? LastPlayed)> Current { get; } = new();
    }
}
