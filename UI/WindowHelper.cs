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

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Brings the window in front of whatever has the focus (e.g. a game). Windows normally only lets
    /// the foreground app hand over the focus, so this briefly joins that app's input queue first.
    /// Works over windowed and borderless games; an exclusive-fullscreen game gets minimized by Windows.
    /// </summary>
    public static void ForceToFront(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint thisThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(thisThread, foregroundThread, false);
        }

        // Jump above "always on top" windows as well, without staying on top afterwards
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
        window.Focus();
    }

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
