using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MDBList.Api.Models;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// Maps Jellyfin's <c>BaseItem.ProviderIds</c> into MDBList's
/// <see cref="MediaIds"/> shape -- port of utils.py's fix_unique_ids, but
/// simplified: Jellyfin's ProviderIds keys are already normalized (Imdb,
/// Tmdb, Tvdb, ...), unlike Kodi's uniqueid which needed alias remapping.
/// Only imdb/tmdb/tvdb are mapped since Jellyfin has no built-in metadata
/// provider that ever populates trakt/mdblist/kitsu ids.
/// </summary>
public static class MediaIdMapper
{
    /// <summary>
    /// Maps a movie's provider ids (no tvdb -- not in MDBList's movie id set).
    /// </summary>
    /// <param name="providerIds">The item's <c>ProviderIds</c>.</param>
    /// <returns>The mapped <see cref="MediaIds"/>.</returns>
    public static MediaIds MapMovieIds(IReadOnlyDictionary<string, string>? providerIds)
    {
        return Map(providerIds, includeTvdb: false);
    }

    /// <summary>
    /// Maps a show's provider ids (includes tvdb).
    /// </summary>
    /// <param name="providerIds">The show's <c>ProviderIds</c>.</param>
    /// <returns>The mapped <see cref="MediaIds"/>.</returns>
    public static MediaIds MapShowIds(IReadOnlyDictionary<string, string>? providerIds)
    {
        return Map(providerIds, includeTvdb: true);
    }

    private static MediaIds Map(IReadOnlyDictionary<string, string>? providerIds, bool includeTvdb)
    {
        var ids = new MediaIds();
        if (providerIds is null)
        {
            return ids;
        }

        foreach (var (key, value) in providerIds)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            switch (key.ToUpperInvariant())
            {
                case "IMDB":
                    ids.Imdb = value;
                    break;
                case "TMDB":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdb))
                    {
                        ids.Tmdb = tmdb;
                    }

                    break;
                case "TVDB":
                    if (includeTvdb && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvdb))
                    {
                        ids.Tvdb = tvdb;
                    }

                    break;
            }
        }

        return ids;
    }
}
