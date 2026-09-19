using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Native;

public class InputSimulator : IDisposable
{
    private bool _multimediaTimerActive = false;

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
        SendKeyboardInput((ushort)vk, false);
    }

    public void KeyUp(string key)
    {
        int vk = KeyHelper.NormalizeToVk(key);
        if (vk == 0) return;
        SendKeyboardInput((ushort)vk, true);
    }

    public void KeyTap(string key, int holdDurationMs = 25)
    {
        KeyDown(key);
        PreciseSleep(holdDurationMs);
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
    }

    public void MouseClick(MouseButtonType button, int holdDurationMs = 20)
    {
        MouseDown(button);
        PreciseSleep(holdDurationMs);
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

    public void ReleaseAllInputs()
    {
        // Release common mouse buttons
        MouseUp(MouseButtonType.Left);
        MouseUp(MouseButtonType.Right);
        MouseUp(MouseButtonType.Middle);

        // Release common modifier keys
        SendKeyboardInput(0x10, true); // Shift
        SendKeyboardInput(0x11, true); // Ctrl
        SendKeyboardInput(0x12, true); // Alt
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

    public static void PreciseSleep(int milliseconds)
    {
        if (milliseconds <= 0) return;

        var sw = Stopwatch.StartNew();
        long targetTicks = (long)(milliseconds * (Stopwatch.Frequency / 1000.0));

        if (milliseconds > 15)
        {
            // Thread.Sleep for the coarse portion (leaving ~2ms for precision spin)
            int sleepTime = milliseconds - 2;
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }

        // High precision spin-wait for the fine portion
        while (sw.ElapsedTicks < targetTicks)
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
