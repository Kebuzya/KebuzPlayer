using KebuzPlayer.Infrastructure;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(0, 1, 0);

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("v1", "1.0.0")]
    [InlineData("V1.2.3-beta", "1.2.3")]
    [InlineData("1.2.3+build", "1.2.3")]
    [InlineData("0.1.0.7", "0.1.0")]
    public void ParseVersion_ReadsTagsInCommonShapes(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void ParseVersion_RejectsGarbage(string? tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    [Fact]
    public void ParseRelease_TakesTagAndTrustedPage()
    {
        var info = UpdateChecker.ParseRelease(
            """{"tag_name":"v0.2.0","html_url":"https://github.com/Kebuzya/KebuzPlayer/releases/tag/v0.2.0"}""");

        Assert.NotNull(info);
        Assert.Equal(new Version(0, 2, 0), info!.Version);
        Assert.Equal("https://github.com/Kebuzya/KebuzPlayer/releases/tag/v0.2.0", info.Url);
    }

    [Fact]
    public void ParseRelease_ReplacesForeignLinkWithReleasesPage()
    {
        var info = UpdateChecker.ParseRelease(
            """{"tag_name":"v0.2.0","html_url":"https://example.com/evil"}""");

        Assert.Equal(UpdateChecker.ReleasesPage, info!.Url);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":5}""")]
    public void ParseRelease_ReturnsNullOnUnexpectedPayload(string json)
    {
        Assert.Null(UpdateChecker.ParseRelease(json));
    }

    [Fact]
    public void ShouldCheck_OncePerDay()
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(UpdateChecker.ShouldCheck(null, now));
        Assert.False(UpdateChecker.ShouldCheck(now.AddHours(-23), now));
        Assert.True(UpdateChecker.ShouldCheck(now.AddHours(-24), now));
        Assert.True(UpdateChecker.ShouldCheck(now.AddDays(3), now));
    }

    [Fact]
    public void Banner_ShowsOnlyForNewerVersionNotYetDismissed()
    {
        Assert.True(UpdateChecker.ShouldShowBanner("0.2.0", null, Current));
        Assert.False(UpdateChecker.ShouldShowBanner("0.2.0", "0.2.0", Current));
        Assert.True(UpdateChecker.ShouldShowBanner("0.3.0", "0.2.0", Current));
        Assert.False(UpdateChecker.ShouldShowBanner("0.1.0", null, Current));
        Assert.False(UpdateChecker.ShouldShowBanner(null, null, Current));
    }

    [Fact]
    public void Available_GoesAwayOnceInstalledVersionCatchesUp()
    {
        Assert.True(UpdateChecker.IsAvailable("0.2.0", Current));
        Assert.False(UpdateChecker.IsAvailable("0.2.0", new Version(0, 2, 0, 0)));
    }

    [Fact]
    public void Settings_KeepUpdateStateAcrossRestart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            string file = Path.Combine(dir, "settings.json");
            var first = new JsonSettingsService(file);
            Assert.True(first.Current.CheckForUpdates);
            var checkedAt = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);
            first.Current.CheckForUpdates = false;
            first.Current.LastUpdateCheckUtc = checkedAt;
            first.Current.AvailableUpdateVersion = "0.2.0";
            first.Current.AvailableUpdateUrl = UpdateChecker.ReleasesPage;
            first.Current.DismissedUpdateVersion = "0.2.0";
            first.Save();

            var second = new JsonSettingsService(file).Current;
            Assert.False(second.CheckForUpdates);
            Assert.Equal(checkedAt, second.LastUpdateCheckUtc!.Value.ToUniversalTime());
            Assert.Equal("0.2.0", second.AvailableUpdateVersion);
            Assert.Equal(UpdateChecker.ReleasesPage, second.AvailableUpdateUrl);
            Assert.Equal("0.2.0", second.DismissedUpdateVersion);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
