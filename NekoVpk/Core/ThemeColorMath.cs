using System;
using System.Globalization;
using System.Linq;

namespace NekoVpk.Core;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6) return false;
        if (!int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int v)) return false;

        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public Rgb Mix(Rgb other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return new Rgb(Lerp(R, other.R), Lerp(G, other.G), Lerp(B, other.B));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

public static class ThemeColorMath
{
    public readonly record struct Shades(Rgb Primary, Rgb Hover, Rgb Active, Rgb Disabled);

    private static readonly Rgb LightSurface = new(0xF0, 0xF0, 0xF2);
    private static readonly Rgb DarkSurface = new(0x2A, 0x2B, 0x30);
    private static readonly Rgb LightBase = Rgb.White;
    private static readonly Rgb DarkBase = new(0x16, 0x17, 0x1B);

    private static readonly Rgb DarkText = new(0x1A, 0x1A, 0x1A);

    public static readonly string[] PresetHexes =
    [
        "#1CB5D6", "#1FB9A5", "#2FBF71", "#E6A817", "#F5871F",
        "#F0524F", "#EC5FA4", "#9B6DFF", "#6C7BFF",
    ];

    public static Shades Derive(Rgb baseColor, bool dark) => dark
        ? new Shades(baseColor, baseColor.Mix(Rgb.White, 0.20), baseColor.Mix(Rgb.White, 0.40), baseColor.Mix(Rgb.Black, 0.45))
        : new Shades(baseColor, baseColor.Mix(Rgb.Black, 0.12), baseColor.Mix(Rgb.Black, 0.28), baseColor.Mix(Rgb.White, 0.60));

    public static double Luminance(Rgb c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }

    public static Rgb OnColor(Rgb background)
        => Contrast(Rgb.White, background) >= 3.0 ? Rgb.White : DarkText;

    public static Rgb EnsureReadable(Rgb color, bool dark, double minRatio = 3.0)
    {
        var surface = dark ? DarkSurface : LightSurface;
        if (Contrast(color, surface) >= minRatio) return color;

        var target = dark ? Rgb.White : Rgb.Black;
        for (int i = 1; i <= 100; i++)
        {
            var candidate = color.Mix(target, i / 100.0);
            if (Contrast(candidate, surface) >= minRatio) return candidate;
        }

        return target;
    }

    public static Rgb LightTint(Rgb primary, bool dark, int level)
    {
        double[] amounts = dark ? new[] { 0.22, 0.30, 0.38 } : new[] { 0.12, 0.18, 0.24 };
        return (dark ? DarkBase : LightBase).Mix(primary, amounts[Math.Clamp(level, 0, 2)]);
    }
}
