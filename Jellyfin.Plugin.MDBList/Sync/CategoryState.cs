using System.Collections.Generic;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// One category's persisted sync state for one user: the incremental-pull
/// cursor and the last-pushed known-items map.
/// </summary>
public class CategoryState
{
    /// <summary>
    /// Gets or sets the cursor to resume an incremental pull from -- null
    /// means no pull has ever succeeded, so the next pull must be a full one.
    /// </summary>
    public string? SyncedAt { get; set; }

    /// <summary>
    /// Gets the last-pushed identity + value for every known item, keyed by
    /// canonical id (see <see cref="Library.ItemKeys"/>).
    /// </summary>
    public Dictionary<string, KnownSyncItem> KnownItems { get; } = new();
}
