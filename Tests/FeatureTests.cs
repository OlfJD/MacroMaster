using System.Collections.ObjectModel;
using System.IO;
using MacroMaster.Core.Engine;
using MacroMaster.Core.Models;
using MacroMaster.Core.Parsers;
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

public class StepTypeTests
{
    private static MacroAction RepeatStart(int times) => new() { Type = ActionType.RepeatStart, RepeatCount = times };
    private static MacroAction RepeatEnd() => new() { Type = ActionType.RepeatEnd };
    private static MacroAction Pixel(int x, int y, string color, int giveUpMs) =>
        new() { Type = ActionType.WaitForPixel, MoveX = x, MoveY = y, TextContent = color, DelayMs = giveUpMs };

    /// <summary>Runs one macro on L and returns the typed characters once "!" (the last step) shows up.</summary>
    private static string Run(params MacroAction[] steps)
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), Macro("M", "l", actions: steps.Append(Type("!")).ToArray())));
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char !") == 1, 5000), "the macro never reached its last step");
        return string.Concat(h.Sim.Sent.Where(s => s.StartsWith("char ")).Select(s => s[5..]));
    }

    [Fact]
    public void A_repeat_block_runs_its_steps_that_many_times()
        => Assert.Equal("abbbc!", Run(Type("a"), RepeatStart(3), Type("b"), RepeatEnd(), Type("c")));

    [Fact]
    public void Repeat_blocks_can_be_nested()
        => Assert.Equal("xyyxyy!", Run(RepeatStart(2), Type("x"), RepeatStart(2), Type("y"), RepeatEnd(), RepeatEnd()));

    [Fact]
    public void A_repeat_count_below_one_still_runs_once()
        => Assert.Equal("b!", Run(RepeatStart(0), Type("b"), RepeatEnd()));

    [Fact]
    public void Stray_repeat_rows_do_no_harm()
    {
        Assert.Equal("ab!", Run(Type("a"), RepeatEnd(), Type("b")));       // end without a start
        Assert.Equal("ab!", Run(RepeatStart(5), Type("a"), Type("b")));    // start without an end
    }

    [Fact]
    public void Move_to_puts_the_pointer_on_the_exact_spot()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(),
            Macro("M", "l", actions: new MacroAction { Type = ActionType.MouseMoveAbsolute, MoveX = 640, MoveY = 360 })));

        h.Tap(L);

        Assert.True(WaitFor(() => h.Sim.Count("mouse to 640,360") == 1));
    }

    [Fact]
    public void Wait_for_pixel_continues_as_soon_as_the_colour_shows_up()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(),
            Macro("M", "l", actions: new[] { Type("a"), Pixel(100, 200, "#FFCC00", 5000), Type("b") })));
        h.Tap(L);

        Assert.True(WaitFor(() => h.Sim.Count("char a") == 1));
        Thread.Sleep(120);
        Assert.Equal(0, h.Sim.Count("char b")); // still black there: waiting

        // A close match (games dither colours) at exactly that pixel
        h.Sim.Screen = (x, y) => x == 100 && y == 200 ? 0xFAC805 : 0x000000;
        Assert.True(WaitFor(() => h.Sim.Count("char b") == 1));
    }

    [Fact]
    public void Wait_for_pixel_gives_up_and_skips_the_rest()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(),
            Macro("M", "l", actions: new[] { Type("a"), Pixel(1, 1, "#FF0000", 150), Type("b") })));
        h.Sim.Screen = (_, _) => 0x00FF00; // never red

        h.Tap(L);
        Thread.Sleep(500);

        Assert.Equal(1, h.Sim.Count("char a"));
        Assert.Equal(0, h.Sim.Count("char b"));

        // ...and the macro is free to run again afterwards
        h.Sim.Screen = (_, _) => 0xFF0000;
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char b") == 1));
    }

    [Fact]
    public void Wait_for_pixel_is_cut_short_by_the_engine_switch()
    {
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(),
            Macro("M", "l", actions: new[] { Pixel(1, 1, "#FF0000", 0), Type("b") }))); // 0 = wait forever
        h.Tap(L);
        Thread.Sleep(80);

        h.Tap(F12);
        h.Sim.Screen = (_, _) => 0xFF0000;
        Thread.Sleep(150);

        Assert.Equal(0, h.Sim.Count("char b"));
    }

    [Theory]
    [InlineData("#FFCC00", true)]
    [InlineData("ffcc00", true)]
    [InlineData("", false)]
    [InlineData("#FFF", false)]
    [InlineData("yellow", false)]
    public void Colours_are_recognised(string text, bool valid)
        => Assert.Equal(valid, MacroAction.TryParseColor(text, out _, out _, out _));
}

public class ModeIndicatorTests
{
    private static EngineHarness Destiny(out List<string?> shown)
    {
        var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" },
            Mode("Wellskate", "l", "PgUp", active: true, marker: "W"),
            Mode("Groundskate", "l", "PgDn", active: false, marker: "G")));
        var list = new List<string?>();
        h.Engine.OnModeIndicatorChanged += list.Add;
        shown = list;
        return h;
    }

    [Fact]
    public void It_shows_the_active_mode_only_while_the_game_is_in_front()
    {
        using var h = Destiny(out var shown);

        h.Engine.WatchdogTick();               // desktop
        Assert.Null(h.Engine.ModeIndicatorText);

        h.InApp("destiny2");
        h.Engine.WatchdogTick();
        Assert.Equal("Wellskate", h.Engine.ModeIndicatorText);

        h.InApp("chrome");
        h.Engine.WatchdogTick();
        Assert.Null(h.Engine.ModeIndicatorText);

        Assert.Equal(new string?[] { "Wellskate", null }, shown); // told once per change, not every tick
    }

    [Fact]
    public void It_follows_the_switch_key_immediately()
    {
        using var h = Destiny(out var shown);
        h.InApp("destiny2");
        h.Engine.WatchdogTick();

        h.Tap(PgDn);

        Assert.Equal("Groundskate", h.Engine.ModeIndicatorText);
        Assert.Equal("Groundskate", shown[^1]);
    }

    [Fact]
    public void It_hides_while_the_engine_is_off_or_the_setting_is_off()
    {
        using var h = Destiny(out _);
        h.InApp("destiny2");
        h.Engine.WatchdogTick();

        h.Tap(F12);
        Assert.Null(h.Engine.ModeIndicatorText);
        h.Tap(F12);
        Assert.Equal("Wellskate", h.Engine.ModeIndicatorText);

        h.Settings.ShowModeIndicator = false;
        h.Engine.RefreshModeIndicator();
        Assert.Null(h.Engine.ModeIndicatorText);
    }

    [Fact]
    public void Nothing_is_shown_for_profiles_without_mode_groups()
    {
        using var h = new EngineHarness(Profile("Everywhere", new[] { "*" }, Macro("M", "l", actions: Type("x"))));
        h.Engine.WatchdogTick();
        Assert.Null(h.Engine.ModeIndicatorText);
    }
}

public class UndoAndReorderTests
{
    private static MacroProfile Sample(out MacroDefinition well, out MacroDefinition ground, out MacroDefinition a, out MacroDefinition b)
    {
        well = Mode("Wellskate", "l", "PgUp", active: true, marker: "W");
        ground = Mode("Groundskate", "l", "PgDn", active: false, marker: "G");
        a = Macro("A", "F6");
        b = Macro("B", "F7");
        return Profile("P", Array.Empty<string>(), well, ground, a, b);
    }

    private static string Names(MacroProfile p) => string.Join(",", p.Macros.Select(m => m.Name));

    [Fact]
    public void Undo_puts_a_deleted_macro_back_where_it_was()
    {
        var p = Sample(out _, out _, out var a, out _);

        Action undo = ProfileEdits.RemoveMacro(p, a);
        Assert.Equal("Wellskate,Groundskate,B", Names(p));

        undo();
        Assert.Equal("Wellskate,Groundskate,A,B", Names(p));

        undo(); // a second time must not add it twice
        Assert.Equal(4, p.Macros.Count);
    }

    [Fact]
    public void Deleting_the_active_mode_hands_over_to_the_next_one_and_undo_restores_it()
    {
        var p = Sample(out var well, out var ground, out _, out _);

        Action undo = ProfileEdits.RemoveMacro(p, well);
        Assert.True(ground.IsCurrentlyArmed); // the shared hotkey must not go dead

        undo();
        Assert.Equal("Wellskate,Groundskate,A,B", Names(p));
        Assert.True(well.IsCurrentlyArmed);
        Assert.False(ground.IsCurrentlyArmed);
    }

    [Fact]
    public void Undo_puts_a_deleted_profile_back_in_its_place()
    {
        var profiles = new ObservableCollection<MacroProfile> { Profile("One", Array.Empty<string>()), Sample(out _, out _, out _, out _), Profile("Three", Array.Empty<string>()) };
        var middle = profiles[1];

        Action undo = ProfileEdits.RemoveProfile(profiles, middle);
        Assert.Equal(new[] { "One", "Three" }, profiles.Select(x => x.Name));

        undo();
        Assert.Equal(new[] { "One", "P", "Three" }, profiles.Select(x => x.Name));
        Assert.Equal(4, middle.Macros.Count);
    }

    [Fact]
    public void Dragging_reorders_within_a_block_only()
    {
        var p = Sample(out var well, out var ground, out var a, out var b);

        Assert.True(ProfileEdits.MoveOnto(p.Macros, b, a));          // B dragged up onto A
        Assert.Equal("Wellskate,Groundskate,B,A", Names(p));

        Assert.True(ProfileEdits.MoveOnto(p.Macros, well, ground));  // inside the mode group
        Assert.Equal("Groundskate,Wellskate,B,A", Names(p));

        Assert.False(ProfileEdits.MoveOnto(p.Macros, a, well));      // not into another block
        Assert.False(ProfileEdits.MoveOnto(p.Macros, a, a));
        Assert.False(ProfileEdits.MoveOnto(p.Macros, a, null));
        Assert.Equal("Groundskate,Wellskate,B,A", Names(p));
    }
}

public class MacroFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-tests-" + Guid.NewGuid().ToString("N"));

    public MacroFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string name) => Path.Combine(_dir, name);

    [Fact]
    public void An_exported_macro_comes_back_complete_but_switched_off()
    {
        var original = Mode("Wellskate", "l", "PgUp", active: true, marker: "W");
        original.Description = "desc";
        original.Trigger.ChordKeys = new List<string> { "ä", "ö" };
        original.Trigger.RequireCtrl = true;
        original.Actions.Add(new MacroAction { Type = ActionType.RepeatStart, RepeatCount = 7 });
        original.Actions.Add(new MacroAction { Type = ActionType.WaitForPixel, MoveX = 10, MoveY = 20, TextContent = "#112233", DelayMs = 900 });
        original.Actions.Add(new MacroAction { Type = ActionType.RepeatEnd });

        MacroJsonSerializer.ExportMacros(new[] { original }, File("m.json"));
        Assert.Equal(MacroJsonSerializer.ImportKind.Macros, MacroJsonSerializer.DetectImportKind(File("m.json")));
        var back = Assert.Single(MacroJsonSerializer.ImportMacros(File("m.json")));

        Assert.Equal("Wellskate", back.Name);
        Assert.Equal("desc", back.Description);
        Assert.Equal("l", back.Trigger.PrimaryKey);
        Assert.Equal(new[] { "ä", "ö" }, back.Trigger.ChordKeys);
        Assert.True(back.Trigger.RequireCtrl);
        Assert.Equal("PgUp", back.ArmingKey);
        Assert.Equal(original.Actions.Select(a => a.DisplayText), back.Actions.Select(a => a.DisplayText));
        Assert.Equal(7, back.Actions[1].RepeatCount);

        Assert.NotEqual(original.Id, back.Id);   // importing twice can't collide
        Assert.False(back.IsEnabled);            // someone else's file doesn't go live by itself
        Assert.False(back.IsCurrentlyArmed);
    }

    [Fact]
    public void Profile_files_and_macro_files_are_told_apart()
    {
        MacroJsonSerializer.ExportProfiles(new[] { Profile("P", Array.Empty<string>(), Macro("A", "F6")) }, File("p.json"));
        System.IO.File.WriteAllText(File("other.json"), """{ "hello": 1 }""");
        System.IO.File.WriteAllText(File("broken.json"), "{ not json");

        Assert.Equal(MacroJsonSerializer.ImportKind.Profiles, MacroJsonSerializer.DetectImportKind(File("p.json")));
        Assert.Equal(MacroJsonSerializer.ImportKind.Unknown, MacroJsonSerializer.DetectImportKind(File("other.json")));
        Assert.Equal(MacroJsonSerializer.ImportKind.Unknown, MacroJsonSerializer.DetectImportKind(File("broken.json")));

        Assert.Throws<InvalidDataException>(() => MacroJsonSerializer.ImportMacros(File("p.json")));
        Assert.Throws<InvalidDataException>(() => MacroJsonSerializer.ImportMacros(File("other.json")));
        Assert.Single(MacroJsonSerializer.ImportProfiles(File("p.json")));
    }
}
