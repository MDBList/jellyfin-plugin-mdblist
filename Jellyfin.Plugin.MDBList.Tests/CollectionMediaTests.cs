using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Library;
using Jellyfin.Plugin.MDBList.Sync;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

public class CollectionMediaTests
{
    [Theory]
    [InlineData(3840, 2160, false, "uhd_4k")]
    [InlineData(3840, 1600, false, "uhd_4k")]
    [InlineData(1920, 800, false, "hd_1080p")]
    [InlineData(1920, 1080, true, "hd_1080i")]
    [InlineData(1280, 536, false, "hd_720p")]
    [InlineData(720, 576, false, "sd_576p")]
    [InlineData(720, 480, true, "sd_480i")]
    [InlineData(640, 360, false, "sd_480p")]
    public void Resolution_FromSize(int width, int height, bool interlaced, string expected)
    {
        Assert.Equal(expected, MediaInfoMapper.Resolution(width, height, interlaced));
    }

    [Fact]
    public void Resolution_UnknownSize_IsNull()
    {
        Assert.Null(MediaInfoMapper.Resolution(null, null, false));
    }

    [Theory]
    [InlineData("DOVI", "dolby_vision")]
    [InlineData("DOVIWithHDR10", "dolby_vision")]
    [InlineData("DOVIWithHDR10Plus", "dolby_vision")]
    [InlineData("DOVIWithSDR", "dolby_vision")]
    [InlineData("HDR10Plus", "hdr10_plus")]
    [InlineData("HDR10", "hdr10")]
    [InlineData("HLG", "hlg")]
    [InlineData("SDR", null)]
    [InlineData("Unknown", null)]
    [InlineData("DOVIInvalid", null)]
    public void Hdr_FromVideoRangeType(string rangeType, string? expected)
    {
        Assert.Equal(expected, MediaInfoMapper.Hdr(rangeType));
    }

    [Theory]
    [InlineData("truehd", "Dolby TrueHD + Dolby Atmos", "dolby_atmos")]
    [InlineData("truehd", null, "dolby_truehd")]
    [InlineData("eac3", "Dolby Digital Plus + Dolby Atmos", "dolby_digital_plus_atmos")]
    [InlineData("eac3", null, "dolby_digital_plus")]
    [InlineData("ac3", null, "dolby_digital")]
    [InlineData("dts", "DTS-HD MA + DTS:X", "dts_x")]
    [InlineData("dts", "DTS-HD MA", "dts_ma")]
    [InlineData("dts", "DTS-HD HRA", "dts_hr")]
    [InlineData("dts", "DTS", "dts")]
    [InlineData("aac", "LC", "aac")]
    [InlineData("opus", null, "ogg_opus")]
    [InlineData("pcm_s24le", null, "lpcm")]
    [InlineData("weird", null, null)]
    public void Audio_FromCodecAndProfile(string codec, string? profile, string? expected)
    {
        Assert.Equal(expected, MediaInfoMapper.Audio(codec, profile));
    }

    [Theory]
    [InlineData(2, "2.0")]
    [InlineData(6, "5.1")]
    [InlineData(8, "7.1")]
    [InlineData(12, null)]
    public void Channels_FromCount(int channels, string? expected)
    {
        Assert.Equal(expected, MediaInfoMapper.Channels(channels));
    }

    private static MediaCopy Copy(string key, string? resolution, string? hdr = null)
    {
        return new MediaCopy { CopyKey = key, Resolution = resolution, Hdr = hdr };
    }

    [Fact]
    public void Fingerprint_IgnoresOrder()
    {
        Assert.Equal(
            MediaCopy.Fingerprint([Copy("a", "uhd_4k"), Copy("b", "hd_1080p")]),
            MediaCopy.Fingerprint([Copy("b", "hd_1080p"), Copy("a", "uhd_4k")]));
    }

    [Fact]
    public void MediaChanged_DetectsAnUpgrade()
    {
        var known = new KnownSyncItem { Copies = [Copy("a", "hd_1080p")] };
        var current = new KnownSyncItem { Copies = [Copy("a", "uhd_4k", "hdr10")] };

        Assert.True(CollectionSync.MediaChanged(known, current));
        Assert.False(CollectionSync.MediaChanged(current, new KnownSyncItem { Copies = [Copy("a", "uhd_4k", "hdr10")] }));
    }

    [Fact]
    public void MediaChanged_StateFromBeforeMediaInfo_PushesItemsWithMedia_Once()
    {
        // State written by an older version has no copies at all.
        Assert.True(CollectionSync.MediaChanged(new KnownSyncItem(), new KnownSyncItem { Copies = [Copy("a", "hd_720p")] }));
        Assert.False(CollectionSync.MediaChanged(new KnownSyncItem(), new KnownSyncItem { Copies = [] }));
    }

    [Fact]
    public void MediaChanged_NoCopiesNow_IsNotAChange()
    {
        // Not probed yet or being replaced: MDBList keeps its copies until the item is read again.
        var current = new KnownSyncItem { Copies = [] };

        Assert.False(CollectionSync.MediaChanged(new KnownSyncItem { Copies = [Copy("a", "hd_1080p")] }, current));
        var entry = new JsonObject();
        CollectionSync.AddMediaFields(current, entry);
        Assert.False(entry.ContainsKey("copies"));
    }

    private static SnapshotItem Record(Guid id)
    {
        return new SnapshotItem { Type = "movie", ItemId = id, Ids = new MediaIds { Tmdb = 1 } };
    }

    [Fact]
    public void AddItem_OneUnreadableVersion_KeepsTheReadableCopies()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new Dictionary<string, KnownSyncItem>();
        IReadOnlyList<MediaCopy>? Read(SnapshotItem r) => r.ItemId == a ? [Copy("a", "hd_1080p")] : null;

        CollectionSync.AddItem(items, "movie:tmdb:1", Record(a), Read);
        CollectionSync.AddItem(items, "movie:tmdb:1", Record(b), Read);
        Assert.Equal(["a"], items["movie:tmdb:1"].Copies!.Select(c => c.CopyKey));

        items.Clear();
        CollectionSync.AddItem(items, "movie:tmdb:1", Record(b), Read);
        CollectionSync.AddItem(items, "movie:tmdb:1", Record(a), Read);
        Assert.Equal(["a"], items["movie:tmdb:1"].Copies!.Select(c => c.CopyKey));
    }

    [Fact]
    public void AddItem_AThrowingRead_StillAddsTheItem()
    {
        var items = new Dictionary<string, KnownSyncItem>();
        IReadOnlyList<MediaCopy>? Read(SnapshotItem r) => throw new MissingMethodException();

        CollectionSync.AddItem(items, "movie:tmdb:1", Record(Guid.NewGuid()), Read);

        Assert.Null(items["movie:tmdb:1"].Copies);
    }

    [Fact]
    public void AddItem_AVersionsOwnReadWinsOverASiblingsCachedOne()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new Dictionary<string, KnownSyncItem>();
        IReadOnlyList<MediaCopy>? Read(SnapshotItem r) => r.ItemId == a
            ? [new MediaCopy { CopyKey = "a", Resolution = "hd_1080p", IsOwn = true }, new MediaCopy { CopyKey = "b", Resolution = "hd_1080p" }]
            : [new MediaCopy { CopyKey = "b", Resolution = "uhd_4k", IsOwn = true }];

        CollectionSync.AddItem(items, "movie:tmdb:1", Record(a), Read);
        CollectionSync.AddItem(items, "movie:tmdb:1", Record(b), Read);

        Assert.Equal("uhd_4k", items["movie:tmdb:1"].Copies!.Single(c => c.CopyKey == "b").Resolution);
    }

    [Fact]
    public void AddItem_MergesVersionsOfOneTitle()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new Dictionary<string, KnownSyncItem>();
        IReadOnlyList<MediaCopy>? Read(SnapshotItem r) => r.ItemId == a ? [Copy("a", "hd_1080p")] : [Copy("b", "uhd_4k")];

        CollectionSync.AddItem(items, "movie:tmdb:1", Record(a), Read);
        CollectionSync.AddItem(items, "movie:tmdb:1", Record(b), Read);

        Assert.Equal(["a", "b"], items["movie:tmdb:1"].Copies!.Select(c => c.CopyKey));
    }

    [Fact]
    public void MediaChanged_UnreadableCopies_AreNotAChange()
    {
        Assert.False(CollectionSync.MediaChanged(new KnownSyncItem { Copies = [Copy("a", "hd_1080p")] }, new KnownSyncItem { Copies = null }));
    }

    [Fact]
    public void AddMediaFields_SendsCopiesWithoutUnknownValues()
    {
        var entry = new JsonObject();
        CollectionSync.AddMediaFields(new KnownSyncItem { Copies = [new MediaCopy { CopyKey = "a", Resolution = "uhd_4k", Is3D = true }] }, entry);

        Assert.Equal("""[{"copy_key":"a","resolution":"uhd_4k","3d":true}]""", entry["copies"]!.ToJsonString());
    }

    [Fact]
    public void AddMediaFields_NoCopies_LeavesStoredCopiesAlone()
    {
        var entry = new JsonObject();
        CollectionSync.AddMediaFields(new KnownSyncItem { Copies = [] }, entry);

        Assert.False(entry.ContainsKey("copies"));
    }

    [Fact]
    public void BuildShowsPayload_AddsEpisodeCopies()
    {
        var items = new List<KnownSyncItem>
        {
            new() { Type = "episode", Ids = new MediaIds { Tvdb = 1 }, Season = 1, Episode = 2, Copies = [Copy("x", "hd_720p")] },
        };

        var shows = SyncPayloadBuilder.BuildShowsPayload(items, "collected_at", _ => null, CollectionSync.AddMediaFields);

        var episode = shows[0]!["seasons"]![0]!["episodes"]![0]!;
        Assert.Equal("hd_720p", episode["copies"]![0]!["resolution"]!.GetValue<string>());
    }

    [Fact]
    public void NullCopies_AreNotWrittenToTheStateFile()
    {
        // Watched/ratings entries never have copies; they shouldn't grow the file.
        Assert.DoesNotContain("Copies", JsonSerializer.Serialize(new KnownSyncItem { Type = "movie" }));
    }

    [Fact]
    public void Copies_RoundTripThroughTheStateFileSerializer()
    {
        // Same options as SyncStateStore.
        var options = new JsonSerializerOptions { WriteIndented = true, PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate };
        var item = new KnownSyncItem { Type = "movie", Copies = [Copy("a", "uhd_4k", "dolby_vision")] };

        var loaded = JsonSerializer.Deserialize<KnownSyncItem>(JsonSerializer.Serialize(item, options), options)!;

        Assert.False(CollectionSync.MediaChanged(item, loaded));
        Assert.Null(JsonSerializer.Deserialize<KnownSyncItem>("""{"Type":"movie"}""", options)!.Copies);
    }
}
