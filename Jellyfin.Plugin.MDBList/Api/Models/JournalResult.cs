using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// All journal rows across every page of a /sync/journal GET call, merged.
/// </summary>
public class JournalResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the caller's watermark is
    /// outside the 30-day retention window -- callers must fall back to a
    /// full pull instead of trusting <see cref="Entries"/> (which is empty
    /// in that case).
    /// </summary>
    public bool RequiresFullSync { get; set; }

    /// <summary>
    /// Gets the merged journal rows.
    /// </summary>
    public Collection<JournalEntry> Entries { get; } = new();

    /// <summary>
    /// Gets or sets the oldest timestamp still in the retention window.
    /// </summary>
    public string? JournalOldestAt { get; set; }
}
