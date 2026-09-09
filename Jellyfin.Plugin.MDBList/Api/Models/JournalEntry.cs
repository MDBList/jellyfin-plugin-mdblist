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
    /// Gets or sets the status: "added" (or similar non-removed value) or
    /// "removed". Only "removed" is checked explicitly; anything else is
    /// treated as an add/update.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets when this journal row was written, per the server's
    /// clock -- NOT the same as <see cref="ValueAt"/> (the actual watched/
    /// rated timestamp), which is what conflict resolution and the value
    /// written locally must use instead.
    /// </summary>
    [JsonPropertyName("action_at")]
    public string? ActionAt { get; set; }

    /// <summary>
    /// Gets or sets the actual value's own timestamp (e.g. when the item
    /// was watched/rated), always present regardless of category.
    /// </summary>
    [JsonPropertyName("value_at")]
    public string? ValueAt { get; set; }

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
    /// Gets or sets the episode's own tmdb id (episode rows only) -- flat
    /// fields here rather than nested under <see cref="Ids"/>, unlike the
    /// /sync/watched full-pull shape. Distinct from the show's tmdb id in
    /// <see cref="Ids"/>; tried first since it's stable across metadata
    /// providers renumbering seasons/episodes differently (common for anime).
    /// </summary>
    [JsonPropertyName("episode_tmdb_id")]
    public int? EpisodeTmdbId { get; set; }

    /// <summary>
    /// Gets or sets the episode's own tvdb id (episode rows only). See
    /// <see cref="EpisodeTmdbId"/>.
    /// </summary>
    [JsonPropertyName("episode_tvdb_id")]
    public int? EpisodeTvdbId { get; set; }

    /// <summary>
    /// Gets the episode's own ids as a <see cref="MediaIds"/> (episode rows
    /// only), combining <see cref="EpisodeTmdbId"/>/<see cref="EpisodeTvdbId"/>
    /// -- null if the row carries neither.
    /// </summary>
    [JsonIgnore]
    public MediaIds? EpisodeIds => EpisodeTmdbId is null && EpisodeTvdbId is null
        ? null
        : new MediaIds { Tmdb = EpisodeTmdbId, Tvdb = EpisodeTvdbId };

    /// <summary>
    /// Gets or sets the rating (rated-category rows only).
    /// </summary>
    [JsonPropertyName("rating")]
    public int? Rating { get; set; }
}
