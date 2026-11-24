using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Native;

public class InputSimulator : IDisposable
{
    // Inputs this simulator pressed and hasn't released yet, so ReleaseAllInputs can let go of
    // exactly those (instead of sending stray "up" events for buttons the user is holding)
    private readonly ConcurrentDictionary<ushort, byte> _heldKeys = new();
    private readonly ConcurrentDictionary<MouseButtonType, byte> _heldMouseButtons = new();

    /// <summary>
    /// A macro run starts: ask Windows for the 1 ms timer (short waits such as the 1 ms yield of a
    /// loop depend on it). It used to be held from start-up on, also while MacroMaster only sat in the
    /// tray, which keeps the PC's timer ticking fast and costs power. Windows counts these requests,
    /// so every run asks for its own and <see cref="EndPreciseTiming"/> gives it back.
    /// </summary>
    public virtual void BeginPreciseTiming() => Win32Api.TimeBeginPeriod(1);

    /// <summary>A macro run ended: give its 1 ms timer request back.</summary>
    public virtual void EndPreciseTiming() => Win32Api.TimeEndPeriod(1);

    /// <summary>A press, click or move Windows refused ends the macro's run (see <see cref="InputBlockedException"/>).</summary>
    private static void Require(bool accepted)
    {
        if (!accepted) throw new InputBlockedException();
    }

    public void KeyDown(string key)
    {
        int vk = KeyHelper.NormalizeToVk(key);
        if (vk == 0) return;
        Require(SendKeyboardInput((ushort)vk, false));
        // Noted only once it's sent: a ReleaseAllInputs on another thread at this very moment either
        // sees it and lets go, or the macro's run lets go of it when it ends. Noted first, it could be
        // "released" before it was even pressed, and stay held for good.
        _heldKeys[(ushort)vk] = 0;
    }

    public void KeyUp(string key)
    {
        int vk = KeyHelper.NormalizeToVk(key);
        if (vk == 0) return;
        SendKeyboardInput((ushort)vk, true);
        _heldKeys.TryRemove((ushort)vk, out _);
    }

    public void KeyTap(string key, int holdDurationMs = 25, CancellationToken token = default)
    {
        KeyDown(key);
        PreciseSleep(holdDurationMs, token); // cancellable, so the killswitch can cut a long hold short
        KeyUp(key);
    }

    public void MouseDown(MouseButtonType button)
    {
        uint flag = button switch
        {
            MouseButtonType.Left => Win32Api.MOUSEEVENTF_LEFTDOWN,
            MouseButtonType.Right => Win32Api.MOUSEEVENTF_RIGHTDOWN,
            MouseButtonType.Middle => Win32Api.MOUSEEVENTF_MIDDLEDOWN,
            MouseButtonType.XButton1 => Win32Api.MOUSEEVENTF_XDOWN,
            MouseButtonType.XButton2 => Win32Api.MOUSEEVENTF_XDOWN,
            _ => Win32Api.MOUSEEVENTF_LEFTDOWN
        };

        uint data = button switch
        {
            MouseButtonType.XButton1 => Win32Api.XBUTTON1,
            MouseButtonType.XButton2 => Win32Api.XBUTTON2,
            _ => 0
        };

        Require(SendMouseEvent(flag, 0, 0, data));
        _heldMouseButtons[button] = 0; // after sending, see KeyDown
    }

    public void MouseUp(MouseButtonType button)
    {
        SendMouseUp(button);
        _heldMouseButtons.TryRemove(button, out _);
    }

    private void SendMouseUp(MouseButtonType button)
    {
        uint flag = button switch
        {
            MouseButtonType.Left => Win32Api.MOUSEEVENTF_LEFTUP,
            MouseButtonType.Right => Win32Api.MOUSEEVENTF_RIGHTUP,
            MouseButtonType.Middle => Win32Api.MOUSEEVENTF_MIDDLEUP,
            MouseButtonType.XButton1 => Win32Api.MOUSEEVENTF_XUP,
            MouseButtonType.XButton2 => Win32Api.MOUSEEVENTF_XUP,
            _ => Win32Api.MOUSEEVENTF_LEFTUP
        };

        uint data = button switch
        {
            MouseButtonType.XButton1 => Win32Api.XBUTTON1,
            MouseButtonType.XButton2 => Win32Api.XBUTTON2,
            _ => 0
        };

        SendMouseEvent(flag, 0, 0, data);
    }

    /// <summary>
    /// Lets go of this key if a macro is holding it. Several threads may try at once (the engine
    /// switch and the run that pressed it); only the one that takes it off the list sends the "up".
    /// </summary>
    public bool ReleaseIfHeld(string key)
    {
        int vk = KeyHelper.NormalizeToVk(key);
        if (vk == 0 || !_heldKeys.TryRemove((ushort)vk, out _)) return false;
        SendKeyboardInput((ushort)vk, true);
        return true;
    }

    /// <summary>Lets go of this mouse button if a macro is holding it (sent once, see the key version).</summary>
    public bool ReleaseIfHeld(MouseButtonType button)
    {
        if (!_heldMouseButtons.TryRemove(button, out _)) return false;
        SendMouseUp(button);
        return true;
    }

    public void MouseClick(MouseButtonType button, int holdDurationMs = 20, CancellationToken token = default)
    {
        MouseDown(button);
        PreciseSleep(holdDurationMs, token);
        MouseUp(button);
    }

    public void MouseMoveRelative(int dx, int dy)
    {
        // A relative move, like the mouse itself sends (what DllCall("mouse_event", 1, dx, dy) does in AHK)
        Require(SendMouseEvent(Win32Api.MOUSEEVENTF_MOVE, dx, dy, 0));
    }

    /// <summary>Puts the pointer on an exact screen position (pixels from the top-left of the main screen).</summary>
    public void MouseMoveTo(int x, int y) => Require(SetPointerPosition(x, y));

    /// <summary>
    /// An absolute move through SendInput, like a tablet would send, rather than SetCursorPos: it goes
    /// through the input stream (games that read raw input see it too) and Windows says whether it
    /// took it. The position is given in 0…65535 across all monitors.
    /// </summary>
    protected virtual bool SetPointerPosition(int x, int y)
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0) return SetCursorPos(x, y);

        return SendMouseEvent(Win32Api.MOUSEEVENTF_MOVE | Win32Api.MOUSEEVENTF_ABSOLUTE | Win32Api.MOUSEEVENTF_VIRTUALDESK,
                              ToAbsolute(x - left, width), ToAbsolute(y - top, height), 0);
    }

    /// <summary>
    /// Pixel → 0…65535. Windows turns the value back into pixel = value × size / 65536 and drops the
    /// fraction, so this rounds up to land on exactly that pixel.
    /// </summary>
    internal static int ToAbsolute(int pixel, int size)
        => (int)Math.Clamp(((long)pixel * 65536 + size - 1) / size, 0, 65535);

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>Colour of one screen pixel as 0xRRGGBB, or -1 if Windows wouldn't say.</summary>
    public virtual int ReadPixel(int x, int y) => ReadScreenPixel(x, y);

    public static int ReadScreenPixel(int x, int y)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return -1;
        try
        {
            uint c = GetPixel(dc, x, y); // 0x00BBGGRR
            if (c == 0xFFFFFFFF) return -1;
            return (int)(((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF));
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

    public void MouseScroll(int delta)
    {
        Require(SendMouseEvent(Win32Api.MOUSEEVENTF_WHEEL, 0, 0, (uint)delta));
    }

    public void SendMediaCommand(string command)
    {
        int vk = (command ?? string.Empty).ToLowerInvariant() switch // null from a hand-edited / imported file
        {
            "playpause" or "play_pause" or "pause" => 0xB3, // VK_MEDIA_PLAY_PAUSE
            "next" or "nexttrack" => 0xB0,                  // VK_MEDIA_NEXT_TRACK
            "prev" or "prevtrack" => 0xB1,                  // VK_MEDIA_PREV_TRACK
            "stop" => 0xB2,                                 // VK_MEDIA_STOP
            "volumeup" => 0xAF,                             // VK_VOLUME_UP
            "volumedown" => 0xAE,                           // VK_VOLUME_DOWN
            "mute" => 0xAD,                                 // VK_VOLUME_MUTE
            _ => 0xB3
        };

        if (vk != 0)
        {
            Require(SendKeyboardInput((ushort)vk, false));
            PreciseSleep(20);
            SendKeyboardInput((ushort)vk, true);
        }
    }

    /// <summary>
    /// Types text as Unicode characters, so it comes out the same on every keyboard layout.
    /// Line breaks are sent as Enter. A few ms between characters, because games and chat boxes
    /// drop letters that arrive faster.
    /// </summary>
    public void TypeText(string text, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text.Replace("\r\n", "\n"))
        {
            if (token.IsCancellationRequested) return;

            if (c == '\n')
            {
                Require(SendKeyboardInput(0x0D, false));
                SendKeyboardInput(0x0D, true);
            }
            else
            {
                Require(SendUnicodeInput(c, false));
                SendUnicodeInput(c, true);
            }
            PreciseSleep(6, token);
        }
    }

    protected virtual bool SendUnicodeInput(char c, bool isKeyUp)
    {
        var input = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion
            {
                ki = new Win32Api.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = Win32Api.KEYEVENTF_UNICODE | (isKeyUp ? Win32Api.KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        return Send(input);
    }

    /// <summary>Releases every key and mouse button this simulator is still holding down.</summary>
    public void ReleaseAllInputs()
    {
        foreach (var button in _heldMouseButtons.Keys)
        {
            ReleaseIfHeld(button);
        }

        foreach (var vk in _heldKeys.Keys)
        {
            if (_heldKeys.TryRemove(vk, out _)) SendKeyboardInput(vk, true);
        }
    }

    /// <summary>
    /// The Send* methods and <see cref="SetPointerPosition"/> are the only places that touch the real
    /// keyboard and mouse; tests override them to record instead. Each returns whether Windows took
    /// the event.
    /// </summary>
    protected virtual bool SendMouseEvent(uint flags, int dx, int dy, uint data)
    {
        // SendInput instead of the old mouse_event: same event, but Windows says whether it took it
        var input = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_MOUSE,
            u = new Win32Api.InputUnion
            {
                mi = new Win32Api.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, time = 0, dwExtraInfo = UIntPtr.Zero }
            }
        };
        return Send(input);
    }

    protected virtual bool SendKeyboardInput(ushort vkCode, bool isKeyUp)
    {
        var input = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion { ki = KeyEvent(vkCode, isKeyUp) }
        };
        return Send(input);
    }

    private static readonly int InputSize = Marshal.SizeOf<Win32Api.INPUT>();

    private static bool Send(Win32Api.INPUT input) => Win32Api.SendInput(1, new[] { input }, InputSize) == 1;

    private const uint MAPVK_VK_TO_VSC_EX = 4;

    /// <summary>
    /// One key event as a real keyboard would produce it: the virtual key (what normal programs read)
    /// plus its scan code (what games reading the keyboard directly look at), with the "extended" flag
    /// for the keys that share a scan code with a numpad key. Without that flag such games took the
    /// arrow keys, Home / End, PgUp / PgDn, Insert / Delete, right Ctrl / Alt and numpad Enter for
    /// numpad keys. The virtual key stays set (rather than sending by scan code alone) so a numpad
    /// key doesn't turn into Insert / End / … depending on Num Lock.
    /// </summary>
    internal static Win32Api.KEYBDINPUT KeyEvent(ushort vkCode, bool isKeyUp)
    {
        uint scan = Win32Api.MapVirtualKey(vkCode, MAPVK_VK_TO_VSC_EX); // e.g. 0xE01D for right Ctrl: prefix byte + code
        // Windows' own list of extended keys. The lookup above only reports some of them: for the
        // arrows, Home / End, PgUp / PgDn and Insert / Delete it returns the numpad key's code,
        // because with Num Lock off the numpad sends those same virtual keys.
        bool extended = (scan >> 8) is 0xE0 or 0xE1 || vkCode is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or 0x90 or 0x2C;
        if (vkCode == 0x2C) scan = 0x37; // Print Screen sends E0 37 (the table names SysRq's 0x54)
        return new Win32Api.KEYBDINPUT
        {
            wVk = vkCode,
            wScan = (ushort)(scan & 0xFF),
            dwFlags = (isKeyUp ? Win32Api.KEYEVENTF_KEYUP : 0) | (extended ? Win32Api.KEYEVENTF_EXTENDEDKEY : 0),
            time = 0,
            dwExtraInfo = UIntPtr.Zero
        };
    }

    #region High-Resolution Waitable Timer

    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x1F0003;
    private const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr hTimer, ref long pDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

    // One timer per thread (macro tasks run on thread-pool threads, which are reused)
    [ThreadStatic] private static IntPtr t_timer;
    [ThreadStatic] private static bool t_timerUnavailable;

    /// <summary>
    /// Blocks for ~<paramref name="milliseconds"/> using a high-resolution waitable timer
    /// (Windows 10 1803+). Unlike Thread.Sleep it isn't quantised to the 15.6 ms system tick, which
    /// Windows 11 restores for background/minimised processes even after timeBeginPeriod(1).
    /// Returns false if the timer isn't available.
    /// </summary>
    private static bool HighResolutionWait(int milliseconds, CancellationToken token)
    {
        if (t_timerUnavailable) return false;
        if (t_timer == IntPtr.Zero)
        {
            t_timer = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            if (t_timer == IntPtr.Zero)
            {
                t_timerUnavailable = true;
                return false;
            }
        }

        long dueTime = -milliseconds * 10_000L; // negative = relative, in 100 ns units
        if (!SetWaitableTimer(t_timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false)) return false;

        if (token.CanBeCanceled)
        {
            var handles = new[] { t_timer, token.WaitHandle.SafeWaitHandle.DangerousGetHandle() };
            WaitForMultipleObjects(2, handles, false, INFINITE);
        }
        else
        {
            WaitForSingleObject(t_timer, INFINITE);
        }
        return true;
    }

    #endregion

    public static void PreciseSleep(int milliseconds) => PreciseSleep(milliseconds, CancellationToken.None);

    /// <summary>
    /// Precise delay: sleeps on a high-resolution timer for all but the last ~1 ms, then spin-waits
    /// the remainder. Returns early if <paramref name="token"/> is cancelled.
    /// </summary>
    public static void PreciseSleep(int milliseconds, CancellationToken token)
    {
        if (milliseconds <= 0 || token.IsCancellationRequested) return;

        var sw = Stopwatch.StartNew();
        long targetTicks = (long)(milliseconds * (Stopwatch.Frequency / 1000.0));

        int coarseMs = milliseconds - 1;
        if (coarseMs > 0 && !HighResolutionWait(coarseMs, token))
        {
            // Fallback: coarse Thread.Sleep only where 15.6 ms tick granularity can't overshoot
            if (milliseconds > 15)
            {
                if (token.CanBeCanceled) token.WaitHandle.WaitOne(milliseconds - 2);
                else Thread.Sleep(milliseconds - 2);
            }
        }

        // High precision spin-wait for the fine portion
        while (sw.ElapsedTicks < targetTicks && !token.IsCancellationRequested)
        {
            Thread.SpinWait(20);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
