namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// The config page's status panel: whether a user is linked and connected,
/// and a summary of the most recent sync run.
/// </summary>
public class SyncStatusResult
{
    /// <summary>
    /// Gets or sets a value indicating whether this user is linked and holds
    /// a stored access token.
    /// </summary>
    public bool Connected { get; set; }

    /// <summary>
    /// Gets or sets a human-readable summary of the most recent sync run, or
    /// null if none has completed yet.
    /// </summary>
    public string? LastRunSummary { get; set; }
}
