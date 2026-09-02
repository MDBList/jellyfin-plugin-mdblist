using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.MDBList.Api.Models;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

/// <summary>
/// Deserializes raw /sync/journal wire-format JSON, the same shape
/// api.mdblist's SyncJournalView returns, without any HTTP call --
/// specifically covers the value_at/action_at distinction (a real bug once:
/// see WatchedSync.PullIncrementalAsync's remoteAt fallback) and the
/// requires_full_sync early-exit flag.
/// </summary>
public class JournalParsingTests
{
    // Matches MDBListApiClient's own deserialize options: System.Text.Json
    // does not populate a get-only Collection{T}/Dictionary{K,V} property
    // (JournalPage.Journal among them) without this, silently coming back
    // empty instead of throwing -- see JournalPage_EpisodeRow test below.
    private static readonly JsonSerializerOptions Options = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    [Fact]
    public void JournalEntry_AddRow_HasBothActionAtAndValueAt()
    {
        const string Json = """
            {
                "category": "watched",
                "item_type": "movie",
                "status": "active",
                "action_at": "2026-01-02T00:00:00Z",
                "value_at": "2026-01-01T00:00:00Z",
                "ids": { "imdb": "tt0133093" }
            }
            """;

        var entry = JsonSerializer.Deserialize<JournalEntry>(Json);

        Assert.NotNull(entry);
        Assert.Equal("2026-01-02T00:00:00Z", entry!.ActionAt);
        Assert.Equal("2026-01-01T00:00:00Z", entry.ValueAt);
        Assert.Equal("tt0133093", entry.Ids?.Imdb);
    }

    [Fact]
    public void JournalEntry_RemovalRow_HasNoValueAt()
    {
        // Confirmed against api.mdblist's _remove_movies/_remove_shows/etc.:
        // removal rows only ever write action_at, never value_at. Conflict
        // resolution must fall back to action_at for these rows instead of
        // treating a missing value_at as "no timestamp at all".
        const string Json = """
            {
                "category": "watched",
                "item_type": "movie",
                "status": "removed",
                "action_at": "2026-01-03T00:00:00Z",
                "value_at": null,
                "ids": { "imdb": "tt0133093" }
            }
            """;

        var entry = JsonSerializer.Deserialize<JournalEntry>(Json);

        Assert.NotNull(entry);
        Assert.Null(entry!.ValueAt);
        Assert.Equal("2026-01-03T00:00:00Z", entry.ActionAt);

        var effectiveTimestamp = entry.ValueAt ?? entry.ActionAt;
        Assert.Equal("2026-01-03T00:00:00Z", effectiveTimestamp);
    }

    [Fact]
    public void JournalPage_RequiresFullSync_ParsesTrue()
    {
        const string Json = """
            {
                "requires_full_sync": true,
                "journal": [],
                "journal_oldest_at": null,
                "pagination": null
            }
            """;

        var page = JsonSerializer.Deserialize<JournalPage>(Json, Options);

        Assert.NotNull(page);
        Assert.True(page!.RequiresFullSync);
        Assert.Empty(page.Journal);
    }

    [Fact]
    public void JournalPage_EpisodeRow_ParsesSeasonAndEpisode()
    {
        const string Json = """
            {
                "requires_full_sync": false,
                "journal": [
                    {
                        "category": "watched",
                        "item_type": "episode",
                        "status": "active",
                        "action_at": "2026-01-01T00:00:00Z",
                        "value_at": "2026-01-01T00:00:00Z",
                        "ids": { "tvdb": 81189 },
                        "season": 1,
                        "episode": 2
                    }
                ]
            }
            """;

        var page = JsonSerializer.Deserialize<JournalPage>(Json, Options);

        Assert.NotNull(page);
        var entry = Assert.Single(page!.Journal);
        Assert.Equal(1, entry.Season);
        Assert.Equal(2, entry.Episode);
        Assert.Equal(81189, entry.Ids?.Tvdb);
    }
}
