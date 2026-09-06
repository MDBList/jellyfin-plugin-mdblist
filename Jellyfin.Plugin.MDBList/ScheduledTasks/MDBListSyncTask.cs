using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Sync;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.MDBList.ScheduledTasks;

/// <summary>
/// Full push-and-pull reconciliation for every enabled category -- the
/// periodic backstop that covers anything the live listener and the cheap
/// activity poll missed (e.g. while Jellyfin was down). The config page's
/// "Sync now" button calls the same <see cref="SyncOrchestrator"/> operation
/// with the same trust level; a post-library-scan trigger calls it too but
/// with removals disabled -- see <see cref="SyncOrchestrator.RunAsync"/>.
/// Port of sync_orchestrator.py's run(), wired to Jellyfin's own
/// scheduled-task engine instead of a hand-rolled timer.
/// </summary>
public class MDBListSyncTask : IScheduledTask
{
    private readonly SyncOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListSyncTask"/> class.
    /// </summary>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    public MDBListSyncTask(SyncOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public string Name => "MDBList Sync";

    /// <inheritdoc />
    public string Key => "MDBListSync";

    /// <inheritdoc />
    public string Description => "Full push-and-pull reconciliation of watched status, ratings, and collection with MDBList.";

    /// <inheritdoc />
    public string Category => "MDBList";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks,
            },
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // allowRemovals: true -- the deliberate 24h reconciliation backstop,
        // trusted to actually remove things from MDBList (still subject to
        // SyncPayloadBuilder's magnitude circuit-breaker).
        await _orchestrator.RunAllLinkedUsersAsync(allowRemovals: true, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
