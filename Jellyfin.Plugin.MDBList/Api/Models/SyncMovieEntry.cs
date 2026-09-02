using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// One movie row from /sync/watched or /sync/ratings (full mode). Only the
/// field relevant to the category being fetched is populated by the server;
/// the other is left null.
/// </summary>
public class SyncMovieEntry
{
    /// <summary>
    /// Gets or sets the movie reference.
    /// </summary>
    [JsonPropertyName("movie")]
    public MovieRef? Movie { get; set; }

    /// <summary>
    /// Gets or sets when this movie was last watched (present for /sync/watched).
    /// </summary>
    [JsonPropertyName("last_watched_at")]
    public string? LastWatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the rating (present for /sync/ratings).
    /// </summary>
    [JsonPropertyName("rating")]
    public int? Rating { get; set; }
}
