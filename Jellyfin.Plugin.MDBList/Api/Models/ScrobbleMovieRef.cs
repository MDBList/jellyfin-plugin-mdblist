using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The "movie" object in a /scrobble/* request body.
/// </summary>
public class ScrobbleMovieRef
{
    /// <summary>
    /// Gets or sets the movie's provider ids.
    /// </summary>
    [JsonPropertyName("ids")]
    public MediaIds Ids { get; set; } = new();
}
