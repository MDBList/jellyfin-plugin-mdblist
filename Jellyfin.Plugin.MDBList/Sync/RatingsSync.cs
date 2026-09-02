using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Ratings two-way sync -- port of ratings_sync.py.
///
/// No local "rated at" timestamp exists in Jellyfin's UserItemData any more
/// than it did in Kodi, so unlike watched status this is not true
/// last-write-wins: push always runs before pull (see SyncOrchestrator), so
/// on the first sync any conflicting item is already resolved local-wins by
/// the time pull happens. After that, pull only ever applies items that
/// changed remotely since the last sync watermark, which keeps the
/// collision window to "rated locally and remotely in between two sync
/// runs" -- acceptable given there's nothing to compare timestamps against.
/// </summary>
public class RatingsSync
{
    private const SyncCategory Category = SyncCategory.Ratings;
    private const string Endpoint = "/sync/ratings";
    private const string RemoveEndpoint = "/sync/ratings/remove";
    private const string FieldName = "rating";
    private const string JournalCategory = "rated";
    private const int JournalPageSize = 1000;

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly SyncStateStore _stateStore;
    private readonly MDBListApiClient _apiClient;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RatingsSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    public RatingsSync(
        SyncPayloadBuilder payloadBuilder,
        SyncStateStore stateStore,
        MDBListApiClient apiClient,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        _payloadBuilder = payloadBuilder;
        _stateStore = stateStore;
        _apiClient = apiClient;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    /// <summary>
    /// Full membership+value diff against the whole snapshot.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="snapshot">The current library snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, CancellationToken cancellationToken)
    {
        var current = CurrentRatedItems(snapshot);

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(accessToken, Endpoint, FieldName, items, GetRatingValue, cancellationToken),
            items => _payloadBuilder.PushItemsRemoveAsync(accessToken, RemoveEndpoint, items, cancellationToken),
            valueChanged: (known, item) => known.Rating != item.Rating,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Immediate push for one item, triggered by a live <c>UserDataSaved</c>
    /// notification.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="record">The single item's current state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What happened: not mappable, no-op, added, or removed.</returns>
    public async Task<PushOutcome> PushSingleAsync(Guid userId, string accessToken, SnapshotItem record, CancellationToken cancellationToken)
    {
        var key = CanonicalKey(record);
        if (key is null)
        {
            return PushOutcome.NotMappable;
        }

        var known = await _stateStore.GetKnownItemsAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        known.TryGetValue(key, out var knownItem);

        var rating = ToApiRating(record.Rating);

        if (rating > 0)
        {
            var item = BuildKnownItem(record, rating);
            if (knownItem is not null && knownItem.Rating == rating)
            {
                return PushOutcome.NoOp;
            }

            await _payloadBuilder.PushItemsAsync(accessToken, Endpoint, FieldName, [item], GetRatingValue, cancellationToken).ConfigureAwait(false);
            await _stateStore.UpdateKnownItemAsync(userId, Category, key, item, cancellationToken).ConfigureAwait(false);
            return PushOutcome.Added;
        }

        if (knownItem is null)
        {
            return PushOutcome.NoOp;
        }

        await _payloadBuilder.PushItemsRemoveAsync(accessToken, RemoveEndpoint, [knownItem], cancellationToken).ConfigureAwait(false);
        await _stateStore.UpdateKnownItemAsync(userId, Category, key, null, cancellationToken).ConfigureAwait(false);
        return PushOutcome.Removed;
    }

    /// <summary>
    /// Pulls remote rating changes into Jellyfin.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="user">The resolved Jellyfin user, for writing user data.</param>
    /// <param name="snapshot">The current library snapshot, to match remote entries against.</param>
    /// <param name="serverTime">/sync/last_activities' own server_time -- see WatchedSync.PullAsync.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were actually changed, and which mode ran.</returns>
    public async Task<PullResult> PullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, CancellationToken cancellationToken)
    {
        var since = await _stateStore.GetSyncedAtAsync(userId, Category, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(since))
        {
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, cancellationToken).ConfigureAwait(false);
        }

        var journal = await _apiClient.FetchJournalAsync(accessToken, since, JournalPageSize, cancellationToken).ConfigureAwait(false);
        if (journal.RequiresFullSync)
        {
            return await PullFullAsync(userId, accessToken, user, snapshot, serverTime, cancellationToken).ConfigureAwait(false);
        }

        return await PullIncrementalAsync(userId, user, journal.Entries, snapshot, serverTime, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PullResult> PullFullAsync(Guid userId, string accessToken, User user, LibrarySnapshot snapshot, string? serverTime, CancellationToken cancellationToken)
    {
        // extended=null (full, not ids_only): MDBList's ids_only ratings
        // response only carries the episode's own tmdb id, not
        // season/episode/show, so it can't be matched the way ids_only
        // works for /sync/watched.
        var data = await _apiClient.FetchSyncItemsAsync(accessToken, Endpoint, mediatype: null, since: null, extended: null, JournalPageSize, cancellationToken)
            .ConfigureAwait(false);

        var applied = 0;

        foreach (var entry in data.Movies)
        {
            var ids = entry.Movie?.Ids;
            if (ids is null || ids.IsEmpty)
            {
                continue;
            }

            if (ApplyRating(user, snapshot.FindMovie(ids), entry.Rating ?? 0))
            {
                applied++;
            }
        }

        foreach (var entry in data.Episodes)
        {
            var showIds = entry.Episode?.Show?.Ids;
            if (showIds is null || showIds.IsEmpty)
            {
                continue;
            }

            var match = snapshot.FindEpisode(showIds, entry.Episode?.Season, entry.Episode?.Number);
            if (ApplyRating(user, match, entry.Rating ?? 0))
            {
                applied++;
            }
        }

        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        return new PullResult { PulledApplied = applied, Mode = "full" };
    }

    private async Task<PullResult> PullIncrementalAsync(
        Guid userId,
        User user,
        IReadOnlyCollection<JournalEntry> entries,
        LibrarySnapshot snapshot,
        string? serverTime,
        CancellationToken cancellationToken)
    {
        var applied = 0;

        foreach (var entry in entries)
        {
            if (entry.Category != JournalCategory || entry.Ids is null)
            {
                continue;
            }

            var rating = entry.Status != "removed" ? entry.Rating ?? 0 : 0;

            if (entry.ItemType == "movie")
            {
                if (ApplyRating(user, snapshot.FindMovie(entry.Ids), rating))
                {
                    applied++;
                }
            }
            else if (entry.ItemType == "episode")
            {
                var match = snapshot.FindEpisode(entry.Ids, entry.Season, entry.Episode);
                if (ApplyRating(user, match, rating))
                {
                    applied++;
                }
            }
        }

        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        return new PullResult { PulledApplied = applied, Mode = "incremental" };
    }

    private bool ApplyRating(User user, SnapshotItem? match, int rating)
    {
        if (match is null || ToApiRating(match.Rating) == rating)
        {
            return false;
        }

        SetRating(user, match.ItemId, rating);
        return true;
    }

    private void SetRating(User user, Guid itemId, int rating)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return;
        }

        var userData = _userDataManager.GetUserData(user, item) ?? new UserItemData { Key = item.GetUserDataKeys().First() };

        // 0 means "unrated" -- must map to null, not 0, which is itself a
        // legal (if unusual) low rating and would read back as one.
        userData.Rating = rating > 0 ? rating : null;

        _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
    }

    private static int ToApiRating(double? rating)
    {
        return rating.HasValue ? (int)Math.Round(rating.Value, MidpointRounding.AwayFromZero) : 0;
    }

    private static string? CanonicalKey(SnapshotItem record)
    {
        return record.Type == "movie"
            ? ItemKeys.CanonicalMovieKey(record.Ids)
            : ItemKeys.CanonicalEpisodeKey(record.Ids, record.Season, record.EpisodeNumber);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record, int rating)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            Rating = rating,
        };
    }

    private static JsonNode? GetRatingValue(KnownSyncItem item)
    {
        return item.Rating.HasValue ? JsonValue.Create(item.Rating.Value) : null;
    }

    private static Dictionary<string, KnownSyncItem> CurrentRatedItems(LibrarySnapshot snapshot)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        foreach (var movie in snapshot.Movies)
        {
            var rating = ToApiRating(movie.Rating);
            if (rating <= 0)
            {
                continue;
            }

            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                items[key] = BuildKnownItem(movie, rating);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            var rating = ToApiRating(episode.Rating);
            if (rating <= 0)
            {
                continue;
            }

            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                items[key] = BuildKnownItem(episode, rating);
            }
        }

        return items;
    }

    private static string NowIso()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
