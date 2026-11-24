using System.IO;
using MacroMaster.Core.Models;
using MacroMaster.Core.Parsers;
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

/// <summary>Saving when another program has the file open, and macros whose steps fail.</summary>
public class SaveAndErrorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mm-save-").FullName;
    private string ProfilesFile => Path.Combine(_dir, "profiles.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static List<MacroProfile> Profiles(string name) => new() { new MacroProfile { Name = name } };

    [Fact]
    public void A_save_waits_for_a_file_that_another_program_has_open_for_a_moment()
    {
        // A virus scanner / OneDrive reading the file used to make the save throw (a crash dialog)
        MacroJsonSerializer.SaveProfiles(Profiles("old"), ProfilesFile);
        var locked = new FileStream(ProfilesFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var unlock = Task.Delay(150).ContinueWith(_ => locked.Dispose());

        MacroJsonSerializer.SaveProfiles(Profiles("new"), ProfilesFile);

        unlock.Wait();
        Assert.Equal("new", MacroJsonSerializer.LoadProfiles(ProfilesFile).Profiles!.Single().Name);
    }

    [Fact]
    public void A_file_that_stays_locked_is_reported_to_the_caller_and_left_intact()
    {
        MacroJsonSerializer.SaveProfiles(Profiles("old"), ProfilesFile);
        using (new FileStream(ProfilesFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => MacroJsonSerializer.SaveProfiles(Profiles("new"), ProfilesFile));
        }
        Assert.Equal("old", MacroJsonSerializer.LoadProfiles(ProfilesFile).Profiles!.Single().Name);
    }

    [Fact]
    public void A_step_that_fails_ends_the_run_says_so_and_lets_go_of_what_it_held()
    {
        var m = Macro("Broken", "l", actions: new[]
        {
            new MacroAction { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Right },
            Type("x")
        });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), m));
        bool fail = true;
        h.Sim.BeforeSend = what => { if (fail && what.StartsWith("mouse down")) { fail = false; throw new InvalidOperationException("driver said no"); } };

        h.Tap(L);

        Assert.True(WaitFor(() => h.Hud.Contains("Broken | Stopped on an error (see errors.log)")));
        Assert.Equal(0, h.Sim.Count("char x"));

        // Not stuck as "still playing": the hotkey works again
        h.Tap(L);
        Assert.True(WaitFor(() => h.Sim.Count("char x") == 1));
        Assert.True(WaitFor(() => h.Sim.Count("mouse up Right") == 1)); // and lets go once it ends
    }

    [Fact]
    public void Odd_but_possible_step_values_do_not_end_the_run_with_an_error()
    {
        // A "give or take" as large as the field allows overflowed; a media step without a command
        // (null in a hand-edited or imported file) threw
        var m = Macro("Odd", "l", actions: new[]
        {
            new MacroAction { Type = ActionType.Media, MediaCommand = null! },
            new MacroAction { Type = ActionType.Delay, DelayMs = 1, RandomJitterMs = int.MaxValue },
            Type("x")
        });
        using var h = new EngineHarness(Profile("P", Array.Empty<string>(), m));

        h.Tap(L);
        Thread.Sleep(200);
        h.Tap(F12); // the wait may come out long: cut it short

        Assert.DoesNotContain(h.Hud, s => s.Contains("Stopped on an error"));
    }
}
