using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LiveBackdrop;

/// <summary>
/// Owns the GPU side of the overlay: a Direct3D 11 device, a composition swap chain sized to
/// the target window (drawn into directly, or through <see cref="BlurRenderer"/> when blurred),
/// and the DirectComposition tree that shows it with the chosen opacity, leaving out the areas
/// to keep clear. Safe to call from the capture thread and the UI thread.
/// </summary>
internal sealed class Compositor : IDisposable
{
    private readonly object gate = new();
    private readonly IDCompositionDesktopDevice compositionDevice;
    private readonly IDCompositionTarget compositionTarget;
    private readonly IDCompositionVisual2 visual;
    private readonly BlurRenderer blurRenderer;
    private readonly List<IDCompositionVisual2> pieces = [];
    private readonly List<IDCompositionRectangleClip> clips = [];
    private IDXGISwapChain1? swapChain;
    private int swapWidth;
    private int swapHeight;
    private BlurMode blurMode;
    private float blurAmount;
    private IReadOnlyList<Native.Rect> clearAreas = [];

    public Compositor(IntPtr hwnd)
    {
        D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out var device).CheckError();
        Device = device!;
        Context = Device.ImmediateContext;
        using (var multithread = Device.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
        }

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(Native.DCompositionCreateDevice3(
            dxgiDevice.NativePointer, typeof(IDCompositionDesktopDevice).GUID, out var compositionPointer));
        compositionDevice = new IDCompositionDesktopDevice(compositionPointer);
        compositionDevice.CreateTargetForHwnd(hwnd, true, out var target).CheckError();
        compositionTarget = target!;
        compositionDevice.CreateVisual(out var root).CheckError();
        visual = root!;
        compositionTarget.SetRoot(visual);

        Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable));
        try
        {
            WinRtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }

        blurRenderer = new BlurRenderer(Device, Context);
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    /// <summary>The same device, as Windows Graphics Capture takes it.</summary>
    public IDirect3DDevice WinRtDevice { get; }

    public void SetOpacity(double opacity)
    {
        lock (gate)
        {
            using var visual3 = visual.QueryInterface<IDCompositionVisual3>();
            visual3.SetOpacity((float)Math.Clamp(opacity, 0, 1));
            compositionDevice.Commit();
        }
    }

    /// <summary>
    /// Sets how the picture is laid out over the window: blurred everywhere, behind what the app
    /// draws, or nowhere; and left out entirely over <paramref name="clear"/> (in pixels relative
    /// to the window).
    /// </summary>
    public void SetLayout(BlurMode mode, double amount, IReadOnlyList<Native.Rect> clear)
    {
        lock (gate)
        {
            blurMode = mode;
            blurAmount = (float)Math.Clamp(amount, 0, Settings.MaxBlurAmount);
            clearAreas = clear;
            BuildTree();
            compositionDevice.Commit();
        }
    }

    /// <summary>
    /// Shows the <paramref name="width"/> × <paramref name="height"/> region of
    /// <paramref name="source"/> starting at (<paramref name="x"/>, <paramref name="y"/>);
    /// whatever falls outside the source stays black. <paramref name="app"/>, the app's own
    /// picture, tells where to blur when blurring behind what the app draws.
    /// </summary>
    public void Present(ID3D11Texture2D source, int x, int y, int width, int height, WindowRegion? app, bool appChanged)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        lock (gate)
        {
            EnsureSwapChain(width, height);
            if (blurMode != BlurMode.Off && blurAmount > 0)
            {
                blurRenderer.Render(new WindowRegion("wallpaper", source, x, y, width, height), swapChain!, blurMode, blurAmount, app, appChanged);
                swapChain!.Present(0, PresentFlags.None);
                return;
            }

            using var back = swapChain!.GetBuffer<ID3D11Texture2D>(0);
            var description = source.Description;
            var left = Math.Max(0, x);
            var top = Math.Max(0, y);
            var right = Math.Min((int)description.Width, x + width);
            var bottom = Math.Min((int)description.Height, y + height);
            if (right > left && bottom > top)
            {
                Context.CopySubresourceRegion(
                    back, 0, (uint)(left - x), (uint)(top - y), 0, source, 0, new Box(left, top, 0, right, bottom, 1));
            }

            swapChain.Present(0, PresentFlags.None);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            ClearTree();
            blurRenderer.Dispose();
            swapChain?.Dispose();
            visual.Dispose();
            compositionTarget.Dispose();
            compositionDevice.Dispose();
            Context.Dispose();
            Device.Dispose();
        }
    }

    private void EnsureSwapChain(int width, int height)
    {
        if (swapChain is not null && width == swapWidth && height == swapHeight)
        {
            return;
        }

        if (swapChain is null)
        {
            using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            swapChain = factory.CreateSwapChainForComposition(Device, new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                Scaling = Scaling.Stretch,
            });
        }
        else
        {
            swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        }

        swapWidth = width;
        swapHeight = height;
        BuildTree();
        compositionDevice.Commit();
    }

    // The tree under the root visual (which carries the opacity, so it applies to everything at
    // once). With nothing to keep clear, the root shows the swap chain itself. Otherwise the root
    // shows nothing and pieces do: the window minus the clear areas. Caller holds the gate.
    private void BuildTree()
    {
        ClearTree();
        if (swapChain is null)
        {
            return;
        }

        if (clearAreas.Count == 0)
        {
            visual.SetContent(swapChain);
            return;
        }

        visual.SetContent(null);
        foreach (var piece in RectMath.Subtract(new Native.Rect(0, 0, swapWidth, swapHeight), clearAreas))
        {
            AddPiece(piece);
        }
    }

    // A visual clipped to the area, holding a visual that shows the swap chain. Clip with an
    // IDCompositionRectangleClip; Vortice's SetClip(RawRectF) does not reach Windows correctly.
    private void AddPiece(Native.Rect area)
    {
        compositionDevice.CreateVisual(out var clipped).CheckError();
        compositionDevice.CreateRectangleClip(out var clip).CheckError();
        clip!.SetLeft(area.Left);
        clip.SetTop(area.Top);
        clip.SetRight(area.Right);
        clip.SetBottom(area.Bottom);
        clipped!.SetClip(clip);
        clips.Add(clip);

        compositionDevice.CreateVisual(out var content).CheckError();
        content!.SetContent(swapChain);
        clipped.AddVisual(content, false, null);
        visual.AddVisual(clipped, false, null);
        pieces.Add(content);
        pieces.Add(clipped);
    }

    private void ClearTree()
    {
        visual.RemoveAllVisuals();
        foreach (var piece in pieces)
        {
            piece.Dispose();
        }

        foreach (var clip in clips)
        {
            clip.Dispose();
        }

        pieces.Clear();
        clips.Clear();
    }
}
