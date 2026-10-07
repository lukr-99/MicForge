using System.IO;

namespace MicForge.Tests;

public sealed class SettingsCompatibilityTests
{
    [Fact]
    public void FromJson_PreThemeProfile_PreservesSettingsAndUsesDark()
    {
        var settings = Settings.FromJson("""
            {"InputDeviceId":"mic-1","MonitorDeviceId":"phones-2","MonitorEnabled":true,
             "AutoStartProcessing":true,"StartMinimized":false,"LastPreset":"My voice",
             "StageOrder":["eq","compressor"],"InputGainDb":4.5}
            """);
        Assert.NotNull(settings);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("mic-1", settings.InputDeviceId);
        Assert.Equal("phones-2", settings.MonitorDeviceId);
        Assert.True(settings.MonitorEnabled);
        Assert.True(settings.AutoStartProcessing);
        Assert.False(settings.StartMinimized);
        Assert.Equal("My voice", settings.LastPreset);
        Assert.Equal(new[] { "eq", "compressor" }, settings.StageOrder);
        Assert.Equal(4.5, settings.InputGainDb);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Save_ThemeAndExistingProfile_RoundTrips(string theme)
    {
        var path = Path.Combine(Path.GetTempPath(), $"micforge-test-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { Theme = theme, LastPreset = "Voice", MonitorDeviceId = "Headphones", StartMinimized = false }.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(theme, loaded.Theme);
            Assert.Equal("Voice", loaded.LastPreset);
            Assert.Equal("Headphones", loaded.MonitorDeviceId);
            Assert.False(loaded.StartMinimized);
        }
        finally { File.Delete(path); }
    }
}
