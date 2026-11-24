using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

/// <summary>Windows refusing a macro's input (lock screen, UAC, administrator window).</summary>
public class RefusedInputTests
{
    [Fact]
    public void A_loop_stops_and_says_so_when_Windows_refuses_its_input()
    {
        var clicker = Macro("Clicker", "F6", ExecutionMode.Toggle, actions: new[]
        {
            new MacroAction { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left, DelayMs = 2 },
            Wait(5)
        });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), clicker));
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("mouse down Left") >= 3));

        h.Sim.RefuseInput = true; // e.g. the PC was locked

        Assert.True(WaitFor(() => !clicker.IsRunning));
        Assert.Contains("Clicker | Stopped: Windows blocked its input", h.Hud);
        Assert.DoesNotContain(h.Hud, s => s.Contains("Stopped on an error"));

        // Unlocked again: the hotkey starts it as usual
        h.Sim.RefuseInput = false;
        int before = h.Sim.Count("mouse down Left");
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("mouse down Left") > before));
        h.Tap(F6);
    }

    [Fact]
    public void A_refused_press_is_not_taken_for_a_held_key()
    {
        // Nothing went down, so nothing may be "released" later (the player may hold S themselves)
        var walk = Macro("Walk", "l", actions: new MacroAction { Type = ActionType.KeyDown, Key = "S" });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), walk));
        h.Sim.RefuseInput = true;

        h.Tap(L);
        Assert.True(WaitFor(() => h.Hud.Contains("Walk | Stopped: Windows blocked its input")));

        h.Sim.RefuseInput = false;
        h.Engine.SetMasterEngineEnabled(false); // releases everything still held
        Thread.Sleep(50);
        Assert.Equal(0, h.Sim.Count("key up S"));
    }

    [Theory]
    [InlineData(1920)]
    [InlineData(2560)]
    [InlineData(4480)] // two monitors side by side
    [InlineData(3840)]
    public void Every_pixel_maps_to_an_absolute_position_that_lands_on_it(int size)
    {
        // Windows: pixel = value * size / 65536, fraction dropped
        for (int pixel = 0; pixel < size; pixel++)
        {
            int value = InputSimulator.ToAbsolute(pixel, size);
            Assert.InRange(value, 0, 65535);
            Assert.Equal(pixel, (int)((long)value * size / 65536));
        }
    }
}

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
