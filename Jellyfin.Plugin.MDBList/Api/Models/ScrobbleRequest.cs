using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Request body for /scrobble/start, /scrobble/pause, and /scrobble/stop --
/// exactly one of <see cref="Movie"/>/<see cref="Show"/> is set.
/// </summary>
public class ScrobbleRequest
{
    /// <summary>
    /// Gets or sets the movie being scrobbled, if this is a movie.
    /// </summary>
    [JsonPropertyName("movie")]
    public ScrobbleMovieRef? Movie { get; set; }

    /// <summary>
    /// Gets or sets the show/episode being scrobbled, if this is an episode.
    /// </summary>
    [JsonPropertyName("show")]
    public ScrobbleShowRef? Show { get; set; }

    /// <summary>
    /// Gets or sets the playback progress, 0-100.
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the reporting client's version string.
    /// </summary>
    [JsonPropertyName("app_version")]
    public string? AppVersion { get; set; }
}
