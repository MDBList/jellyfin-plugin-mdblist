using System;

namespace Jellyfin.Plugin.MDBList.Configuration;

/// <summary>
/// One Jellyfin user's link to an MDBList account. V1 supports exactly one
/// entry, but every sync method takes the linked user id from day one so a
/// later multi-user version is additive rather than a state migration.
/// </summary>
public class UserSyncConfig
{
    /// <summary>
    /// Gets or sets the linked Jellyfin user's id.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the MDBList OAuth access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MDBList OAuth refresh token.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the access token's expiry, as Unix seconds.
    /// </summary>
    public long ExpiresAt { get; set; }
}
