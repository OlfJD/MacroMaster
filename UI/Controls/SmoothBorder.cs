using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace MacroMaster.UI.Controls;

/// <summary>
/// A <see cref="Border"/> whose rounded corners have no seam.
/// The stock Border draws the outline and the fill as two separate anti-aliased shapes, so at
/// every corner the colour behind the control shows through between them. On bright fills
/// (primary button, switch) that reads as a dark, jagged notch. This draws fill and outline as
/// one rounded rectangle instead, so the outline blends into the fill underneath it.
/// Falls back to the stock rendering for uneven thicknesses or corner radii.
/// </summary>
public class SmoothBorder : Border
{
    /// <summary>
    /// Clips the child to the rounded inside of the outline, so things that fill the whole width
    /// (row hover backgrounds, dividers) can't poke out past the corners.
    /// </summary>
    public bool ClipContent
    {
        get => (bool)GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    public static readonly DependencyProperty ClipContentProperty =
        DependencyProperty.Register(nameof(ClipContent), typeof(bool), typeof(SmoothBorder),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (Child != null)
        {
            if (ClipContent)
            {
                double t = BorderThickness.Left;
                double radius = Math.Max(0, CornerRadius.TopLeft - t / 2);
                // In the child's own coordinates: it starts after the outline and the padding
                var inner = new Rect(-Padding.Left, -Padding.Top,
                                     Math.Max(0, finalSize.Width - 2 * t), Math.Max(0, finalSize.Height - 2 * t));
                Child.Clip = new RectangleGeometry(inner, radius, radius);
            }
            else if (Child.Clip != null)
            {
                Child.Clip = null;
            }
        }
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        Thickness t = BorderThickness;
        CornerRadius r = CornerRadius;
        double w = RenderSize.Width, h = RenderSize.Height;

        bool uniform = t.Left == t.Top && t.Left == t.Right && t.Left == t.Bottom &&
                       r.TopLeft == r.TopRight && r.TopLeft == r.BottomLeft && r.TopLeft == r.BottomRight;
        bool hasOutline = t.Left > 0;
        if (!uniform || r.TopLeft <= 0 || (hasOutline && BorderBrush == null) || w <= t.Left * 2 || h <= t.Left * 2)
        {
            base.OnRender(dc);
            return;
        }

        double th = t.Left;
        Pen? pen = null;
        if (hasOutline)
        {
            pen = new Pen(BorderBrush, th);
            if (pen.CanFreeze) pen.Freeze();
        }

        // Like Border, CornerRadius is measured on the outline's centre line
        double half = hasOutline ? th / 2 : 0;
        var rect = new Rect(half, half, w - th, h - th);
        if (!hasOutline) rect = new Rect(0, 0, w, h);
        double radius = Math.Min(r.TopLeft, Math.Min(rect.Width, rect.Height) / 2);

        // Keep the straight edges on whole pixels (what SnapsToDevicePixels does for Border)
        var guides = new GuidelineSet();
        guides.GuidelinesX.Add(0); guides.GuidelinesX.Add(th); guides.GuidelinesX.Add(w - th); guides.GuidelinesX.Add(w);
        guides.GuidelinesY.Add(0); guides.GuidelinesY.Add(th); guides.GuidelinesY.Add(h - th); guides.GuidelinesY.Add(h);
        guides.Freeze();

        dc.PushGuidelineSet(guides);
        dc.DrawRoundedRectangle(Background, pen, rect, radius, radius);
        dc.Pop();
    }
}
