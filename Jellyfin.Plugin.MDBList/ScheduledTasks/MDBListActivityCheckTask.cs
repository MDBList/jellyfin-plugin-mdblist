using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Sync;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.MDBList.ScheduledTasks;

/// <summary>
/// Cheap, frequent poll: checks /sync/last_activities and only pulls when a
/// relevant bucket actually advanced -- port of sync_orchestrator.py's
/// check_activity(), wired to Jellyfin's own scheduled-task engine instead
/// of a hand-rolled timer thread.
/// </summary>
public class MDBListActivityCheckTask : IScheduledTask
{
    private readonly SyncOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListActivityCheckTask"/> class.
    /// </summary>
    /// <param name="orchestrator">Instance of the <see cref="SyncOrchestrator"/>.</param>
    public MDBListActivityCheckTask(SyncOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public string Name => "MDBList Activity Check";

    /// <inheritdoc />
    public string Key => "MDBListActivityCheck";

    /// <inheritdoc />
    public string Description => "Checks MDBList for new activity and pulls watched-status changes if anything changed.";

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
                IntervalTicks = TimeSpan.FromMinutes(15).Ticks,
            },
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _orchestrator.CheckAllActivityAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
