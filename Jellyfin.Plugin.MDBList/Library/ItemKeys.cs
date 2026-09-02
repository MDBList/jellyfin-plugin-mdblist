using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MDBList.Api.Models;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// Canonical-id-based key generation and matching -- port of
/// library_snapshot.py's PROVIDER_PRIORITY-ordered canonical_movie_key/
/// canonical_episode_key/find_movie_match/find_episode_match. An item
/// carrying more than one provider id is matched on whichever one this
/// order finds first, tolerant of a remote entry using a different id than
/// the local item's preferred one.
/// </summary>
public static class ItemKeys
{
    private static readonly (string Name, Func<MediaIds, string?> GetValue)[] ProviderPriority =
    [
        ("tmdb", ids => ids.Tmdb?.ToString(CultureInfo.InvariantCulture)),
        ("imdb", ids => ids.Imdb),
        ("tvdb", ids => ids.Tvdb?.ToString(CultureInfo.InvariantCulture)),
        ("trakt", ids => ids.Trakt?.ToString(CultureInfo.InvariantCulture)),
        ("mdblist", ids => ids.Mdblist?.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>
    /// Builds the canonical key for a movie -- for the flat known-items sync
    /// state map, which spans movies and episodes together.
    /// </summary>
    /// <param name="ids">The movie's provider ids.</param>
    /// <returns>The canonical key, or null if <paramref name="ids"/> is empty.</returns>
    public static string? CanonicalMovieKey(MediaIds ids)
    {
        foreach (var (name, getValue) in ProviderPriority)
        {
            var value = getValue(ids);
            if (!string.IsNullOrEmpty(value))
            {
                return $"movie:{name}:{value}";
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the canonical key for an episode.
    /// </summary>
    /// <param name="showIds">The parent show's provider ids.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <returns>The canonical key, or null if unresolvable.</returns>
    public static string? CanonicalEpisodeKey(MediaIds showIds, int? season, int? episode)
    {
        if (season is null || episode is null)
        {
            return null;
        }

        foreach (var (name, getValue) in ProviderPriority)
        {
            var value = getValue(showIds);
            if (!string.IsNullOrEmpty(value))
            {
                return $"episode:{name}:{value}:{season}:{episode}";
            }
        }

        return null;
    }

    /// <summary>
    /// Every per-provider index key a movie should be registered under in a
    /// <see cref="LibrarySnapshot"/>'s movie index -- one per id it carries,
    /// so a lookup succeeds regardless of which provider a remote entry uses.
    /// </summary>
    /// <param name="ids">The movie's provider ids.</param>
    /// <returns>The index keys.</returns>
    internal static IEnumerable<string> AllMovieIndexKeys(MediaIds ids)
    {
        foreach (var (name, getValue) in ProviderPriority)
        {
            var value = getValue(ids);
            if (!string.IsNullOrEmpty(value))
            {
                yield return $"{name}:{value}";
            }
        }
    }

    /// <summary>
    /// Every per-provider index key an episode should be registered under.
    /// </summary>
    /// <param name="showIds">The parent show's provider ids.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <returns>The index keys.</returns>
    internal static IEnumerable<string> AllEpisodeIndexKeys(MediaIds showIds, int? season, int? episode)
    {
        if (season is null || episode is null)
        {
            yield break;
        }

        foreach (var (name, getValue) in ProviderPriority)
        {
            var value = getValue(showIds);
            if (!string.IsNullOrEmpty(value))
            {
                yield return $"{name}:{value}:{season}:{episode}";
            }
        }
    }

    /// <summary>
    /// Looks up a movie in a snapshot's movie index, trying every id
    /// <paramref name="ids"/> carries in priority order.
    /// </summary>
    /// <param name="index">The snapshot's movie index.</param>
    /// <param name="ids">The ids to match against.</param>
    /// <returns>The matched item, or null.</returns>
    internal static SnapshotItem? FindMovieMatch(IReadOnlyDictionary<string, SnapshotItem> index, MediaIds ids)
    {
        foreach (var key in AllMovieIndexKeys(ids))
        {
            if (index.TryGetValue(key, out var match))
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Looks up an episode in a snapshot's episode index.
    /// </summary>
    /// <param name="index">The snapshot's episode index.</param>
    /// <param name="showIds">The parent show's ids to match against.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <returns>The matched item, or null.</returns>
    internal static SnapshotItem? FindEpisodeMatch(IReadOnlyDictionary<string, SnapshotItem> index, MediaIds showIds, int? season, int? episode)
    {
        foreach (var key in AllEpisodeIndexKeys(showIds, season, episode))
        {
            if (index.TryGetValue(key, out var match))
            {
                return match;
            }
        }

        return null;
    }
}
