using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Events;

/// <summary>
/// Live playback progress pushed to MDBList's /scrobble/* endpoints -- port
/// of player_monitor.py. One-way and independent of watched-status sync:
/// the server uses this feed for its own watched-marking (at >=80% progress)
/// and "continue watching" position, so this never writes back to Jellyfin
/// and needs none of WatchedSync's echo-loop guards.
///
/// Kodi needed its own interval timer because xbmc.Player only fires
/// discrete pause/resume/seek callbacks. Jellyfin already reports playback
/// progress on its own -- every second while a session is "automated" (see
/// SessionInfo.StartAutomaticProgress) plus whenever a client actually
/// checks in -- so this only reacts to ISessionManager's events and
/// throttles, rather than running a timer of its own.
/// </summary>
public sealed class PlaybackScrobbleService
{
    private const string StartEndpoint = "/scrobble/start";
    private const string PauseEndpoint = "/scrobble/pause";
    private const string StopEndpoint = "/scrobble/stop";

    // Jellyfin's automated progress timer ticks every second (SessionInfo's
    // internal _progressTimer) -- without a floor here, every active
    // playback session would hit /scrobble/start once a second.
    private static readonly TimeSpan MinProgressInterval = TimeSpan.FromSeconds(15);

    // How long periodic progress pushes pause for a user after a 429 that
    // carries no Retry-After.
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(10);

    private readonly OAuthService _oauthService;
    private readonly MDBListApiClient _apiClient;
    private readonly ILogger<PlaybackScrobbleService> _logger;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    // Per user: periodic progress pushes are skipped until this time after a
    // 429, rather than spending more of an exhausted quota every tick.
    private readonly ConcurrentDictionary<Guid, DateTime> _pausedUntil = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackScrobbleService"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PlaybackScrobbleService}"/> interface.</param>
    public PlaybackScrobbleService(OAuthService oauthService, MDBListApiClient apiClient, ILogger<PlaybackScrobbleService> logger)
    {
        _oauthService = oauthService;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackStart</c>.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackStart(PlaybackProgressEventArgs e)
    {
        var key = SessionKey(e);
        if (key is null)
        {
            return;
        }

        // Recorded synchronously, before the send below even runs: the
        // automated progress timer can tick again within a second, and it
        // must see "just sent" immediately, not race the async send.
        var state = new SessionState { LastSentAt = DateTime.UtcNow };
        _sessions[key] = state;
        Dispatch(e, StartEndpoint, playedToCompletion: false, state);
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackProgress</c> -- covers pause,
    /// resume, seek, and periodic progress ticks alike, same as Kodi's
    /// pause/resume/seek/interval events all mapping to two endpoints.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackProgress(PlaybackProgressEventArgs e)
    {
        var key = SessionKey(e);
        if (key is null)
        {
            return;
        }

        var state = _sessions.GetOrAdd(key, static _ => new SessionState());

        if (e.IsPaused)
        {
            if (state.WasPaused)
            {
                return;
            }

            state.WasPaused = true;
            Dispatch(e, PauseEndpoint, playedToCompletion: false);
            return;
        }

        var resuming = state.WasPaused;
        state.WasPaused = false;

        // MDBList answered 404 for this title: re-sending it every tick
        // won't change that before the playback ends
        if (!resuming && state.TitleUnknown)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!resuming && now - state.LastSentAt < MinProgressInterval)
        {
            return;
        }

        state.LastSentAt = now;
        Dispatch(e, StartEndpoint, playedToCompletion: false, state, periodic: !resuming);
    }

    /// <summary>
    /// Handles <c>ISessionManager.PlaybackStopped</c> -- covers both a
    /// genuine stop and playing through to the end, same as Kodi's
    /// onPlayBackStopped/onPlayBackEnded both mapping to /scrobble/stop.
    /// </summary>
    /// <param name="e">The event args.</param>
    public void HandlePlaybackStopped(PlaybackStopEventArgs e)
    {
        var key = SessionKey(e);
        if (key is not null)
        {
            _sessions.TryRemove(key, out _);
        }

        Dispatch(e, StopEndpoint, e.PlayedToCompletion);
    }

    private static string? SessionKey(PlaybackProgressEventArgs e)
    {
        return e.PlaySessionId ?? e.Session?.Id;
    }

    private void Dispatch(PlaybackProgressEventArgs e, string endpoint, bool playedToCompletion, SessionState? state = null, bool periodic = false)
    {
        _ = Task.Run(() => SendAsync(e, endpoint, playedToCompletion, state, periodic));
    }

    private async Task SendAsync(PlaybackProgressEventArgs e, string endpoint, bool playedToCompletion, SessionState? state, bool periodic)
    {
        var userIds = ResolveScrobblingLinkedUserIds(e.Users);
        if (periodic)
        {
            var now = DateTime.UtcNow;
            userIds.RemoveAll(id => _pausedUntil.TryGetValue(id, out var until) && now < until);
        }

        if (userIds.Count == 0)
        {
            return;
        }

        var request = BuildScrobbleRequest(e.Item, e.PlaybackPositionTicks, playedToCompletion);
        if (request is null)
        {
            return;
        }

        // A session usually has exactly one user, but Jellyfin models it as
        // a list -- push once per linked user actually on this session, not
        // just the first one found overall.
        foreach (var userId in userIds)
        {
            await SendForUserAsync(userId, endpoint, request, state).ConfigureAwait(false);
        }
    }

    private async Task SendForUserAsync(Guid userId, string endpoint, ScrobbleRequest request, SessionState? state)
    {
        try
        {
            var accessToken = await _oauthService.EnsureValidTokenAsync(userId, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            // Only a stop is worth waiting out a 429 for (it can be what
            // marks the title watched); a start/pause would land stale, and
            // possibly after a later stop
            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, request, CancellationToken.None, retryRateLimit: endpoint == StopEndpoint).ConfigureAwait(false);
        }
        catch (MDBListApiException ex)
        {
            if (ex.IsRateLimited)
            {
                _pausedUntil[userId] = DateTime.UtcNow + (ex.RetryAfter ?? RateLimitPause);
            }
            else if (ex.StatusCode == HttpStatusCode.NotFound && endpoint == StartEndpoint && state is not null)
            {
                state.TitleUnknown = true;
            }

            _logger.LogDebug(ex, "MDBList scrobble push to {Endpoint} failed for user {UserId}", endpoint, userId);
        }
    }

    private static List<Guid> ResolveScrobblingLinkedUserIds(IReadOnlyList<User> sessionUsers)
    {
        var linkedUsers = Plugin.Instance?.Configuration.Users;
        if (linkedUsers is null || linkedUsers.Count == 0)
        {
            return [];
        }

        var result = new List<Guid>();
        foreach (var sessionUser in sessionUsers)
        {
            var config = linkedUsers.FirstOrDefault(u => u.JellyfinUserId == sessionUser.Id);
            if (config is not null && config.ScrobblingEnabled)
            {
                result.Add(sessionUser.Id);
            }
        }

        return result;
    }

    private static ScrobbleRequest? BuildScrobbleRequest(BaseItem? item, long? positionTicks, bool playedToCompletion)
    {
        if (item is null)
        {
            return null;
        }

        var progress = playedToCompletion ? 100.0 : ComputeProgress(item.RunTimeTicks, positionTicks);
        if (progress is null)
        {
            return null;
        }

        var appVersion = Plugin.Instance?.Version.ToString();

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
            return new ScrobbleRequest
            {
                Show = new ScrobbleShowRef
                {
                    Ids = showIds,
                    Season = new ScrobbleSeasonRef
                    {
                        Number = episode.ParentIndexNumber,
                        Episode = new ScrobbleEpisodeRef
                        {
                            Number = episode.IndexNumber,
                            Ids = episodeIds.IsEmpty ? null : episodeIds,
                        },
                    },
                },
                Progress = progress.Value,
                AppVersion = appVersion,
            };
        }

        if (item is Movie)
        {
            var ids = MediaIdMapper.MapMovieIds(item.ProviderIds);
            if (ids.IsEmpty)
            {
                return null;
            }

            return new ScrobbleRequest
            {
                Movie = new ScrobbleMovieRef { Ids = ids },
                Progress = progress.Value,
                AppVersion = appVersion,
            };
        }

        return null;
    }

    internal static double? ComputeProgress(long? runTimeTicks, long? positionTicks)
    {
        if (runTimeTicks is null or <= 0 || positionTicks is null)
        {
            return null;
        }

        var clamped = Math.Clamp(positionTicks.Value, 0, runTimeTicks.Value);
        return Math.Round((double)clamped / runTimeTicks.Value * 100, 2);
    }

    private sealed class SessionState
    {
        public bool WasPaused { get; set; }

        public bool TitleUnknown { get; set; }

        public DateTime LastSentAt { get; set; } = DateTime.MinValue;
    }
}
