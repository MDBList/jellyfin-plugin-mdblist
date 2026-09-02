using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Response from GET /sync/last_activities -- per-bucket watermark
/// timestamps, cheap to fetch, used to decide which sync categories have
/// something new before paying for a real pull.
/// </summary>
public class LastActivities
{
    /// <summary>
    /// Gets or sets the watchlist bucket timestamp.
    /// </summary>
    [JsonPropertyName("watchlisted_at")]
    public string? WatchlistedAt { get; set; }

    /// <summary>
    /// Gets or sets the movie-watched bucket timestamp.
    /// </summary>
    [JsonPropertyName("watched_at")]
    public string? WatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the season-watched bucket timestamp.
    /// </summary>
    [JsonPropertyName("season_watched_at")]
    public string? SeasonWatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the episode-watched bucket timestamp.
    /// </summary>
    [JsonPropertyName("episode_watched_at")]
    public string? EpisodeWatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the ratings bucket timestamp.
    /// </summary>
    [JsonPropertyName("rated_at")]
    public string? RatedAt { get; set; }

    /// <summary>
    /// Gets or sets the journal bucket timestamp -- the only bucket that
    /// advances on a removal (unwatch/unrate), covering both categories.
    /// </summary>
    [JsonPropertyName("journal_at")]
    public string? JournalAt { get; set; }

    /// <summary>
    /// Gets or sets the collection bucket timestamp.
    /// </summary>
    [JsonPropertyName("collected_at")]
    public string? CollectedAt { get; set; }

    /// <summary>
    /// Gets or sets the collection-removal bucket timestamp.
    /// </summary>
    [JsonPropertyName("dropped_at")]
    public string? DroppedAt { get; set; }

    /// <summary>
    /// Gets or sets the server's own watermark -- persist this as the next
    /// sync cursor instead of the device's own clock, which can drift.
    /// </summary>
    [JsonPropertyName("server_time")]
    public string? ServerTime { get; set; }
}
