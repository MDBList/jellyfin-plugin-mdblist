using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// One episode row from /sync/watched or /sync/ratings (full mode).
/// </summary>
public class SyncEpisodeEntry
{
    /// <summary>
    /// Gets or sets the episode reference.
    /// </summary>
    [JsonPropertyName("episode")]
    public EpisodeRef? Episode { get; set; }

    /// <summary>
    /// Gets or sets when this episode was last watched (present for /sync/watched).
    /// </summary>
    [JsonPropertyName("last_watched_at")]
    public string? LastWatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the rating (present for /sync/ratings).
    /// </summary>
    [JsonPropertyName("rating")]
    public int? Rating { get; set; }
}
