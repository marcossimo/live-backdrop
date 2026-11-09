using LiveBackdrop.Capture;
using Vortice.Direct3D11;

namespace LiveBackdrop.Sources;

/// <summary>
/// The live desktop wallpaper. Windows hosts it, Wallpaper Engine's included, in the
/// "Program Manager" window, which spans every monitor; it is captured like any window, at the
/// wallpaper's own rate, and the part under the target window is shown.
/// </summary>
internal sealed class WallpaperSource : IBackdropSource
{
    private const string DesktopHostClass = "Progman";

    private readonly WindowCapture capture;

    private WallpaperSource(Compositor compositor, IntPtr desktop, TimeSpan? minInterval)
    {
        capture = new WindowCapture(compositor, desktop, "wallpaper", minInterval);
        capture.FrameReady += (_, _) => FrameReady?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? FrameReady;

    /// <summary>True while the desktop host this source captures still exists.</summary>
    public bool IsAlive => capture.IsAlive;

    /// <summary>While true, the wallpaper's picture is held still.</summary>
    public bool Paused
    {
        get => capture.Paused;
        set => capture.Paused = value;
    }

    /// <summary>
    /// Starts capturing the desktop wallpaper, at most once per <paramref name="minInterval"/>
    /// where Windows supports it, or returns null when there is no desktop host.
    /// </summary>
    public static WallpaperSource? TryCreate(Compositor compositor, TimeSpan? minInterval)
    {
        var desktop = Native.FindWindow(DesktopHostClass, IntPtr.Zero);
        return desktop == IntPtr.Zero ? null : new WallpaperSource(compositor, desktop, minInterval);
    }

    public bool TryGetFrame(Native.Rect target, out ID3D11Texture2D? texture, out int x, out int y)
    {
        Native.GetWindowRect(capture.Window, out var host);
        x = target.Left - host.Left;
        y = target.Top - host.Top;
        texture = capture.Latest;
        return texture is not null;
    }

    public void Dispose() => capture.Dispose();
}
