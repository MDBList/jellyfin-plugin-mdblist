using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Wire-format response from MDBList's POST /oauth/device-authorization/.
/// </summary>
public class DeviceAuthorizationResponse
{
    /// <summary>
    /// Gets or sets the device code -- passed back on each token poll.
    /// </summary>
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }

    /// <summary>
    /// Gets or sets the short code the user enters at the verification URI.
    /// </summary>
    [JsonPropertyName("user_code")]
    public string? UserCode { get; set; }

    /// <summary>
    /// Gets or sets the verification URI the user visits to enter the code.
    /// </summary>
    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; set; }

    /// <summary>
    /// Gets or sets the verification URI with the user code pre-filled, if provided.
    /// </summary>
    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; set; }

    /// <summary>
    /// Gets or sets the minimum seconds to wait between poll attempts.
    /// </summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;

    /// <summary>
    /// Gets or sets how long the device code is valid for, in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; } = 300;

    /// <summary>
    /// Gets or sets the error code, if the request failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the human-readable error description, if the request failed.
    /// </summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}
