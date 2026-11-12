using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;

namespace MacroMaster.UI;

/// <summary>
/// Makes the interface take up the same share of the screen on every monitor. The layout was
/// designed for a 1080p screen; Windows only enlarges apps by its own "Scale" setting, which many
/// people leave at 100 % on 1440p / 4K screens, so there everything looked small. "Auto" scales by
/// how much bigger the monitor is (in Windows' scaled pixels) than 1920 × 1080, never below 100 %.
/// The setting can also be a fixed percentage (Settings → Interface size).
/// </summary>
public static class UiScale
{
    /// <summary>The screen size (in device-independent pixels) the layout was designed on.</summary>
    private const double ReferenceWidth = 1920, ReferenceHeight = 1080;
    private const double MinAuto = 1.0, MaxAuto = 2.5;

    /// <summary>Choices offered in Settings; 0 = Auto.</summary>
    public static readonly int[] Choices = { 0, 80, 90, 100, 110, 125, 150, 175, 200 };

    /// <summary>0 = Auto, otherwise a fixed percentage (from AppSettings.UiScalePercent).</summary>
    public static int Preference { get; set; }

    /// <summary>The scale the main window currently uses; dialogs follow it.</summary>
    public static double Current { get; private set; } = 1.0;

    /// <summary>The scale to use for a window on the monitor of <paramref name="hwnd"/>.</summary>
    public static double Resolve(IntPtr hwnd) => ResolveOn(MonitorOf(hwnd));

    /// <summary>The scale to use on a monitor (from <see cref="MonitorOf"/> / <see cref="MonitorAtCursor"/>).</summary>
    public static double ResolveOn(IntPtr monitor) =>
        Preference > 0 ? Math.Clamp(Preference / 100.0, 0.5, 3.0) : AutoOn(monitor);

    /// <summary>What Auto picks on the monitor of <paramref name="hwnd"/> (or the primary one).</summary>
    public static double AutoFor(IntPtr hwnd) => AutoOn(MonitorOf(hwnd));

    private static double AutoOn(IntPtr monitor)
    {
        if (!TryGetMonitor(monitor, out var bounds, out _, out double dpiScale)) return 1.0;
        double w = bounds.Width / dpiScale, h = bounds.Height / dpiScale;
        // The smaller ratio, so ultrawide screens scale by their height and portrait ones by their width
        double s = Math.Min(w / ReferenceWidth, h / ReferenceHeight);
        return Math.Round(Math.Clamp(s, MinAuto, MaxAuto), 2);
    }

    /// <summary>Work area of a monitor in its own device-independent pixels.</summary>
    public static Rect WorkAreaDipOn(IntPtr monitor)
    {
        if (!TryGetMonitor(monitor, out _, out var work, out double dpiScale))
            return SystemParameters.WorkArea;
        return new Rect(work.X / dpiScale, work.Y / dpiScale, work.Width / dpiScale, work.Height / dpiScale);
    }

    /// <summary>Scales an element and everything in it.</summary>
    public static void ApplyTo(FrameworkElement root, double scale)
    {
        root.LayoutTransform = Math.Abs(scale - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Records the main window's scale and picks the app's text mode for it: "Display" snaps glyphs
    /// to the pixel grid and is sharpest at exactly 100 %, but its pixel-snapped metrics don't scale
    /// cleanly, so any other size uses "Ideal".
    /// </summary>
    public static void SetCurrent(double scale)
    {
        Current = scale;
        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.Resources["UiTextFormattingMode"] =
                Math.Abs(scale - 1.0) < 0.001 ? TextFormattingMode.Display : TextFormattingMode.Ideal;
        }
    }

    /// <summary>The caption (drag) strip is set in window units, outside the scaled content, so scale it too.</summary>
    public static void ScaleCaption(Window window, double baseCaptionHeight, double scale)
    {
        if (WindowChrome.GetWindowChrome(window) is not { } chrome) return;
        double target = Math.Round(baseCaptionHeight * scale);
        if (Math.Abs(chrome.CaptionHeight - target) < 0.5) return;
        var copy = (WindowChrome)chrome.Clone(); // the one from the style is frozen
        copy.CaptionHeight = target;
        WindowChrome.SetWindowChrome(window, copy);
    }

    /// <summary>
    /// For dialogs owned by the main window: scale content, fixed width and caption by
    /// <see cref="Current"/>. Call after InitializeComponent.
    /// </summary>
    public static void ApplyToDialog(Window dialog)
    {
        double s = Current;
        if (Math.Abs(s - 1.0) < 0.001 || dialog.Content is not FrameworkElement root) return;
        ApplyTo(root, s);
        if (!double.IsNaN(dialog.Width)) dialog.Width *= s;
        if (WindowChrome.GetWindowChrome(dialog) is { } chrome) ScaleCaption(dialog, chrome.CaptionHeight, s);
    }

    #region Win32

    private static bool TryGetMonitor(IntPtr mon, out Rect bounds, out Rect work, out double dpiScale)
    {
        bounds = work = Rect.Empty;
        dpiScale = 1.0;
        try
        {
            if (mon == IntPtr.Zero) mon = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
            if (mon == IntPtr.Zero) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref info)) return false;
            if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) != 0 || dpiX == 0) dpiX = 96;

            dpiScale = dpiX / 96.0;
            bounds = info.rcMonitor.ToRect();
            work = info.rcWork.ToRect();
            return bounds.Width > 0 && bounds.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Handle of the monitor a window is on, to notice when it moves to another one.</summary>
    public static IntPtr MonitorOf(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    /// <summary>The monitor the mouse is on: where the user is looking when they start the app.</summary>
    public static IntPtr MonitorAtCursor() =>
        GetCursorPos(out var pt) ? MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    #endregion
}
