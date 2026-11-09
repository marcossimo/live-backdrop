using LiveBackdrop.Capture;
using LiveBackdrop.Sources;

namespace LiveBackdrop;

/// <summary>
/// The overlay: a borderless, click-through window owned by the Claude window, so Windows keeps
/// it just above Claude, hides it when Claude is minimized and never gives it the focus. It
/// follows Claude's frame and shows the current <see cref="IBackdropSource"/> through the
/// <see cref="Compositor"/>. It also owns the tray icon and the hotkey, holds the picture still
/// to save power, breathes while Claude works and samples the wallpaper's colours.
/// </summary>
internal sealed class OverlayWindow : Form
{
    private const int FollowIntervalMs = 15;
    private const int SettingsDebounceMs = 80;
    private const long DefaultRedrawIntervalMs = 30;

    // Claude's activity, the power line, the focus and the wallpaper's colours are checked this often.
    private const int WatchIntervalMs = 500;
    private const long ColorsIntervalMs = 3000;
    private const int ColorsSampleSize = 32;
    private const int HotkeyId = 1;

    // The blur's mask follows the app at most this often: enough to track scrolling, and it is
    // the costly part of a redraw.
    private const long MaskIntervalMs = 66;

    private readonly SettingsStore settings;
    private readonly System.Windows.Forms.Timer follow = new() { Interval = FollowIntervalMs };
    private readonly System.Windows.Forms.Timer settingsDebounce = new() { Interval = SettingsDebounceMs };
    private readonly System.Windows.Forms.Timer watch = new() { Interval = WatchIntervalMs };
    private readonly WebPageScanner webPages = new();
    private readonly TrayIcon tray;
    private readonly string activityFolder;
    private Compositor? compositor;
    private IBackdropSource? source;

    // The app's own picture, which shows where it draws text, for the blur.
    private WindowCapture? appCapture;

    // The embedded web pages the last scan found, relative to the window.
    private IReadOnlyList<Native.Rect> pages = [];
    private long lastRedraw;
    private int stale;

    // Set when the app drew a new picture, so the blur's mask is rebuilt from it.
    private int appChanged = 1;
    private long lastMask;

    // The settings the current source was built from, to tell when it must be rebuilt.
    private Settings? sourceSettings;
    private IntPtr owner;
    private bool shown;

    // The hotkey as registered, or null.
    private string? hotkey;
    private bool paused;
    private bool colorsStale = true;
    private long lastColors;
    private WallpaperColors? colors;

    // The target's frame, written on the UI thread and read when a frame arrives.
    private Native.Rect target;

    public OverlayWindow(SettingsStore settings)
    {
        this.settings = settings;
        activityFolder = ClaudeActivity.FolderFor(settings.FilePath);
        tray = new TrayIcon(settings, Close);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        Bounds = new Rectangle(-32000, -32000, 1, 1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= Native.WsExNoRedirectionBitmap | Native.WsExLayered | Native.WsExTransparent
                | Native.WsExToolWindow | Native.WsExNoActivate;
            return parameters;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // DirectComposition draws everything.
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // A layered window stays invisible until it has attributes.
        Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LwaAlpha);
        compositor = new Compositor(Handle);
        ApplySettings(settings.Current, isFirst: true);

        follow.Tick += (_, _) => Follow();
        follow.Start();
        watch.Tick += (_, _) => Watch();
        watch.Start();
        settingsDebounce.Tick += (_, _) =>
        {
            settingsDebounce.Stop();
            if (settings.Reload())
            {
                ApplySettings(settings.Current, isFirst: false);
            }
        };
        settings.Changed += (_, _) => BeginInvoke(() =>
        {
            settingsDebounce.Stop();
            settingsDebounce.Start();
        });
        webPages.PagesChanged += (_, next) => BeginInvoke(() =>
        {
            pages = next;
            Log.Write($"web pages: {Describe(next)}");
            PushLayout();
        });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        follow.Stop();
        watch.Stop();
        settingsDebounce.Stop();
        SetHotkey(null);
        tray.Dispose();
        webPages.Dispose();
        SetAppCapture(IntPtr.Zero);
        SetSource(null);
        compositor?.Dispose();
        follow.Dispose();
        watch.Dispose();
        settingsDebounce.Dispose();
        base.OnFormClosed(e);
    }

    private void ApplySettings(Settings current, bool isFirst)
    {
        Log.Write($"settings: enabled={current.Enabled} source={current.Source} opacity={current.Opacity:0.00} "
            + $"blur={current.Blur}/{current.BlurAmount:0} autostart={current.StartWithWindows} fps={current.MaxFps} "
            + $"pause={current.PauseOnBattery}/{current.PauseInBackground} react={current.ReactToClaude} colors={current.MatchWallpaperColors}");
        compositor!.SetOpacity(current.Opacity);
        SetHotkey(current.Hotkey);
        tray.Update(current, hotkey);
        colorsStale |= current.MatchWallpaperColors;
        Autostart.Apply(current.StartWithWindows, Environment.ProcessPath!);
        UpdateWatchers();
        PushLayout();

        // The source restarts only when what it shows changed.
        var previous = isFirst ? null : sourceSettings;
        sourceSettings = current;
        if (!current.Enabled)
        {
            SetSource(null);
            HideOverlay();
            Watch();
            return;
        }

        if (previous is null || !previous.Enabled || previous.Source != current.Source || previous.ImagePath != current.ImagePath
            || previous.MaxFps != current.MaxFps)
        {
            SetSource(CreateSource(current));
        }

        Watch();
        Redraw();
    }

    private static string Describe(IReadOnlyList<Native.Rect> areas) =>
        areas.Count == 0 ? "none" : string.Join(" ", areas.Select(a => $"{a.Left},{a.Top} {a.Width}x{a.Height}"));

    // The app is captured only to blur behind what it draws, and its accessibility tree scanned
    // only to keep web pages clear; both run only for a visible window.
    private void UpdateWatchers()
    {
        var current = settings.Current;
        var active = current.Enabled && shown;
        SetAppCapture(active && current.Blur == BlurMode.TextAreas ? owner : IntPtr.Zero);
        webPages.Target = active && current.ClearWebPages ? owner : IntPtr.Zero;
    }

    private void SetAppCapture(IntPtr window)
    {
        if (appCapture?.Window == window)
        {
            return;
        }

        if (appCapture is not null)
        {
            appCapture.FrameReady -= OnAppFrame;
            appCapture.Dispose();
            appCapture = null;
        }

        if (window == IntPtr.Zero || compositor is null)
        {
            return;
        }

        try
        {
            appCapture = new WindowCapture(compositor, window, "app", TimeSpan.FromMilliseconds(MaskIntervalMs));
            appCapture.FrameReady += OnAppFrame;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            Log.Write($"app capture unavailable: {ex.Message}");
        }
    }

    private void PushLayout()
    {
        var current = settings.Current;
        compositor?.SetLayout(current.Blur, current.BlurAmount, current.ClearWebPages ? pages : []);

        // The blur is drawn into the frame, so a still wallpaper needs a redraw to show it.
        Redraw();
    }

    private IBackdropSource? CreateSource(Settings current) => current.Source switch
    {
        BackdropSource.Image => ImageSource.TryCreate(compositor!, current.ImagePath),
        _ => CreateWallpaper(),
    };

    private WallpaperSource? CreateWallpaper()
    {
        var fps = settings.Current.MaxFps;
        var wallpaper = WallpaperSource.TryCreate(compositor!, fps > 0 ? TimeSpan.FromMilliseconds(1000.0 / fps) : null);
        if (wallpaper is not null)
        {
            wallpaper.Paused = paused;
        }

        return wallpaper;
    }

    private long RedrawIntervalMs => settings.Current.MaxFps > 0 ? 1000 / settings.Current.MaxFps : DefaultRedrawIntervalMs;

    private void SetSource(IBackdropSource? next)
    {
        if (source is not null)
        {
            source.FrameReady -= OnFrameReady;
            source.Dispose();
        }

        source = next;
        if (source is not null)
        {
            source.FrameReady += OnFrameReady;
        }
    }

    // New frames (the wallpaper's, the app's) only mark the picture as stale; the follow timer
    // redraws it at most every RedrawIntervalMs, so frames arriving together are drawn once and
    // the last change is never lost.
    private void OnFrameReady(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref stale, 1);
        colorsStale = true;
    }

    private void OnAppFrame(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref appChanged, 1);
        Interlocked.Exchange(ref stale, 1);
    }

    private void RedrawIfStale()
    {
        var now = Environment.TickCount64;
        if (now - lastRedraw < RedrawIntervalMs || Interlocked.Exchange(ref stale, 0) == 0)
        {
            return;
        }

        lastRedraw = now;
        Redraw();
    }

    private void Redraw()
    {
        var current = source;
        var frame = target;
        if (!shown || current is null || compositor is null)
        {
            return;
        }

        if (current.TryGetFrame(frame, out var texture, out var x, out var y) && texture is not null)
        {
            compositor.Present(texture, x, y, frame.Width, frame.Height, AppRegion(frame), TakeAppChange());
            SampleColors(texture, x, y, frame);
        }
    }

    // The wallpaper's colours under the window, for the plugin, at most every ColorsIntervalMs and
    // only after the picture changed; written only when they changed noticeably.
    private void SampleColors(Vortice.Direct3D11.ID3D11Texture2D texture, int x, int y, Native.Rect frame)
    {
        var now = Environment.TickCount64;
        if (!settings.Current.MatchWallpaperColors || !colorsStale || now - lastColors < ColorsIntervalMs)
        {
            return;
        }

        lastColors = now;
        colorsStale = false;
        try
        {
            var next = WallpaperColors.FromPixels(compositor!.Shrink(texture, x, y, frame.Width, frame.Height, ColorsSampleSize));
            if (next.DiffersFrom(colors))
            {
                colors = next;
                next.Save(Path.GetDirectoryName(settings.FilePath)!);
                Log.Write($"wallpaper colours: accent {next.Accent.ToHex()} text {next.Text.ToHex()}");
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or SharpGen.Runtime.SharpGenException)
        {
            Log.Write($"wallpaper colours unavailable: {ex.Message}");
        }
    }

    // Checked every WatchIntervalMs: whether Claude is working (the backdrop breathes) and whether
    // to hold the picture still (on battery, Claude in the background, or not shown at all).
    private void Watch()
    {
        var current = settings.Current;
        var working = current.Enabled && current.ReactToClaude && shown && ClaudeActivity.IsWorking(activityFolder, DateTime.UtcNow);
        compositor?.SetBreathing(working);

        var next = !shown
            || (current.PauseOnBattery && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline)
            || (current.PauseInBackground && !ClaudeHasFocus());
        if (next != paused)
        {
            paused = next;
            Log.Write(paused ? "paused" : "resumed");
            if (source is WallpaperSource wallpaper)
            {
                wallpaper.Paused = paused;
            }
        }
    }

    // Claude, or one of its own windows (a dialog, a menu), is the active window.
    private bool ClaudeHasFocus()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || owner == IntPtr.Zero)
        {
            return false;
        }

        if (foreground == owner || Native.GetAncestor(foreground, Native.GaRootOwner) == owner)
        {
            return true;
        }

        Native.GetWindowThreadProcessId(foreground, out var foregroundProcess);
        Native.GetWindowThreadProcessId(owner, out var ownerProcess);
        return foregroundProcess == ownerProcess;
    }

    // Registers the hotkey that turns the backdrop on and off, replacing the previous one.
    private void SetHotkey(string? text)
    {
        var parsed = Hotkey.Parse(text);
        var wanted = parsed is null ? null : text;
        if (wanted == hotkey)
        {
            return;
        }

        if (hotkey is not null)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            hotkey = null;
        }

        if (parsed is { } combination)
        {
            if (Native.RegisterHotKey(Handle, HotkeyId, combination.Modifiers | Hotkey.NoRepeat, combination.Key))
            {
                hotkey = wanted;
                Log.Write($"hotkey {wanted}");
            }
            else
            {
                Log.Write($"hotkey {wanted} unavailable: another app uses it");
            }
        }
        else if (!string.IsNullOrEmpty(text))
        {
            Log.Write($"hotkey \"{text}\" not understood");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WmHotkey && (int)m.WParam == HotkeyId)
        {
            settings.SetEnabled(!settings.Current.Enabled);
            return;
        }

        base.WndProc(ref m);
    }

    // Whether to rebuild the mask now: the app changed and the last rebuild is old enough. A change
    // held back keeps the picture stale, so a later redraw picks it up.
    private bool TakeAppChange()
    {
        if (Volatile.Read(ref appChanged) == 0)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastMask) < MaskIntervalMs)
        {
            Interlocked.Exchange(ref stale, 1);
            return false;
        }

        Interlocked.Exchange(ref lastMask, now);
        Interlocked.Exchange(ref appChanged, 0);
        return true;
    }

    // The window's visible frame within the app's captured picture. The capture can include the
    // window's invisible resize border, which the visible frame leaves out.
    private WindowRegion? AppRegion(Native.Rect frame)
    {
        var capture = appCapture;
        if (capture?.Latest is not { } picture)
        {
            return null;
        }

        Native.GetWindowRect(capture.Window, out var bounds);
        var description = picture.Description;
        var x = description.Width == bounds.Width ? frame.Left - bounds.Left : 0;
        var y = description.Height == bounds.Height ? frame.Top - bounds.Top : 0;
        return new WindowRegion("app", picture, x, y, frame.Width, frame.Height);
    }

    private void Follow()
    {
        if (!settings.Current.Enabled)
        {
            return;
        }

        // The desktop host comes back after Explorer restarts; capture it again.
        if (source is WallpaperSource wallpaper && !wallpaper.IsAlive)
        {
            SetSource(CreateWallpaper());
        }
        else if (source is null && settings.Current.Source == BackdropSource.Wallpaper)
        {
            SetSource(CreateWallpaper());
        }

        if (owner == IntPtr.Zero || !Native.IsWindow(owner))
        {
            owner = TargetWindow.Find();
            if (owner == IntPtr.Zero)
            {
                HideOverlay();
                return;
            }

            // Owned by Claude: rides just above it and minimizes with it.
            Native.SetWindowLongPtr(Handle, Native.GwlpHwndParent, owner);
        }

        if (Native.IsIconic(owner) || !Native.IsWindowVisible(owner))
        {
            HideOverlay();
            return;
        }

        var frame = Native.VisibleFrame(owner);
        var moved = frame != target;
        if (frame.Width != target.Width || frame.Height != target.Height)
        {
            // A new size can rearrange the app's panels.
            webPages.ScanSoon();
        }

        target = frame;
        if (moved)
        {
            Native.SetWindowPos(Handle, IntPtr.Zero, frame.Left, frame.Top, frame.Width, frame.Height,
                Native.SwpNoActivate | Native.SwpNoZOrder | Native.SwpNoOwnerZOrder);
        }

        if (!shown)
        {
            Native.ShowWindow(Handle, Native.SwShowNoActivate);
            shown = true;
            moved = true;
            colorsStale = true;
            UpdateWatchers();
            Watch();
        }

        if (moved)
        {
            Interlocked.Exchange(ref stale, 1);
        }

        RedrawIfStale();
    }

    private void HideOverlay()
    {
        if (shown)
        {
            Native.ShowWindow(Handle, Native.SwHide);
            shown = false;
        }

        owner = IntPtr.Zero;
        target = default;
        UpdateWatchers();
    }
}
