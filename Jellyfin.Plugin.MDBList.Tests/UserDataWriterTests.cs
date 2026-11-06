using Jellyfin.Plugin.MDBList.Library;
using Xunit;

namespace Jellyfin.Plugin.MDBList.Tests;

public class UserDataWriterTests
{
    [Fact]
    public void FindDuplicateKeys_ReportsEachRepeatedKeyOnce()
    {
        var keys = new[] { "81189001001", "81189001001", "1396", "81189001001", "abc" };

        Assert.Equal(new[] { "81189001001" }, UserDataWriter.FindDuplicateKeys(keys));
    }

    [Fact]
    public void FindDuplicateKeys_IsCaseSensitiveLikeEfCore()
    {
        Assert.Empty(UserDataWriter.FindDuplicateKeys(new[] { "tt0903747", "TT0903747" }));
    }
}
