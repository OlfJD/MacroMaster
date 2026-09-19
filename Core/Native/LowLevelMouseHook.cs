using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Native;

public class LowLevelMouseHook : IDisposable
{
    public delegate bool MouseEventHandler(MouseButtonType button, bool isDown);
    public event MouseEventHandler? OnMouseEvent;

    private IntPtr _hookId = IntPtr.Zero;
    private Win32Api.HookProc? _hookProc;

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookProc = HookCallback;
        IntPtr hMod = IntPtr.Zero;
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule?.ModuleName != null)
            {
                hMod = Win32Api.GetModuleHandle(curModule.ModuleName);
            }
        }
        catch { }

        _hookId = Win32Api.SetWindowsHookEx(
            Win32Api.WH_MOUSE_LL,
            _hookProc,
            hMod,
            0);
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32Api.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _hookProc = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnMouseEvent != null)
        {
            int msg = wParam.ToInt32();
            var mouse = Marshal.PtrToStructure<Win32Api.MSLLHOOKSTRUCT>(lParam);

            MouseButtonType? button = null;
            bool isDown = false;

            switch (msg)
            {
                case Win32Api.WM_LBUTTONDOWN:
                    button = MouseButtonType.Left;
                    isDown = true;
                    break;
                case Win32Api.WM_LBUTTONUP:
                    button = MouseButtonType.Left;
                    isDown = false;
                    break;
                case Win32Api.WM_RBUTTONDOWN:
                    button = MouseButtonType.Right;
                    isDown = true;
                    break;
                case Win32Api.WM_RBUTTONUP:
                    button = MouseButtonType.Right;
                    isDown = false;
                    break;
                case Win32Api.WM_MBUTTONDOWN:
                    button = MouseButtonType.Middle;
                    isDown = true;
                    break;
                case Win32Api.WM_MBUTTONUP:
                    button = MouseButtonType.Middle;
                    isDown = false;
                    break;
                case Win32Api.WM_XBUTTONDOWN:
                    button = (mouse.mouseData >> 16) == Win32Api.XBUTTON1 ? MouseButtonType.XButton1 : MouseButtonType.XButton2;
                    isDown = true;
                    break;
                case Win32Api.WM_XBUTTONUP:
                    button = (mouse.mouseData >> 16) == Win32Api.XBUTTON1 ? MouseButtonType.XButton1 : MouseButtonType.XButton2;
                    isDown = false;
                    break;
            }

            if (button.HasValue)
            {
                bool handled = OnMouseEvent.Invoke(button.Value, isDown);
                if (handled)
                {
                    return (IntPtr)1;
                }
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
