using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// A full local-library snapshot, indexed by every provider id a movie
/// carries (or a parent show carries, for episodes) -- port of
/// library_snapshot.py's build_snapshot(). Read-only: builds no writes, only
/// a snapshot to match remote sync state against.
/// </summary>
public class LibrarySnapshot
{
    private readonly Dictionary<string, SnapshotItem> _movieIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SnapshotItem> _episodeIndex = new(StringComparer.Ordinal);
    private readonly List<SnapshotItem> _movies = [];
    private readonly List<SnapshotItem> _episodes = [];

    /// <summary>
    /// Gets every movie in the snapshot, whether or not it mapped to any
    /// provider id (unmappable movies are counted in
    /// <see cref="UnmappableMovieCount"/> but not included here, since
    /// nothing can be matched against them).
    /// </summary>
    public IReadOnlyList<SnapshotItem> Movies => _movies;

    /// <summary>
    /// Gets every episode in the snapshot.
    /// </summary>
    public IReadOnlyList<SnapshotItem> Episodes => _episodes;

    /// <summary>
    /// Gets the count of movies skipped for lacking any usable provider id.
    /// </summary>
    public int UnmappableMovieCount { get; private set; }

    /// <summary>
    /// Gets the count of episodes skipped for lacking a parent show with a
    /// usable provider id, or a season/episode number.
    /// </summary>
    public int UnmappableEpisodeCount { get; private set; }

    /// <summary>
    /// Builds a snapshot of one Jellyfin user's movie and episode library.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="user">The Jellyfin user whose watched/rating state to read.</param>
    /// <returns>The built snapshot.</returns>
    public static LibrarySnapshot Build(ILibraryManager libraryManager, IUserDataManager userDataManager, User user)
    {
        var snapshot = new LibrarySnapshot();
        snapshot.AddMovies(libraryManager, userDataManager, user);
        snapshot.AddEpisodes(libraryManager, userDataManager, user);
        return snapshot;
    }

    /// <summary>
    /// Looks up a movie by any id it carries.
    /// </summary>
    /// <param name="ids">The ids to match against.</param>
    /// <returns>The matched item, or null.</returns>
    public SnapshotItem? FindMovie(Api.Models.MediaIds ids)
    {
        return ItemKeys.FindMovieMatch(_movieIndex, ids);
    }

    /// <summary>
    /// Looks up an episode by its parent show's ids plus season/episode number.
    /// </summary>
    /// <param name="showIds">The parent show's ids.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <returns>The matched item, or null.</returns>
    public SnapshotItem? FindEpisode(Api.Models.MediaIds showIds, int? season, int? episode)
    {
        return ItemKeys.FindEpisodeMatch(_episodeIndex, showIds, season, episode);
    }

    private void AddMovies(ILibraryManager libraryManager, IUserDataManager userDataManager, User user)
    {
        var query = new InternalItemsQuery
        {
            User = user,
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsVirtualItem = false,
        };

        foreach (var item in libraryManager.GetItemList(query))
        {
            var ids = MediaIdMapper.MapMovieIds(item.ProviderIds);
            if (ids.IsEmpty)
            {
                UnmappableMovieCount++;
                continue;
            }

            var userData = userDataManager.GetUserData(user, item);
            var record = new SnapshotItem
            {
                Type = "movie",
                ItemId = item.Id,
                Title = item.Name,
                Ids = ids,
                Played = userData?.Played ?? false,
                PlayCount = userData?.PlayCount ?? 0,
                LastPlayedDate = userData?.LastPlayedDate,
                Rating = userData?.Rating,
                DateCreated = item.DateCreated,
            };

            _movies.Add(record);
            foreach (var key in ItemKeys.AllMovieIndexKeys(ids))
            {
                _movieIndex[key] = record;
            }
        }
    }

    private void AddEpisodes(ILibraryManager libraryManager, IUserDataManager userDataManager, User user)
    {
        var query = new InternalItemsQuery
        {
            User = user,
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
        };

        foreach (var item in libraryManager.GetItemList(query))
        {
            if (item is not Episode episode || episode.Series is null
                || episode.ParentIndexNumber is null || episode.IndexNumber is null)
            {
                UnmappableEpisodeCount++;
                continue;
            }

            var showIds = MediaIdMapper.MapShowIds(episode.Series.ProviderIds);
            if (showIds.IsEmpty)
            {
                UnmappableEpisodeCount++;
                continue;
            }

            var userData = userDataManager.GetUserData(user, episode);
            var season = episode.ParentIndexNumber;
            var startNumber = episode.IndexNumber.Value;

            // A multi-episode file (IndexNumberEnd set) covers a numeric
            // range -- register one record per number so each syncs
            // independently, matching how MDBList tracks watched/rated
            // state per episode number, not per file.
            var endNumber = episode.IndexNumberEnd ?? startNumber;

            for (var number = startNumber; number <= endNumber; number++)
            {
                var record = new SnapshotItem
                {
                    Type = "episode",
                    ItemId = episode.Id,
                    Title = episode.Name,
                    Ids = showIds,
                    Season = season,
                    EpisodeNumber = number,
                    Played = userData?.Played ?? false,
                    PlayCount = userData?.PlayCount ?? 0,
                    LastPlayedDate = userData?.LastPlayedDate,
                    Rating = userData?.Rating,
                    DateCreated = episode.DateCreated,
                };

                _episodes.Add(record);
                foreach (var key in ItemKeys.AllEpisodeIndexKeys(showIds, season, number))
                {
                    _episodeIndex[key] = record;
                }
            }
        }
    }
}
