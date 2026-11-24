using MacroMaster.Core.Native;
using Xunit;

namespace MacroMaster.Tests;

/// <summary>What we hand to Windows for a key press (built, not sent: nothing reaches the PC).</summary>
public class InputInjectionTests
{
    private const ushort Left = 0x25, Numpad4 = 0x64, RightCtrl = 0xA3, Enter = 0x0D, Delete = 0x2E, Decimal = 0x6E;

    [Fact]
    public void Arrow_keys_carry_the_extended_flag_so_games_dont_read_a_numpad_key()
    {
        var left = InputSimulator.KeyEvent(Left, isKeyUp: false);
        var numpad4 = InputSimulator.KeyEvent(Numpad4, isKeyUp: false);

        // Same scan code on the keyboard; only the extended flag tells them apart
        Assert.Equal(0x4B, left.wScan);
        Assert.Equal(0x4B, numpad4.wScan);
        Assert.NotEqual(0u, left.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY);
        Assert.Equal(0u, numpad4.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY);

        // The virtual key stays, so programs reading it (and numpad keys with Num Lock off) are unchanged
        Assert.Equal(Left, left.wVk);
        Assert.Equal(Numpad4, numpad4.wVk);
    }

    [Theory]
    [InlineData(RightCtrl, 0x1D, true)]
    [InlineData(Delete, 0x53, true)]
    [InlineData(Decimal, 0x53, false)]   // numpad "," / ".": same code as Delete, not extended
    [InlineData((ushort)0x24, 0x47, true)]  // Home
    [InlineData((ushort)0x67, 0x47, false)] // numpad 7
    [InlineData((ushort)0x22, 0x51, true)]  // PgDn
    [InlineData((ushort)0x90, 0x45, true)]  // Num Lock
    [InlineData((ushort)0x2C, 0x37, true)]  // Print Screen
    [InlineData(Enter, 0x1C, false)]
    public void Keys_get_their_scan_code_and_extended_flag(ushort vk, int scan, bool extended)
    {
        var down = InputSimulator.KeyEvent(vk, isKeyUp: false);
        var up = InputSimulator.KeyEvent(vk, isKeyUp: true);

        Assert.Equal(scan, down.wScan);
        Assert.Equal(extended, (down.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY) != 0);
        Assert.Equal(0u, down.dwFlags & Win32Api.KEYEVENTF_KEYUP);
        Assert.NotEqual(0u, up.dwFlags & Win32Api.KEYEVENTF_KEYUP);
        Assert.Equal(down.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY, up.dwFlags & Win32Api.KEYEVENTF_EXTENDEDKEY);
    }
}
