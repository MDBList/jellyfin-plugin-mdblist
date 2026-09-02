using Jellyfin.Plugin.MDBList.Events;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Exercises <see cref="PlaybackScrobbleService.ComputeProgress"/>, the
/// position-ticks-to-percent conversion behind every /scrobble/* payload.
/// </summary>
public class ScrobbleProgressTests
{
    [Fact]
    public void ComputeProgress_Halfway_Returns50()
    {
        Assert.Equal(50.0, PlaybackScrobbleService.ComputeProgress(runTimeTicks: 10_000_000, positionTicks: 5_000_000));
    }

    [Fact]
    public void ComputeProgress_MissingRuntime_ReturnsNull()
    {
        Assert.Null(PlaybackScrobbleService.ComputeProgress(runTimeTicks: null, positionTicks: 5_000_000));
    }

    [Fact]
    public void ComputeProgress_ZeroRuntime_ReturnsNull()
    {
        Assert.Null(PlaybackScrobbleService.ComputeProgress(runTimeTicks: 0, positionTicks: 0));
    }

    [Fact]
    public void ComputeProgress_MissingPosition_ReturnsNull()
    {
        Assert.Null(PlaybackScrobbleService.ComputeProgress(runTimeTicks: 10_000_000, positionTicks: null));
    }

    [Fact]
    public void ComputeProgress_PositionBeyondRuntime_ClampsTo100()
    {
        Assert.Equal(100.0, PlaybackScrobbleService.ComputeProgress(runTimeTicks: 10_000_000, positionTicks: 15_000_000));
    }

    [Fact]
    public void ComputeProgress_NegativePosition_ClampsToZero()
    {
        Assert.Equal(0.0, PlaybackScrobbleService.ComputeProgress(runTimeTicks: 10_000_000, positionTicks: -5000));
    }
}
