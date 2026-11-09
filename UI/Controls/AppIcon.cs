using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace MacroMaster.UI.Controls;

/// <summary>
/// An icon drawn from Windows' own icon font (Segoe Fluent Icons, falling back to Segoe MDL2 Assets
/// on Windows 10). Font glyphs are hinted to the pixel grid and get ClearType, so they stay sharp
/// at 12–16 px where scaled vector paths smear across pixels. Foreground is inherited like text.
/// </summary>
public class AppIcon : TextBlock
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Semantic name → glyph code point. Keep names stable; XAML and the engine use them.</summary>
    private static readonly Dictionary<string, char> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["play"] = '',
        ["pause"] = '',
        ["square"] = '',          // stop
        ["repeat"] = '',
        ["settings"] = '',
        ["plus"] = '',
        ["minus"] = '',           // window minimize
        ["maximize"] = '',
        ["restore"] = '',
        ["x"] = '',
        ["close-small"] = '',
        ["search"] = '',
        ["download"] = '',
        ["upload"] = '',
        ["trash-2"] = '',
        ["pencil"] = '',
        ["edit"] = '',
        ["arrow-up"] = '',
        ["arrow-down"] = '',
        ["keyboard"] = '',
        ["mouse"] = '',
        ["clock"] = '',
        ["timer"] = '',
        ["crosshair"] = '',       // move
        ["monitor"] = '',
        ["power"] = '',
        ["octagon-x"] = '',       // stop everything
        ["alert"] = '',
        ["circle-dot"] = '',      // record / armed
        ["file-code"] = '',
        ["layers"] = '',
        ["gamepad-2"] = '',
        ["zap"] = '',
        ["type"] = '',
        ["chevron-down"] = '',
        ["chevron-right"] = '',
        ["check"] = '',
        ["copy"] = '',
        ["folder-open"] = '',
        ["shield-alert"] = '',
        ["inbox"] = '',
        ["help"] = '',
    };

    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(AppIcon),
            new PropertyMetadata("play", (d, _) => ((AppIcon)d).UpdateGlyph()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(AppIcon),
            new PropertyMetadata(16.0, (d, _) => ((AppIcon)d).UpdateSize()));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <summary>Glyph size in px. The font is designed for 16 (also good at 12, 20, 24).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    static AppIcon()
    {
        // Defaults via metadata (not local values) so XAML inside templates can still override them
        HorizontalAlignmentProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(HorizontalAlignment.Center));
        VerticalAlignmentProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(VerticalAlignment.Center));
        TextAlignmentProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(TextAlignment.Center));
        LineStackingStrategyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(LineStackingStrategy.BlockLineHeight));
    }

    public AppIcon()
    {
        // Set locally: FontFamily is inherited, and the window's text font would win over a default
        FontFamily = IconFont;
        FontWeight = FontWeights.Normal;
        UpdateGlyph();
        UpdateSize();
    }

    private void UpdateGlyph()
    {
        Text = Glyphs.TryGetValue(IconKey ?? string.Empty, out char c) ? c.ToString() : "";
    }

    private void UpdateSize()
    {
        FontSize = Size;
        LineHeight = Size;
        Width = Size;
        Height = Size;
    }
}
