using System;
using Jellyfin.Plugin.MDBList.Sync;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Exercises <see cref="WatchedSync.ShouldApplyRemoteWatched"/>, the
/// last-write-wins matrix behind pull-side conflict resolution: local newer
/// / remote newer / exact tie / missing timestamps, crossed with add vs
/// remove.
/// </summary>
public class WatchedConflictResolutionTests
{
    private static readonly DateTime Earlier = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Add_RemoteNewerThanLocal_Applies()
    {
        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: false, localPlayCount: 1, localTs: Earlier, remoteTs: Later));
    }

    [Fact]
    public void Add_LocalNewerThanRemote_DoesNotApply()
    {
        Assert.False(WatchedSync.ShouldApplyRemoteWatched(removed: false, localPlayCount: 1, localTs: Later, remoteTs: Earlier));
    }

    [Fact]
    public void Add_ExactTie_RemoteWins()
    {
        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: false, localPlayCount: 1, localTs: Earlier, remoteTs: Earlier));
    }

    [Fact]
    public void Add_LocalNotYetWatched_AlwaysApplies()
    {
        // PlayCount 0 means there's no local timestamp to conflict with,
        // regardless of what localTs itself happens to hold.
        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: false, localPlayCount: 0, localTs: Later, remoteTs: Earlier));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "2026-01-01")]
    [InlineData("2026-01-01", null)]
    public void Add_MissingTimestampOnEitherSide_Applies(string? localTsRaw, string? remoteTsRaw)
    {
        var localTs = localTsRaw is null ? (DateTime?)null : DateTime.Parse(localTsRaw, System.Globalization.CultureInfo.InvariantCulture);
        var remoteTs = remoteTsRaw is null ? (DateTime?)null : DateTime.Parse(remoteTsRaw, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: false, localPlayCount: 1, localTs, remoteTs));
    }

    [Fact]
    public void Remove_RemoteNewerThanLocal_Applies()
    {
        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: true, localPlayCount: 1, localTs: Earlier, remoteTs: Later));
    }

    [Fact]
    public void Remove_LocalNewerThanRemote_DoesNotApply()
    {
        Assert.False(WatchedSync.ShouldApplyRemoteWatched(removed: true, localPlayCount: 1, localTs: Later, remoteTs: Earlier));
    }

    [Fact]
    public void Remove_ExactTie_RemoteWins()
    {
        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: true, localPlayCount: 1, localTs: Earlier, remoteTs: Earlier));
    }

    [Fact]
    public void Remove_AlreadyUnwatchedLocally_IsNoOp()
    {
        Assert.False(WatchedSync.ShouldApplyRemoteWatched(removed: true, localPlayCount: 0, localTs: null, remoteTs: Later));
    }

    [Fact]
    public void Remove_MissingRemoteTimestamp_Applies()
    {
        Assert.True(WatchedSync.ShouldApplyRemoteWatched(removed: true, localPlayCount: 1, localTs: Earlier, remoteTs: null));
    }
}
