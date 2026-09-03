using System;

namespace Jellyfin.Plugin.MDBList.Configuration;

/// <summary>
/// One Jellyfin user's link to an MDBList account. <see cref="PluginConfiguration.Users"/>
/// holds one of these per linked user; every sync/live-push/scrobble path
/// is keyed by <see cref="JellyfinUserId"/>, so multiple users can each link
/// their own MDBList account independently.
/// </summary>
public class UserSyncConfig
{
    /// <summary>
    /// Gets or sets the linked Jellyfin user's id.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the MDBList OAuth access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MDBList OAuth refresh token.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the access token's expiry, as Unix seconds.
    /// </summary>
    public long ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Jellyfin's thumbs-up/down
    /// (<c>UserDataSaveReason.UpdateUserRating</c>, from the stock web UI's
    /// like/dislike button) should be ignored rather than pushed as a 10 or
    /// 1 rating. That button always writes exactly 10 or 1 regardless of
    /// intent, which would otherwise silently overwrite (or create) a real
    /// numeric MDBList rating. Genuine numeric ratings (e.g. from clients
    /// like Infuse) go through UpdateUserData instead and are unaffected.
    /// Defaults to on.
    /// </summary>
    public bool IgnoreThumbRatings { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether watched status syncs at all
    /// (live push, full run, and the activity-gated pull).
    /// </summary>
    public bool WatchedEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether ratings sync at all.
    /// </summary>
    public bool RatingsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether collection membership pushes
    /// at all.
    /// </summary>
    public bool CollectionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a full sync run should follow
    /// a completed library scan -- port of Kodi's <c>sync.on_library_scan</c>
    /// setting. <see cref="Events.LibraryChangeDebouncer"/> already reacts to
    /// every item add/update/remove regardless of what triggered them (a
    /// scheduled scan, real-time monitoring, a manual "Scan Library"), so
    /// this toggle gates that debouncer rather than adding a second,
    /// redundant trigger off <c>ITaskManager.TaskCompleted</c>.
    /// </summary>
    public bool SyncAfterLibraryScan { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether live playback progress pushes
    /// to MDBList's /scrobble/* endpoints -- port of Kodi's player_monitor.py.
    /// Independent of <see cref="WatchedEnabled"/>: scrobbling is a
    /// real-time progress feed the server uses for its own watched-marking
    /// and "continue watching" position, not the /sync/watched
    /// diff-and-reconcile flow -- either can be on without the other.
    /// </summary>
    public bool ScrobblingEnabled { get; set; } = true;
}
