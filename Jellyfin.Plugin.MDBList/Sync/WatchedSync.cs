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
/// Watched-status two-way sync -- port of watched_sync.py.
///
/// Push: membership diff only. A rewatch that updates LastPlayedDate
/// without changing Played is covered by a live single-item push from
/// <see cref="PushSingleAsync"/>, not the full diff.
///
/// Pull: real last-write-wins conflict resolution using UTC timestamps on
/// both sides (Jellyfin's LastPlayedDate is already Kind=Utc -- confirmed
/// in Phase 3 -- so unlike the Kodi addon, no naive-local-time conversion is
/// needed here). Remote wins an exact tie in both directions.
/// </summary>
public class WatchedSync
{
    private const SyncCategory Category = SyncCategory.Watched;
    private const string Endpoint = "/sync/watched";
    private const string RemoveEndpoint = "/sync/watched/remove";
    private const string FieldName = "watched_at";
    private const string JournalCategory = "watched";
    private const int JournalPageSize = 1000;

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly SyncStateStore _stateStore;
    private readonly MDBListApiClient _apiClient;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    public WatchedSync(
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
    /// Full membership diff against the whole snapshot.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="snapshot">The current library snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, CancellationToken cancellationToken)
    {
        var current = CurrentWatchedItems(snapshot);

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(accessToken, Endpoint, FieldName, items, GetWatchedAtValue, cancellationToken),
            items => _payloadBuilder.PushItemsRemoveAsync(accessToken, RemoveEndpoint, items, cancellationToken),
            valueChanged: HasWatchedAtChanged,
            cancellationToken).ConfigureAwait(false);
    }

    internal static bool HasWatchedAtChanged(KnownSyncItem known, KnownSyncItem item) => known.WatchedAt != item.WatchedAt;

    /// <summary>
    /// Immediate push for one item, triggered by a live <c>UserDataSaved</c>
    /// notification (Jellyfin's native watched toggle, not just our own
    /// pull-applied writes -- those are filtered out by the caller before
    /// this is reached).
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

        if (record.Played)
        {
            var item = BuildKnownItem(record);
            if (knownItem is not null && knownItem.WatchedAt == item.WatchedAt)
            {
                return PushOutcome.NoOp;
            }

            await _payloadBuilder.PushItemsAsync(accessToken, Endpoint, FieldName, [item], GetWatchedAtValue, cancellationToken).ConfigureAwait(false);
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
    /// Pulls remote watched-status changes into Jellyfin -- an incremental
    /// journal read if a cursor exists, otherwise (or if the cursor is
    /// outside the 30-day journal retention window) a full reconciliation.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="user">The resolved Jellyfin user, for writing user data.</param>
    /// <param name="snapshot">The current library snapshot, to match remote entries against.</param>
    /// <param name="serverTime">
    /// /sync/last_activities' own server_time -- a safety-margined
    /// timestamp meant to be persisted as the next watermark, rather than
    /// the device's own clock, which can drift and under-cover the next
    /// incremental window.
    /// </param>
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
        // extended=null (full, not ids_only): ids_only only exposes a
        // movie's tmdb id (and an episode's parent show's tmdb id). A local
        // item identified only by imdb/tvdb carries no tmdb id at all, so it
        // could never be matched below with that alone -- full mode gives
        // every provider id.
        var data = await _apiClient.FetchSyncItemsAsync(accessToken, Endpoint, mediatype: null, since: null, extended: null, JournalPageSize, cancellationToken)
            .ConfigureAwait(false);

        var applied = 0;
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in data.Movies)
        {
            var ids = entry.Movie?.Ids;
            if (ids is null || ids.IsEmpty)
            {
                continue;
            }

            var (appliedOk, key) = ApplyMovieEntry(user, snapshot, ids, "active", entry.LastWatchedAt);
            if (key is not null)
            {
                matchedKeys.Add(key);
            }

            if (appliedOk)
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

            var (appliedOk, key) = ApplyEpisodeEntry(user, snapshot, showIds, entry.Episode?.Season, entry.Episode?.Number, "active", entry.LastWatchedAt);
            if (key is not null)
            {
                matchedKeys.Add(key);
            }

            if (appliedOk)
            {
                applied++;
            }
        }

        // The full list above is authoritative: anything locally watched but
        // not in it was unwatched remotely -- the fallback for when the
        // journal's 30-day retention window has lapsed, so there's no
        // incremental removal feed to rely on instead.
        //
        // The removal timestamp is the server-provided watermark, not "now":
        // if the item was genuinely rewatched between when the server
        // generated this snapshot and now, its local timestamp needs to be
        // newer than server_time (not a later client-side "now") to
        // correctly win the conflict-resolution check in ApplyWatched.
        var removalAt = serverTime ?? NowIso();

        foreach (var movie in snapshot.Movies)
        {
            if (!movie.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null && !matchedKeys.Contains(key) && ApplyWatched(user, movie, "removed", removalAt))
            {
                applied++;
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            if (!episode.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null && !matchedKeys.Contains(key) && ApplyWatched(user, episode, "removed", removalAt))
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

            // value_at is the actual watched timestamp and is what
            // conflict resolution must compare against -- but it's only
            // ever set on add/active rows; a removal row has no "value" to
            // speak of and only carries action_at (confirmed against
            // api.mdblist's _remove_movies/_remove_shows/etc., which write
            // the journal row with action_at but no value_at at all).
            // Falling back to action_at there keeps last-write-wins working
            // for removals instead of silently skipping the conflict check.
            var remoteAt = entry.ValueAt ?? entry.ActionAt;

            if (entry.ItemType == "movie")
            {
                var (appliedOk, _) = ApplyMovieEntry(user, snapshot, entry.Ids, entry.Status, remoteAt);
                if (appliedOk)
                {
                    applied++;
                }
            }
            else if (entry.ItemType == "episode")
            {
                var (appliedOk, _) = ApplyEpisodeEntry(user, snapshot, entry.Ids, entry.Season, entry.Episode, entry.Status, remoteAt);
                if (appliedOk)
                {
                    applied++;
                }
            }

            // show/season-level rows have no directly writable Jellyfin field; skipped
        }

        await _stateStore.SetSyncedAtAsync(userId, Category, serverTime ?? NowIso(), cancellationToken).ConfigureAwait(false);
        return new PullResult { PulledApplied = applied, Mode = "incremental" };
    }

    private (bool Applied, string? Key) ApplyMovieEntry(User user, LibrarySnapshot snapshot, MediaIds ids, string? status, string? remoteAt)
    {
        var match = snapshot.FindMovie(ids);
        if (match is null)
        {
            return (false, null);
        }

        return (ApplyWatched(user, match, status, remoteAt), ItemKeys.CanonicalMovieKey(match.Ids));
    }

    private (bool Applied, string? Key) ApplyEpisodeEntry(User user, LibrarySnapshot snapshot, MediaIds showIds, int? season, int? episode, string? status, string? remoteAt)
    {
        var match = snapshot.FindEpisode(showIds, season, episode);
        if (match is null)
        {
            return (false, null);
        }

        var key = ItemKeys.CanonicalEpisodeKey(match.Ids, match.Season, match.EpisodeNumber);
        return (ApplyWatched(user, match, status, remoteAt), key);
    }

    /// <summary>
    /// Last-write-wins using Jellyfin's LastPlayedDate vs the remote
    /// timestamp -- the one sync category where Jellyfin tracks a
    /// comparable local timestamp, so real conflict resolution (not just
    /// remote-wins) applies. An exact tie resolves the same way in both
    /// branches below -- remote wins -- one consistent rule rather than
    /// local winning on removal but losing on activation.
    /// </summary>
    private bool ApplyWatched(User user, SnapshotItem record, string? status, string? remoteAt)
    {
        var localTs = record.LastPlayedDate;
        var remoteTs = ParseTimestamp(remoteAt);
        var removed = status == "removed";

        if (!ShouldApplyRemoteWatched(removed, record.PlayCount, localTs, remoteTs))
        {
            return false;
        }

        if (removed)
        {
            SetWatched(user, record.ItemId, played: false, playCount: 0, lastPlayedDate: null);
        }
        else
        {
            SetWatched(user, record.ItemId, played: true, playCount: Math.Max(record.PlayCount, 1), lastPlayedDate: remoteTs ?? record.LastPlayedDate);
        }

        return true;
    }

    /// <summary>
    /// The conflict-resolution decision at the heart of <see cref="ApplyWatched"/>,
    /// pulled out as a pure function so the matrix (local newer / remote
    /// newer / exact tie / missing timestamps, crossed with add vs remove)
    /// is unit-testable without a live <c>IUserDataManager</c>. An exact tie
    /// resolves the same way in both branches -- remote wins -- rather than
    /// local winning on removal but losing on activation.
    /// </summary>
    /// <param name="removed">Whether the remote row is a removal.</param>
    /// <param name="localPlayCount">The local item's current play count.</param>
    /// <param name="localTs">The local item's <c>LastPlayedDate</c>, if any.</param>
    /// <param name="remoteTs">The remote row's effective timestamp, if any.</param>
    /// <returns>True if the remote state should be applied locally.</returns>
    internal static bool ShouldApplyRemoteWatched(bool removed, int localPlayCount, DateTime? localTs, DateTime? remoteTs)
    {
        if (removed)
        {
            if (localPlayCount <= 0)
            {
                return false;
            }

            return !(localTs.HasValue && remoteTs.HasValue && localTs > remoteTs);
        }

        return !(localPlayCount > 0 && localTs.HasValue && remoteTs.HasValue && localTs > remoteTs);
    }

    private void SetWatched(User user, Guid itemId, bool played, int playCount, DateTime? lastPlayedDate)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return;
        }

        var userData = _userDataManager.GetUserData(user, item) ?? new UserItemData { Key = item.GetUserDataKeys().First() };
        userData.Played = played;
        userData.PlayCount = playCount;

        // Only overwrite when we have a real value -- on removal this
        // leaves it untouched, matching Jellyfin's own "mark unplayed"
        // behavior rather than forcing an empty/invalid date onto the item.
        if (lastPlayedDate.HasValue)
        {
            userData.LastPlayedDate = lastPlayedDate;
        }

        _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static string NowIso()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string? CanonicalKey(SnapshotItem record)
    {
        return record.Type == "movie"
            ? ItemKeys.CanonicalMovieKey(record.Ids)
            : ItemKeys.CanonicalEpisodeKey(record.Ids, record.Season, record.EpisodeNumber);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            WatchedAt = record.LastPlayedDate?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private static JsonNode? GetWatchedAtValue(KnownSyncItem item)
    {
        return item.WatchedAt is null ? null : JsonValue.Create(item.WatchedAt);
    }

    private static Dictionary<string, KnownSyncItem> CurrentWatchedItems(LibrarySnapshot snapshot)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        foreach (var movie in snapshot.Movies)
        {
            if (!movie.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                items[key] = BuildKnownItem(movie);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            if (!episode.Played)
            {
                continue;
            }

            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                items[key] = BuildKnownItem(episode);
            }
        }

        return items;
    }
}
