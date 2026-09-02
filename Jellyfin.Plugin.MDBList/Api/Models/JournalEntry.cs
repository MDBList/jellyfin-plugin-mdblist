using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// One row from /sync/journal -- an incremental diff entry (add/remove) for
/// watched status, ratings, or collection membership.
/// </summary>
public class JournalEntry
{
    /// <summary>
    /// Gets or sets the category: "watched", "rated", "collected", etc.
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the item type: "movie" or "episode".
    /// </summary>
    [JsonPropertyName("item_type")]
    public string? ItemType { get; set; }

    /// <summary>
    /// Gets or sets the status: "active" or "removed".
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets when this action happened, per the server's clock.
    /// </summary>
    [JsonPropertyName("action_at")]
    public string? ActionAt { get; set; }

    /// <summary>
    /// Gets or sets the item's provider ids -- the movie's own ids, or the
    /// parent show's ids for an episode row.
    /// </summary>
    [JsonPropertyName("ids")]
    public MediaIds? Ids { get; set; }

    /// <summary>
    /// Gets or sets the season number (episode rows only).
    /// </summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number (episode rows only).
    /// </summary>
    [JsonPropertyName("episode")]
    public int? Episode { get; set; }

    /// <summary>
    /// Gets or sets the rating (rated-category rows only).
    /// </summary>
    [JsonPropertyName("rating")]
    public int? Rating { get; set; }
}
