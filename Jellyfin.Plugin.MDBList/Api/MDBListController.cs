using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MDBList.Api;

/// <summary>
/// Endpoints backing the plugin's config page: device-code OAuth flow, a
/// connectivity test, manual "Sync now", and the last-run status. All
/// actions require an elevated (admin) caller, same as the dashboard itself.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("MDBList")]
[Produces(MediaTypeNames.Application.Json)]
public class MDBListController : ControllerBase
{
    private readonly OAuthService _oauthService;
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncStateStore _stateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListController"/> class.
    /// </summary>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    public MDBListController(OAuthService oauthService, SyncOrchestrator orchestrator, SyncStateStore stateStore)
    {
        _oauthService = oauthService;
        _orchestrator = orchestrator;
        _stateStore = stateStore;
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

    /// <summary>
    /// Triggers a full sync run immediately -- the config page's "Sync now"
    /// button. Trivial here: unlike Kodi's addon-process-vs-service split,
    /// this runs in the same process as everything else, so there's no
    /// cross-process signaling to do -- just call the orchestrator directly.
    /// </summary>
    /// <param name="userId">The linked Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting status, including the run's summary.</returns>
    [HttpPost("Users/{userId}/Sync")]
    public async Task<ActionResult<SyncStatusResult>> Sync(Guid userId, CancellationToken cancellationToken)
    {
        await _orchestrator.RunAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(await BuildStatusAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Gets the linked/connected state and the most recent sync run's summary.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current status.</returns>
    [HttpGet("Users/{userId}/Status")]
    public async Task<ActionResult<SyncStatusResult>> Status(Guid userId, CancellationToken cancellationToken)
    {
        return Ok(await BuildStatusAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    private async Task<SyncStatusResult> BuildStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault(u => u.JellyfinUserId == userId);
        var summary = await _stateStore.GetLastRunSummaryAsync(userId, cancellationToken).ConfigureAwait(false);
        return new SyncStatusResult
        {
            Connected = !string.IsNullOrEmpty(linkedUserConfig?.AccessToken),
            LastRunSummary = summary,
        };
    }
}
