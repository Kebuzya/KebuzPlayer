using KebuzPlayer.Files;

namespace KebuzPlayer.Core.Tests;

public class FileNameHelperTests
{
    [Theory]
    [InlineData("AC/DC", "AC_DC")]
    [InlineData("what?", "what_")]
    [InlineData("a:b*c", "a_b_c")]
    [InlineData("<pipe>|", "_pipe__")]
    [InlineData("back\\slash", "back_slash")]
    [InlineData("quo\"te", "quo_te")]
    public void SanitizeFileName_ReplacesInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FileNameHelper.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_KeepsValidName()
    {
        Assert.Equal("Обычное имя 123", FileNameHelper.SanitizeFileName("Обычное имя 123"));
    }

    [Fact]
    public void BuildFileName_WithArtist()
    {
        Assert.Equal("Artist - Title.mp3", FileNameHelper.BuildFileName("Artist", "Title", ".mp3"));
    }

    [Fact]
    public void BuildFileName_WithoutArtist()
    {
        Assert.Equal("Title.flac", FileNameHelper.BuildFileName("", "Title", ".flac"));
    }

    [Fact]
    public void BuildFileName_SanitizesCombinedName()
    {
        Assert.Equal("AC_DC - T.N.T_.mp3", FileNameHelper.BuildFileName("AC/DC", "T.N.T?", ".mp3"));
    }
}
