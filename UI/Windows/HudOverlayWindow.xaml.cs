using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MacroMaster.Core.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MacroMaster.UI.Windows;

/// <summary>
/// Small on-screen notification ("Wellskate armed", "Everything stopped", ...).
/// Never takes focus and shows on the monitor you're playing on.
///
/// It is deliberately a normal, opaque window: WPF renders any layered/transparent window with
/// per-pixel alpha, which turns off ClearType and made the old popups look fuzzy. The trade-off is
/// that it can't be click-through, so it gets out of the way as soon as the mouse touches it.
/// Windows' own show/hide animation provides the fade.
/// </summary>
public partial class HudOverlayWindow : Window
{
    private const int EdgeMarginDip = 28;

    private readonly DispatcherTimer _hideTimer = new();
    private IntPtr _hwnd;

    public HudOverlayWindow()
    {
        InitializeComponent();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        // Never sit under the cursor: vanish the moment the mouse reaches it
        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            // Never activated (no focus stealing from the game), not in Alt+Tab
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            int border = 0x00433835; // #353843 as 0x00BBGGRR
            DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        };
    }

    public void ShowNotification(string title, string subtitle, string iconKey, string accentColorHex,
                                 HudPosition position = HudPosition.TopCenter, int durationMs = 2200)
    {
        // BeginInvoke: this is often called from inside the low-level input hook, which must return fast
        Dispatcher.BeginInvoke(() =>
        {
            HudTitle.Text = title;
            HudSubtitle.Text = subtitle;
            HudSubtitle.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;

            bool isLogo = iconKey == "logo";
            HudLogo.Visibility = isLogo ? Visibility.Visible : Visibility.Collapsed;
            IconBadge.Visibility = isLogo ? Visibility.Collapsed : Visibility.Visible;

            if (!isLogo)
            {
                HudIcon.IconKey = iconKey;
                var accent = ParseColor(accentColorHex);
                HudIcon.Foreground = new SolidColorBrush(accent);
                // Solid tint (~16 % accent over the popup background), so text and icon stay ClearType-crisp
                IconBadge.Background = new SolidColorBrush(Blend(Color.FromRgb(0x1D, 0x1F, 0x26), accent, 0.16));
            }

            UiScale.ApplyTo((FrameworkElement)Content, UiScale.Resolve(GetForegroundWindow()));
            if (!IsVisible) Show();
            UpdateLayout();
            Reposition(position);

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, durationMs));
            _hideTimer.Start();
        });
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x7C, 0x83, 0xFF); }
    }

    private static Color Blend(Color bg, Color fg, double t) => Color.FromRgb(
        (byte)(bg.R + (fg.R - bg.R) * t), (byte)(bg.G + (fg.G - bg.G) * t), (byte)(bg.B + (fg.B - bg.B) * t));

    #region Placement (physical pixels on the monitor of the foreground window)

    private void Reposition(HudPosition position)
    {
        if (_hwnd == IntPtr.Zero) return;

        // Show on the screen the user is looking at (the game), not always the primary one
        IntPtr fg = GetForegroundWindow();
        var screen = System.Windows.Forms.Screen.FromHandle(fg != IntPtr.Zero ? fg : _hwnd);
        var area = screen.WorkingArea;

        // Twice: the first move can land on a monitor with another DPI, which resizes the window
        for (int pass = 0; pass < 2; pass++)
        {
            GetWindowRect(_hwnd, out RECT r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int margin = (int)Math.Round(EdgeMarginDip * GetDpiForWindow(_hwnd) / 96.0);

            int x = position switch
            {
                HudPosition.TopLeft => area.Left + margin,
                HudPosition.TopRight or HudPosition.BottomRight => area.Right - w - margin,
                _ => area.Left + (area.Width - w) / 2
            };
            int y = position is HudPosition.BottomRight or HudPosition.BottomCenter
                ? area.Bottom - h - margin
                : area.Top + margin;

            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    #endregion

    #region Win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    #endregion
}
