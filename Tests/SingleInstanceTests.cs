using System.IO;
using MacroMaster.Core.Native;
using Xunit;

namespace MacroMaster.Tests;

/// <summary>
/// One copy per data folder: a second copy on the same profiles overwrote the first one's saves
/// (macros made in its window were lost). The second launch hands over to the running copy instead.
/// </summary>
public class SingleInstanceTests
{
    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "mm-instance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_second_copy_on_the_same_data_folder_does_not_run()
    {
        string folder = NewFolder();
        using var first = SingleInstance.Claim(folder);
        using var second = SingleInstance.Claim(folder);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void A_copy_on_another_data_folder_runs_alongside()
    {
        // MACROMASTER_DATA_DIR copies (for trying things out) keep working next to the real one
        using var real = SingleInstance.Claim(NewFolder());
        using var trial = SingleInstance.Claim(NewFolder());

        Assert.True(real.IsFirst);
        Assert.True(trial.IsFirst);
    }

    [Fact]
    public void The_same_folder_spelled_differently_is_the_same_folder()
        => Assert.Equal(SingleInstance.NameFor(@"C:\Data\MacroMaster"), SingleInstance.NameFor(@"c:\data\macromaster\"));

    [Fact]
    public void A_second_launch_brings_the_running_copy_to_the_front()
    {
        string folder = NewFolder();
        using var running = SingleInstance.Claim(folder);
        using var shown = new ManualResetEventSlim();
        running.ListenForShowRequests(shown.Set);

        using var second = SingleInstance.Claim(folder);

        Assert.True(second.AskRunningCopyToShow());
        Assert.True(shown.Wait(2000));
        Assert.False(running.AskRunningCopyToShow()); // the running copy doesn't ask itself
    }

    [Fact]
    public void Once_the_running_copy_has_quit_the_next_launch_runs()
    {
        string folder = NewFolder();
        var running = SingleInstance.Claim(folder);
        running.ListenForShowRequests(() => { });
        running.Dispose();

        using var next = SingleInstance.Claim(folder);
        Assert.True(next.IsFirst);
    }
}
