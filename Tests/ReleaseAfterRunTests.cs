using MacroMaster.Core.Models;
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

public class ReleaseAfterRunTests
{
    private static MacroAction KeyDown(string key) => new() { Type = ActionType.KeyDown, Key = key };
    private static MacroAction KeyUp(string key) => new() { Type = ActionType.KeyUp, Key = key };
    private static MacroAction RightDown() => new() { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Right };

    [Fact]
    public void A_once_macro_that_only_presses_down_lets_go_when_it_ends()
    {
        // The user's "Walk": Key down S, Mouse down Right, set to Once. Both stayed held for good.
        var walk = Macro("Walk", "l", ExecutionMode.Once, actions: new[] { KeyDown("S"), RightDown() });
        using var h = new EngineHarness(Profile("Everywhere", new[] { "*" }, walk));

        h.Tap(L);

        Assert.True(WaitFor(() => h.Sim.Count("key up S") == 1 && h.Sim.Count("mouse up Right") == 1));
        Assert.Equal(1, h.Sim.Count("key down S"));
        Assert.Equal(1, h.Sim.Count("mouse down Right"));
    }

    [Fact]
    public void Keys_the_macro_already_released_are_not_released_twice()
    {
        var jump = Macro("Sprint jump", "l", ExecutionMode.Once,
            actions: new[] { KeyDown("LShift"), KeyDown("Space"), KeyUp("space"), RightDown() });
        using var h = new EngineHarness(Profile("Everywhere", new[] { "*" }, jump));

        h.Tap(L);

        Assert.True(WaitFor(() => h.Sim.Count("key up Shift") == 1 && h.Sim.Count("mouse up Right") == 1)); // "LShift" is sent as Shift
        Thread.Sleep(50);
        Assert.Equal(1, h.Sim.Count("key up Space")); // only the macro's own "let go of Space"
        Assert.Equal(1, h.Sim.Count("key up Shift"));
    }

    [Fact]
    public void A_number_of_times_macro_lets_go_once_at_the_end()
    {
        var hold = Macro("Hold W", "l", ExecutionMode.RepeatCount, actions: new[] { KeyDown("W") });
        hold.RepeatCount = 3;
        using var h = new EngineHarness(Profile("Everywhere", new[] { "*" }, hold));

        h.Tap(L);

        Assert.True(WaitFor(() => h.Sim.Count("key up W") == 1));
        Assert.Equal(3, h.Sim.Count("key down W"));
    }
}
