using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Library;
using Jellyfin.Plugin.MDBList.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Events;

/// <summary>
/// Live single-item push triggered by <c>UserDataSaved</c> -- port of
/// live_sync.py's handle_library_update, reacting to Jellyfin's native
/// watched-toggle/playback-completion the same way VideoLibrary.OnUpdate
/// did for Kodi.
/// </summary>
public class LiveSyncService
{
    private static readonly HashSet<UserDataSaveReason> WatchedTriggerReasons =
    [
        UserDataSaveReason.TogglePlayed,
        UserDataSaveReason.PlaybackFinished,
    ];

    private static readonly HashSet<UserDataSaveReason> RatingTriggerReasons =
    [
        UserDataSaveReason.UpdateUserRating,
        UserDataSaveReason.UpdateUserData,
    ];

    // Longer than any sync should take -- a push still waiting after this is
    // left to the next full run
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromMinutes(30);

    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly OAuthService _oauthService;
    private readonly WatchedSync _watchedSync;
    private readonly RatingsSync _ratingsSync;
    private readonly SyncOrchestrator _orchestrator;
    private readonly ILogger<LiveSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveSyncService"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="watchedSync">Instance of the <see cref="WatchedSync"/>.</param>
    /// <param name="ratingsSync">Instance of the <see cref="RatingsSync"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LiveSyncService}"/> interface.</param>
    public LiveSyncService(
        IUserManager userManager,
        IUserDataManager userDataManager,
        OAuthService oauthService,
        WatchedSync watchedSync,
        RatingsSync ratingsSync,
        SyncOrchestrator orchestrator,
        ILogger<LiveSyncService> logger)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
        _oauthService = oauthService;
        _watchedSync = watchedSync;
        _ratingsSync = ratingsSync;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Entry point from <see cref="MDBListEventHostedService"/>. Filters
    /// down to relevant events synchronously, then hands off to a
    /// background task -- <c>UserDataSaved</c> fires synchronously on the
    /// caller's own thread (an API request, a playback session), so
    /// awaiting an HTTP push here would block it.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandleUserDataSaved(UserDataSaveEventArgs e)
    {
        if (e.SaveReason == UserDataSaveReason.Import)
        {
            // Our own pull-applied write, once a pull exists -- ignoring it
            // here is one of three independent echo-loop guards (the others:
            // writing pulled state with reason Import in the first place,
            // and holding the orchestrator lock for the whole live push).
            return;
        }

        if (e.Item is not (Movie or Episode))
        {
            return;
        }

        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.JellyfinUserId == e.UserId);
        if (linkedUserConfig is null)
        {
            return;
        }

        var pushWatched = linkedUserConfig.WatchedEnabled && WatchedTriggerReasons.Contains(e.SaveReason);
        var pushRating = linkedUserConfig.RatingsEnabled && RatingTriggerReasons.Contains(e.SaveReason);

        // The stock web UI's thumbs-up/down button always writes exactly
        // 10 or 1 via UpdateUserRating, regardless of what the user
        // actually intends -- pushing that as a real MDBList rating would
        // silently overwrite (or invent) one. UpdateUserData (genuine
        // numeric ratings, e.g. from Infuse) is unaffected.
        if (pushRating && e.SaveReason == UserDataSaveReason.UpdateUserRating && linkedUserConfig.IgnoreThumbRatings)
        {
            pushRating = false;
        }

        if (!pushWatched && !pushRating)
        {
            return;
        }

        // Taken now, not in the background task: that may wait out a sync
        // first, and this save is the moment the playback ended
        DateTime? finishedAt = pushWatched && e.SaveReason == UserDataSaveReason.PlaybackFinished ? DateTime.UtcNow : null;
        _ = Task.Run(() => HandleAsync(e.UserId, e.Item, e.UserData, pushWatched, pushRating, finishedAt));
    }

    /// <summary>
    /// Whether a <c>PlaybackFinished</c> save is a play to completion:
    /// Jellyfin resets the resume position to 0 and marks the item played
    /// then, while a stop partway through keeps the position.
    /// </summary>
    /// <param name="played">The item's saved played flag.</param>
    /// <param name="positionTicks">The item's saved resume position.</param>
    /// <returns>True when the playback ran to completion.</returns>
    internal static bool IsPlayedToCompletion(bool played, long positionTicks)
    {
        return played && positionTicks == 0;
    }

    private async Task HandleAsync(Guid userId, BaseItem item, UserItemData userData, bool pushWatched, bool pushRating, DateTime? finishedAt)
    {
        var handle = _orchestrator.TryLock();
        if (handle is null)
        {
            // A sync (or another live push) holds the lock. Wait for it rather
            // than drop this push: the pull's own writes never get here (saved
            // with reason Import, filtered above), so this is a real user
            // change. The lock is held for the whole block below, not just
            // checked once, so a pull starting mid-handler can't race it.
            try
            {
                handle = await _orchestrator.WaitLockAsync(LockWaitTimeout).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (handle is null)
            {
                _logger.LogDebug("MDBList live push for {ItemName} dropped - sync still running", item.Name);
                return;
            }

            // The item may have changed again while waiting (e.g. toggled
            // twice) -- push its current state, not this event's
            var user = _userManager.GetUserById(userId);
            userData = (user is null ? null : _userDataManager.GetUserData(user, item)) ?? userData;
        }

        using var heldLock = handle;
        try
        {
            if (finishedAt.HasValue && IsPlayedToCompletion(userData.Played, userData.PlaybackPositionTicks))
            {
                userData = StampFinishedAt(userId, item, userData, finishedAt.Value);
            }

            var record = BuildRecord(item, userData);
            if (record is null)
            {
                return;
            }

            var accessToken = await _oauthService.EnsureValidTokenAsync(userId, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            if (pushWatched)
            {
                await _watchedSync.PushSingleAsync(userId, accessToken, record, CancellationToken.None).ConfigureAwait(false);
            }

            if (pushRating)
            {
                await _ratingsSync.PushSingleAsync(userId, accessToken, record, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (MDBListApiException ex)
        {
            _logger.LogDebug(ex, "MDBList live push failed");
        }
    }

    /// <summary>
    /// Jellyfin sets <c>LastPlayedDate</c> when playback starts and leaves
    /// it there when it finishes, so the watch would land on MDBList at the
    /// start time. It's moved to when the playback finished, in Jellyfin
    /// too: the full sync pushes <c>LastPlayedDate</c>, and a push of the
    /// start time after this one would read as a second, earlier play.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="item">The item that finished playing.</param>
    /// <param name="userData">The item's user data as saved.</param>
    /// <param name="finishedAt">When the playback finished.</param>
    /// <returns>The item's user data with the finish time, as Jellyfin stored it.</returns>
    private UserItemData StampFinishedAt(Guid userId, BaseItem item, UserItemData userData, DateTime finishedAt)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return userData;
        }

        // A fresh copy, not the event's object Jellyfin may still hold
        var data = _userDataManager.GetUserData(user, item) ?? userData;

        // Whole seconds, MDBList's precision, so the stored value and the
        // pushed one compare equal on the next full sync
        data.LastPlayedDate = new DateTime(finishedAt.Ticks - (finishedAt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

        // Saved as Import, which this service ignores above, so it doesn't
        // come back here as another push
        _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.Import, CancellationToken.None);
        return _userDataManager.GetUserData(user, item) ?? data;
    }

    private static SnapshotItem? BuildRecord(BaseItem item, UserItemData userData)
    {
        if (item is Episode episode)
        {
            if (episode.Series is null || episode.ParentIndexNumber is null || episode.IndexNumber is null)
            {
                return null;
            }

            var showIds = MediaIdMapper.MapShowIds(episode.Series.ProviderIds);
            if (showIds.IsEmpty)
            {
                return null;
            }

            var episodeIds = MediaIdMapper.MapShowIds(episode.ProviderIds);
            return new SnapshotItem
            {
                Type = "episode",
                ItemId = episode.Id,
                Title = episode.Name,
                Ids = showIds,
                EpisodeIds = episodeIds.IsEmpty ? null : episodeIds,
                Season = episode.ParentIndexNumber,
                EpisodeNumber = episode.IndexNumber,
                Played = userData.Played,
                PlayCount = userData.PlayCount,
                LastPlayedDate = userData.LastPlayedDate,
                Rating = userData.Rating,
                DateCreated = episode.DateCreated,
            };
        }

        var ids = MediaIdMapper.MapMovieIds(item.ProviderIds);
        if (ids.IsEmpty)
        {
            return null;
        }

        return new SnapshotItem
        {
            Type = "movie",
            ItemId = item.Id,
            Title = item.Name,
            Ids = ids,
            Played = userData.Played,
            PlayCount = userData.PlayCount,
            LastPlayedDate = userData.LastPlayedDate,
            Rating = userData.Rating,
            DateCreated = item.DateCreated,
        };
    }
}
