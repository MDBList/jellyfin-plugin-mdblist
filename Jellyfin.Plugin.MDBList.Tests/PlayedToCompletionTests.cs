using System;
using Jellyfin.Plugin.MDBList.Events;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Exercises <see cref="LiveSyncService.IsPlayedToCompletion"/>, which picks
/// the <c>PlaybackFinished</c> saves whose watch time moves to the finish.
/// </summary>
public class PlayedToCompletionTests
{
    [Fact]
    public void IsPlayedToCompletion_PlayedWithPositionReset_ReturnsTrue()
    {
        Assert.True(LiveSyncService.IsPlayedToCompletion(played: true, positionTicks: 0));
    }

    [Fact]
    public void IsPlayedToCompletion_StoppedPartwayThroughRewatch_ReturnsFalse()
    {
        Assert.False(LiveSyncService.IsPlayedToCompletion(played: true, positionTicks: TimeSpan.FromMinutes(20).Ticks));
    }

    [Fact]
    public void IsPlayedToCompletion_NotPlayed_ReturnsFalse()
    {
        Assert.False(LiveSyncService.IsPlayedToCompletion(played: false, positionTicks: 0));
    }
}
