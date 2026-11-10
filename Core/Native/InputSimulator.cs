using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Native;

public class InputSimulator : IDisposable
{
    private bool _multimediaTimerActive = false;

    // Inputs this simulator pressed and hasn't released yet, so ReleaseAllInputs can let go of
    // exactly those (instead of sending stray "up" events for buttons the user is holding)
    private readonly ConcurrentDictionary<ushort, byte> _heldKeys = new();
    private readonly ConcurrentDictionary<MouseButtonType, byte> _heldMouseButtons = new();

    public InputSimulator()
    {
        // Request 1ms timer resolution from Windows multimedia timer
        if (Win32Api.TimeBeginPeriod(1) == 0)
        {
            _multimediaTimerActive = true;
        }
    }

    public void KeyDown(string key)
    {
        int vk = KeyHelper.NormalizeToVk(key);
        if (vk == 0) return;
        _heldKeys[(ushort)vk] = 0;
        SendKeyboardInput((ushort)vk, false);
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

        _heldMouseButtons[button] = 0;
        Win32Api.mouse_event(flag, 0, 0, data, UIntPtr.Zero);
    }

    public void MouseUp(MouseButtonType button)
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

        Win32Api.mouse_event(flag, 0, 0, data, UIntPtr.Zero);
        _heldMouseButtons.TryRemove(button, out _);
    }

    public void MouseClick(MouseButtonType button, int holdDurationMs = 20, CancellationToken token = default)
    {
        MouseDown(button);
        PreciseSleep(holdDurationMs, token);
        MouseUp(button);
    }

    public void MouseMoveRelative(int dx, int dy)
    {
        // Direct hardware-level mouse event (identical to DllCall("mouse_event", 1, dx, dy) in AHK)
        Win32Api.mouse_event(Win32Api.MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
    }

    public void MouseScroll(int delta)
    {
        Win32Api.mouse_event(Win32Api.MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
    }

    public void SendMediaCommand(string command)
    {
        int vk = command.ToLowerInvariant() switch
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
            SendKeyboardInput((ushort)vk, false);
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
                SendKeyboardInput(0x0D, false);
                SendKeyboardInput(0x0D, true);
            }
            else
            {
                SendUnicodeInput(c, false);
                SendUnicodeInput(c, true);
            }
            PreciseSleep(6, token);
        }
    }

    private static void SendUnicodeInput(char c, bool isKeyUp)
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

        Win32Api.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Win32Api.INPUT)));
    }

    /// <summary>Releases every key and mouse button this simulator is still holding down.</summary>
    public void ReleaseAllInputs()
    {
        foreach (var button in _heldMouseButtons.Keys)
        {
            MouseUp(button);
        }

        foreach (var vk in _heldKeys.Keys)
        {
            SendKeyboardInput(vk, true);
            _heldKeys.TryRemove(vk, out _);
        }
    }

    private static void SendKeyboardInput(ushort vkCode, bool isKeyUp)
    {
        var input = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion
            {
                ki = new Win32Api.KEYBDINPUT
                {
                    wVk = vkCode,
                    wScan = (ushort)Win32Api.MapVirtualKey(vkCode, 0),
                    dwFlags = isKeyUp ? Win32Api.KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        Win32Api.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Win32Api.INPUT)));
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
        if (_multimediaTimerActive)
        {
            Win32Api.TimeEndPeriod(1);
            _multimediaTimerActive = false;
        }
        GC.SuppressFinalize(this);
    }
}
