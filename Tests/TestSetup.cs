using System.Runtime.CompilerServices;

namespace MacroMaster.Tests;

internal static class TestSetup
{
    /// <summary>Before any test runs: tests never write log files into the real data folder.</summary>
    [ModuleInitializer]
    internal static void NoLogFiles()
    {
        Core.AppLog.LogPath = null;
        Core.Native.AutoStartManager.LogPath = null;
    }
}
