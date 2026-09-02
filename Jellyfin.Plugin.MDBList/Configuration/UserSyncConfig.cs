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

    /// <summary>
    /// Gets or sets a value indicating whether Jellyfin's thumbs-up/down
    /// (<c>UserDataSaveReason.UpdateUserRating</c>, from the stock web UI's
    /// like/dislike button) should be ignored rather than pushed as a 10 or
    /// 1 rating. That button always writes exactly 10 or 1 regardless of
    /// intent, which would otherwise silently overwrite (or create) a real
    /// numeric MDBList rating. Genuine numeric ratings (e.g. from clients
    /// like Infuse) go through UpdateUserData instead and are unaffected.
    /// Defaults to on.
    /// </summary>
    public bool IgnoreThumbRatings { get; set; } = true;
}
