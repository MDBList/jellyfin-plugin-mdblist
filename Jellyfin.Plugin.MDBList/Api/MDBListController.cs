using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Api.Models;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MDBList.Api;

/// <summary>
/// Endpoints backing the plugin's config page: device-code OAuth flow and a
/// connectivity test. All actions require an elevated (admin) caller, same
/// as the dashboard itself.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("MDBList")]
[Produces(MediaTypeNames.Application.Json)]
public class MDBListController : ControllerBase
{
    private readonly OAuthService _oauthService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListController"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    public MDBListController(OAuthService oauthService)
    {
        _oauthService = oauthService;
    }

    /// <summary>
    /// Starts a device-authorization flow.
    /// </summary>
    /// <param name="userId">The Jellyfin user to link.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The device/user codes and verification URI.</returns>
    [HttpPost("Users/{userId}/DeviceCode")]
    public async Task<ActionResult<DeviceCodeResult>> StartDeviceCode(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _oauthService.StartDeviceAuthorizationAsync(cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (MDBListApiException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Makes one poll attempt against MDBList's token endpoint.
    /// </summary>
    /// <param name="userId">The Jellyfin user to link on success.</param>
    /// <param name="request">The device code to poll for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The poll status.</returns>
    [HttpPost("Users/{userId}/Poll")]
    public async Task<ActionResult<PollResult>> Poll(Guid userId, [FromBody] PollRequest request, CancellationToken cancellationToken)
    {
        var result = await _oauthService.PollTokenAsync(userId, request.DeviceCode, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Revokes and clears the stored token for a user.
    /// </summary>
    /// <param name="userId">The Jellyfin user to disconnect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("Users/{userId}/Disconnect")]
    public async Task<ActionResult> Disconnect(Guid userId, CancellationToken cancellationToken)
    {
        await _oauthService.DisconnectAsync(userId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Tests connectivity by calling /sync/last_activities.
    /// </summary>
    /// <param name="userId">The linked Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server's own watermark timestamp.</returns>
    [HttpPost("Users/{userId}/Test")]
    public async Task<ActionResult<ConnectionTestResult>> Test(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var serverTime = await _oauthService.TestConnectionAsync(userId, cancellationToken).ConfigureAwait(false);
            return Ok(new ConnectionTestResult { ServerTime = serverTime });
        }
        catch (MDBListApiException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
