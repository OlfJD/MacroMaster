using System.Text.Json;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

public class ModeSwitchTests
{
    private static EngineHarness Destiny(out MacroDefinition well, out MacroDefinition ground)
    {
        well = Mode("Wellskate", "l", "PgUp", active: true, marker: "W");
        ground = Mode("Groundskate", "l", "PgDn", active: false, marker: "G");
        return new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, well, ground));
    }

    [Fact]
    public void Switch_key_changes_the_mode_inside_the_designated_app()
    {
        using var h = Destiny(out var well, out var ground);
        h.InApp("destiny2");

        bool swallowed = h.Tap(PgDn);

        Assert.False(swallowed); // the game still gets the key
        Assert.False(well.IsCurrentlyArmed);
        Assert.True(ground.IsCurrentlyArmed);
        Assert.Equal(1, h.SaveRequests);
    }

    [Fact]
    public void Switch_key_changes_the_mode_while_MacroMaster_is_in_front()
    {
        using var h = Destiny(out var well, out var ground);
        h.InMacroMaster();

        h.Tap(PgDn);
        Assert.True(ground.IsCurrentlyArmed);

        h.Tap(PgUp);
        Assert.True(well.IsCurrentlyArmed);
        Assert.False(ground.IsCurrentlyArmed);
    }

    [Fact]
    public void Switch_key_does_nothing_on_the_desktop_or_in_other_apps()
    {
        using var h = Destiny(out var well, out var ground);

        foreach (var app in new[] { "explorer", "chrome", "notepad" })
        {
            h.InApp(app);
            Assert.False(h.Tap(PgDn));
        }

        Assert.True(well.IsCurrentlyArmed);
        Assert.False(ground.IsCurrentlyArmed);
        Assert.Equal(0, h.SaveRequests);
        Assert.Empty(h.Hud);
    }

    [Fact]
    public void Switch_key_does_nothing_while_the_profile_is_off_even_in_MacroMaster()
    {
        using var h = Destiny(out var well, out var ground);
        h.Engine.Profiles[0].IsOn = false;
        h.InMacroMaster();

        h.Tap(PgDn);

        Assert.True(well.IsCurrentlyArmed);
    }

    [Fact]
    public void The_hotkey_runs_whichever_mode_is_active()
    {
        using var h = Destiny(out _, out _);
        h.InApp("destiny2");

        Assert.True(h.Tap(L));
        Assert.True(WaitFor(() => h.Sim.Count("char W") == 1));

        h.Tap(PgDn);
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char G") == 1));
        Assert.Equal(1, h.Sim.Count("char W"));
    }

    [Fact]
    public void Macros_themselves_do_not_fire_in_MacroMaster_or_other_apps()
    {
        using var h = Destiny(out _, out _);

        h.InMacroMaster();
        Assert.False(h.Tap(L));
        h.InApp("chrome");
        Assert.False(h.Tap(L));

        Thread.Sleep(80);
        Assert.Empty(h.Sim.Sent);
    }
}

public class ModifierTests
{
    [Fact]
    public void A_plain_hotkey_does_not_fire_with_an_extra_modifier_held()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), Macro("Plain", "l", actions: Type("x"))));

        h.Press(Ctrl);
        Assert.False(h.Tap(L)); // Ctrl+L belongs to the app in front
        h.Release(Ctrl);

        h.Press(Shift);
        Assert.False(h.Tap(L));
        h.Release(Shift);

        Thread.Sleep(60);
        Assert.Empty(h.Sim.Sent);

        Assert.True(h.Tap(L));
        Assert.True(WaitFor(() => h.Sim.Count("char x") == 1));
    }

    [Fact]
    public void Ctrl_L_and_L_can_be_two_different_macros()
    {
        var plain = Macro("Plain", "l", actions: Type("p"));
        var withCtrl = Macro("With Ctrl", "l", actions: Type("c"));
        withCtrl.Trigger.RequireCtrl = true;
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), plain, withCtrl));

        h.Press(Ctrl);
        Assert.True(h.Tap(L));
        h.Release(Ctrl);
        Assert.True(WaitFor(() => h.Sim.Count("char c") == 1));
        Assert.Equal(0, h.Sim.Count("char p"));

        Assert.True(h.Tap(L));
        Assert.True(WaitFor(() => h.Sim.Count("char p") == 1));
        Assert.Equal(1, h.Sim.Count("char c"));
    }

    [Fact]
    public void A_required_modifier_does_not_allow_other_modifiers()
    {
        var withCtrl = Macro("With Ctrl", "l", actions: Type("c"));
        withCtrl.Trigger.RequireCtrl = true;
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), withCtrl));

        h.Press(Ctrl);
        h.Press(Shift);
        Assert.False(h.Tap(L)); // Ctrl+Shift+L is not Ctrl+L
    }

    [Fact]
    public void A_modifier_can_itself_be_the_hotkey()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), Macro("On Shift", "Shift", actions: Type("s"))));

        Assert.True(h.Tap(Shift));
        Assert.True(WaitFor(() => h.Sim.Count("char s") == 1));
    }
}

public class EngineSwitchTests
{
    [Fact]
    public void The_engine_key_stops_everything_and_keeps_hotkeys_off_until_pressed_again()
    {
        var hold = Macro("Hold click", "F6", ExecutionMode.Toggle, actions: MouseDown());
        var once = Macro("Once", "l", actions: Type("x"));
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), hold, once));

        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("mouse down Left") == 1));
        Assert.True(hold.IsRunning);

        // Off: the key is swallowed, the loop stops, the held button is released
        Assert.True(h.Tap(F12));
        Assert.False(h.Settings.MasterEngineEnabled);
        Assert.False(hold.IsRunning);
        Assert.Equal(1, h.Sim.Count("mouse up Left"));
        Assert.Equal(1, h.SaveRequests);

        // While off no hotkey does anything, and the keys reach the app in front
        Assert.False(h.Tap(F6));
        Assert.False(h.Tap(L));
        Thread.Sleep(60);
        Assert.Equal(1, h.Sim.Count("mouse down Left"));
        Assert.Equal(0, h.Sim.Count("char x"));

        // On again with the same key: hotkeys are back, nothing restarts by itself
        Assert.True(h.Tap(F12));
        Assert.True(h.Settings.MasterEngineEnabled);
        Assert.False(hold.IsRunning);
        Assert.True(h.Tap(L));
        Assert.True(WaitFor(() => h.Sim.Count("char x") == 1));
    }

    [Fact]
    public void Turning_the_engine_off_cuts_a_long_macro_short()
    {
        var slow = Macro("Slow", "l", actions: new[] { Type("a"), Wait(5000), Type("b") });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), slow));

        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char a") == 1));
        h.Tap(F12);

        Thread.Sleep(150);
        Assert.Equal(0, h.Sim.Count("char b"));
    }

    [Fact]
    public void No_engine_key_means_F12_is_an_ordinary_key()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>()));
        h.Settings.EngineToggleKey = "";

        Assert.False(h.Tap(F12));
        Assert.True(h.Settings.MasterEngineEnabled);
    }

    [Fact]
    public void An_old_kill_switch_key_becomes_the_engine_key()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("""{ "DataVersion": 3, "EmergencyKillswitchKey": "F9", "MasterEngineEnabled": true }""")!;
        old.UpgradeLegacyKeys();

        Assert.Equal("F9", old.EngineToggleKey);
        string saved = JsonSerializer.Serialize(old);
        Assert.DoesNotContain("EmergencyKillswitchKey", saved);
        Assert.Equal("F9", JsonSerializer.Deserialize<AppSettings>(saved)!.EngineToggleKey);
    }
}

public class LoopTests
{
    [Fact]
    public void A_hold_macro_presses_once_instead_of_spamming()
    {
        var hold = Macro("Hold click", "F6", ExecutionMode.Toggle, actions: MouseDown());
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), hold));

        h.Tap(F6);
        Thread.Sleep(300);

        Assert.Equal(1, h.Sim.Count("mouse down Left")); // was: tens of thousands
        Assert.True(hold.IsRunning);

        h.Tap(F6);
        Assert.True(WaitFor(() => !hold.IsRunning));
        Assert.Equal(1, h.Sim.Count("mouse up Left"));
    }

    [Fact]
    public void A_loop_without_any_delay_is_throttled()
    {
        var spam = Macro("Spam", "F6", ExecutionMode.Toggle, actions: new MacroAction { Type = ActionType.MouseMoveRelative, MoveX = 1 });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), spam));

        h.Tap(F6);
        Thread.Sleep(300);
        h.Tap(F6);

        int sent = h.Sim.Count("mouse move 1,0");
        Assert.InRange(sent, 5, 400); // at most ~1 per ms; unthrottled this is 100,000+
    }

    [Fact]
    public void A_loop_stops_when_you_leave_its_app()
    {
        var clicker = Macro("Clicker", "F6", ExecutionMode.Toggle, "destiny2.exe", Type("c"), Wait(10));
        using var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, clicker));

        h.InApp("destiny2");
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("char c") >= 2));
        h.Engine.WatchdogTick();
        Assert.True(clicker.IsRunning); // still in the game: keeps going

        h.InApp("explorer"); // alt-tab to the desktop
        h.Engine.WatchdogTick();

        Assert.False(clicker.IsRunning);
        Thread.Sleep(60);
        int after = h.Sim.Count("char c");
        Thread.Sleep(120);
        Assert.Equal(after, h.Sim.Count("char c"));
    }

    [Fact]
    public void A_held_button_is_released_when_you_leave_the_app()
    {
        var hold = Macro("Hold click", "F6", ExecutionMode.Toggle, "destiny2.exe", MouseDown());
        using var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, hold));

        h.InApp("destiny2");
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("mouse down Left") == 1));

        h.InMacroMaster();
        h.Engine.WatchdogTick();

        Assert.Equal(1, h.Sim.Count("mouse up Left"));
    }

    [Fact]
    public void A_long_run_once_macro_is_cut_short_when_you_leave_its_app()
    {
        var slow = Macro("Slow", "l", ExecutionMode.Once, "destiny2.exe", Type("a"), Wait(5000), Type("b"));
        using var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, slow));

        h.InApp("destiny2");
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char a") == 1));

        h.InApp("chrome");
        h.Engine.WatchdogTick();

        Thread.Sleep(150);
        Assert.Equal(0, h.Sim.Count("char b"));
    }

    [Fact]
    public void A_global_loop_keeps_running_in_every_app()
    {
        var clicker = Macro("Clicker", "F6", ExecutionMode.Toggle, "*", Type("c"), Wait(10));
        using var h = new EngineHarness(Profile("Everywhere", new[] { "*" }, clicker));

        h.Tap(F6);
        h.InApp("chrome");
        h.Engine.WatchdogTick();
        h.InMacroMaster();
        h.Engine.WatchdogTick();

        Assert.True(clicker.IsRunning);
    }

    [Fact]
    public void While_held_macros_run_until_the_key_is_released()
    {
        var held = Macro("Held", "F6", ExecutionMode.HoldWhileDown, actions: new[] { Type("h"), Wait(10) });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), held));

        h.Press(F6);
        Assert.True(WaitFor(() => h.Sim.Count("char h") >= 3));
        h.Release(F6);

        Assert.True(WaitFor(() => !held.IsRunning));
    }
}

public class AdminWindowTests
{
    [Fact]
    public void An_administrator_app_in_front_is_reported_once()
    {
        using var h = new EngineHarness(Profile("Everywhere", new[] { "*" }, Macro("M", "l", actions: Type("x"))));

        h.InApp("chrome");
        h.Engine.WatchdogTick();
        Assert.Null(h.Engine.HotkeysBlockedBy);

        h.InApp("somegame", admin: true);
        h.Engine.WatchdogTick();
        h.Engine.WatchdogTick();

        Assert.Equal("somegame", h.Engine.HotkeysBlockedBy);
        Assert.Single(h.Hud, s => s.StartsWith("Hotkeys can't work in somegame"));

        // Stays visible after switching back to MacroMaster, where the user can act on it
        h.InMacroMaster();
        h.Engine.WatchdogTick();
        Assert.Equal("somegame", h.Engine.HotkeysBlockedBy);
    }

    [Fact]
    public void No_popup_when_no_hotkey_was_meant_for_that_app()
    {
        using var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, Macro("M", "l", actions: Type("x"))));

        h.InApp("taskmgr", admin: true);
        h.Engine.WatchdogTick();

        Assert.Equal("taskmgr", h.Engine.HotkeysBlockedBy);
        Assert.Empty(h.Hud);
    }

    [Fact]
    public void Windows_reports_this_test_process_as_not_elevated_or_elevated_consistently()
    {
        // The real Win32 check, against a process whose answer we know from .NET itself
        ProcessWatcher.TestForeground = null;
        Assert.Equal(Environment.IsPrivilegedProcess, ProcessWatcher.IsProcessElevated((uint)Environment.ProcessId));
    }
}
