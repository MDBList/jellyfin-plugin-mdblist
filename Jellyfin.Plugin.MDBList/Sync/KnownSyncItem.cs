using Jellyfin.Plugin.MDBList.Api.Models;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// The last-pushed identity + value for one item in a category's
/// known-items bucket -- port of the item dicts in sync_state.py. Storing
/// the full identity (not just a lookup key) means a removal payload can
/// still be built even after the item has left the Jellyfin library
/// snapshot entirely (e.g. the file was deleted, not just marked unwatched).
/// </summary>
public class KnownSyncItem
{
    /// <summary>
    /// Gets or sets the item type: "movie" or "episode".
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the movie's own ids, or the parent show's ids for an episode.
    /// </summary>
    public MediaIds Ids { get; set; } = new();

    /// <summary>
    /// Gets or sets the season number (episodes only).
    /// </summary>
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number (episodes only).
    /// </summary>
    public int? Episode { get; set; }

    /// <summary>
    /// Gets or sets when this item was last watched (watched category only).
    /// </summary>
    public string? WatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the rating (ratings category only).
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    /// Gets or sets when this item was collected (collection category only).
    /// </summary>
    public string? CollectedAt { get; set; }
}
