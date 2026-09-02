namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Result of a connectivity test against /sync/last_activities.
/// </summary>
public class ConnectionTestResult
{
    /// <summary>
    /// Gets or sets the server's own watermark timestamp, confirming a valid authenticated call.
    /// </summary>
    public string? ServerTime { get; set; }
}
