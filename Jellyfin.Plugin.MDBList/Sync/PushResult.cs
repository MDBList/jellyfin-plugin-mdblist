namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Summary of a full-diff push (<see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>).
/// </summary>
public class PushResult
{
    /// <summary>
    /// Gets or sets how many items were pushed as newly active.
    /// </summary>
    public int PushedAdd { get; set; }

    /// <summary>
    /// Gets or sets how many items were pushed as removed.
    /// </summary>
    public int PushedRemove { get; set; }
}
