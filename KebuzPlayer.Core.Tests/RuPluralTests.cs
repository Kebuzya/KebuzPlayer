using KebuzPlayer.Infrastructure;

namespace KebuzPlayer.Core.Tests;

public class RuPluralTests
{
    [Theory]
    [InlineData(0, "0 треков")]
    [InlineData(1, "1 трек")]
    [InlineData(2, "2 трека")]
    [InlineData(4, "4 трека")]
    [InlineData(5, "5 треков")]
    [InlineData(11, "11 треков")]
    [InlineData(12, "12 треков")]
    [InlineData(14, "14 треков")]
    [InlineData(21, "21 трек")]
    [InlineData(22, "22 трека")]
    [InlineData(25, "25 треков")]
    [InlineData(101, "101 трек")]
    [InlineData(111, "111 треков")]
    public void Tracks_PluralizesCorrectly(int n, string expected) =>
        Assert.Equal(expected, RuPlural.Tracks(n));
}
