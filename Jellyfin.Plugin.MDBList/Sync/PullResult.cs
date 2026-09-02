namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Summary of a pull (<see cref="WatchedSync.PullAsync"/>/RatingsSync.PullAsync).
/// </summary>
public class PullResult
{
    /// <summary>
    /// Gets or sets how many items were actually changed locally.
    /// </summary>
    public int PulledApplied { get; set; }

    /// <summary>
    /// Gets or sets the pull mode: "full" or "incremental".
    /// </summary>
    public string Mode { get; set; } = string.Empty;
}
