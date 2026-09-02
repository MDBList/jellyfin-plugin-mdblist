using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The "episode" object nested in a /sync/watched or /sync/ratings entry.
/// </summary>
public class EpisodeRef
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>
    /// Gets or sets the parent show reference.
    /// </summary>
    [JsonPropertyName("show")]
    public ShowRef? Show { get; set; }
}
