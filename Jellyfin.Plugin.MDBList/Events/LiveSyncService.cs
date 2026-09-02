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

    private readonly OAuthService _oauthService;
    private readonly WatchedSync _watchedSync;
    private readonly SyncOrchestrator _orchestrator;
    private readonly ILogger<LiveSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveSyncService"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="watchedSync">Instance of the <see cref="WatchedSync"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LiveSyncService}"/> interface.</param>
    public LiveSyncService(OAuthService oauthService, WatchedSync watchedSync, SyncOrchestrator orchestrator, ILogger<LiveSyncService> logger)
    {
        _oauthService = oauthService;
        _watchedSync = watchedSync;
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

        if (!WatchedTriggerReasons.Contains(e.SaveReason))
        {
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

        _ = Task.Run(() => HandleAsync(e.UserId, e.Item, e.UserData));
    }

    private async Task HandleAsync(Guid userId, BaseItem item, UserItemData userData)
    {
        using var handle = _orchestrator.TryLock();
        if (handle is null)
        {
            // A pull is in progress, most likely applying remote state to
            // this same item right now -- skip rather than echo it straight
            // back, instead of just checking once at entry (a pull starting
            // mid-handler would be caught too, since the lock is held for
            // the whole block below, not just this check).
            return;
        }

        try
        {
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

            await _watchedSync.PushSingleAsync(userId, accessToken, record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (MDBListApiException ex)
        {
            _logger.LogDebug(ex, "MDBList live push failed");
        }
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

            return new SnapshotItem
            {
                Type = "episode",
                ItemId = episode.Id,
                Title = episode.Name,
                Ids = showIds,
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
