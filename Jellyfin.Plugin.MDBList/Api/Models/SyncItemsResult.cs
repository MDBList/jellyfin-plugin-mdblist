using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// All movie/episode rows across every page of a /sync/watched or
/// /sync/ratings GET call, merged.
/// </summary>
public class SyncItemsResult
{
    /// <summary>
    /// Gets the merged movie rows.
    /// </summary>
    public Collection<SyncMovieEntry> Movies { get; } = new();

    /// <summary>
    /// Gets the merged episode rows.
    /// </summary>
    public Collection<SyncEpisodeEntry> Episodes { get; } = new();
}
