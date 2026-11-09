using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MacroMaster.UI;

/// <summary>
/// Small DWM helpers for the custom-chrome windows: Windows 11 rounded corners,
/// a dark 1px frame colour, and correct padding when maximized.
/// </summary>
public static class WindowHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Attach(Window window, FrameworkElement? root = null)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                // COLORREF is 0x00BBGGRR – #2A2C35
                int border = 0x00352C2A;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch
            {
                // Older Windows: attributes are simply ignored
            }
        };

        if (root != null)
        {
            // With WindowChrome a maximized window overhangs the screen by the resize border
            window.StateChanged += (_, _) =>
            {
                root.Margin = window.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            };
        }
    }
}
