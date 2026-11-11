using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MacroMaster.Core.Models;

namespace MacroMaster.UI.Windows;

/// <summary>
/// Small label that stays on screen while a mode group's app is in front and names the active
/// mode ("Wellskate"). Unlike the notification popup it doesn't go away by itself, so it must
/// never be in the way: clicks pass straight through it and it never takes focus.
/// </summary>
public partial class ModeIndicatorWindow : Window
{
    private const int EdgeMarginDip = 14;
    private IntPtr _hwnd;

    public ModeIndicatorWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            // Layered + transparent = mouse clicks go to the game underneath
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetLayeredWindowAttributes(_hwnd, 0, 225, LWA_ALPHA); // slightly see-through

            int corner = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            int border = 0x00433835; // #353843 as 0x00BBGGRR
            DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        };
    }

    /// <summary>Shows the label with this text in a screen corner (of the monitor the app in front is on).</summary>
    public void ShowMode(string text, HudPosition position)
    {
        ModeText.Text = text;
        if (!IsVisible) Show();
        UpdateLayout();
        Reposition(position);
    }

    private void Reposition(HudPosition position)
    {
        if (_hwnd == IntPtr.Zero) return;

        IntPtr fg = GetForegroundWindow();
        var area = System.Windows.Forms.Screen.FromHandle(fg != IntPtr.Zero ? fg : _hwnd).WorkingArea;

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

    #region Win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUNDSMALL = 3;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    #endregion
}
