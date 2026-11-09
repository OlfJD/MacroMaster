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
    // Physically held keys (injected events are ignored so our own SendInput can't corrupt chord state)
    private readonly HashSet<int> _pressedVkCodes = new();
    // Keys whose initial key-down a handler swallowed: their auto-repeats and key-up are swallowed too
    private readonly HashSet<int> _suppressedVkCodes = new();
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
            _suppressedVkCodes.Clear();
        }
    }

    /// <summary>
    /// Low-level hooks report modifiers as left/right specific codes (VK_LSHIFT 0xA0 ... VK_RMENU 0xA5),
    /// so the generic VK_SHIFT/VK_CONTROL/VK_MENU (and VK_LWIN for "Win") match either side.
    /// </summary>
    public bool IsKeyDown(int vkCode)
    {
        lock (_lock)
        {
            return vkCode switch
            {
                0x10 => _pressedVkCodes.Contains(0x10) || _pressedVkCodes.Contains(0xA0) || _pressedVkCodes.Contains(0xA1),
                0x11 => _pressedVkCodes.Contains(0x11) || _pressedVkCodes.Contains(0xA2) || _pressedVkCodes.Contains(0xA3),
                0x12 => _pressedVkCodes.Contains(0x12) || _pressedVkCodes.Contains(0xA4) || _pressedVkCodes.Contains(0xA5),
                0x5B => _pressedVkCodes.Contains(0x5B) || _pressedVkCodes.Contains(0x5C),
                _ => _pressedVkCodes.Contains(vkCode)
            };
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

            if (!isInjected && (msg == Win32Api.WM_KEYDOWN || msg == Win32Api.WM_SYSKEYDOWN))
            {
                bool isRepeat;
                lock (_lock)
                {
                    isRepeat = !_pressedVkCodes.Add(vkCode);
                }

                if (isRepeat)
                {
                    // Auto-repeat while the key is held: don't re-fire handlers (a held toggle hotkey
                    // would flip on/off every repeat), but keep swallowing it if the first press was.
                    lock (_lock)
                    {
                        if (_suppressedVkCodes.Contains(vkCode)) return (IntPtr)1;
                    }
                }
                else if (OnKeyDown != null && OnKeyDown.Invoke(vkCode, isExtended))
                {
                    lock (_lock)
                    {
                        _suppressedVkCodes.Add(vkCode);
                    }
                    return (IntPtr)1; // Suppress original keystroke
                }
            }
            else if (!isInjected && (msg == Win32Api.WM_KEYUP || msg == Win32Api.WM_SYSKEYUP))
            {
                bool wasSuppressed;
                lock (_lock)
                {
                    _pressedVkCodes.Remove(vkCode);
                    wasSuppressed = _suppressedVkCodes.Remove(vkCode);
                }

                bool handled = OnKeyUp?.Invoke(vkCode, isExtended) ?? false;
                // The app never saw the key-down, so don't hand it an orphaned key-up either
                if (handled || wasSuppressed)
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
