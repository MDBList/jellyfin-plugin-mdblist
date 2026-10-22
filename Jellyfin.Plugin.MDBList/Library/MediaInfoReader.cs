using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// Reads the media info of a library item's versions (media sources) for the
/// collection push. Each item's result is cached until the item is saved again
/// (DateLastSaved, which also moves when a probe fills in its streams) or its
/// file changes (DateModified), and for at most about CacheLifetime: a version
/// linked from another item doesn't always move this item's dates. Reading
/// media streams costs database queries per item and a sync touches the whole
/// library, so empty results (e.g. .strm items) are cached too; an unreadable
/// item (null) is not.
/// </summary>
public class MediaInfoReader
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MediaInfoReader> _logger;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    // Expiry on the monotonic tick count, so a wall-clock jump can't keep entries forever.
    private readonly ConcurrentDictionary<Guid, (DateTime Saved, DateTime Modified, long ExpiresAtMs, IReadOnlyList<MediaCopy> Copies)> _cache = new();
    private int _reads;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaInfoReader"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{MediaInfoReader}"/> interface.</param>
    public MediaInfoReader(ILibraryManager libraryManager, ILogger<MediaInfoReader> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// The copies (one per version with known media info) of a library item.
    /// </summary>
    /// <param name="record">The library item's snapshot record; its dates check the cache.</param>
    /// <returns>The copies; empty when the item has none, null when they couldn't be read (unknown).</returns>
    public IReadOnlyList<MediaCopy>? ReadCopies(SnapshotItem record)
    {
        var itemId = record.ItemId;
        try
        {
            if (Interlocked.Increment(ref _reads) % 5000 == 0)
            {
                PruneExpired();
            }

            // A cache hit costs no item lookup: the snapshot loaded the item just
            // now, with its current dates.
            if (_cache.TryGetValue(itemId, out var cached) && cached.Saved == record.DateLastSaved
                && cached.Modified == record.DateModified && Environment.TickCount64 < cached.ExpiresAtMs)
            {
                return cached.Copies;
            }

            if (_libraryManager.GetItemById(itemId) is not IHasMediaSources item)
            {
                return null; // gone or replaced since the snapshot: unknown, not "no copies"
            }

            var saved = ((BaseItem)item).DateLastSaved;
            var modified = ((BaseItem)item).DateModified;
            var ownSourceId = itemId.ToString("N", CultureInfo.InvariantCulture);
            var copies = new List<MediaCopy>();
            foreach (var source in item.GetMediaSources(false))
            {
                var copy = MediaInfoMapper.FromSource(source);
                if (copy is not null)
                {
                    copy.IsOwn = string.Equals(copy.CopyKey, ownSourceId, StringComparison.OrdinalIgnoreCase);
                    copies.Add(copy);
                }
            }

            // Jittered so a library read at once doesn't all expire at once.
            var lifetime = CacheLifetime * (0.75 + (0.5 * Random.Shared.NextDouble()));
            _cache[itemId] = (saved, modified, Environment.TickCount64 + (long)lifetime.TotalMilliseconds, copies);

            return copies;
        }
        catch (Exception ex)
        {
            // Media info is optional: an unreadable item still syncs as collected,
            // and "unknown" never counts as a change (see CollectionSync.MediaChanged).
            _logger.LogWarning(ex, "MDBList Sync: could not read media info of item {ItemId}", itemId);
            return null;
        }
    }

    private void PruneExpired()
    {
        var now = Environment.TickCount64;
        foreach (var (id, entry) in _cache)
        {
            if (now >= entry.ExpiresAtMs)
            {
                _cache.TryRemove(id, out _);
            }
        }
    }
}
