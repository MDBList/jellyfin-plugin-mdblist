using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.MDBList.Events;

/// <summary>
/// Subscribes to Jellyfin's library/user-data/session events for the
/// lifetime of the server -- port of main_monitor.py's event wiring.
/// Delegates actual handling to <see cref="LiveSyncService"/>
/// (watched/ratings), <see cref="LibraryChangeDebouncer"/> (collection
/// membership), and <see cref="PlaybackScrobbleService"/> (live scrobbling).
/// </summary>
public class MDBListEventHostedService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ISessionManager _sessionManager;
    private readonly LiveSyncService _liveSyncService;
    private readonly LibraryChangeDebouncer _libraryChangeQueue;
    private readonly PlaybackScrobbleService _playbackScrobbleService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListEventHostedService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="liveSyncService">Instance of the <see cref="LiveSyncService"/>.</param>
    /// <param name="libraryChangeQueue">Instance of the <see cref="LibraryChangeDebouncer"/>.</param>
    /// <param name="playbackScrobbleService">Instance of the <see cref="PlaybackScrobbleService"/>.</param>
    public MDBListEventHostedService(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        ISessionManager sessionManager,
        LiveSyncService liveSyncService,
        LibraryChangeDebouncer libraryChangeQueue,
        PlaybackScrobbleService playbackScrobbleService)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _sessionManager = sessionManager;
        _liveSyncService = liveSyncService;
        _libraryChangeQueue = libraryChangeQueue;
        _playbackScrobbleService = playbackScrobbleService;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        _libraryManager.ItemRemoved += OnLibraryItemChanged;
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
        _libraryManager.ItemRemoved -= OnLibraryItemChanged;
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
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

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        _playbackScrobbleService.HandlePlaybackStart(e);
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        _playbackScrobbleService.HandlePlaybackProgress(e);
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        _playbackScrobbleService.HandlePlaybackStopped(e);
    }
}
