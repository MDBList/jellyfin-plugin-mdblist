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

    /// <summary>
    /// Gets or sets how many would-be local removals (e.g. unwatch) a full
    /// pull held back because the remote read looked unreliable -- see
    /// <see cref="WatchedSync.PullAsync"/> and
    /// <see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>.
    /// </summary>
    public int SkippedRemove { get; set; }
}
