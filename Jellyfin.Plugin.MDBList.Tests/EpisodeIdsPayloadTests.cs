using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.MDBList.Api.Models;
using Jellyfin.Plugin.MDBList.Sync;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

public class EpisodeIdsPayloadTests
{
    [Fact]
    public void BuildShowsPayload_SendsEpisodeIdsWhenKnown()
    {
        var show = new MediaIds { Tmdb = 1429, Tvdb = 267440 };
        var items = new[]
        {
            new KnownSyncItem { Type = "episode", Ids = show, Season = 1, Episode = 3, EpisodeIds = new MediaIds { Tvdb = 20003 }, WatchedAt = "2024-01-02T03:04:05" },
            new KnownSyncItem { Type = "episode", Ids = show, Season = 1, Episode = 4, WatchedAt = "2024-01-03T03:04:05" },
        };

        var shows = SyncPayloadBuilder.BuildShowsPayload(items, "watched_at", item => JsonValue.Create(item.WatchedAt));

        var episodes = shows[0]!["seasons"]![0]!["episodes"]!.AsArray();
        Assert.Equal(20003, episodes[0]!["ids"]!["tvdb"]!.GetValue<int>());
        Assert.False(episodes[0]!["ids"]!.AsObject().ContainsKey("tmdb"));
        Assert.Equal(3, episodes[0]!["number"]!.GetValue<int>());
        Assert.False(episodes[1]!.AsObject().ContainsKey("ids"));
    }

    [Fact]
    public void ScrobbleEpisodeRef_OmitsIdsWhenUnknown()
    {
        var withoutIds = JsonSerializer.Serialize(new ScrobbleEpisodeRef { Number = 3 });
        var withIds = JsonSerializer.Serialize(new ScrobbleEpisodeRef { Number = 3, Ids = new MediaIds { Tvdb = 20003 } });

        Assert.DoesNotContain("\"ids\"", withoutIds, System.StringComparison.Ordinal);
        Assert.Contains("\"ids\":{", withIds, System.StringComparison.Ordinal);
        Assert.Contains("\"tvdb\":20003", withIds, System.StringComparison.Ordinal);
    }
}
