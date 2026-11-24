using System.Diagnostics;
using MacroMaster.Core.Native;
using Xunit;

namespace MacroMaster.Tests;

/// <summary>Recognising the app in front ("Only while this app is in front", profile apps).</summary>
public class ProcessWatcherTests
{
    [Fact]
    public void The_name_is_kept_only_for_the_same_window_and_process_and_for_a_second()
    {
        var cache = new ForegroundNameCache();
        long t0 = 1_000_000;
        cache.Store(new IntPtr(100), 7, "destiny2", t0);

        Assert.True(cache.TryGet(new IntPtr(100), 7, t0 + ForegroundNameCache.MaxAge / 2, out var name));
        Assert.Equal("destiny2", name);

        // Windows gave process id 7 to another program, or another window is in front: look it up again
        Assert.False(cache.TryGet(new IntPtr(200), 7, t0, out _));
        Assert.False(cache.TryGet(new IntPtr(100), 8, t0, out _));
        // Too old
        Assert.False(cache.TryGet(new IntPtr(100), 7, t0 + ForegroundNameCache.MaxAge, out _));
    }

    [Fact]
    public void A_process_name_comes_back_lower_case_without_exe()
    {
        using var me = Process.GetCurrentProcess();
        Assert.Equal(me.ProcessName.ToLowerInvariant(), ProcessWatcher.ProcessNameOf((uint)me.Id));
        Assert.Equal(string.Empty, ProcessWatcher.ProcessNameOf(0xFFFFFFF0)); // no such process
    }
}
