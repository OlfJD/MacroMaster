using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MacroMaster.UI;

/// <summary>
/// The one accent colour (switches, primary buttons, the active mode, focus, the logo tile), chosen
/// in Settings. Everything in the UI refers to the accent brushes as DynamicResource, so replacing
/// them here recolours every open window at once. Hover / pressed shades and the text colour on
/// the accent are worked out from the chosen colour, so any colour stays readable.
/// </summary>
public static class AccentTheme
{
    public const string Default = "#ECE4D3"; // cream

    /// <summary>Ready-made choices offered in Settings: (name, colour).</summary>
    public static readonly (string Name, string Hex)[] Presets =
    {
        ("Cream", "#ECE4D3"),
        ("White", "#F4F4F5"),
        ("Sand", "#E3CFA8"),
        ("Peach", "#F2C6A8"),
        ("Rose", "#EDB8C2"),
        ("Lavender", "#C9C3F0"),
        ("Sky", "#AECDEB"),
        ("Mint", "#B4DCCB"),
        ("Sage", "#B9D3B0"),
    };

    /// <summary>The colour currently applied, "#RRGGBB".</summary>
    public static string Current { get; private set; } = Default;

    /// <summary>"#RRGGBB" (or "RRGGBB") → colour; false for anything else.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        string s = (text ?? string.Empty).Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        if (s.Length != 7) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(s);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Relative brightness 0..1 (sRGB weights), used to pick dark or light text on the accent.</summary>
    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>Recolours the whole app. An invalid colour falls back to the default cream.</summary>
    public static void Apply(string? hex)
    {
        if (!TryParse(hex, out var accent)) TryParse(Default, out accent);
        Current = ToHex(accent);

        var res = System.Windows.Application.Current?.Resources;
        if (res == null) return;

        bool light = Luminance(accent) > 0.55;
        // Light accents get brighter on hover and darker when pressed; dark accents the other way round
        res["AccentBrush"] = Frozen(accent);
        res["AccentHoverBrush"] = Frozen(Mix(accent, light ? Colors.White : Colors.Black, light ? 0.35 : 0.15));
        res["AccentPressedBrush"] = Frozen(Mix(accent, light ? Colors.Black : Colors.White, light ? 0.10 : 0.15));
        res["AccentTextBrush"] = Frozen(light ? accent : Mix(accent, Colors.White, 0.35)); // readable as text on the dark greys
        res["OnAccentBrush"] = Frozen(light ? Color.FromRgb(0x11, 0x11, 0x13) : Color.FromRgb(0xF4, 0xF4, 0xF5));
        res["LogoMark"] = BuildLogo(accent, light);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>The 24 px logo (same shapes as LogoMark in DarkTheme.xaml): an accent tile with the M cut in.</summary>
    private static DrawingImage BuildLogo(Color accent, bool light)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Frozen(accent), null, new RectangleGeometry(new Rect(0, 0, 24, 24), 5.25, 5.25)));
        group.Children.Add(new GeometryDrawing(Frozen(light ? Color.FromRgb(0x11, 0x11, 0x13) : Color.FromRgb(0xF4, 0xF4, 0xF5)), null,
            Geometry.Parse("M 6,17 L 6,7 L 9.6,7 L 12,10.15 L 14.4,7 L 18,7 L 18,17 L 15,17 L 15,10.94 L 12,14.88 L 9,10.94 L 9,17 Z")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
