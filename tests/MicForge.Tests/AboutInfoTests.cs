namespace MicForge.Tests;

public sealed class AboutInfoTests
{
    [Fact]
    public void Links_AreTheAuthorsAndThisRepo()
    {
        Assert.Equal("https://www.instagram.com/lukas9krejci/", AboutInfo.Instagram.Url);
        Assert.Equal("@lukas9krejci", AboutInfo.Instagram.Detail);
        Assert.Equal("https://github.com/lukr-99", AboutInfo.GitHub.Url);
        Assert.Equal("https://github.com/lukr-99/MicForge/issues", AboutInfo.Issues.Url);
        Assert.Equal("Made by Lukáš Krejčí", AboutInfo.AuthorLine);
    }

    [Fact]
    public void EveryUrl_IsHttpsAndKnown()
    {
        foreach (var url in AboutInfo.Links.Select(l => l.Url).Concat(AboutInfo.Credits.Select(c => c.Url)))
        {
            Assert.StartsWith("https://", url);
            Assert.True(AboutInfo.IsKnownUrl(url));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("https://example.com/")]
    public void UnknownUrls_AreRejected(string? url) => Assert.False(AboutInfo.IsKnownUrl(url!));

    [Fact]
    public void Text_HasNoEmDashes()
    {
        var all = new List<string> { AboutInfo.Tagline, AboutInfo.Description, AboutInfo.FeedbackLine };
        all.AddRange(AboutInfo.Facts("1.0.0").SelectMany(f => new[] { f.Label, f.Value }));
        all.AddRange(AboutInfo.Credits.SelectMany(c => new[] { c.Name, c.Role, c.License }));
        Assert.All(all, text => Assert.DoesNotContain('\u2014', text));
    }

    [Fact]
    public void Facts_StartWithTheVersion() =>
        Assert.Equal(new AboutFact("Version", "v2.3.4"), AboutInfo.Facts("2.3.4")[0]);

    [Fact]
    public void Credits_ListTheOwnLicenceAndShippedParts()
    {
        var names = AboutInfo.Credits.Select(c => c.Name).ToList();
        Assert.Contains("MicForge", names);
        Assert.Contains("RNNoise", names);
        Assert.Contains("NAudio", names);
        Assert.Equal("PolyForm Noncommercial 1.0.0", AboutInfo.Credits.First(c => c.Name == "MicForge").License);
    }
}
