using System;
using Jellyfin.Plugin.MDBList.Events;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Exercises <see cref="PlaybackScrobbleService.IsSeek"/>, which lets a seek
/// through the 15-minute floor on periodic progress pushes.
/// </summary>
public class SeekDetectionTests
{
    private static readonly DateTime SentAt = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsSeek_SteadyPlayback_ReturnsFalse()
    {
        var position = TimeSpan.FromMinutes(10).Ticks;
        Assert.False(PlaybackScrobbleService.IsSeek(position, SentAt, position + TimeSpan.FromMinutes(5).Ticks, SentAt.AddMinutes(5)));
    }

    [Fact]
    public void IsSeek_SmallDrift_ReturnsFalse()
    {
        var position = TimeSpan.FromMinutes(10).Ticks;
        Assert.False(PlaybackScrobbleService.IsSeek(position, SentAt, position + TimeSpan.FromSeconds(280).Ticks, SentAt.AddMinutes(5)));
    }

    [Fact]
    public void IsSeek_JumpForward_ReturnsTrue()
    {
        var position = TimeSpan.FromMinutes(10).Ticks;
        Assert.True(PlaybackScrobbleService.IsSeek(position, SentAt, TimeSpan.FromMinutes(85).Ticks, SentAt.AddSeconds(20)));
    }

    [Fact]
    public void IsSeek_JumpBack_ReturnsTrue()
    {
        var position = TimeSpan.FromMinutes(40).Ticks;
        Assert.True(PlaybackScrobbleService.IsSeek(position, SentAt, TimeSpan.FromMinutes(5).Ticks, SentAt.AddSeconds(20)));
    }

    [Fact]
    public void IsSeek_MissingPosition_ReturnsFalse()
    {
        Assert.False(PlaybackScrobbleService.IsSeek(null, SentAt, TimeSpan.FromMinutes(5).Ticks, SentAt.AddSeconds(20)));
        Assert.False(PlaybackScrobbleService.IsSeek(TimeSpan.FromMinutes(5).Ticks, SentAt, null, SentAt.AddSeconds(20)));
    }
}
