using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveBackdrop;

/// <summary>Where the backdrop's picture comes from.</summary>
internal enum BackdropSource
{
    /// <summary>The live desktop wallpaper (static, slideshow or Wallpaper Engine).</summary>
    Wallpaper,

    /// <summary>A picture file chosen by the user.</summary>
    Image,
}

/// <summary>Where the backdrop is blurred, so text over it stays easy to read.</summary>
internal enum BlurMode
{
    Off,

    /// <summary>Over the app's side panels, toolbar and conversation column.</summary>
    TextAreas,

    Everywhere,
}

/// <summary>
/// The user's settings, shared with the Claude Code plugin through <c>settings.json</c>.
/// Every field has a default, so a missing or partial file still yields a usable value.
/// </summary>
internal sealed record Settings
{
    /// <summary>The file format version this build writes.</summary>
    public const int CurrentVersion = 1;

    public const double DefaultOpacity = 0.18;

    public const double DefaultBlurAmount = 12;

    public const double MaxBlurAmount = 40;

    public const int MaxFrameRate = 60;

    public const string DefaultHotkey = "Ctrl+Alt+B";

    public bool Enabled { get; init; } = true;

    public BackdropSource Source { get; init; } = BackdropSource.Wallpaper;

    public string? ImagePath { get; init; }

    /// <summary>How opaque the layer is, from 0 (invisible) to 1.</summary>
    public double Opacity { get; init; } = DefaultOpacity;

    public bool StartWithWindows { get; init; }

    public BlurMode Blur { get; init; } = BlurMode.TextAreas;

    /// <summary>Leaves embedded web pages (the built-in browser, previews) without a backdrop.</summary>
    public bool ClearWebPages { get; init; } = true;

    /// <summary>How strong the blur is: the Gaussian standard deviation, in pixels.</summary>
    public double BlurAmount { get; init; } = DefaultBlurAmount;

    /// <summary>Shows an icon in the notification area, with a menu.</summary>
    public bool TrayIcon { get; init; } = true;

    /// <summary>The key combination that turns the backdrop on and off; empty for none.</summary>
    public string Hotkey { get; init; } = DefaultHotkey;

    /// <summary>The most frames a second the backdrop is redrawn; 0 for the wallpaper's own rate.</summary>
    public int MaxFps { get; init; }

    /// <summary>Holds the picture still while the computer runs on battery.</summary>
    public bool PauseOnBattery { get; init; } = true;

    /// <summary>Holds the picture still while another app has the focus.</summary>
    public bool PauseInBackground { get; init; }

    /// <summary>
    /// The plugin colours messages from the wallpaper (<c>"colorMode": "wallpaper"</c>), so the
    /// overlay samples it and writes <see cref="WallpaperColors"/>.
    /// </summary>
    public bool MatchWallpaperColors { get; init; }

    /// <summary>Makes the backdrop breathe, gently, while Claude is working.</summary>
    public bool ReactToClaude { get; init; } = true;

    /// <summary>
    /// Parses settings JSON leniently: unknown fields are ignored, invalid values fall back to
    /// their defaults and numbers are clamped to their range.
    /// </summary>
    public static Settings Parse(string json)
    {
        var defaults = new Settings();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return defaults;
        }

        if (root is null)
        {
            return defaults;
        }

        return new Settings
        {
            Enabled = ReadBool(root, "enabled") ?? defaults.Enabled,
            Source = ReadString(root, "source") switch
            {
                "image" => BackdropSource.Image,
                _ => BackdropSource.Wallpaper,
            },
            ImagePath = string.IsNullOrWhiteSpace(ReadString(root, "imagePath")) ? null : ReadString(root, "imagePath"),
            Opacity = Math.Clamp(ReadDouble(root, "opacity") ?? defaults.Opacity, 0, 1),
            StartWithWindows = ReadBool(root, "startWithWindows") ?? defaults.StartWithWindows,
            Blur = ReadString(root, "blur") switch
            {
                "off" => BlurMode.Off,
                "everywhere" => BlurMode.Everywhere,
                "text" => BlurMode.TextAreas,
                _ => defaults.Blur,
            },
            BlurAmount = Math.Clamp(ReadDouble(root, "blurAmount") ?? defaults.BlurAmount, 0, MaxBlurAmount),
            ClearWebPages = ReadBool(root, "clearWebPages") ?? defaults.ClearWebPages,
            TrayIcon = ReadBool(root, "trayIcon") ?? defaults.TrayIcon,
            Hotkey = root["hotkey"] is JsonValue hotkey && hotkey.TryGetValue(out string? combination)
                ? combination.Trim()
                : defaults.Hotkey,
            MaxFps = (int)Math.Round(Math.Clamp(ReadDouble(root, "maxFps") ?? defaults.MaxFps, 0, MaxFrameRate)),
            PauseOnBattery = ReadBool(root, "pauseOnBattery") ?? defaults.PauseOnBattery,
            PauseInBackground = ReadBool(root, "pauseInBackground") ?? defaults.PauseInBackground,
            MatchWallpaperColors = ReadString(root, "colorMode") == "wallpaper",
            ReactToClaude = ReadBool(root, "reactToClaude") ?? defaults.ReactToClaude,
        };
    }

    private static bool? ReadBool(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue(out bool result) ? result : null;

    private static double? ReadDouble(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue(out double result) && double.IsFinite(result) ? result : null;

    private static string? ReadString(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue(out string? result) ? result : null;
}
