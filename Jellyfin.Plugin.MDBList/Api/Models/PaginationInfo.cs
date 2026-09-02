using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Pagination info on a /sync/* response.
/// </summary>
public class PaginationInfo
{
    /// <summary>
    /// Gets or sets the cursor for the next page, if any.
    /// </summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}
