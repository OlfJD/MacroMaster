using MacroMaster.Core.Engine;
using MacroMaster.Core.Models;
using MacroMaster.Core.Parsers; // CloneMacro
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

/// <summary>
/// "The macro won't stop": a running macro must stop when it is switched off, deleted or edited,
/// and stopping it must never leave a key or button held down.
/// </summary>
public class StopTests
{
    private static MacroAction KeyDown(string key) => new() { Type = ActionType.KeyDown, Key = key };
    private static MacroAction KeyUp(string key) => new() { Type = ActionType.KeyUp, Key = key };

    private static MacroDefinition Clicker() => Macro("Clicker", "F6", ExecutionMode.Toggle, actions: new[] { Type("c"), Wait(10) });

    /// <summary>The number of "c" typed stays the same from now on (the loop really ended).</summary>
    private static void AssertNoMoreOutput(EngineHarness h, string what = "char c")
    {
        Thread.Sleep(40); // a step that was already under way may still finish
        int after = h.Sim.Count(what);
        Thread.Sleep(150);
        Assert.Equal(after, h.Sim.Count(what));
    }

    [Fact]
    public void Switching_a_running_loop_off_stops_it_and_lets_go_of_the_button()
    {
        var hold = Macro("Hold click", "F6", ExecutionMode.Toggle, actions: MouseDown());
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), hold));
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("mouse down Left") == 1));

        hold.IsEnabled = false;                    // its switch in the list â€¦
        Assert.True(h.Engine.StopMacro(hold));     // â€¦ which tells the engine

        Assert.False(hold.IsRunning);
        Assert.True(WaitFor(() => h.Sim.Count("mouse up Left") == 1));
        Assert.Contains("Hold click | Stopped", h.Hud);
    }

    [Fact]
    public void A_loop_that_was_switched_off_is_stopped_by_the_watchdog_too()
    {
        // Before: its hotkey was ignored once it was off, so nothing but F12 could stop it
        var clicker = Clicker();
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), clicker));
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("char c") >= 2));

        clicker.IsEnabled = false;
        h.Engine.WatchdogTick();

        Assert.False(clicker.IsRunning);
        AssertNoMoreOutput(h);
    }

    [Fact]
    public void A_deleted_loop_does_not_carry_on_in_the_background()
    {
        var clicker = Clicker();
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), clicker));
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("char c") >= 2));

        ProfileEdits.RemoveMacro(h.Engine.Profiles[0], clicker);
        h.Engine.WatchdogTick();

        Assert.False(clicker.IsRunning);
        AssertNoMoreOutput(h);
    }

    [Fact]
    public void A_long_run_of_a_deleted_profile_is_cut_short()
    {
        var slow = Macro("Slow", "l", ExecutionMode.RepeatCount, actions: new[] { Type("a"), Wait(20) });
        slow.RepeatCount = 1000;
        var profile = Profile("P", Array.Empty<string>(), slow);
        using var h = new EngineHarness(profile, Profile("Other", Array.Empty<string>()));
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char a") >= 2));

        ProfileEdits.RemoveProfile(h.Engine.Profiles, h.Engine.Profiles[0]);
        h.Engine.WatchdogTick();

        AssertNoMoreOutput(h, "char a");
    }

    [Fact]
    public void Saving_an_edit_of_a_running_loop_stops_the_old_copy()
    {
        var clicker = Clicker();
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), clicker));
        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("char c") >= 2));

        // The editor saves a copy with the same id in its place; here it now runs once and types "e".
        // The old loop used to keep going, and as a "once" macro the hotkey couldn't stop it any more.
        var edited = MacroJsonSerializer.CloneMacro(clicker);
        edited.Id = clicker.Id;
        edited.Mode = ExecutionMode.Once;
        edited.Actions = new() { Type("e") };
        h.Engine.Profiles[0].Macros[0] = edited;
        h.Engine.WatchdogTick();

        Assert.False(clicker.IsRunning);
        AssertNoMoreOutput(h);

        h.Tap(F6);
        Assert.True(WaitFor(() => h.Sim.Count("char e") == 1));
    }

    [Fact]
    public void Stop_macro_also_ends_a_once_run_that_is_still_playing()
    {
        var slow = Macro("Slow", "l", actions: new[] { Type("a"), Wait(5000), Type("b") });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), slow));
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char a") == 1));

        Assert.True(h.Engine.StopMacro(slow));

        Thread.Sleep(150);
        Assert.Equal(0, h.Sim.Count("char b"));
        Assert.False(h.Engine.StopMacro(slow)); // nothing left to stop
    }

    [Fact]
    public void Stopping_a_loop_in_the_middle_of_a_key_press_never_leaves_the_key_held()
    {
        // The stop arrives (on the hook thread) exactly while the loop is pressing W down: the old
        // code released everything first and pressed W afterwards, so W stayed held for good.
        var walk = Macro("Walk", "F6", ExecutionMode.Toggle, actions: new[] { KeyDown("W"), Wait(30), KeyUp("W") });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), walk));
        int fired = 0;
        h.Sim.BeforeSend = what =>
        {
            if (what != "key down W" || Interlocked.Exchange(ref fired, 1) == 1) return;
            var stopper = new Thread(() => h.Tap(F6));
            stopper.Start();
            stopper.Join();
        };

        h.Tap(F6);

        Assert.True(WaitFor(() => fired == 1 && !walk.IsRunning));
        Assert.True(WaitFor(() => h.Sim.Sent.LastOrDefault(s => s.EndsWith(" W")) == "key up W"),
                    "W is still held: " + string.Join(", ", h.Sim.Sent));
    }

    [Fact]
    public void A_cancelled_once_run_does_not_send_key_ups_for_keys_it_never_pressed()
    {
        // Leaving the app during the wait: S was never pressed, so no "S up" may reach the game
        // (the player may be holding S themselves)
        var walk = Macro("Later", "l", ExecutionMode.Once, "destiny2.exe", Wait(5000), KeyDown("S"));
        using var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, walk));
        h.InApp("destiny2");
        h.Tap(L);
        Thread.Sleep(50);

        h.InApp("chrome");
        h.Engine.WatchdogTick();

        Thread.Sleep(100);
        Assert.Equal(0, h.Sim.Count("key up S"));
        Assert.Equal(0, h.Sim.Count("key down S"));
    }
}
