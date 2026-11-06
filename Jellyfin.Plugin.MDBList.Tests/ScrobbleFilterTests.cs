using Jellyfin.Plugin.MDBList.Events;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Exercises <see cref="PlaybackScrobbleService.IsScrobblable"/>: pre-rolls,
/// extras and anything not a movie or episode are ignored before they touch
/// session state. Based on the tests in PR #3 by mattsigal.
/// </summary>
public class ScrobbleFilterTests
{
    [Fact]
    public void IsScrobblable_NullItem_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsScrobblable(null));
    }

    [Fact]
    public void IsScrobblable_Movie_ReturnsTrue()
    {
        Assert.True(PlaybackScrobbleService.IsScrobblable(new Movie()));
    }

    [Fact]
    public void IsScrobblable_Episode_ReturnsTrue()
    {
        Assert.True(PlaybackScrobbleService.IsScrobblable(new Episode()));
    }

    [Fact]
    public void IsScrobblable_MovieExtra_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsScrobblable(new Movie { ExtraType = ExtraType.Trailer }));
        Assert.False(PlaybackScrobbleService.IsScrobblable(new Movie { ExtraType = ExtraType.Clip }));
    }

    [Fact]
    public void IsScrobblable_EpisodeExtra_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsScrobblable(new Episode { ExtraType = ExtraType.BehindTheScenes }));
    }

    [Fact]
    public void IsScrobblable_PlainVideoPreRoll_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsScrobblable(new Video()));
    }

    [Fact]
    public void IsScrobblable_Trailer_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsScrobblable(new Trailer()));
    }
}
