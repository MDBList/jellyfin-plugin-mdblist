using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Library;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Collection/library membership push -- port of collection_sync.py.
/// Jellyfin -> MDBList only: this reflects what's actually in the local
/// library so MDBList's collected status is accurate. There is
/// deliberately no pull direction -- Jellyfin can't materialize a file just
/// because MDBList thinks it's collected, so a remote-only "collected" flag
/// has nothing local to apply, and no self-heal if a bad removal is pushed.
///
/// The removal safety net (magnitude circuit-breaker, empty-snapshot abort,
/// scan-in-progress check) lives centrally in
/// <see cref="SyncPayloadBuilder"/>/<see cref="SyncOrchestrator"/> now, applied
/// uniformly to all three categories -- see removal_safety_pattern.md.
/// </summary>
public class CollectionSync
{
    private const SyncCategory Category = SyncCategory.Collection;
    private const string Endpoint = "/sync/collection";
    private const string RemoveEndpoint = "/sync/collection/remove";
    private const string FieldName = "collected_at";

    private readonly SyncPayloadBuilder _payloadBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    public CollectionSync(SyncPayloadBuilder payloadBuilder)
    {
        _payloadBuilder = payloadBuilder;
    }

    /// <summary>
    /// Push + reconcile: anything newly present in the library is added,
    /// anything that dropped out (file removed/library item deleted) since
    /// the last run is removed from MDBList's collection.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="snapshot">The current library snapshot.</param>
    /// <param name="allowRemovals">See <see cref="SyncPayloadBuilder.DiffAndReconcileAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed/skipped.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, bool allowRemovals, CancellationToken cancellationToken)
    {
        var current = CurrentCollectedItems(snapshot);

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, items, GetCollectedAtValue, cancellationToken),
            items => _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, items, cancellationToken),
            valueChanged: null,
            cancellationToken,
            allowRemovals).ConfigureAwait(false);
    }

    private static JsonNode? GetCollectedAtValue(KnownSyncItem item)
    {
        return item.CollectedAt is null ? null : JsonValue.Create(item.CollectedAt);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            CollectedAt = record.DateCreated.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private static Dictionary<string, KnownSyncItem> CurrentCollectedItems(LibrarySnapshot snapshot)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        // Every item in the snapshot is already a real, non-virtual library
        // entry (LibrarySnapshot's own query filters IsVirtualItem=false),
        // so unlike Kodi's explicit movie["file"] check, nothing further
        // needs filtering here -- everything present is collected.
        foreach (var movie in snapshot.Movies)
        {
            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                items[key] = BuildKnownItem(movie);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                items[key] = BuildKnownItem(episode);
            }
        }

        return items;
    }
}
