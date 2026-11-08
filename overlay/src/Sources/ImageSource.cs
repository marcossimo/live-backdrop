using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LiveBackdrop.Sources;

/// <summary>
/// A still picture, scaled to cover the target window (cropping what overflows, centred).
/// The scaled copy is rebuilt only when the window's size changes.
/// </summary>
internal sealed class ImageSource : IBackdropSource
{
    private readonly Compositor compositor;
    private readonly Bitmap picture;
    private readonly object gate = new();
    private ID3D11Texture2D? scaled;
    private int scaledWidth;
    private int scaledHeight;

    private ImageSource(Compositor compositor, Bitmap picture)
    {
        this.compositor = compositor;
        this.picture = picture;
    }

    // A still picture never produces a new frame by itself; moves and resizes redraw it.
    public event EventHandler? FrameReady
    {
        add { }
        remove { }
    }

    /// <summary>Loads the picture, or returns null (and logs why) when it cannot be read.</summary>
    public static ImageSource? TryCreate(Compositor compositor, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Log.Write($"image not found: {path}");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var loaded = Image.FromStream(stream);
            return new ImageSource(compositor, new Bitmap(loaded));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
        {
            // GDI+ reports an unsupported format as OutOfMemoryException.
            Log.Write($"image unreadable: {path}: {ex.Message}");
            return null;
        }
    }

    public bool TryGetFrame(Native.Rect target, out ID3D11Texture2D? texture, out int x, out int y)
    {
        x = 0;
        y = 0;
        lock (gate)
        {
            if (scaled is null || scaledWidth != target.Width || scaledHeight != target.Height)
            {
                Rebuild(target.Width, target.Height);
            }

            texture = scaled;
            return texture is not null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            scaled?.Dispose();
            scaled = null;
        }

        picture.Dispose();
    }

    private void Rebuild(int width, int height)
    {
        scaled?.Dispose();
        scaled = null;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Max((double)width / picture.Width, (double)height / picture.Height);
            var drawWidth = (float)(picture.Width * scale);
            var drawHeight = (float)(picture.Height * scale);
            graphics.DrawImage(picture, (width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight);
        }

        var bits = canvas.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            scaled = compositor.Device.CreateTexture2D(
                new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                },
                new SubresourceData(bits.Scan0, (uint)bits.Stride));
            scaledWidth = width;
            scaledHeight = height;
        }
        finally
        {
            canvas.UnlockBits(bits);
        }
    }
}
