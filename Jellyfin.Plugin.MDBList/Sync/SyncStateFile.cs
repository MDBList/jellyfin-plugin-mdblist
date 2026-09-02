using System.Collections.Generic;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Root of the persisted sync-state file, keyed by Jellyfin user id (as a
/// string) from day one -- multi-user support later is then a UI change,
/// not a state migration.
/// </summary>
public class SyncStateFile
{
    /// <summary>
    /// Gets or sets the schema version, for future migrations.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets the per-user sync state, keyed by <c>Guid.ToString()</c>.
    /// </summary>
    public Dictionary<string, UserSyncState> Users { get; } = new();
}
