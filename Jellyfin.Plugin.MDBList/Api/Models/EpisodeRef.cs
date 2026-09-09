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

    /// <summary>
    /// Gets or sets the episode's own provider ids (currently tmdb/tvdb) --
    /// distinct from <see cref="Show"/>'s ids. Stable across shows that
    /// renumber seasons/episodes differently between metadata providers
    /// (common for anime), so this is tried before falling back to
    /// show id + season/episode number.
    /// </summary>
    [JsonPropertyName("ids")]
    public MediaIds? Ids { get; set; }
}
