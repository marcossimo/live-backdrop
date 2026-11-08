using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace LiveBackdrop.Capture;

/// <summary>
/// Captures one window with Windows Graphics Capture, on the GPU, even where other windows cover
/// it. Frames arrive as the window redraws; the latest is kept in a texture of our own, so the
/// overlay can redraw from it while the window stands still.
/// </summary>
internal sealed class WindowCapture : IDisposable
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    private readonly Compositor compositor;
    private readonly string name;
    private readonly object gate = new();
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private SizeInt32 poolSize;
    private ID3D11Texture2D? latest;

    /// <param name="minInterval">The shortest time between two frames, or null for every frame.</param>
    public WindowCapture(Compositor compositor, IntPtr hwnd, string name, TimeSpan? minInterval = null)
    {
        this.compositor = compositor;
        this.name = name;
        Window = hwnd;
        var item = CaptureItems.ForWindow(hwnd);
        poolSize = item.Size;
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(compositor.WinRtDevice, PixelFormat, 2, poolSize);
        pool.FrameArrived += OnFrameArrived;
        session = pool.CreateCaptureSession(item);
        TrySet(() => session.IsCursorCaptureEnabled = false);
        TrySet(() => session.IsBorderRequired = false);
        if (minInterval is { } interval)
        {
            // Windows 11 24H2 and later; older builds deliver every frame.
            TrySet(() => session.MinUpdateInterval = interval);
        }

        session.StartCapture();
        Log.Write($"{name} capture started ({poolSize.Width}x{poolSize.Height})");
    }

    /// <summary>Raised on a capture thread after a new frame was kept.</summary>
    public event EventHandler? FrameReady;

    public IntPtr Window { get; }

    /// <summary>True while the captured window still exists.</summary>
    public bool IsAlive => Native.IsWindow(Window);

    /// <summary>The latest frame, or null before the first one.</summary>
    public ID3D11Texture2D? Latest
    {
        get
        {
            lock (gate)
            {
                return latest;
            }
        }
    }

    public void Dispose()
    {
        pool.FrameArrived -= OnFrameArrived;
        session.Dispose();
        pool.Dispose();
        lock (gate)
        {
            latest?.Dispose();
            latest = null;
        }
    }

    private static void TrySet(Action set)
    {
        try
        {
            set();
        }
        catch (COMException)
        {
            // Older Windows builds lack these switches; the defaults still work.
        }
        catch (InvalidCastException)
        {
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            if (frame.ContentSize.Width != poolSize.Width || frame.ContentSize.Height != poolSize.Height)
            {
                // The window was resized, or a monitor added, removed or rescaled.
                poolSize = frame.ContentSize;
                sender.Recreate(compositor.WinRtDevice, PixelFormat, 2, poolSize);
                return;
            }

            using var surface = CaptureItems.Texture(frame.Surface);
            lock (gate)
            {
                var description = surface.Description;
                if (latest is null || latest.Description.Width != description.Width || latest.Description.Height != description.Height)
                {
                    latest?.Dispose();
                    latest = compositor.Device.CreateTexture2D(new Texture2DDescription
                    {
                        Width = description.Width,
                        Height = description.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = description.Format,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                    });
                }

                compositor.Context.CopyResource(latest, surface);
            }

            FrameReady?.Invoke(this, EventArgs.Empty);
        }
        catch (COMException ex)
        {
            Log.Write($"{name} frame dropped: {ex.Message}");
        }
    }
}
