using System.Collections.Generic;
using Jellyfin.Plugin.MDBList.Library;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

public class MediaIdMapperTests
{
    [Fact]
    public void MapMovieIds_ReadsImdbAndTmdb()
    {
        var providerIds = new Dictionary<string, string>
        {
            ["Imdb"] = "tt1234567",
            ["Tmdb"] = "603",
        };

        var ids = MediaIdMapper.MapMovieIds(providerIds);

        Assert.Equal("tt1234567", ids.Imdb);
        Assert.Equal(603, ids.Tmdb);
        Assert.Null(ids.Tvdb);
    }

    [Fact]
    public void MapMovieIds_IgnoresTvdb()
    {
        var providerIds = new Dictionary<string, string> { ["Tvdb"] = "12345" };

        var ids = MediaIdMapper.MapMovieIds(providerIds);

        Assert.Null(ids.Tvdb);
    }

    [Fact]
    public void MapShowIds_IncludesTvdb()
    {
        var providerIds = new Dictionary<string, string> { ["Tvdb"] = "12345" };

        var ids = MediaIdMapper.MapShowIds(providerIds);

        Assert.Equal(12345, ids.Tvdb);
    }

    [Fact]
    public void Map_IsCaseInsensitiveOnKeys()
    {
        var providerIds = new Dictionary<string, string> { ["IMDB"] = "tt7654321" };

        var ids = MediaIdMapper.MapMovieIds(providerIds);

        Assert.Equal("tt7654321", ids.Imdb);
    }

    [Fact]
    public void Map_SkipsEmptyValues()
    {
        var providerIds = new Dictionary<string, string> { ["Imdb"] = string.Empty, ["Tmdb"] = "603" };

        var ids = MediaIdMapper.MapMovieIds(providerIds);

        Assert.Null(ids.Imdb);
        Assert.Equal(603, ids.Tmdb);
    }

    [Fact]
    public void Map_SkipsUnparseableNumericIds()
    {
        var providerIds = new Dictionary<string, string> { ["Tmdb"] = "not-a-number" };

        var ids = MediaIdMapper.MapMovieIds(providerIds);

        Assert.Null(ids.Tmdb);
    }

    [Fact]
    public void MapMovieIds_NullProviderIds_ReturnsEmpty()
    {
        var ids = MediaIdMapper.MapMovieIds(null);

        Assert.True(ids.IsEmpty);
    }
}
