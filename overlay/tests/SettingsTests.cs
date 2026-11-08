using Xunit;

namespace LiveBackdrop.Tests;

public class SettingsTests
{
    [Fact]
    public void EmptyObjectYieldsDefaults()
    {
        var settings = Settings.Parse("{}");

        Assert.Equal(new Settings(), settings);
        Assert.True(settings.Enabled);
        Assert.Equal(BackdropSource.Wallpaper, settings.Source);
        Assert.Equal(Settings.DefaultOpacity, settings.Opacity);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(BlurMode.TextAreas, settings.Blur);
        Assert.True(settings.ClearWebPages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    public void UnreadableJsonYieldsDefaults(string json)
    {
        Assert.Equal(new Settings(), Settings.Parse(json));
    }

    [Fact]
    public void ReadsEveryField()
    {
        var settings = Settings.Parse("""
            {
              "version": 1,
              "enabled": false,
              "source": "image",
              "imagePath": "C:\\pictures\\forest.jpg",
              "opacity": 0.4,
              "startWithWindows": true,
              "clearWebPages": false,
              "overlayPath": "C:\\tools\\LiveBackdrop.exe"
            }
            """);

        Assert.False(settings.Enabled);
        Assert.Equal(BackdropSource.Image, settings.Source);
        Assert.Equal(@"C:\pictures\forest.jpg", settings.ImagePath);
        Assert.Equal(0.4, settings.Opacity);
        Assert.True(settings.StartWithWindows);
        Assert.False(settings.ClearWebPages);
    }

    [Theory]
    [InlineData("-1", 0)]
    [InlineData("2.5", 1)]
    [InlineData("0.25", 0.25)]
    public void ClampsOpacity(string raw, double expected)
    {
        Assert.Equal(expected, Settings.Parse($$"""{ "opacity": {{raw}} }""").Opacity);
    }

    [Theory]
    [InlineData("""{ "opacity": "high" }""")]
    [InlineData("""{ "enabled": "yes" }""")]
    [InlineData("""{ "source": 3 }""")]
    public void WrongTypesFallBackToDefaults(string json)
    {
        Assert.Equal(new Settings(), Settings.Parse(json));
    }

    [Theory]
    [InlineData("off", BlurMode.Off)]
    [InlineData("text", BlurMode.TextAreas)]
    [InlineData("everywhere", BlurMode.Everywhere)]
    [InlineData("max", BlurMode.TextAreas)]
    internal void ReadsBlurMode(string raw, BlurMode expected)
    {
        Assert.Equal(expected, Settings.Parse($$"""{ "blur": "{{raw}}" }""").Blur);
    }

    [Theory]
    [InlineData("-5", 0)]
    [InlineData("100", Settings.MaxBlurAmount)]
    [InlineData("16", 16)]
    public void ClampsBlurAmount(string raw, double expected)
    {
        Assert.Equal(expected, Settings.Parse($$"""{ "blurAmount": {{raw}} }""").BlurAmount);
    }

    [Fact]
    public void UnknownSourceMeansWallpaper()
    {
        Assert.Equal(BackdropSource.Wallpaper, Settings.Parse("""{ "source": "video" }""").Source);
    }

    [Fact]
    public void BlankImagePathIsNone()
    {
        Assert.Null(Settings.Parse("""{ "imagePath": "   " }""").ImagePath);
    }
}
