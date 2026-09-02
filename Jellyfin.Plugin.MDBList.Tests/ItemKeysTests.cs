using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Library;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

public class ItemKeysTests
{
    [Fact]
    public void CanonicalMovieKey_PrefersTmdbOverImdb()
    {
        var ids = new MediaIds { Tmdb = 603, Imdb = "tt0133093" };

        Assert.Equal("movie:tmdb:603", ItemKeys.CanonicalMovieKey(ids));
    }

    [Fact]
    public void CanonicalMovieKey_FallsBackToImdbWhenNoTmdb()
    {
        var ids = new MediaIds { Imdb = "tt0133093" };

        Assert.Equal("movie:imdb:tt0133093", ItemKeys.CanonicalMovieKey(ids));
    }

    [Fact]
    public void CanonicalMovieKey_FallsBackToMdblistSlug()
    {
        var ids = new MediaIds { Mdblist = "a2na" };

        Assert.Equal("movie:mdblist:a2na", ItemKeys.CanonicalMovieKey(ids));
    }

    [Fact]
    public void CanonicalMovieKey_EmptyIds_ReturnsNull()
    {
        Assert.Null(ItemKeys.CanonicalMovieKey(new MediaIds()));
    }

    [Fact]
    public void CanonicalEpisodeKey_IncludesSeasonAndEpisode()
    {
        var ids = new MediaIds { Tvdb = 81189 };

        Assert.Equal("episode:tvdb:81189:1:1", ItemKeys.CanonicalEpisodeKey(ids, season: 1, episode: 1));
    }

    [Fact]
    public void CanonicalEpisodeKey_MissingSeasonOrEpisode_ReturnsNull()
    {
        var ids = new MediaIds { Tvdb = 81189 };

        Assert.Null(ItemKeys.CanonicalEpisodeKey(ids, season: null, episode: 1));
        Assert.Null(ItemKeys.CanonicalEpisodeKey(ids, season: 1, episode: null));
    }

    [Fact]
    public void FindMovieMatch_MatchesOnAnyCarriedId_RegardlessOfPriorityOrder()
    {
        // The index entry was registered under imdb (e.g. only imdb is known
        // for it), but the lookup ids carry a different provider first in
        // priority order plus the matching imdb id -- confirms every id is
        // tried, not just the first-priority one.
        var indexed = new SnapshotItem { Type = "movie", ItemId = Guid.NewGuid(), Ids = new MediaIds { Imdb = "tt0133093" } };
        var index = new Dictionary<string, SnapshotItem>
        {
            ["imdb:tt0133093"] = indexed,
        };

        var lookupIds = new MediaIds { Tmdb = 999999, Imdb = "tt0133093" };

        Assert.Same(indexed, ItemKeys.FindMovieMatch(index, lookupIds));
    }

    [Fact]
    public void FindMovieMatch_NoOverlappingId_ReturnsNull()
    {
        var index = new Dictionary<string, SnapshotItem>
        {
            ["imdb:tt0133093"] = new SnapshotItem { Type = "movie", ItemId = Guid.NewGuid(), Ids = new MediaIds { Imdb = "tt0133093" } },
        };

        var lookupIds = new MediaIds { Tmdb = 603 };

        Assert.Null(ItemKeys.FindMovieMatch(index, lookupIds));
    }

    [Fact]
    public void FindEpisodeMatch_MatchesOnShowIdSeasonAndEpisode()
    {
        var indexed = new SnapshotItem { Type = "episode", ItemId = Guid.NewGuid(), Ids = new MediaIds { Tvdb = 81189 }, Season = 1, EpisodeNumber = 1 };
        var index = new Dictionary<string, SnapshotItem>
        {
            ["tvdb:81189:1:1"] = indexed,
        };

        var showIds = new MediaIds { Tvdb = 81189 };

        Assert.Same(indexed, ItemKeys.FindEpisodeMatch(index, showIds, season: 1, episode: 1));
    }
}
