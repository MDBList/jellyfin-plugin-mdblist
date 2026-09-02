using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MDBList.Events;

/// <summary>
/// Subscribes to Jellyfin's library/user-data events for the lifetime of
/// the server -- port of main_monitor.py's event wiring. Delegates actual
/// handling to <see cref="LiveSyncService"/> (watched/ratings) and
/// <see cref="LibraryChangeDebouncer"/> (collection membership).
/// </summary>
public class MDBListEventHostedService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly LiveSyncService _liveSyncService;
    private readonly LibraryChangeDebouncer _libraryChangeQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListEventHostedService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="liveSyncService">Instance of the <see cref="LiveSyncService"/>.</param>
    /// <param name="libraryChangeQueue">Instance of the <see cref="LibraryChangeDebouncer"/>.</param>
    public MDBListEventHostedService(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        LiveSyncService liveSyncService,
        LibraryChangeDebouncer libraryChangeQueue)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _liveSyncService = liveSyncService;
        _libraryChangeQueue = libraryChangeQueue;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        _libraryManager.ItemRemoved += OnLibraryItemChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
        _libraryManager.ItemRemoved -= OnLibraryItemChanged;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        _liveSyncService.HandleUserDataSaved(e);
    }

    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        _libraryChangeQueue.NotifyChange(e.Item);
    }
}
