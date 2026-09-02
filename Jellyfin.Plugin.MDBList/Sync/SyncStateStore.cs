using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Persisted sync state (cursors + known-items maps), one file per plugin
/// install covering every linked user -- port of sync_state.py.
///
/// Lives at <c>PluginConfigurationsPath/MDBList/sync_state.json</c>, not
/// under <c>DataFolderPath</c> (that path is version-suffixed and wiped by
/// the plugin updater -- confirmed against BasePluginOfT's source, see the
/// plan's verified findings).
///
/// Kodi needed a *file* lock because "Sync now" ran in a separate OS
/// process from the service; here everything is one process, so a single
/// in-memory cache guarded by one <see cref="SemaphoreSlim"/> replaces both
/// the file lock and Python's reload-on-every-getter (nothing else can have
/// written the file between two calls in this process). Still written
/// atomically (temp file + move) for crash safety.
/// </summary>
public sealed class SyncStateStore : IDisposable
{
    private const string FileName = "sync_state.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SyncStateFile? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStateStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    public SyncStateStore(IApplicationPaths applicationPaths)
    {
        _filePath = Path.Combine(applicationPaths.PluginConfigurationsPath, "MDBList", FileName);
    }

    /// <summary>
    /// Gets the incremental-pull cursor for a category. Null means no pull
    /// has ever succeeded, so the next pull must be a full one.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cursor, or null.</returns>
    public async Task<string?> GetSyncedAtAsync(Guid userId, SyncCategory category, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return TryGetUserState(file, userId, out var userState) ? GetCategoryState(userState, category).SyncedAt : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets the incremental-pull cursor for a category.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="timestamp">The new cursor value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetSyncedAtAsync(Guid userId, SyncCategory category, string timestamp, CancellationToken cancellationToken)
    {
        await MutateAsync(
            file => GetCategoryState(GetOrCreateUserState(file, userId), category).SyncedAt = timestamp,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the last-pushed identity + value for every known item in a category.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A copy of the known-items map.</returns>
    public async Task<Dictionary<string, KnownSyncItem>> GetKnownItemsAsync(Guid userId, SyncCategory category, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!TryGetUserState(file, userId, out var userState))
            {
                return new Dictionary<string, KnownSyncItem>(StringComparer.Ordinal);
            }

            return new Dictionary<string, KnownSyncItem>(GetCategoryState(userState, category).KnownItems, StringComparer.Ordinal);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Replaces the whole known-items map for a category -- used by a
    /// full-diff push, which examines the entire current library each time.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="items">The new known-items map.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetKnownItemsAsync(
        Guid userId,
        SyncCategory category,
        IReadOnlyDictionary<string, KnownSyncItem> items,
        CancellationToken cancellationToken)
    {
        await MutateAsync(
            file =>
            {
                var state = GetCategoryState(GetOrCreateUserState(file, userId), category);
                state.KnownItems.Clear();
                foreach (var (key, value) in items)
                {
                    state.KnownItems[key] = value;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Patches a single key in a category's known-items map -- used by a
    /// single-item live push, which only ever examines one item, not the
    /// whole library.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="category">The sync category.</param>
    /// <param name="key">The canonical key.</param>
    /// <param name="item">The new item, or null to remove the key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateKnownItemAsync(Guid userId, SyncCategory category, string key, KnownSyncItem? item, CancellationToken cancellationToken)
    {
        await MutateAsync(
            file =>
            {
                var state = GetCategoryState(GetOrCreateUserState(file, userId), category);
                if (item is null)
                {
                    state.KnownItems.Remove(key);
                }
                else
                {
                    state.KnownItems[key] = item;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the last /sync/last_activities snapshot checked for this user.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A copy of the last-seen activity buckets.</returns>
    public async Task<Dictionary<string, string>> GetLastActivitiesSeenAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!TryGetUserState(file, userId, out var userState))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(userState.LastActivitiesSeen, StringComparer.Ordinal);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets the last /sync/last_activities snapshot checked for this user.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="activities">The activity buckets to record as seen.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetLastActivitiesSeenAsync(Guid userId, IReadOnlyDictionary<string, string> activities, CancellationToken cancellationToken)
    {
        await MutateAsync(
            file =>
            {
                var userState = GetOrCreateUserState(file, userId);
                userState.LastActivitiesSeen.Clear();
                foreach (var (key, value) in activities)
                {
                    userState.LastActivitiesSeen[key] = value;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static CategoryState GetCategoryState(UserSyncState userState, SyncCategory category)
    {
        return category switch
        {
            SyncCategory.Watched => userState.Watched,
            SyncCategory.Ratings => userState.Ratings,
            SyncCategory.Collection => userState.Collection,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }

    private static bool TryGetUserState(SyncStateFile file, Guid userId, out UserSyncState userState)
    {
        return file.Users.TryGetValue(userId.ToString(), out userState!);
    }

    private static UserSyncState GetOrCreateUserState(SyncStateFile file, Guid userId)
    {
        var key = userId.ToString();
        if (!file.Users.TryGetValue(key, out var userState))
        {
            userState = new UserSyncState();
            file.Users[key] = userState;
        }

        return userState;
    }

    private async Task MutateAsync(Action<SyncStateFile> mutate, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            mutate(file);
            await WriteToDiskAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SyncStateFile> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        _cache = await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
        return _cache;
    }

    private async Task<SyncStateFile> LoadFromDiskAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<SyncStateFile>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return loaded ?? new SyncStateFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // No file yet (first run) or an unreadable one -- start fresh
            // rather than fail the whole plugin; a corrupt state file forces
            // a full reconciliation on the next sync, which is safe by design.
            return new SyncStateFile();
        }
    }

    private async Task WriteToDiskAsync(SyncStateFile file, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, file, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }
}
