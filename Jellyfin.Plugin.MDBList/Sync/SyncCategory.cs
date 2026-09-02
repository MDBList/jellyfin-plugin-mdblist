namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// The sync categories, each with its own known-items bucket and cursor in
/// <see cref="SyncStateStore"/>.
/// </summary>
public enum SyncCategory
{
    /// <summary>
    /// Watched status (two-way).
    /// </summary>
    Watched,

    /// <summary>
    /// Ratings (two-way).
    /// </summary>
    Ratings,

    /// <summary>
    /// Collection/library membership (push-only).
    /// </summary>
    Collection,
}
