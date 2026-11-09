using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace LiveBackdrop;

/// <summary>
/// Draws a frame into the swap chain with Direct2D, blurred where asked: everywhere, or behind
/// what the app draws. For the latter it reads the app's own picture: edges (letters, icons) are
/// found, grown into blocks and softened into a mask, and the blurred wallpaper is drawn through
/// that mask over the sharp one. The mask follows the app pixel for pixel, scrolling included,
/// and needs nothing to know about the app's layout. It is built at half size, and only when the
/// app's picture changed. Used by <see cref="Compositor"/>, under its lock.
/// </summary>
internal sealed class BlurRenderer : IDisposable
{
    // The mask is built at this fraction of the window's size: a quarter of the pixels.
    private const float MaskScale = 0.5f;

    // Growing the edges merges the letters of a paragraph into one block, in mask pixels each
    // way (twice that on screen).
    private const int GrowWidth = 18;
    private const int GrowHeight = 14;

    // Shrunk to half size, a one-pixel edge keeps about a quarter of its opacity.
    private const float StrengthenAfterShrink = 4f;

    // Turning edge strength into mask opacity: faint edges (gradients, anti-aliasing of flat
    // panels) stay out, text is fully in.
    private const float EdgeGain = 2.5f;
    private const float EdgeThreshold = 0.35f;

    // How far the blur fades in around what the app draws, relative to the blur's strength.
    private const float FeatherPerBlur = 0.75f;
    private const float MinFeather = 4;
    private const float MaxFeather = 30;

    private readonly ID3D11Device d3d;
    private readonly ID3D11DeviceContext d3dContext;
    private readonly ID2D1Factory1 factory;
    private readonly ID2D1Device device;
    private readonly ID2D1DeviceContext context;
    private readonly GaussianBlur blur;
    private readonly EdgeDetection edges;
    private readonly ColorMatrix edgesToAlpha;
    private readonly ColorMatrix strengthen;
    private readonly Morphology grow;
    private readonly GaussianBlur soften;
    private readonly AlphaMask maskedBlur;
    private ID2D1Bitmap1? smallEdges;
    private ID2D1Bitmap1? smallMask;
    private ID2D1Bitmap1? mask;
    private float maskFeather;

    public BlurRenderer(ID3D11Device d3d, ID3D11DeviceContext d3dContext)
    {
        this.d3d = d3d;
        this.d3dContext = d3dContext;
        factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        using (var dxgiDevice = d3d.QueryInterface<IDXGIDevice>())
        {
            device = factory.CreateDevice(dxgiDevice);
        }

        context = device.CreateDeviceContext(DeviceContextOptions.None);

        // Hard borders: sample the picture's own edge instead of fading to transparent, so the
        // window's edges stay blurred.
        blur = new GaussianBlur(context) { BorderMode = BorderMode.Hard };

        edges = new EdgeDetection(context) { Strength = 1f, BlurRadius = 0f, Mode = EdgeDetectionMode.Sobel };
        edgesToAlpha = new ColorMatrix(context)
        {
            // White, with opacity from the edge strength (the mean of red, green and blue).
            Matrix = new Matrix5x4(
                0, 0, 0, EdgeGain / 3,
                0, 0, 0, EdgeGain / 3,
                0, 0, 0, EdgeGain / 3,
                0, 0, 0, 0,
                1, 1, 1, -EdgeThreshold),
        };

        // D2D1_COLORMATRIX_PROP_CLAMP_OUTPUT: keep opacity within 0 to 1 (Vortice has no
        // property for it).
        edgesToAlpha.SetValue(2, true);
        grow = new Morphology(context) { Mode = MorphologyMode.Dilate, Width = GrowWidth, Height = GrowHeight };
        soften = new GaussianBlur(context) { BorderMode = BorderMode.Hard };

        // Shrinking thins the edges' opacity; this restores it before the borders are softened.
        strengthen = new ColorMatrix(context)
        {
            Matrix = new Matrix5x4(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, StrengthenAfterShrink,
                0, 0, 0, 0),
        };
        strengthen.SetValue(2, true);

        edgesToAlpha.SetInputEffect(0, edges, true);
        strengthen.SetInputEffect(0, grow, true);
        soften.SetInputEffect(0, strengthen, true);

        maskedBlur = new AlphaMask(context);
        maskedBlur.SetInputEffect(0, blur, true);
    }

    /// <summary>The fade width around what the app draws for a given blur strength.</summary>
    internal static float FeatherFor(float blurAmount) => Math.Clamp(blurAmount * FeatherPerBlur, MinFeather, MaxFeather);

    /// <summary>
    /// Draws the window's region of <paramref name="source"/> into the swap chain's back buffer:
    /// blurred everywhere, or blurred behind what <paramref name="appPicture"/>, the app's own
    /// picture, draws. <paramref name="appChanged"/> says whether that picture is new since the
    /// last draw, so the mask is rebuilt only then.
    /// </summary>
    public void Render(
        WindowRegion source,
        IDXGISwapChain1 swapChain,
        BlurMode mode,
        float amount,
        WindowRegion? appPicture,
        bool appChanged)
    {
        var picture = Bitmap(source);
        blur.SetInput(0, picture, true);
        blur.StandardDeviation = amount;

        var masked = false;
        if (mode == BlurMode.TextAreas && appPicture is { } region)
        {
            UpdateMask(region, FeatherFor(amount), appChanged);
            masked = mask is not null;
        }

        using var surface = swapChain.GetBuffer<IDXGISurface>(0);
        using var target = context.CreateBitmapFromDxgiSurface(
            surface,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Ignore), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));

        context.Target = target;
        context.BeginDraw();
        if (mode == BlurMode.Everywhere)
        {
            using var blurred = blur.Output;
            context.DrawImage(blurred);
        }
        else
        {
            context.DrawImage(picture);
            if (masked)
            {
                using var throughMask = maskedBlur.Output;
                context.DrawImage(throughMask);
            }
        }

        context.EndDraw();
        context.Target = null;
    }

    /// <summary>
    /// The window's region of <paramref name="source"/>, shrunk on the GPU to
    /// <paramref name="size"/> × <paramref name="size"/> (every source pixel counts, through
    /// high-quality cubic downscaling) and read back as BGRA bytes.
    /// </summary>
    public byte[] Shrink(WindowRegion source, int size)
    {
        var picture = Bitmap(source);
        var properties = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target);
        using var small = context.CreateBitmap(new SizeI(size, size), IntPtr.Zero, 0, properties);
        context.Target = small;
        context.BeginDraw();
        context.DrawBitmap(picture, new Vortice.RawRectF(0, 0, size, size), 1, InterpolationMode.HighQualityCubic, null, null);
        context.EndDraw();
        context.Target = null;

        using var readable = context.CreateBitmap(
            new SizeI(size, size), IntPtr.Zero, 0, new BitmapProperties1(properties.PixelFormat, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        readable.CopyFromBitmap(small);
        var map = readable.Map(MapOptions.Read);
        try
        {
            var pixels = new byte[size * size * 4];
            for (var row = 0; row < size; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(map.Bits + (int)(row * map.Pitch), pixels, row * size * 4, size * 4);
            }

            return pixels;
        }
        finally
        {
            readable.Unmap();
        }
    }

    // Rebuilds the half-size mask from the app's picture: find its edges at full size (shrinking
    // first would blur the letters below the threshold), turn them into opacity and shrink them;
    // then, at half size, grow them into blocks and soften the blocks' borders.
    private void UpdateMask(WindowRegion region, float feather, bool appChanged)
    {
        var width = (int)MathF.Ceiling(region.Width * MaskScale);
        var height = (int)MathF.Ceiling(region.Height * MaskScale);
        var resized = mask is null || mask.PixelSize.Width != region.Width || mask.PixelSize.Height != region.Height;
        if (!resized && !appChanged && feather == maskFeather)
        {
            return;
        }

        if (resized)
        {
            smallEdges?.Dispose();
            smallMask?.Dispose();
            mask?.Dispose();
            var properties = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target);
            smallEdges = context.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0, properties);
            smallMask = context.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0, properties);
            mask = context.CreateBitmap(new SizeI(region.Width, region.Height), IntPtr.Zero, 0, properties);
        }

        edges.SetInput(0, Bitmap(region), true);
        context.Target = smallEdges;
        context.BeginDraw();
        context.Clear(new Color4(0, 0, 0, 0));
        context.Transform = Matrix3x2.CreateScale(MaskScale);
        using (var found = edgesToAlpha.Output)
        {
            context.DrawImage(found, interpolationMode: InterpolationMode.Linear);
        }

        context.Transform = Matrix3x2.Identity;
        context.EndDraw();

        // Direct2D keeps an effect's result until its input is set again; these bitmaps were
        // just redrawn, so they are set again every time.
        grow.SetInput(0, smallEdges, true);
        soften.StandardDeviation = feather * MaskScale;
        context.Target = smallMask;
        context.BeginDraw();
        context.Clear(new Color4(0, 0, 0, 0));
        using (var built = soften.Output)
        {
            context.DrawImage(built);
        }

        context.EndDraw();

        // Back to the window's size, smoothly, for the alpha mask.
        context.Target = mask;
        context.BeginDraw();
        context.Clear(new Color4(0, 0, 0, 0));
        context.Transform = Matrix3x2.CreateScale(1 / MaskScale);
        context.DrawImage(smallMask!, interpolationMode: InterpolationMode.Linear);
        context.Transform = Matrix3x2.Identity;
        context.EndDraw();
        context.Target = null;
        maskedBlur.SetInput(1, mask, true);
        maskFeather = feather;
        DumpForDebugging(smallEdges!, "edges");
        DumpForDebugging(mask!, "mask");
    }

    // LIVEBACKDROP_DUMP=<folder>: saves the mask's stages as PNGs once, to tune the mask.
    private void DumpForDebugging(ID2D1Bitmap1 bitmap, string name)
    {
        var folder = Environment.GetEnvironmentVariable("LIVEBACKDROP_DUMP");
        if (string.IsNullOrEmpty(folder) || File.Exists(Path.Combine(folder, name + ".png")))
        {
            return;
        }

        var size = bitmap.PixelSize;
        using var readable = context.CreateBitmap(
            size, IntPtr.Zero, 0, new BitmapProperties1(bitmap.PixelFormat, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        readable.CopyFromBitmap(bitmap);
        var map = readable.Map(MapOptions.Read);
        try
        {
            using var image = new System.Drawing.Bitmap(size.Width, size.Height, (int)map.Pitch, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, map.Bits);
            image.Save(Path.Combine(folder, name + ".png"));
        }
        finally
        {
            readable.Unmap();
        }
    }

    public void Dispose()
    {
        smallEdges?.Dispose();
        smallMask?.Dispose();
        mask?.Dispose();
        maskedBlur.Dispose();
        strengthen.Dispose();
        soften.Dispose();
        grow.Dispose();
        edgesToAlpha.Dispose();
        edges.Dispose();
        blur.Dispose();
        foreach (var copy in copies.Values)
        {
            copy.Dispose();
        }

        context.Dispose();
        device.Dispose();
        factory.Dispose();
    }

    private readonly Dictionary<string, RegionCopy> copies = [];

    // A region of a captured texture, copied into a texture Direct2D can read (one kept per
    // kind of source, reused while its size holds).
    private ID2D1Bitmap1 Bitmap(WindowRegion region)
    {
        if (!copies.TryGetValue(region.Name, out var copy) || copy.Width != region.Width || copy.Height != region.Height)
        {
            copy?.Dispose();
            copy = new RegionCopy(d3d, context, region.Width, region.Height);
            copies[region.Name] = copy;
        }

        var description = region.Texture.Description;
        var left = Math.Max(0, region.X);
        var top = Math.Max(0, region.Y);
        var right = Math.Min((int)description.Width, region.X + region.Width);
        var bottom = Math.Min((int)description.Height, region.Y + region.Height);
        if (right > left && bottom > top)
        {
            d3dContext.CopySubresourceRegion(
                copy.Texture, 0, (uint)(left - region.X), (uint)(top - region.Y), 0, region.Texture, 0, new Box(left, top, 0, right, bottom, 1));
        }

        // Kept for the next draws; it is released with the renderer.
        return copy.Bitmap;
    }

    private sealed class RegionCopy : IDisposable
    {
        public RegionCopy(ID3D11Device d3d, ID2D1DeviceContext context, int width, int height)
        {
            Width = width;
            Height = height;
            Texture = d3d.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
            });
            using var surface = Texture.QueryInterface<IDXGISurface>();
            Bitmap = context.CreateBitmapFromDxgiSurface(
                surface,
                new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, BitmapOptions.None));
        }

        public int Width { get; }

        public int Height { get; }

        public ID3D11Texture2D Texture { get; }

        public ID2D1Bitmap1 Bitmap { get; }

        public void Dispose()
        {
            Bitmap.Dispose();
            Texture.Dispose();
        }
    }
}

/// <summary>
/// The window-sized region of a captured texture that starts at (<see cref="X"/>,
/// <see cref="Y"/>); <see cref="Name"/> tells the kinds of source apart.
/// </summary>
internal readonly record struct WindowRegion(string Name, ID3D11Texture2D Texture, int X, int Y, int Width, int Height);
