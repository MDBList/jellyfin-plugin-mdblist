using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MDBList.Events;

/// <summary>
/// Subscribes to Jellyfin's library/user-data events for the lifetime of
/// the server -- port of main_monitor.py's event wiring. Delegates actual
/// handling to <see cref="LiveSyncService"/>.
/// </summary>
public class MDBListEventHostedService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly LiveSyncService _liveSyncService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListEventHostedService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="liveSyncService">Instance of the <see cref="LiveSyncService"/>.</param>
    public MDBListEventHostedService(IUserDataManager userDataManager, LiveSyncService liveSyncService)
    {
        _userDataManager = userDataManager;
        _liveSyncService = liveSyncService;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        _liveSyncService.HandleUserDataSaved(e);
    }
}
