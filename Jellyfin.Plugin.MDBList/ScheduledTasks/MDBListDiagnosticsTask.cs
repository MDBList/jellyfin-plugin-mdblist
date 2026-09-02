using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Library;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.ScheduledTasks;

/// <summary>
/// Phase-3 diagnostics: builds a library snapshot and calls
/// /sync/last_activities, logging counts and coverage. Writes nothing to
/// Jellyfin or MDBList. No default trigger -- run manually from the
/// dashboard's Scheduled Tasks page. Temporary: removed once the real sync
/// tasks (Phase 5/8) exist and can report this through the config page
/// instead.
/// </summary>
public class MDBListDiagnosticsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly OAuthService _oauthService;
    private readonly MDBListApiClient _apiClient;
    private readonly ILogger<MDBListDiagnosticsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListDiagnosticsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="oauthService">Instance of the <see cref="OAuthService"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{MDBListDiagnosticsTask}"/> interface.</param>
    public MDBListDiagnosticsTask(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        OAuthService oauthService,
        MDBListApiClient apiClient,
        ILogger<MDBListDiagnosticsTask> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _oauthService = oauthService;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "MDBList Diagnostics";

    /// <inheritdoc />
    public string Key => "MDBListDiagnostics";

    /// <inheritdoc />
    public string Description => "Logs library snapshot counts, id coverage, and MDBList connectivity. Writes nothing.";

    /// <inheritdoc />
    public string Category => "MDBList";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault();
        if (linkedUserConfig is null)
        {
            _logger.LogInformation("MDBList Diagnostics: no linked user -- connect via the plugin's config page first");
            return;
        }

        var user = _userManager.GetUserById(linkedUserConfig.JellyfinUserId);
        if (user is null)
        {
            _logger.LogWarning("MDBList Diagnostics: linked Jellyfin user {UserId} no longer exists", linkedUserConfig.JellyfinUserId);
            return;
        }

        LogSnapshot(user);
        progress.Report(50);

        await LogLastActivitiesAsync(user, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    private void LogSnapshot(User user)
    {
        var snapshot = LibrarySnapshot.Build(_libraryManager, _userDataManager, user);

        _logger.LogInformation(
            "MDBList Diagnostics: {MovieCount} movies ({UnmappableMovies} unmappable), {EpisodeCount} episodes ({UnmappableEpisodes} unmappable)",
            snapshot.Movies.Count,
            snapshot.UnmappableMovieCount,
            snapshot.Episodes.Count,
            snapshot.UnmappableEpisodeCount);

        // Confirms whether LastPlayedDate/DateCreated are UTC-normalized --
        // needed before Phase 4/5 can safely compare them against MDBList's
        // own UTC timestamps without guessing.
        var sampleMovie = snapshot.Movies.FirstOrDefault(m => m.LastPlayedDate.HasValue);
        if (sampleMovie is not null)
        {
            _logger.LogInformation(
                "MDBList Diagnostics: sample movie '{Title}' LastPlayedDate={LastPlayedDate:o} Kind={Kind} DateCreated={DateCreated:o} Kind={CreatedKind}",
                sampleMovie.Title,
                sampleMovie.LastPlayedDate,
                sampleMovie.LastPlayedDate!.Value.Kind,
                sampleMovie.DateCreated,
                sampleMovie.DateCreated.Kind);
        }
    }

    private async Task LogLastActivitiesAsync(User user, CancellationToken cancellationToken)
    {
        var accessToken = await _oauthService.EnsureValidTokenAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogInformation("MDBList Diagnostics: not connected to MDBList");
            return;
        }

        var activities = await _apiClient.FetchLastActivitiesAsync(accessToken, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "MDBList Diagnostics: last_activities server_time={ServerTime} watched_at={WatchedAt} rated_at={RatedAt} journal_at={JournalAt}",
            activities.ServerTime,
            activities.WatchedAt,
            activities.RatedAt,
            activities.JournalAt);
    }
}
