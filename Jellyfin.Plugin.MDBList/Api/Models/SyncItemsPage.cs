using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Wire format of one page of a /sync/watched or /sync/ratings GET response.
/// </summary>
public class SyncItemsPage
{
    /// <summary>
    /// Gets the movie rows on this page.
    /// </summary>
    [JsonPropertyName("movies")]
    public Collection<SyncMovieEntry> Movies { get; } = new();

    /// <summary>
    /// Gets the episode rows on this page.
    /// </summary>
    [JsonPropertyName("episodes")]
    public Collection<SyncEpisodeEntry> Episodes { get; } = new();

    /// <summary>
    /// Gets or sets the pagination info.
    /// </summary>
    [JsonPropertyName("pagination")]
    public PaginationInfo? Pagination { get; set; }
}
