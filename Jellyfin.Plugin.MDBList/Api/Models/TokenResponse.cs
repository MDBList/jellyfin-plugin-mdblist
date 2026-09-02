using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MDBList.Api.Models;

/// <summary>
/// Wire-format response from MDBList's POST /oauth/token/ (device-code poll
/// or refresh-token grant).
/// </summary>
public class TokenResponse
{
    /// <summary>
    /// Gets or sets the access token, present once the device code is authorized.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets how long the access token is valid for, in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; } = 2592000;

    /// <summary>
    /// Gets or sets the error code -- e.g. authorization_pending, slow_down,
    /// expired_token, access_denied -- present while polling before the user
    /// has completed authorization, or on failure.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the human-readable error description.
    /// </summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}
