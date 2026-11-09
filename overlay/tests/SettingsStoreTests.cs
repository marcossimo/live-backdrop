using System.Text.Json.Nodes;
using Xunit;

namespace LiveBackdrop.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "LiveBackdrop.Tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(folder, "settings.json");

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        using var store = new SettingsStore(SettingsPath);

        Assert.Equal(new Settings(), store.Current);
    }

    [Fact]
    public void ReloadReportsAChange()
    {
        using var store = new SettingsStore(SettingsPath);
        File.WriteAllText(SettingsPath, """{ "opacity": 0.5 }""");

        Assert.True(store.Reload());
        Assert.Equal(0.5, store.Current.Opacity);
        Assert.False(store.Reload());
    }

    [Fact]
    public void RecordingTheOverlayPathKeepsOtherFields()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, """{ "opacity": 0.3, "custom": "kept" }""");
        using var store = new SettingsStore(SettingsPath);

        store.RecordOverlayPath(@"C:\tools\LiveBackdrop.exe");

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal(@"C:\tools\LiveBackdrop.exe", root["overlayPath"]!.GetValue<string>());
        Assert.Equal(Settings.CurrentVersion, root["version"]!.GetValue<int>());
        Assert.Equal(0.3, root["opacity"]!.GetValue<double>());
        Assert.Equal("kept", root["custom"]!.GetValue<string>());
    }

    [Fact]
    public void TogglingEnabledKeepsOtherFields()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, """{ "enabled": true, "opacity": 0.3, "custom": "kept" }""");
        using var store = new SettingsStore(SettingsPath);

        store.SetEnabled(false);

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.False(root["enabled"]!.GetValue<bool>());
        Assert.Equal(0.3, root["opacity"]!.GetValue<double>());
        Assert.Equal("kept", root["custom"]!.GetValue<string>());
    }

    [Fact]
    public void AnUnreadableFileIsNotReplaced()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, "{ half written");
        using var store = new SettingsStore(SettingsPath);

        store.SetEnabled(false);

        Assert.Equal("{ half written", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void RecordsTheOverlayVersion()
    {
        using var store = new SettingsStore(SettingsPath);

        store.RecordOverlayPath(@"C:\tools\LiveBackdrop.exe");

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.False(string.IsNullOrEmpty(root["overlayVersion"]!.GetValue<string>()));
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
