using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveBackdrop;

/// <summary>
/// Colours taken from the wallpaper, for the plugin's "match the wallpaper" message colours:
/// <see cref="Accent"/>, its most vivid colour, toned so it works as a message bubble;
/// <see cref="Text"/>, white or black, whichever reads better on the accent; and
/// <see cref="Average"/>, the overall tone.
/// </summary>
internal sealed record WallpaperColors(Rgb Accent, Rgb Text, Rgb Average)
{
    public const string FileName = "wallpaper-colors.json";

    private const int HueBins = 12;

    // Pixels this dark or this grey carry no colour worth picking.
    private const double MinValue = 0.15;
    private const double MinSaturation = 0.2;

    // The share of the picture's weight a hue needs before it is used over the average.
    private const double MinVividShare = 0.02;

    /// <summary>
    /// Picks the colours of a small picture, as 32-bit BGRA pixels (alpha ignored). The accent is
    /// the mean colour of the hue holding the most vivid weight (saturation × value), kept within
    /// a brightness range a bubble can use; a grey picture's accent is its toned average.
    /// </summary>
    public static WallpaperColors FromPixels(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length < 4)
        {
            var grey = new Rgb(51, 65, 85);
            return new WallpaperColors(grey, Rgb.White, grey);
        }

        Span<double> weight = stackalloc double[HueBins];
        Span<double> red = stackalloc double[HueBins];
        Span<double> green = stackalloc double[HueBins];
        Span<double> blue = stackalloc double[HueBins];
        double sumR = 0, sumG = 0, sumB = 0, total = 0;
        var count = bgra.Length / 4;
        for (var i = 0; i < count; i++)
        {
            var pixel = new Rgb(bgra[(i * 4) + 2], bgra[(i * 4) + 1], bgra[i * 4]);
            sumR += pixel.R;
            sumG += pixel.G;
            sumB += pixel.B;

            var (hue, saturation, value) = pixel.ToHsv();
            var vivid = saturation * value;
            total += vivid;
            if (value < MinValue || saturation < MinSaturation)
            {
                continue;
            }

            var bin = (int)(hue / 360 * HueBins) % HueBins;
            weight[bin] += vivid;
            red[bin] += pixel.R * vivid;
            green[bin] += pixel.G * vivid;
            blue[bin] += pixel.B * vivid;
        }

        var average = new Rgb(Byte(sumR / count), Byte(sumG / count), Byte(sumB / count));
        var best = 0;
        for (var bin = 1; bin < HueBins; bin++)
        {
            if (weight[bin] > weight[best])
            {
                best = bin;
            }
        }

        var accent = weight[best] > 0 && weight[best] >= MinVividShare * Math.Max(total, 1)
            ? Tone(new Rgb(Byte(red[best] / weight[best]), Byte(green[best] / weight[best]), Byte(blue[best] / weight[best])), vivid: true)
            : Tone(average, vivid: false);
        return new WallpaperColors(accent, ReadableOn(accent), average);
    }

    /// <summary>White or black, whichever has more contrast on <paramref name="background"/>.</summary>
    public static Rgb ReadableOn(Rgb background) =>
        Rgb.Contrast(background, Rgb.White) >= Rgb.Contrast(background, Rgb.Black) ? Rgb.White : Rgb.Black;

    /// <summary>
    /// True when the colours differ enough from <paramref name="previous"/> to be worth writing:
    /// an animated wallpaper drifts a little all the time, and every write recolours the chat.
    /// </summary>
    public bool DiffersFrom(WallpaperColors? previous, double threshold = 24) =>
        previous is null || Rgb.Distance(Accent, previous.Accent) > threshold || Text != previous.Text;

    public string ToJson() => new JsonObject
    {
        ["version"] = 1,
        ["accent"] = Accent.ToHex(),
        ["text"] = Text.ToHex(),
        ["average"] = Average.ToHex(),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Writes the colours beside the settings, atomically, for the plugin to read.</summary>
    public void Save(string folder)
    {
        var path = Path.Combine(folder, FileName);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, ToJson());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"wallpaper colours not written: {ex.Message}");
        }
    }

    // Keeps a colour within the brightness a bubble can use: never near black or near white,
    // and a vivid one keeps some saturation.
    private static Rgb Tone(Rgb color, bool vivid)
    {
        var (hue, saturation, value) = color.ToHsv();
        value = Math.Clamp(value, 0.35, 0.78);
        if (vivid)
        {
            saturation = Math.Clamp(saturation, 0.45, 0.9);
        }

        return Rgb.FromHsv(hue, saturation, value);
    }

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}

/// <summary>An sRGB colour.</summary>
internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";

    /// <summary>Hue in degrees, saturation and value from 0 to 1.</summary>
    public (double Hue, double Saturation, double Value) ToHsv()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            hue = max == r ? 60 * (((g - b) / delta) % 6)
                : max == g ? 60 * (((b - r) / delta) + 2)
                : 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, max == 0 ? 0 : delta / max, max);
    }

    public static Rgb FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;
        var (r, g, b) = (hue % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(Channel(r + m), Channel(g + m), Channel(b + m));
    }

    /// <summary>WCAG relative luminance.</summary>
    public double Luminance()
    {
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));
    }

    /// <summary>WCAG contrast ratio, from 1 to 21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var (light, dark) = (Math.Max(a.Luminance(), b.Luminance()), Math.Min(a.Luminance(), b.Luminance()));
        return (light + 0.05) / (dark + 0.05);
    }

    public static double Distance(Rgb a, Rgb b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
