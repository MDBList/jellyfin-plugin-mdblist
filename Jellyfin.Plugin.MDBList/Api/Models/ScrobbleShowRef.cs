using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The "show" object in a /scrobble/* request body -- the show's own ids
/// plus the season/episode being played.
/// </summary>
public class ScrobbleShowRef
{
    /// <summary>
    /// Gets or sets the show's provider ids.
    /// </summary>
    [JsonPropertyName("ids")]
    public MediaIds Ids { get; set; } = new();

    /// <summary>
    /// Gets or sets the season being played.
    /// </summary>
    [JsonPropertyName("season")]
    public ScrobbleSeasonRef Season { get; set; } = new();
}
