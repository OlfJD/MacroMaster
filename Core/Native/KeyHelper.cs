using System.Collections.Concurrent;
using System.Windows.Input;

namespace MacroMaster.Core.Native;

public static class KeyHelper
{
    // Called for every macro on every keystroke inside the low-level hook, so cache the parse
    private static readonly ConcurrentDictionary<string, int> _vkCache = new();

    public static int NormalizeToVk(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        return _vkCache.GetOrAdd(key, ParseKeyName);
    }

    private static int ParseKeyName(string key)
    {
        string k = key.Trim().ToLowerInvariant();

        return k switch
        {
            "lbutton" or "leftclick" or "left click" or "lclick" or "mouse1" => 0x01,
            "rbutton" or "rightclick" or "right click" or "rclick" or "mouse2" => 0x02,
            "mbutton" or "middleclick" or "middle click" or "mclick" or "mouse3" => 0x04,
            "xbutton1" or "mouse4" or "mouse 4" or "x1" => 0x05,
            "xbutton2" or "mouse5" or "mouse 5" or "x2" => 0x06,
            "backspace" or "back" => 0x08,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "shift" or "lshift" or "rshift" => 0x10,
            "ctrl" or "lctrl" or "rctrl" or "control" => 0x11,
            "alt" or "lalt" or "ralt" => 0x12,
            "pause" => 0x13,
            "caps" or "capslock" => 0x14,
            "esc" or "escape" => 0x1B,
            "space" or "spacebar" => 0x20,
            "pgup" or "pageup" or "page up" => 0x21,
            "pgdn" or "pagedown" or "page down" => 0x22,
            "end" => 0x23,
            "home" => 0x24,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "printscreen" or "prtsc" or "snapshot" => 0x2C,
            "insert" or "ins" => 0x2D,
            "delete" or "del" => 0x2E,
            "numpad0" or "num0" => 0x60,
            "numpad1" or "num1" => 0x61,
            "numpad2" or "num2" => 0x62,
            "numpad3" or "num3" => 0x63,
            "numpad4" or "num4" => 0x64,
            "numpad5" or "num5" => 0x65,
            "numpad6" or "num6" => 0x66,
            "numpad7" or "num7" => 0x67,
            "numpad8" or "num8" => 0x68,
            "numpad9" or "num9" => 0x69,
            "multiply" or "num*" => 0x6A,
            "add" or "num+" => 0x6B,
            "subtract" or "num-" => 0x6D,
            "decimal" or "num." => 0x6E,
            "divide" or "num/" => 0x6F,
            "f1" => 0x70,
            "f2" => 0x71,
            "f3" => 0x72,
            "f4" => 0x73,
            "f5" => 0x74,
            "f6" => 0x75,
            "f7" => 0x76,
            "f8" => 0x77,
            "f9" => 0x78,
            "f10" => 0x79,
            "f11" => 0x7A,
            "f12" => 0x7B,
            "f13" => 0x7C,
            "f14" => 0x7D,
            "f15" => 0x7E,
            "f16" => 0x7F,
            "f17" => 0x80,
            "f18" => 0x81,
            "f19" => 0x82,
            "f20" => 0x83,
            "f21" => 0x84,
            "f22" => 0x85,
            "f23" => 0x86,
            "f24" => 0x87,
            "volume_mute" or "mute" => 0xAD,
            "volume_down" or "volumedown" => 0xAE,
            "volume_up" or "volumeup" => 0xAF,
            "media_next" or "nexttrack" => 0xB0,
            "media_prev" or "prevtrack" => 0xB1,
            "media_stop" => 0xB2,
            "media_play_pause" or "playpause" => 0xB3,
            // German / European Layout Keys (Destiny 2 skate keys: ä, ö, ü, ß)
            "ä" or "ae" => 0xDE,
            "ö" or "oe" => 0xC0,
            "ü" or "ue" => 0xBA,
            "ß" or "ss" => 0xDB,
            "tilde" or "~" => 0xC0,
            _ => ParseStandardKey(k)
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern short VkKeyScanW(char ch);

    /// <summary>
    /// Everything the name table above doesn't list. Together with <see cref="VkToString"/> this
    /// guarantees that any key the capture buttons can produce is understood again when it's read back.
    /// </summary>
    private static int ParseStandardKey(string k)
    {
        if (k.Length == 1)
        {
            char c = char.ToUpperInvariant(k[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;

            // Any other single character (+ # - , . < ^ …): ask the keyboard layout which key types it
            short scan = VkKeyScanW(k[0]);
            if (scan != -1) return scan & 0xFF;
        }

        switch (k)
        {
            case "win" or "lwin" or "windows": return 0x5B;
            case "rwin": return 0x5C;
            case "apps" or "menu" or "contextmenu": return 0x5D;
            case "numlock": return 0x90;
            case "scrolllock" or "scroll": return 0x91;
        }

        // Raw fallback written by VkToString for keys that have no name at all: "VK_0xE2"
        if (k.StartsWith("vk_0x", StringComparison.Ordinal) &&
            int.TryParse(k.AsSpan(5), System.Globalization.NumberStyles.HexNumber, null, out int raw) && raw is > 0 and < 256)
        {
            return raw;
        }

        if (Enum.TryParse<Key>(k, true, out var wpfKey))
        {
            return KeyInterop.VirtualKeyFromKey(wpfKey);
        }

        return 0;
    }

    public static string VkToString(uint vk)
    {
        return vk switch
        {
            0x01 => "Left Click",
            0x02 => "Right Click",
            0x04 => "Middle Click",
            0x05 => "Mouse 4",
            0x06 => "Mouse 5",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            >= 0x7C and <= 0x87 => $"F{vk - 0x7C + 13}",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x2C => "PrintScreen",
            0x5B => "Win",
            0x5C => "RWin",
            0x5D => "Apps",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            // Numpad keys type the same characters as the main block, so they need their own names
            >= 0x60 and <= 0x69 => $"Numpad{vk - 0x60}",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0xDE => "Ä",
            0xC0 => "Ö",
            0xBA => "Ü",
            0xDB => "ß",
            _ => ConvertVkToCharOrName(vk)
        };
    }

    private static string ConvertVkToCharOrName(uint vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();

        // Punctuation keys: show the character printed on the key in the current layout (+ # - , . < ^)
        uint mapped = Win32Api.MapVirtualKey(vk, 2) & 0x7FFFFFFF; // MAPVK_VK_TO_CHAR; top bit marks dead keys
        if (mapped > 0x20 && mapped < 0xFFFF && !char.IsControl((char)mapped) && NormalizeToVk(((char)mapped).ToString()) == (int)vk)
        {
            return ((char)mapped).ToString();
        }

        // Named keys WPF knows (media keys, left/right modifiers…); these parse back via Enum.TryParse
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        if (key != Key.None && NormalizeToVk(key.ToString()) == (int)vk) return key.ToString();

        // Last resort so that no key is ever "not recognised"
        return $"VK_0x{vk:X2}";
    }
}
