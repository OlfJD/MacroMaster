using System.IO;
using MacroMaster.Core.Models;
using MacroMaster.Core.Parsers;
using Xunit;

namespace MacroMaster.Tests;

public class SettingsLoadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mm-settings-").FullName;
    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_readable_file_counts_as_the_users_settings()
    {
        MacroJsonSerializer.SaveSettings(new AppSettings { StartWithWindows = true }, File_);
        var s = MacroJsonSerializer.LoadSettings(File_, out bool fromFile);
        Assert.True(fromFile);
        Assert.True(s.StartWithWindows);
    }

    [Fact]
    public void A_broken_file_falls_back_to_the_last_good_copy()
    {
        MacroJsonSerializer.SaveSettings(new AppSettings { StartWithWindows = true }, File_);
        File.Copy(File_, File_ + ".bak", overwrite: true);
        File.WriteAllText(File_, "{ \"StartWithWin"); // cut off mid-write

        var s = MacroJsonSerializer.LoadSettings(File_, out bool fromFile, out bool fromBackup);
        Assert.True(fromFile);
        Assert.True(fromBackup); // restores the user's settings, but the start entry is left alone
        Assert.True(s.StartWithWindows);
    }

    [Fact]
    public void The_backup_is_one_save_behind_so_it_is_marked_as_the_backup()
    {
        // Switching "Start with Windows" on: settings.json says on, the .bak still says off
        MacroJsonSerializer.SaveSettings(new AppSettings { StartWithWindows = false }, File_);
        MacroJsonSerializer.SaveSettings(new AppSettings { StartWithWindows = true }, File_);
        Assert.False(MacroJsonSerializer.LoadSettings(File_ + ".bak").StartWithWindows);

        var current = MacroJsonSerializer.LoadSettings(File_, out _, out bool currentFromBackup);
        Assert.True(current.StartWithWindows);
        Assert.False(currentFromBackup);

        File.WriteAllText(File_, "{ broken");
        MacroJsonSerializer.LoadSettings(File_, out bool fromFile, out bool fromBackup);
        Assert.True(fromFile);
        Assert.True(fromBackup); // MainWindow must not sync "off" from it
    }

    [Fact]
    public void Without_any_readable_copy_the_defaults_are_marked_as_defaults()
    {
        // The defaults have "Start with Windows" off: MacroMaster must not act on them as if the user chose that
        File.WriteAllText(File_, "not json");
        var s = MacroJsonSerializer.LoadSettings(File_, out bool fromFile);
        Assert.False(fromFile);
        Assert.False(s.StartWithWindows);

        MacroJsonSerializer.LoadSettings(Path.Combine(_dir, "missing.json"), out bool missingFromFile);
        Assert.False(missingFromFile);
    }
}
