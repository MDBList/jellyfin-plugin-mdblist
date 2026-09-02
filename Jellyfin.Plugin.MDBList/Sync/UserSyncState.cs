using System.Collections.Generic;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// One Jellyfin user's full persisted sync state.
/// </summary>
public class UserSyncState
{
    /// <summary>
    /// Gets the watched-status sync state.
    /// </summary>
    public CategoryState Watched { get; } = new();

    /// <summary>
    /// Gets the ratings sync state.
    /// </summary>
    public CategoryState Ratings { get; } = new();

    /// <summary>
    /// Gets the collection sync state.
    /// </summary>
    public CategoryState Collection { get; } = new();

    /// <summary>
    /// Gets the last /sync/last_activities snapshot this user's account was
    /// checked against -- gates the cheap incremental-pull check.
    /// </summary>
    public Dictionary<string, string> LastActivitiesSeen { get; } = new();

    /// <summary>
    /// Gets or sets a human-readable summary of the most recent sync run
    /// (full or activity-triggered), shown on the config page. Null until
    /// the first run completes.
    /// </summary>
    public string? LastRunSummary { get; set; }
}
