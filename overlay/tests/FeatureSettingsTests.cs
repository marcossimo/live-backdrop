using Xunit;

namespace LiveBackdrop.Tests;

public class FeatureSettingsTests
{
    [Fact]
    public void NewFieldsHaveDefaults()
    {
        var settings = Settings.Parse("{}");

        Assert.True(settings.TrayIcon);
        Assert.Equal(Settings.DefaultHotkey, settings.Hotkey);
        Assert.Equal(0, settings.MaxFps);
        Assert.True(settings.PauseOnBattery);
        Assert.False(settings.PauseInBackground);
        Assert.True(settings.ReactToClaude);
        Assert.False(settings.MatchWallpaperColors);
    }

    [Fact]
    public void ReadsNewFields()
    {
        var settings = Settings.Parse("""
            {
              "trayIcon": false,
              "hotkey": " Win+Shift+F9 ",
              "maxFps": 15,
              "pauseOnBattery": false,
              "pauseInBackground": true,
              "reactToClaude": false,
              "colorMode": "wallpaper"
            }
            """);

        Assert.False(settings.TrayIcon);
        Assert.Equal("Win+Shift+F9", settings.Hotkey);
        Assert.Equal(15, settings.MaxFps);
        Assert.False(settings.PauseOnBattery);
        Assert.True(settings.PauseInBackground);
        Assert.False(settings.ReactToClaude);
        Assert.True(settings.MatchWallpaperColors);
    }

    [Theory]
    [InlineData("-3", 0)]
    [InlineData("500", Settings.MaxFrameRate)]
    [InlineData("14.6", 15)]
    public void ClampsFrameRate(string raw, int expected)
    {
        Assert.Equal(expected, Settings.Parse($$"""{ "maxFps": {{raw}} }""").MaxFps);
    }

    [Fact]
    public void EmptyHotkeyMeansNone()
    {
        Assert.Equal("", Settings.Parse("""{ "hotkey": "" }""").Hotkey);
        Assert.Equal(Settings.DefaultHotkey, Settings.Parse("""{ "hotkey": null }""").Hotkey);
    }

    [Fact]
    public void ManualColorModeDoesNotSample()
    {
        Assert.False(Settings.Parse("""{ "colorMode": "manual" }""").MatchWallpaperColors);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+B", Hotkey.Control | Hotkey.Alt, (uint)Keys.B)]
    [InlineData("ctrl + shift + f9", Hotkey.Control | Hotkey.Shift, (uint)Keys.F9)]
    [InlineData("Win+Alt+1", Hotkey.Windows | Hotkey.Alt, (uint)Keys.D1)]
    [InlineData("Control+Space", Hotkey.Control, (uint)Keys.Space)]
    public void ParsesCombinations(string text, uint modifiers, uint key)
    {
        Assert.Equal(new Hotkey(modifiers, key), Hotkey.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("B")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+B+C")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+65")]
    [InlineData("Ctrl+ShiftKey")]
    public void RejectsWhatCannotBeAHotkey(string? text)
    {
        Assert.Null(Hotkey.Parse(text));
    }
}

public class WallpaperColorsTests
{
    private static byte[] Fill(int count, params (byte R, byte G, byte B, int Share)[] colors)
    {
        var pixels = new List<byte>();
        foreach (var (r, g, b, share) in colors)
        {
            for (var i = 0; i < count * share / 100; i++)
            {
                pixels.AddRange([b, g, r, 255]);
            }
        }

        return [.. pixels];
    }

    [Fact]
    public void PicksTheMostVividHueOverAGreyBackground()
    {
        var colors = WallpaperColors.FromPixels(Fill(1024, (40, 40, 40, 70), (200, 30, 120, 30)));

        var (hue, saturation, _) = colors.Accent.ToHsv();
        Assert.InRange(hue, 300, 345);
        Assert.True(saturation >= 0.45);
    }

    [Fact]
    public void AGreyPictureGivesAToneOfGrey()
    {
        var colors = WallpaperColors.FromPixels(Fill(1024, (128, 128, 128, 100)));

        Assert.Equal(colors.Accent.R, colors.Accent.G);
        Assert.Equal(colors.Accent.G, colors.Accent.B);
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(10, 30, 200)]
    [InlineData(250, 220, 40)]
    public void TheAccentStaysUsableAndItsTextReadable(byte r, byte g, byte b)
    {
        var colors = WallpaperColors.FromPixels(Fill(256, (r, g, b, 100)));

        var (_, _, value) = colors.Accent.ToHsv();
        Assert.InRange(value, 0.34, 0.79);
        Assert.True(Rgb.Contrast(colors.Accent, colors.Text) >= 3);
    }

    [Fact]
    public void TextIsWhiteOnDarkAndBlackOnLight()
    {
        Assert.Equal(Rgb.White, WallpaperColors.ReadableOn(new Rgb(30, 40, 90)));
        Assert.Equal(Rgb.Black, WallpaperColors.ReadableOn(new Rgb(240, 220, 120)));
    }

    [Fact]
    public void SmallDriftIsNotWorthWriting()
    {
        var a = new WallpaperColors(new Rgb(100, 50, 200), Rgb.White, new Rgb(20, 20, 20));
        var drifted = a with { Accent = new Rgb(104, 52, 196) };
        var changed = a with { Accent = new Rgb(200, 50, 100) };

        Assert.True(a.DiffersFrom(null));
        Assert.False(drifted.DiffersFrom(a));
        Assert.True(changed.DiffersFrom(a));
    }

    [Fact]
    public void HsvRoundTrips()
    {
        var color = new Rgb(37, 99, 235);
        var (h, s, v) = color.ToHsv();

        Assert.True(Rgb.Distance(color, Rgb.FromHsv(h, s, v)) < 2);
    }

    [Fact]
    public void WritesHexColours()
    {
        var json = new WallpaperColors(new Rgb(37, 99, 235), Rgb.White, new Rgb(0, 0, 0)).ToJson();

        Assert.Contains("\"accent\": \"#2563eb\"", json);
        Assert.Contains("\"text\": \"#ffffff\"", json);
    }
}

public sealed class ClaudeActivityTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "LiveBackdrop.Tests", Guid.NewGuid().ToString("N"));

    public ClaudeActivityTests() => Directory.CreateDirectory(folder);

    private string Write(string name, string json, TimeSpan age)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, json);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public void NoFolderMeansIdle()
    {
        Assert.False(ClaudeActivity.IsWorking(Path.Combine(folder, "missing"), DateTime.UtcNow));
    }

    [Fact]
    public void AnyWorkingSessionMeansWorking()
    {
        Write("a.json", """{ "working": false }""", TimeSpan.Zero);
        Write("b.json", """{ "working": true }""", TimeSpan.FromMinutes(1));

        Assert.True(ClaudeActivity.IsWorking(folder, DateTime.UtcNow));
    }

    [Fact]
    public void AStaleWorkingFileIsIdle()
    {
        Write("a.json", """{ "working": true }""", ClaudeActivity.StaleAfter + TimeSpan.FromMinutes(1));

        Assert.False(ClaudeActivity.IsWorking(folder, DateTime.UtcNow));
    }

    [Fact]
    public void OldFilesAreRemoved()
    {
        var old = Write("old.json", """{ "working": false }""", TimeSpan.FromDays(3));

        ClaudeActivity.IsWorking(folder, DateTime.UtcNow);

        Assert.False(File.Exists(old));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "working": "yes" }""")]
    [InlineData("[]")]
    public void UnreadableFilesAreIdle(string json)
    {
        Assert.False(ClaudeActivity.SaysWorking(Write("x.json", json, TimeSpan.Zero)));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}

public class BreathTests
{
    [Theory]
    [InlineData(0.18f, 0.162f)]
    [InlineData(0.5f, 0.2f)]
    [InlineData(0.95f, 0.05f)]
    [InlineData(0f, 0f)]
    public void TheBreathStaysGentleAndWithinRange(float opacity, float depth)
    {
        Assert.Equal(depth, Compositor.BreathDepth(opacity), 3);
    }
}
