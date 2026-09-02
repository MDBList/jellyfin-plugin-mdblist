using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// A movie/show's provider ids, in MDBList's wire format -- imdb is a
/// "tt..." string, the rest are numeric ids. Used both to deserialize
/// remote sync entries and to serialize push payloads.
/// </summary>
public class MediaIds
{
    /// <summary>
    /// Gets or sets the IMDb id (e.g. "tt0133093").
    /// </summary>
    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }

    /// <summary>
    /// Gets or sets the TheMovieDb id.
    /// </summary>
    [JsonPropertyName("tmdb")]
    public int? Tmdb { get; set; }

    /// <summary>
    /// Gets or sets the TheTVDB id.
    /// </summary>
    [JsonPropertyName("tvdb")]
    public int? Tvdb { get; set; }

    /// <summary>
    /// Gets or sets the Trakt id.
    /// </summary>
    [JsonPropertyName("trakt")]
    public int? Trakt { get; set; }

    /// <summary>
    /// Gets or sets the MDBList id -- an alphanumeric slug (e.g. "a2na"),
    /// not numeric like the other providers.
    /// </summary>
    [JsonPropertyName("mdblist")]
    public string? Mdblist { get; set; }

    /// <summary>
    /// Gets or sets the Kitsu id (anime, movies only).
    /// </summary>
    [JsonPropertyName("kitsu")]
    public int? Kitsu { get; set; }

    /// <summary>
    /// Gets a value indicating whether this instance carries no ids at all.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => Imdb is null && Tmdb is null && Tvdb is null && Trakt is null && Mdblist is null && Kitsu is null;
}
