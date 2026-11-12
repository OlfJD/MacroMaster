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
    public void ShowMode(string text, HudPosition position, ScreenSpot? spot = null)
    {
        ModeText.Text = text;
        UiScale.ApplyTo((FrameworkElement)Content, UiScale.Resolve(GetForegroundWindow()));
        if (!IsVisible) Show();
        UpdateLayout();
        Reposition(position, spot);
    }

    private void Reposition(HudPosition position, ScreenSpot? spot)
        => OverlayPlacement.Place(_hwnd, position, spot, EdgeMarginDip);

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

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    #endregion
}
