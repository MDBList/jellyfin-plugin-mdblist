using Jellyfin.Plugin.MDBList.Events;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Exercises <see cref="PlaybackScrobbleService.IsScrobblable"/>, ensuring pre-rolls,
/// extras, trailers, and non-movie/episode media are filtered out before session state
/// or scrobbles are created.
/// </summary>
public class ScrobbleFilterTests
{
    [Fact]
    public void IsScrobblable_NullItem_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsScrobblable(null));
    }

    [Fact]
    public void IsScrobblable_StandardMovie_ReturnsTrue()
    {
        var movie = new Movie();
        Assert.True(PlaybackScrobbleService.IsScrobblable(movie));
    }

    [Fact]
    public void IsScrobblable_StandardEpisode_ReturnsTrue()
    {
        var episode = new Episode();
        Assert.True(PlaybackScrobbleService.IsScrobblable(episode));
    }

    [Fact]
    public void IsScrobblable_MovieWithExtraType_ReturnsFalse()
    {
        var trailer = new Movie { ExtraType = ExtraType.Trailer };
        Assert.False(PlaybackScrobbleService.IsScrobblable(trailer));

        var clip = new Movie { ExtraType = ExtraType.Clip };
        Assert.False(PlaybackScrobbleService.IsScrobblable(clip));
    }

    [Fact]
    public void IsScrobblable_GenericVideoPreRoll_ReturnsFalse()
    {
        var preroll = new Video();
        Assert.False(PlaybackScrobbleService.IsScrobblable(preroll));
    }

    [Fact]
    public void IsScrobblable_TrailerItem_ReturnsFalse()
    {
        var trailer = new Trailer();
        Assert.False(PlaybackScrobbleService.IsScrobblable(trailer));
    }
}

