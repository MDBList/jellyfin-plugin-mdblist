namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Result of a single poll attempt against MDBList's token endpoint.
/// </summary>
public class PollResult
{
    /// <summary>
    /// Gets or sets the poll status: pending, slow_down, authorized, expired, denied, or error.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Gets or sets a human-readable message, set for the expired/denied/error statuses.
    /// </summary>
    public string? Message { get; set; }
}
