using System.Runtime.InteropServices;
using MacroMaster.Core.Models;

namespace MacroMaster.UI;

/// <summary>
/// Puts the notification popup and the mode indicator in their spot on the monitor the user is
/// looking at (the one of the app in front, i.e. the game). Works in physical pixels.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>
    /// Moves <paramref name="hwnd"/> to <paramref name="position"/> on the monitor of the foreground
    /// window. <paramref name="spot"/> is used for <see cref="HudPosition.Custom"/>.
    /// </summary>
    public static void Place(IntPtr hwnd, HudPosition position, ScreenSpot? spot, int edgeMarginDip)
    {
        if (hwnd == IntPtr.Zero) return;

        IntPtr fg = GetForegroundWindow();
        var area = System.Windows.Forms.Screen.FromHandle(fg != IntPtr.Zero ? fg : hwnd).WorkingArea;

        // Twice: the first move can land on a monitor with another DPI, which resizes the window
        for (int pass = 0; pass < 2; pass++)
        {
            GetWindowRect(hwnd, out RECT r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int margin = (int)Math.Round(edgeMarginDip * GetDpiForWindow(hwnd) / 96.0);

            int x, y;
            if (position == HudPosition.Custom)
            {
                spot ??= new ScreenSpot();
                x = area.Left + (int)Math.Round(spot.X * area.Width) - w / 2;
                y = area.Top + (int)Math.Round(spot.Y * area.Height) - h / 2;
            }
            else
            {
                x = position switch
                {
                    HudPosition.TopLeft or HudPosition.BottomLeft => area.Left + margin,
                    HudPosition.TopRight or HudPosition.BottomRight => area.Right - w - margin,
                    _ => area.Left + (area.Width - w) / 2
                };
                y = position is HudPosition.BottomLeft or HudPosition.BottomCenter or HudPosition.BottomRight
                    ? area.Bottom - h - margin
                    : area.Top + margin;
            }

            // Always fully on screen, whatever the saved spot says
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));

            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    /// <summary>Turns a point on screen (physical pixels) into a <see cref="ScreenSpot"/> on its monitor.</summary>
    public static ScreenSpot SpotAt(System.Drawing.Point centre)
    {
        var area = System.Windows.Forms.Screen.FromPoint(centre).WorkingArea;
        return new ScreenSpot
        {
            X = Math.Clamp((centre.X - area.Left) / (double)Math.Max(1, area.Width), 0, 1),
            Y = Math.Clamp((centre.Y - area.Top) / (double)Math.Max(1, area.Height), 0, 1)
        };
    }

    /// <summary>The same spot on the monitor of <paramref name="hwnd"/>, as a screen point (physical pixels).</summary>
    public static System.Drawing.Point PointFor(ScreenSpot spot, IntPtr hwnd)
    {
        var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        return new System.Drawing.Point(area.Left + (int)Math.Round(spot.X * area.Width),
                                         area.Top + (int)Math.Round(spot.Y * area.Height));
    }

    #region Win32

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    #endregion
}
