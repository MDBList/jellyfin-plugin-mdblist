using System;
using System.Threading;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Single-flight coordination for sync work -- port of sync_orchestrator.py.
///
/// Kodi needed a *file* lock because "Sync now" ran in a separate OS
/// process (RunScript) from the service, so a plain in-process lock there
/// couldn't see it. Jellyfin plugins are one process/one DI container, so
/// this collapses to a single <see cref="SemaphoreSlim"/> -- a live push
/// and a future scheduled pull both go through <see cref="TryLock"/> so
/// neither can race the other's writes to the same item.
///
/// Only holds the concurrency primitive for now; the periodic full-run and
/// activity-check methods land here in a later phase once a pull exists to
/// coordinate against.
/// </summary>
public sealed class SyncOrchestrator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Attempts to acquire the sync lock without blocking.
    /// </summary>
    /// <returns>
    /// A disposable that releases the lock when acquired; null if a sync is
    /// already in progress. Dispose the non-null result when done -- it
    /// holds the lock for the caller's whole operation, not just the check.
    /// </returns>
    public IDisposable? TryLock()
    {
        return _gate.Wait(0) ? new Releaser(_gate) : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
        }
    }
}
