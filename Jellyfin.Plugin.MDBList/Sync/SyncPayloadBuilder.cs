using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Library;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Shared push/diff skeleton -- port of sync_payload.py. Used by
/// WatchedSync/RatingsSync/CollectionSync so the three don't duplicate the
/// same diff-then-push-then-persist body.
/// </summary>
public class SyncPayloadBuilder
{
    private const int BatchSize = 100;

    private static readonly JsonSerializerOptions IdsSerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MDBListApiClient _apiClient;
    private readonly SyncStateStore _stateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncPayloadBuilder"/> class.
    /// </summary>
    /// <param name="apiClient">Instance of the <see cref="MDBListApiClient"/>.</param>
    /// <param name="stateStore">Instance of the <see cref="SyncStateStore"/>.</param>
    public SyncPayloadBuilder(MDBListApiClient apiClient, SyncStateStore stateStore)
    {
        _apiClient = apiClient;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Pushes a batch of items with a value field (watched_at/rating/collected_at).
    /// Persists each chunk's known-items state immediately after it pushes
    /// successfully, so a later chunk's failure doesn't undo already-pushed
    /// progress -- see <see cref="SyncStateStore.MergeKnownItemsAsync"/>.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category, for persisting progress.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="endpoint">The push endpoint, e.g. "/sync/watched".</param>
    /// <param name="fieldName">The wire field name for the value, e.g. "watched_at".</param>
    /// <param name="items">The items to push.</param>
    /// <param name="getValue">Extracts the value to send for one item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PushItemsAsync(
        Guid userId,
        SyncCategory category,
        string accessToken,
        string endpoint,
        string fieldName,
        IReadOnlyCollection<KnownSyncItem> items,
        Func<KnownSyncItem, JsonNode?> getValue,
        CancellationToken cancellationToken)
    {
        var movieEntries = items.Where(item => item.Type == "movie").ToList();
        foreach (var batch in Chunk(movieEntries))
        {
            var movies = new JsonArray();
            foreach (var item in batch)
            {
                var entry = new JsonObject { ["ids"] = SerializeIds(item.Ids) };
                entry[fieldName] = getValue(item);
                movies.Add(entry);
            }

            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, new JsonObject { ["movies"] = movies }, cancellationToken)
                .ConfigureAwait(false);
            await PersistPushedChunkAsync(userId, category, batch, cancellationToken).ConfigureAwait(false);
        }

        var episodeEntries = items.Where(item => item.Type == "episode").ToList();
        foreach (var batch in Chunk(episodeEntries))
        {
            var shows = BuildShowsPayload(batch, fieldName, getValue);
            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, new JsonObject { ["shows"] = shows }, cancellationToken)
                .ConfigureAwait(false);
            await PersistPushedChunkAsync(userId, category, batch, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pushes a batch of items to a "/remove" endpoint -- identity only, no
    /// value field. Persists each chunk's removal immediately after it
    /// pushes successfully -- see <see cref="PushItemsAsync"/>.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category, for persisting progress.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="endpoint">The removal endpoint, e.g. "/sync/watched/remove".</param>
    /// <param name="items">The items to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PushItemsRemoveAsync(
        Guid userId,
        SyncCategory category,
        string accessToken,
        string endpoint,
        IReadOnlyCollection<KnownSyncItem> items,
        CancellationToken cancellationToken)
    {
        var movieEntries = items.Where(item => item.Type == "movie").ToList();
        foreach (var batch in Chunk(movieEntries))
        {
            var movies = new JsonArray();
            foreach (var item in batch)
            {
                movies.Add(new JsonObject { ["ids"] = SerializeIds(item.Ids) });
            }

            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, new JsonObject { ["movies"] = movies }, cancellationToken)
                .ConfigureAwait(false);
            await PersistRemovedChunkAsync(userId, category, batch, cancellationToken).ConfigureAwait(false);
        }

        var episodeEntries = items.Where(item => item.Type == "episode").ToList();
        foreach (var batch in Chunk(episodeEntries))
        {
            var shows = BuildShowsPayload(batch, fieldName: null, getValue: null);
            await _apiClient.PushSyncItemsAsync(accessToken, endpoint, new JsonObject { ["shows"] = shows }, cancellationToken)
                .ConfigureAwait(false);
            await PersistRemovedChunkAsync(userId, category, batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistPushedChunkAsync(Guid userId, SyncCategory category, IReadOnlyCollection<KnownSyncItem> chunk, CancellationToken cancellationToken)
    {
        var upserts = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);
        foreach (var item in chunk)
        {
            var key = CanonicalKeyOf(item);
            if (key is not null)
            {
                upserts[key] = item;
            }
        }

        if (upserts.Count > 0)
        {
            await _stateStore.MergeKnownItemsAsync(userId, category, upserts, [], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistRemovedChunkAsync(Guid userId, SyncCategory category, IReadOnlyCollection<KnownSyncItem> chunk, CancellationToken cancellationToken)
    {
        var removedKeys = new List<string>();
        foreach (var item in chunk)
        {
            var key = CanonicalKeyOf(item);
            if (key is not null)
            {
                removedKeys.Add(key);
            }
        }

        if (removedKeys.Count > 0)
        {
            await _stateStore.MergeKnownItemsAsync(userId, category, new Dictionary<string, KnownSyncItem>(), removedKeys, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? CanonicalKeyOf(KnownSyncItem item)
    {
        return item.Type == "movie"
            ? ItemKeys.CanonicalMovieKey(item.Ids)
            : ItemKeys.CanonicalEpisodeKey(item.Ids, item.Season, item.Episode);
    }

    /// <summary>
    /// Diffs <paramref name="currentItems"/> against the stored known-items
    /// map, pushes the deltas, and persists the new known-items map -- port
    /// of diff_and_reconcile.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="currentItems">The full current state, keyed by canonical id.</param>
    /// <param name="pushAdd">
    /// Called with newly-active items to push -- expected to persist each
    /// pushed chunk's known-items state as it goes (see
    /// <see cref="PushItemsAsync"/>), not just report success at the end.
    /// </param>
    /// <param name="pushRemove">Called with newly-removed items to push (see <paramref name="pushAdd"/>).</param>
    /// <param name="valueChanged">
    /// Optional: also add an item whose key is already known but whose value
    /// differs (e.g. a rating change or updated watch date) -- membership-only
    /// categories (collection) leave this null.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="allowRemovals">
    /// When false, skip computing/pushing removals entirely, so items that
    /// are only transiently invisible (e.g. a network mount outage) aren't
    /// recorded as gone and don't get diffed as a mass removal on a later
    /// run. Used by collection sync's safety guard; defaults to true (the
    /// every-other-category behavior).
    /// </param>
    /// <returns>How many items were pushed as added/removed.</returns>
    public async Task<PushResult> DiffAndReconcileAsync(
        Guid userId,
        SyncCategory category,
        IReadOnlyDictionary<string, KnownSyncItem> currentItems,
        Func<IReadOnlyCollection<KnownSyncItem>, Task> pushAdd,
        Func<IReadOnlyCollection<KnownSyncItem>, Task> pushRemove,
        Func<KnownSyncItem, KnownSyncItem, bool>? valueChanged,
        CancellationToken cancellationToken,
        bool allowRemovals = true)
    {
        var known = await _stateStore.GetKnownItemsAsync(userId, category, cancellationToken).ConfigureAwait(false);

        var toAdd = new List<KnownSyncItem>();
        foreach (var (key, item) in currentItems)
        {
            if (!known.TryGetValue(key, out var knownItem) || (valueChanged?.Invoke(knownItem, item) ?? false))
            {
                toAdd.Add(item);
            }
        }

        var toRemove = new List<KnownSyncItem>();
        if (allowRemovals)
        {
            foreach (var (key, item) in known)
            {
                if (!currentItems.ContainsKey(key))
                {
                    toRemove.Add(item);
                }
            }
        }

        if (toAdd.Count > 0)
        {
            await pushAdd(toAdd).ConfigureAwait(false);
        }

        if (toRemove.Count > 0)
        {
            await pushRemove(toRemove).ConfigureAwait(false);
        }

        return new PushResult { PushedAdd = toAdd.Count, PushedRemove = toRemove.Count };
    }

    /// <summary>
    /// Groups flat episode items by parent show into the nested
    /// <c>{"ids", "seasons": [{"number", "episodes": [{"number", ...}]}]}</c>
    /// shape every /sync/* endpoint expects -- port of build_shows_payload.
    /// </summary>
    private static JsonArray BuildShowsPayload(IReadOnlyCollection<KnownSyncItem> episodeItems, string? fieldName, Func<KnownSyncItem, JsonNode?>? getValue)
    {
        var showsByKey = new Dictionary<string, (JsonNode Ids, Dictionary<int, JsonArray> Seasons)>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var item in episodeItems)
        {
            if (item.Season is null || item.Episode is null)
            {
                continue;
            }

            var key = IdsGroupKey(item.Ids);
            if (!showsByKey.TryGetValue(key, out var show))
            {
                show = (SerializeIds(item.Ids), new Dictionary<int, JsonArray>());
                showsByKey[key] = show;
                order.Add(key);
            }

            if (!show.Seasons.TryGetValue(item.Season.Value, out var episodes))
            {
                episodes = new JsonArray();
                show.Seasons[item.Season.Value] = episodes;
            }

            var entry = new JsonObject { ["number"] = item.Episode.Value };
            if (fieldName is not null && getValue is not null)
            {
                entry[fieldName] = getValue(item);
            }

            episodes.Add(entry);
        }

        var result = new JsonArray();
        foreach (var key in order)
        {
            var (ids, seasons) = showsByKey[key];
            var seasonsArray = new JsonArray();
            foreach (var seasonNumber in seasons.Keys.OrderBy(n => n))
            {
                seasonsArray.Add(new JsonObject { ["number"] = seasonNumber, ["episodes"] = seasons[seasonNumber] });
            }

            result.Add(new JsonObject { ["ids"] = ids, ["seasons"] = seasonsArray });
        }

        return result;
    }

    private static string IdsGroupKey(Api.Models.MediaIds ids)
    {
        return string.Join(
            '|',
            ids.Imdb ?? string.Empty,
            ids.Tmdb?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ids.Tvdb?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ids.Trakt?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ids.Mdblist ?? string.Empty);
    }

    private static JsonNode SerializeIds(Api.Models.MediaIds ids)
    {
        return JsonSerializer.SerializeToNode(ids, IdsSerializerOptions)!;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> items)
    {
        for (var i = 0; i < items.Count; i += BatchSize)
        {
            yield return items.GetRange(i, Math.Min(BatchSize, items.Count - i));
        }
    }
}
