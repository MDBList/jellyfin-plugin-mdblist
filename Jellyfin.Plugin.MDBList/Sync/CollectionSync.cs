using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
///
/// Each item also carries the media info of its versions (resolution, HDR,
/// audio) as "copies"; it's kept in the sync state so a quality change (a
/// file upgraded in place) pushes the item again.
/// </summary>
public class CollectionSync
{
    private const SyncCategory Category = SyncCategory.Collection;
    private const string Endpoint = "/sync/collection";
    private const string RemoveEndpoint = "/sync/collection/remove";
    private const string FieldName = "collected_at";

    /// <summary>
    /// Media-only re-pushes per run (an item already collected whose media info
    /// changed): the first run after upgrading would otherwise re-push the whole
    /// library at once; the rest follow on later runs.
    /// </summary>
    private const int MediaRepushPerRun = 1000;

    private readonly SyncPayloadBuilder _payloadBuilder;
    private readonly MediaInfoReader _mediaInfoReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionSync"/> class.
    /// </summary>
    /// <param name="payloadBuilder">Instance of the <see cref="SyncPayloadBuilder"/>.</param>
    /// <param name="mediaInfoReader">Instance of the <see cref="MediaInfoReader"/>.</param>
    public CollectionSync(SyncPayloadBuilder payloadBuilder, MediaInfoReader mediaInfoReader)
    {
        _payloadBuilder = payloadBuilder;
        _mediaInfoReader = mediaInfoReader;
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
    /// <param name="includeMediaInfo">Whether to read and send each item's media info (opt-in).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were pushed as added/removed/skipped.</returns>
    public async Task<PushResult> PushAsync(Guid userId, string accessToken, LibrarySnapshot snapshot, bool allowRemovals, bool includeMediaInfo, CancellationToken cancellationToken)
    {
        // Off, every item's copies are unknown: none are read, sent, or count as a change.
        Func<SnapshotItem, IReadOnlyList<MediaCopy>?> readCopies = includeMediaInfo ? _mediaInfoReader.ReadCopies : static _ => null;
        var current = CurrentCollectedItems(snapshot, readCopies, cancellationToken);
        var repushBudget = MediaRepushPerRun;
        bool Changed(KnownSyncItem known, KnownSyncItem item)
        {
            if (repushBudget <= 0 || !MediaChanged(known, item))
            {
                return false;
            }

            repushBudget--;
            return true;
        }

        return await _payloadBuilder.DiffAndReconcileAsync(
            userId,
            Category,
            current,
            items => _payloadBuilder.PushItemsAsync(userId, Category, accessToken, Endpoint, FieldName, items, GetCollectedAtValue, cancellationToken, AddMediaFields),
            items => _payloadBuilder.PushItemsRemoveAsync(userId, Category, accessToken, RemoveEndpoint, items, cancellationToken),
            Changed,
            cancellationToken,
            allowRemovals).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether an already-collected item's media info differs from what was
    /// last pushed. Only copies count: the collected date is set once. No copies
    /// (unreadable, not probed yet, or a file being replaced) is unknown, never
    /// a change: MDBList keeps its copies until the item is read again.
    /// </summary>
    /// <param name="known">The item as last pushed.</param>
    /// <param name="current">The item as in the library now.</param>
    /// <returns>True to push the item again.</returns>
    internal static bool MediaChanged(KnownSyncItem known, KnownSyncItem current)
    {
        return current.Copies is { Count: > 0 }
            && MediaCopy.Fingerprint(known.Copies) != MediaCopy.Fingerprint(current.Copies);
    }

    /// <summary>
    /// Adds an item's copies to its /sync/collection entry. An item without
    /// known media info sends none, which leaves MDBList's stored copies alone.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="entry">Its payload entry.</param>
    internal static void AddMediaFields(KnownSyncItem item, JsonObject entry)
    {
        if (item.Copies is not { Count: > 0 })
        {
            return;
        }

        var copies = new JsonArray();
        foreach (var copy in item.Copies)
        {
            copies.Add(copy.ToJson());
        }

        entry["copies"] = copies;
    }

    private static JsonNode? GetCollectedAtValue(KnownSyncItem item)
    {
        return item.CollectedAt is null ? null : JsonValue.Create(item.CollectedAt);
    }

    private static KnownSyncItem BuildKnownItem(SnapshotItem record, IReadOnlyList<MediaCopy>? copies)
    {
        return new KnownSyncItem
        {
            Type = record.Type,
            Ids = record.Ids,
            Season = record.Season,
            Episode = record.EpisodeNumber,
            CollectedAt = record.DateCreated.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            Copies = copies,
        };
    }

    /// <summary>
    /// Adds an item under its key; when two library items share a key (the
    /// same title twice), their versions are merged into one item's copies.
    /// Media info is a bonus: an unreadable item adds no copies, and the
    /// title's readable ones are still sent.
    /// </summary>
    /// <param name="items">The items built so far, by key.</param>
    /// <param name="key">The item's canonical key.</param>
    /// <param name="record">The library item.</param>
    /// <param name="readCopies">Reads one library item's copies.</param>
    internal static void AddItem(Dictionary<string, KnownSyncItem> items, string key, SnapshotItem record, Func<SnapshotItem, IReadOnlyList<MediaCopy>?> readCopies)
    {
        IReadOnlyList<MediaCopy>? copies;
        try
        {
            copies = readCopies(record);
        }
        catch (Exception)
        {
            // Never let media info fail the collected sync, including a failure
            // ReadCopies' own catch can't see (e.g. a server API missing on this
            // version, thrown when the method is compiled).
            copies = null;
        }

        if (items.TryGetValue(key, out var existing))
        {
            if (copies is null)
            {
                copies = existing.Copies;
            }
            else if (existing.Copies is not null)
            {
                // A version's own read wins over a sibling's (possibly older) cached read of it.
                var merged = existing.Copies.ToList();
                foreach (var copy in copies)
                {
                    var index = merged.FindIndex(c => c.CopyKey == copy.CopyKey);
                    if (index < 0)
                    {
                        merged.Add(copy);
                    }
                    else if (copy.IsOwn && !merged[index].IsOwn)
                    {
                        merged[index] = copy;
                    }
                }

                copies = merged;
            }
        }

        items[key] = BuildKnownItem(record, copies);
    }

    /// <summary>
    /// Every collected movie and episode, keyed canonically, with its copies.
    /// </summary>
    /// <param name="snapshot">The library snapshot.</param>
    /// <param name="readCopies">Reads one library item's copies.</param>
    /// <param name="cancellationToken">The cancellation token, checked per item: reading media info is slow on a big library.</param>
    /// <returns>The items by key.</returns>
    internal static Dictionary<string, KnownSyncItem> CurrentCollectedItems(
        LibrarySnapshot snapshot,
        Func<SnapshotItem, IReadOnlyList<MediaCopy>?> readCopies,
        CancellationToken cancellationToken = default)
    {
        var items = new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);

        // Every item in the snapshot is already a real, non-virtual library
        // entry (LibrarySnapshot's own query filters IsVirtualItem=false),
        // so unlike Kodi's explicit movie["file"] check, nothing further
        // needs filtering here -- everything present is collected.
        foreach (var movie in snapshot.Movies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = ItemKeys.CanonicalMovieKey(movie.Ids);
            if (key is not null)
            {
                AddItem(items, key, movie, readCopies);
            }
        }

        foreach (var episode in snapshot.Episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = ItemKeys.CanonicalEpisodeKey(episode.Ids, episode.Season, episode.EpisodeNumber);
            if (key is not null)
            {
                AddItem(items, key, episode, readCopies);
            }
        }

        return items;
    }
}
