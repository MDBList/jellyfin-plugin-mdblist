using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The "movie" object nested in a /sync/watched or /sync/ratings entry.
/// </summary>
public class MovieRef
{
    /// <summary>
    /// Gets or sets the movie's provider ids.
    /// </summary>
    [JsonPropertyName("ids")]
    public MediaIds? Ids { get; set; }
}
