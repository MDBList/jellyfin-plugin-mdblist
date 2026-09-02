using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The nested "season" object in a /scrobble/* show reference.
/// </summary>
public class ScrobbleSeasonRef
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>
    /// Gets or sets the episode being played within this season.
    /// </summary>
    [JsonPropertyName("episode")]
    public ScrobbleEpisodeRef Episode { get; set; } = new();
}
