using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Api;

/// <summary>
/// MDBList OAuth device-code flow -- port of the Kodi addon's oauth.py,
/// same endpoints and grant types, against a Jellyfin-specific client id.
/// </summary>
public class OAuthService : IDisposable
{
    private const string ClientId = "egZHfF8coK5gtS3pbxrkgW4Ngg4BqJUtW811pZGp";
    private const string DeviceAuthUrl = "https://api.mdblist.com/oauth/device-authorization/";
    private const string TokenUrl = "https://api.mdblist.com/oauth/token/";
    private const string RevokeUrl = "https://api.mdblist.com/oauth/revoke_token/";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    // Serializes every read-modify-write of PluginConfiguration.Users -- the
    // dashboard's own "Save" button does a full config replace, so token
    // writes from the auth flow and refreshes must not race it or each other.
    private readonly SemaphoreSlim _configLock = new(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MDBListApiClient _apiClient;
    private readonly ILogger<OAuthService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{OAuthService}"/> interface.</param>
    public OAuthService(IHttpClientFactory httpClientFactory, MDBListApiClient apiClient, ILogger<OAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// Starts a device-authorization flow.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The device/user codes and verification URI.</returns>
    public async Task<DeviceCodeResult> StartDeviceAuthorizationAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = "write",
        });

        using var response = await client.PostAsync(new Uri(DeviceAuthUrl), content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        DeviceAuthorizationResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DeviceAuthorizationResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new MDBListApiException("Malformed device authorization response", ex);
        }

        if (parsed is null || string.IsNullOrEmpty(parsed.DeviceCode))
        {
            var message = parsed?.ErrorDescription ?? parsed?.Error ?? "Unknown error starting device authorization";
            throw new MDBListApiException(message);
        }

        var verificationUri = parsed.VerificationUri ?? "https://mdblist.com/oauth/device/";
        var verificationUriComplete = parsed.VerificationUriComplete
            ?? $"{verificationUri}?user_code={parsed.UserCode}";

        return new DeviceCodeResult
        {
            DeviceCode = parsed.DeviceCode,
            UserCode = parsed.UserCode ?? string.Empty,
            VerificationUri = verificationUri,
            VerificationUriComplete = verificationUriComplete,
            Interval = parsed.Interval,
            ExpiresIn = parsed.ExpiresIn,
        };
    }

    /// <summary>
    /// Makes one poll attempt against the token endpoint. On success, saves
    /// the tokens for <paramref name="jellyfinUserId"/>.
    /// </summary>
    /// <param name="jellyfinUserId">The Jellyfin user to link the tokens to.</param>
    /// <param name="deviceCode">The device code from <see cref="StartDeviceAuthorizationAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The poll status.</returns>
    public async Task<PollResult> PollTokenAsync(Guid jellyfinUserId, string deviceCode, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = DeviceGrantType,
            ["device_code"] = deviceCode,
            ["client_id"] = ClientId,
        });

        TokenResponse? token;
        try
        {
            using var response = await client.PostAsync(new Uri(TokenUrl), content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            token = JsonSerializer.Deserialize<TokenResponse>(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // A single failed poll attempt isn't fatal -- the browser retries on
            // its own interval, same as oauth.py's poll loop swallowing a
            // request exception and continuing rather than aborting the flow.
            _logger.LogDebug(ex, "MDBList token poll attempt failed, will retry");
            return new PollResult { Status = "pending" };
        }

        if (token is null)
        {
            return new PollResult { Status = "pending" };
        }

        if (!string.IsNullOrEmpty(token.AccessToken))
        {
            await SaveTokensAsync(jellyfinUserId, token, cancellationToken).ConfigureAwait(false);
            return new PollResult { Status = "authorized" };
        }

        return token.Error switch
        {
            "slow_down" => new PollResult { Status = "slow_down" },
            "expired_token" => new PollResult { Status = "expired", Message = "Authorization expired. Please try again." },
            "access_denied" => new PollResult { Status = "denied", Message = "Authorization was denied." },
            _ => new PollResult { Status = "pending" },
        };
    }

    /// <summary>
    /// Returns a valid access token for the given user, refreshing it first
    /// if it's within 5 minutes of expiry. Returns an empty string if the
    /// user isn't connected.
    /// </summary>
    /// <param name="jellyfinUserId">The linked Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A valid access token, or an empty string.</returns>
    public async Task<string> EnsureValidTokenAsync(Guid jellyfinUserId, CancellationToken cancellationToken)
    {
        var config = FindUserConfig(jellyfinUserId);
        if (config is null || string.IsNullOrEmpty(config.AccessToken))
        {
            return string.Empty;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (config.ExpiresAt != 0 && now > config.ExpiresAt - 300)
        {
            var refreshed = await TryRefreshAsync(jellyfinUserId, config.RefreshToken, cancellationToken).ConfigureAwait(false);
            if (refreshed)
            {
                config = FindUserConfig(jellyfinUserId);
            }
        }

        return config?.AccessToken ?? string.Empty;
    }

    /// <summary>
    /// Revokes the stored token (best-effort) and clears it for the given user.
    /// </summary>
    /// <param name="jellyfinUserId">The linked Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DisconnectAsync(Guid jellyfinUserId, CancellationToken cancellationToken)
    {
        var config = FindUserConfig(jellyfinUserId);
        if (config is not null && !string.IsNullOrEmpty(config.AccessToken))
        {
            try
            {
                var client = _httpClientFactory.CreateClient(NamedClient.Default);
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = config.AccessToken,
                    ["client_id"] = ClientId,
                });
                using var response = await client.PostAsync(new Uri(RevokeUrl), content, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "MDBList token revoke failed, clearing local tokens anyway");
            }
        }

        await _configLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin not initialized");
            var existing = plugin.Configuration.Users.FirstOrDefault(u => u.JellyfinUserId == jellyfinUserId);
            if (existing is not null)
            {
                plugin.Configuration.Users.Remove(existing);
            }

            plugin.SaveConfiguration();
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// Calls /sync/last_activities to confirm the stored token actually works.
    /// </summary>
    /// <param name="jellyfinUserId">The linked Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server's own watermark timestamp.</returns>
    public async Task<string> TestConnectionAsync(Guid jellyfinUserId, CancellationToken cancellationToken)
    {
        var accessToken = await EnsureValidTokenAsync(jellyfinUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new MDBListApiException("Not connected to MDBList");
        }

        var activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);
        return activities.ServerTime ?? string.Empty;
    }

    private async Task<bool> TryRefreshAsync(Guid jellyfinUserId, string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        });

        try
        {
            using var response = await client.PostAsync(new Uri(TokenUrl), content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var token = JsonSerializer.Deserialize<TokenResponse>(body);

            if (token is not null && !string.IsNullOrEmpty(token.AccessToken))
            {
                token.RefreshToken ??= refreshToken;
                await SaveTokensAsync(jellyfinUserId, token, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogError(ex, "MDBList token refresh failed");
        }

        return false;
    }

    private async Task SaveTokensAsync(Guid jellyfinUserId, TokenResponse token, CancellationToken cancellationToken)
    {
        await _configLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin not initialized");
            var config = plugin.Configuration.Users.FirstOrDefault(u => u.JellyfinUserId == jellyfinUserId);
            if (config is null)
            {
                config = new UserSyncConfig { JellyfinUserId = jellyfinUserId };
                plugin.Configuration.Users.Add(config);
            }

            config.AccessToken = token.AccessToken ?? string.Empty;
            config.RefreshToken = token.RefreshToken ?? config.RefreshToken;
            config.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + token.ExpiresIn;

            plugin.SaveConfiguration();
        }
        finally
        {
            _configLock.Release();
        }
    }

    private static UserSyncConfig? FindUserConfig(Guid jellyfinUserId)
    {
        return Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.JellyfinUserId == jellyfinUserId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _configLock.Dispose();
        }
    }
}
