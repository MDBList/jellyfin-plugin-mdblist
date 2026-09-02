using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The nested "episode" object in a /scrobble/* season reference.
/// </summary>
public class ScrobbleEpisodeRef
{
    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }
}
