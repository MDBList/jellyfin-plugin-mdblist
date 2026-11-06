using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// Saves user data so one item Jellyfin can't save doesn't fail a whole sync.
/// Jellyfin writes a row per <see cref="BaseItem.GetUserDataKeys"/> entry in
/// one database context, so an item whose metadata yields the same key twice
/// (e.g. two provider ids with the same value) throws an EF Core tracking
/// error on every save -- Jellyfin's own "mark played" included.
/// </summary>
internal static class UserDataWriter
{
    /// <summary>
    /// Saves <paramref name="userData"/> with reason Import, logging and
    /// skipping the item if Jellyfin can't save it.
    /// </summary>
    /// <param name="userDataManager">Jellyfin's user data manager.</param>
    /// <param name="logger">Where to log an item that couldn't be saved.</param>
    /// <param name="user">The user.</param>
    /// <param name="item">The item.</param>
    /// <param name="userData">The user data to save.</param>
    /// <returns>True if saved.</returns>
    public static bool TrySave(IUserDataManager userDataManager, ILogger logger, User user, BaseItem item, UserItemData userData)
    {
        try
        {
            userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            var keys = item.GetUserDataKeys();
            var duplicates = FindDuplicateKeys(keys);
            logger.LogWarning(
                ex,
                "MDBList Sync: Jellyfin couldn't save user data for {ItemName} ({ItemId}), skipped. Its user data keys: {Keys}; repeated: {Duplicates}. Check the provider ids of the item (and its series)",
                item.Name,
                item.Id,
                string.Join(", ", keys),
                duplicates.Count > 0 ? string.Join(", ", duplicates) : "none");
            return false;
        }
    }

    /// <summary>
    /// The keys appearing more than once, compared ordinally as EF Core's
    /// identity map does.
    /// </summary>
    /// <param name="keys">An item's user data keys.</param>
    /// <returns>Each repeated key, once.</returns>
    internal static IReadOnlyList<string> FindDuplicateKeys(IEnumerable<string> keys)
    {
        return keys
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
    }
}
