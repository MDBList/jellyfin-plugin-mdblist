using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Library;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Watched-status push -- port of watched_sync.py's push half (pull lands
/// in a later phase). Membership diff only: a rewatch that updates
/// LastPlayedDate without changing Played is covered by a live single-item
/// push from <see cref="PushSingleAsync"/>, not the full diff.
/// </summary>
public class WatchedSync
{
    private const SyncCategory Category = SyncCategory.Watched;
    private const string Endpoint = "/sync/watched";
    private const string RemoveEndpoint = "/sync/watched/remove";
    private const string FieldName = "watched_at";

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly SyncStateStore _stateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    public WatchedSync(SyncPayloadBuilder payloadBuilder, SyncStateStore stateStore)
    {
        _payloadBuilder = payloadBuilder;
        _stateStore = stateStore;
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
            valueChanged: null,
            cancellationToken).ConfigureAwait(false);
    }

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
