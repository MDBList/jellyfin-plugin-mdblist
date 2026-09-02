namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Response to the config page after starting a device-authorization flow.
/// </summary>
public class DeviceCodeResult
{
    /// <summary>
    /// Gets or sets the device code to pass back on each poll.
    /// </summary>
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the short code the user enters at the verification URI.
    /// </summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the verification URI the user visits.
    /// </summary>
    public string VerificationUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the verification URI with the user code pre-filled.
    /// </summary>
    public string VerificationUriComplete { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum seconds to wait between poll attempts.
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Gets or sets how long the device code is valid for, in seconds.
    /// </summary>
    public int ExpiresIn { get; set; }
}
