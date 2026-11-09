using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Native;

public class LowLevelMouseHook : IDisposable
{
    public delegate bool MouseEventHandler(MouseButtonType button, bool isDown);
    public event MouseEventHandler? OnMouseEvent;

    private const uint LLMHF_INJECTED = 0x01;

    private IntPtr _hookId = IntPtr.Zero;
    private Win32Api.HookProc? _hookProc;
    // Buttons whose press a handler swallowed; the matching release is swallowed too.
    // Only touched on the hook thread, so no locking needed.
    private readonly HashSet<MouseButtonType> _suppressedButtons = new();

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
        _suppressedButtons.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnMouseEvent != null)
        {
            int msg = wParam.ToInt32();

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
                case Win32Api.WM_XBUTTONUP:
                    var data = Marshal.PtrToStructure<Win32Api.MSLLHOOKSTRUCT>(lParam);
                    button = (data.mouseData >> 16) == Win32Api.XBUTTON1 ? MouseButtonType.XButton1 : MouseButtonType.XButton2;
                    isDown = msg == Win32Api.WM_XBUTTONDOWN;
                    break;
            }

            // Mouse moves (by far the most frequent event) fall straight through without marshaling
            if (button.HasValue)
            {
                var mouse = Marshal.PtrToStructure<Win32Api.MSLLHOOKSTRUCT>(lParam);

                // Ignore our own synthesized clicks (auto clicker, macro actions) so they can't
                // re-trigger mouse-bound macros recursively
                if ((mouse.flags & LLMHF_INJECTED) == 0)
                {
                    bool handled = OnMouseEvent.Invoke(button.Value, isDown);
                    if (isDown && handled)
                    {
                        _suppressedButtons.Add(button.Value);
                        return (IntPtr)1;
                    }
                    if (!isDown && (_suppressedButtons.Remove(button.Value) || handled))
                    {
                        return (IntPtr)1;
                    }
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
