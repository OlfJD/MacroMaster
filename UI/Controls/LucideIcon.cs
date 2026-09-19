using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MacroMaster.UI.Controls;

public class LucideIcon : System.Windows.Controls.UserControl
{
    private readonly System.Windows.Controls.Viewbox _viewbox;
    private readonly System.Windows.Controls.Canvas _canvas;
    private readonly System.Windows.Shapes.Path _path;

    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(LucideIcon),
            new PropertyMetadata("zap", OnIconKeyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(LucideIcon),
            new PropertyMetadata(2.2, OnStrokeThicknessChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(LucideIcon),
            new PropertyMetadata(20.0, OnSizeChanged));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public LucideIcon()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        VerticalAlignment = System.Windows.VerticalAlignment.Center;

        _path = new System.Windows.Shapes.Path
        {
            Stroke = Foreground ?? System.Windows.Media.Brushes.White,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            SnapsToDevicePixels = true
        };

        // Fixed 24x24 SVG coordinate canvas ensures 100% dead-center alignment
        _canvas = new System.Windows.Controls.Canvas
        {
            Width = 24,
            Height = 24,
            SnapsToDevicePixels = true
        };
        _canvas.Children.Add(_path);

        _viewbox = new System.Windows.Controls.Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _canvas,
            Width = Size,
            Height = Size,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        Content = _viewbox;
        Width = Size;
        Height = Size;

        Loaded += (s, e) => UpdateIcon();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty)
        {
            if (_path != null && Foreground != null)
            {
                _path.Stroke = Foreground;
            }
        }
    }

    private static void OnIconKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LucideIcon icon)
        {
            icon.UpdateIcon();
        }
    }

    private static void OnStrokeThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LucideIcon icon && icon._path != null)
        {
            icon._path.StrokeThickness = (double)e.NewValue;
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LucideIcon icon)
        {
            double newSize = (double)e.NewValue;
            icon.Width = newSize;
            icon.Height = newSize;
            if (icon._viewbox != null)
            {
                icon._viewbox.Width = newSize;
                icon._viewbox.Height = newSize;
            }
        }
    }

    public void UpdateIcon()
    {
        if (_path == null) return;
        _path.Stroke = Foreground ?? System.Windows.Media.Brushes.White;
        _path.StrokeThickness = StrokeThickness;

        string key = IconKey?.ToLowerInvariant().Trim() ?? "zap";
        string resKey = $"LucideIcon_{key}";

        if (System.Windows.Application.Current?.TryFindResource(resKey) is Geometry geom)
        {
            _path.Data = geom;
        }
        else if (System.Windows.Application.Current?.TryFindResource("LucideIcon_zap") is Geometry fallback)
        {
            _path.Data = fallback;
        }
    }
}
