using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Events;

/// <summary>
/// Debounces <c>ItemAdded</c>/<c>ItemUpdated</c>/<c>ItemRemoved</c> into a
/// single sync run -- port of the same idea as Trakt's
/// LibraryManagerEventsHelper. A library scan emits one event per item;
/// without collapsing them, a 500-item scan would trigger 500 separate
/// syncs instead of one. Each new event just resets a single timer rather
/// than tracking which items changed -- a full push-and-reconcile after the
/// quiet period is cheap (one local snapshot query) and already correctly
/// handles adds and removes in one pass, so there's no need to build a
/// per-item payload here the way the live watched/ratings push does.
/// </summary>
public sealed class LibraryChangeDebouncer : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(10);

    private readonly SyncOrchestrator _orchestrator;
    private readonly ILogger<LibraryChangeDebouncer> _logger;
    private readonly Timer _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeDebouncer"/> class.
    /// </summary>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LibraryChangeDebouncer}"/> interface.</param>
    public LibraryChangeDebouncer(SyncOrchestrator orchestrator, ILogger<LibraryChangeDebouncer> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _timer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Records a library change, (re)starting the debounce window.
    /// </summary>
    /// <param name="item">The added/updated/removed item.</param>
    public void NotifyChange(BaseItem item)
    {
        if (item is not (Movie or Episode))
        {
            return;
        }

        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault();
        if (linkedUserConfig is null || !linkedUserConfig.SyncAfterLibraryScan)
        {
            return;
        }

        _timer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Dispose();
    }

    private void OnTimerElapsed(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.RunAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MDBList: library-change-triggered sync failed");
            }
        });
    }
}
