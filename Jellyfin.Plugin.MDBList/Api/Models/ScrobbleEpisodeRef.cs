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

    /// <summary>
    /// Gets or sets the episode's own provider ids (TVDB/TMDb episode ids), so
    /// MDBList can resolve the exact episode even when the library numbers it
    /// differently (TVDB-ordered anime). Omitted when unknown.
    /// </summary>
    [JsonPropertyName("ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MediaIds? Ids { get; set; }
}
