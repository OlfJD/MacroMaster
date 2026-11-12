using System.Xml.Linq;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using MacroMaster.UI;
using Xunit;
using static MacroMaster.Tests.EngineHarness;

namespace MacroMaster.Tests;

public class IndicatorForLoopsTests
{
    private static EngineHarness Clicker(bool showOnIndicator, out MacroDefinition macro)
    {
        macro = Macro("Fish farm", "l", ExecutionMode.Toggle, actions: new[] { MouseDown() });
        macro.ShowOnIndicator = showOnIndicator;
        return new EngineHarness(Profile("Everywhere", new[] { "*" }, macro));
    }

    [Fact]
    public void A_running_loop_is_named_while_it_runs()
    {
        using var h = Clicker(showOnIndicator: true, out var macro);
        h.Engine.WatchdogTick();
        Assert.Null(h.Engine.ModeIndicatorText);

        h.Tap(L); // start
        Assert.True(WaitFor(() => macro.IsRunning));
        Assert.Equal("Fish farm running", h.Engine.ModeIndicatorText);

        h.Tap(L); // stop
        Assert.True(WaitFor(() => h.Engine.ModeIndicatorText == null));
    }

    [Fact]
    public void A_loop_with_the_option_off_is_not_shown()
    {
        using var h = Clicker(showOnIndicator: false, out var macro);
        h.Tap(L);
        Assert.True(WaitFor(() => macro.IsRunning));
        h.Engine.WatchdogTick();
        Assert.Null(h.Engine.ModeIndicatorText);
        h.Tap(L);
    }

    [Fact]
    public void A_mode_with_the_option_off_is_not_shown()
    {
        var well = Mode("Wellskate", "l", "PgUp", active: true, marker: "W");
        well.ShowOnIndicator = false;
        using var h = new EngineHarness(Profile("Destiny", new[] { "destiny2.exe" }, well));
        h.InApp("destiny2");
        h.Engine.WatchdogTick();
        Assert.Null(h.Engine.ModeIndicatorText);
    }
}

public class UpdaterTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1-beta", "2.0.1")]
    [InlineData("v10.4.0+build5", "10.4.0")]
    public void Release_tags_are_read_as_versions(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), AppUpdater.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    public void Odd_tags_are_ignored(string tag) => Assert.Null(AppUpdater.ParseVersion(tag));

    [Fact]
    public void The_app_has_a_version_to_compare_with()
        => Assert.True(AppUpdater.CurrentVersion > new Version(1, 0, 0)); // the first GitHub release was v1.0.0
}

public class AutostartTaskTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void The_task_runs_elevated_at_sign_in_and_is_never_stopped()
    {
        var xml = XDocument.Parse(AutoStartManager.BuildTaskXml(@"C:\Tools\Mäcro & Co\MacroMaster.exe", @"PC\Levi", elevated: true));

        Assert.Equal("HighestAvailable", xml.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal(@"PC\Levi", xml.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
        Assert.Equal("PT0S", xml.Descendants(Ns + "ExecutionTimeLimit").Single().Value);            // no 3-day limit
        Assert.Equal("false", xml.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);  // laptops too
        Assert.Equal("4", xml.Descendants(Ns + "Priority").Single().Value);                         // normal priority
        Assert.Equal("\"C:\\Tools\\Mäcro & Co\\MacroMaster.exe\"", xml.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("--autostart", xml.Descendants(Ns + "Arguments").Single().Value);
    }
}
