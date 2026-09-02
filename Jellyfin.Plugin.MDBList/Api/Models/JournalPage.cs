using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Wire format of one page of a /sync/journal GET response.
/// </summary>
public class JournalPage
{
    /// <summary>
    /// Gets or sets a value indicating whether the caller's watermark is
    /// outside the retention window and a full pull is required instead.
    /// </summary>
    [JsonPropertyName("requires_full_sync")]
    public bool RequiresFullSync { get; set; }

    /// <summary>
    /// Gets the journal rows on this page.
    /// </summary>
    [JsonPropertyName("journal")]
    public Collection<JournalEntry> Journal { get; } = new();

    /// <summary>
    /// Gets or sets the oldest timestamp still in the retention window.
    /// </summary>
    [JsonPropertyName("journal_oldest_at")]
    public string? JournalOldestAt { get; set; }

    /// <summary>
    /// Gets or sets the pagination info.
    /// </summary>
    [JsonPropertyName("pagination")]
    public PaginationInfo? Pagination { get; set; }
}
