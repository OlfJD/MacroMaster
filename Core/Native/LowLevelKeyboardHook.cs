using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroMaster.Core.Native;

public class LowLevelKeyboardHook : IDisposable
{
    public delegate bool KeyEventHandler(int vkCode, bool isExtended);
    public event KeyEventHandler? OnKeyDown;
    public event KeyEventHandler? OnKeyUp;

    private IntPtr _hookId = IntPtr.Zero;
    private Win32Api.HookProc? _hookProc;
    private readonly HashSet<int> _pressedVkCodes = new();
    private readonly object _lock = new();

    public IReadOnlySet<int> PressedKeys
    {
        get
        {
            lock (_lock)
            {
                return new HashSet<int>(_pressedVkCodes);
            }
        }
    }

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

        if (hMod == IntPtr.Zero)
        {
            try
            {
                hMod = Win32Api.GetModuleHandle(null);
            }
            catch { }
        }

        _hookId = Win32Api.SetWindowsHookEx(
            Win32Api.WH_KEYBOARD_LL,
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

        lock (_lock)
        {
            _pressedVkCodes.Clear();
        }
    }

    public bool IsKeyDown(int vkCode)
    {
        lock (_lock)
        {
            return _pressedVkCodes.Contains(vkCode);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<Win32Api.KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)kbd.vkCode;
            bool isExtended = (kbd.flags & Win32Api.KEYEVENTF_EXTENDEDKEY) != 0;
            bool isInjected = (kbd.flags & 0x10) != 0; // LLKHF_INJECTED
            int msg = wParam.ToInt32();

            if (msg == Win32Api.WM_KEYDOWN || msg == Win32Api.WM_SYSKEYDOWN)
            {
                lock (_lock)
                {
                    _pressedVkCodes.Add(vkCode);
                }

                if (!isInjected && OnKeyDown != null)
                {
                    bool handled = OnKeyDown.Invoke(vkCode, isExtended);
                    if (handled)
                    {
                        return (IntPtr)1; // Suppress original keystroke
                    }
                }
            }
            else if (msg == Win32Api.WM_KEYUP || msg == Win32Api.WM_SYSKEYUP)
            {
                lock (_lock)
                {
                    _pressedVkCodes.Remove(vkCode);
                }

                if (!isInjected && OnKeyUp != null)
                {
                    bool handled = OnKeyUp.Invoke(vkCode, isExtended);
                    if (handled)
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
