namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Request body for a single poll attempt.
/// </summary>
public class PollRequest
{
    /// <summary>
    /// Gets or sets the device code returned by the device-authorization call.
    /// </summary>
    public string DeviceCode { get; set; } = string.Empty;
}
