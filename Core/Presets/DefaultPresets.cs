using System.Collections.ObjectModel;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Presets;

public static class DefaultPresets
{
    public static List<MacroProfile> CreateDefaultProfiles()
    {
        var profiles = new List<MacroProfile>();

        // Profile 1: Destiny 2 Gaming Suite
        var d2Profile = new MacroProfile
        {
            Name = "Destiny 2 Skate & Tech",
            IconKey = "gamepad-2",
            IsActive = true,
            TargetProcesses = new List<string> { "destiny2.exe", "destiny2" }
        };

        // Wellskate
        var wellskate = new MacroDefinition
        {
            Name = "Wellskate (Heavy Weapon Skate)",
            Description = "Right click heavy cancel into jump and Well of Radiance / Shatterdive super momentum glide",
            Category = "Destiny 2",
            RequiresArming = true,
            ArmingKey = "PgUp",
            ArmingGroupName = "Destiny Skate Modes",
            ArmingDisplayName = "Wellskate (PgUp Mode)",
            IsCurrentlyArmed = true,
            ProcessFilter = "destiny2.exe",
            Mode = ExecutionMode.Once
        };
        wellskate.Trigger.PrimaryKey = "l";
        wellskate.Trigger.ChordKeys = new List<string> { "ä", "ö" };
        wellskate.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Right },
            new() { Type = ActionType.Delay, DelayMs = 40 },
            new() { Type = ActionType.MouseUp, MouseButton = MouseButtonType.Right },
            new() { Type = ActionType.Delay, DelayMs = 50 },
            new() { Type = ActionType.KeyDown, Key = "j" },
            new() { Type = ActionType.Delay, DelayMs = 15 },
            new() { Type = ActionType.KeyDown, Key = "f" },
            new() { Type = ActionType.Delay, DelayMs = 40 },
            new() { Type = ActionType.KeyUp, Key = "f" },
            new() { Type = ActionType.Delay, DelayMs = 15 },
            new() { Type = ActionType.KeyUp, Key = "j" }
        };
        d2Profile.Macros.Add(wellskate);

        // Groundskate
        var groundskate = new MacroDefinition
        {
            Name = "Groundskate (Flat Ground Skate)",
            Description = "Ground launch skate sequence: Jump, Light Attack, Jump, Super cancel",
            Category = "Destiny 2",
            RequiresArming = true,
            ArmingKey = "PgDn",
            ArmingGroupName = "Destiny Skate Modes",
            ArmingDisplayName = "Groundskate (PgDn Mode)",
            IsCurrentlyArmed = false,
            ProcessFilter = "destiny2.exe",
            Mode = ExecutionMode.Once
        };
        groundskate.Trigger.PrimaryKey = "l";
        groundskate.Trigger.ChordKeys = new List<string> { "ä", "ö" };
        groundskate.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.KeyTap, Key = "j", DelayMs = 25 },
            new() { Type = ActionType.Delay, DelayMs = 50 },
            new() { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left },
            new() { Type = ActionType.Delay, DelayMs = 50 },
            new() { Type = ActionType.KeyTap, Key = "j", DelayMs = 25 },
            new() { Type = ActionType.Delay, DelayMs = 1 },
            new() { Type = ActionType.KeyTap, Key = "f", DelayMs = 25 }
        };
        d2Profile.Macros.Add(groundskate);

        // Rocket Recoil Pull-Down
        var rocketRecoil = new MacroDefinition
        {
            Name = "Rocket Recoil Pull-Down",
            Description = "Fires weapon, triggers ability, and applies hardware-level 26px Y-axis recoil pull",
            Category = "Destiny 2",
            RequiresArming = false,
            ProcessFilter = "destiny2.exe",
            Mode = ExecutionMode.Once
        };
        rocketRecoil.Trigger.PrimaryKey = "l";
        rocketRecoil.Trigger.ChordKeys = new List<string> { "ä", "ö" };
        rocketRecoil.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Left },
            new() { Type = ActionType.Delay, DelayMs = 30 },
            new() { Type = ActionType.MouseUp, MouseButton = MouseButtonType.Left },
            new() { Type = ActionType.Delay, DelayMs = 30 },
            new() { Type = ActionType.KeyDown, Key = "q" },
            new() { Type = ActionType.Delay, DelayMs = 30 },
            new() { Type = ActionType.KeyUp, Key = "q" },
            new() { Type = ActionType.Delay, DelayMs = 50 },
            new() { Type = ActionType.MouseMoveRelative, MoveX = 0, MoveY = 26 }
        };
        d2Profile.Macros.Add(rocketRecoil);

        // V & Escape Menu Fast Action
        var vEscape = new MacroDefinition
        {
            Name = "Fast V + Escape",
            Description = "Instant V keypress followed by Escape after 50ms",
            Category = "Destiny 2",
            RequiresArming = false,
            ProcessFilter = "destiny2.exe",
            Mode = ExecutionMode.Once
        };
        vEscape.Trigger.PrimaryKey = "End";
        vEscape.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.KeyTap, Key = "v" },
            new() { Type = ActionType.Delay, DelayMs = 50 },
            new() { Type = ActionType.KeyTap, Key = "Esc" }
        };
        d2Profile.Macros.Add(vEscape);

        profiles.Add(d2Profile);

        // Profile 2: Utilities & Hotkeys
        var generalProfile = new MacroProfile
        {
            Name = "Utilities & Hotkeys",
            IconKey = "zap",
            IsActive = false,
            TargetProcesses = new List<string> { "*" }
        };

        // Left Click Hold Toggle
        var leftClickHold = new MacroDefinition
        {
            Name = "Toggle Left Click Hold",
            Description = "Press hotkey to hold Left Click Down; press again to release",
            Category = "Utility",
            RequiresArming = false,
            ProcessFilter = "*",
            Mode = ExecutionMode.Toggle
        };
        leftClickHold.Trigger.PrimaryKey = "PgDn";
        leftClickHold.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Left }
        };
        generalProfile.Macros.Add(leftClickHold);

        // Auto Clicker 15 CPS
        var autoClicker = new MacroDefinition
        {
            Name = "Auto Clicker (15 CPS)",
            Description = "Press hotkey to start fast Left Click spamming; press again to stop",
            Category = "Utility",
            RequiresArming = false,
            ProcessFilter = "*",
            Mode = ExecutionMode.Toggle,
            LoopDelayMs = 66
        };
        autoClicker.Trigger.PrimaryKey = "F6";
        autoClicker.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left },
            new() { Type = ActionType.Delay, DelayMs = 66 }
        };
        generalProfile.Macros.Add(autoClicker);

        // Media Play/Pause
        var mediaPause = new MacroDefinition
        {
            Name = "Media Play / Pause",
            Description = "Global media play and pause controller",
            Category = "Media",
            RequiresArming = false,
            ProcessFilter = "*",
            Mode = ExecutionMode.Once
        };
        mediaPause.Trigger.PrimaryKey = "PgUp";
        mediaPause.Actions = new ObservableCollection<MacroAction>
        {
            new() { Type = ActionType.Media, MediaCommand = "PlayPause" }
        };
        generalProfile.Macros.Add(mediaPause);

        profiles.Add(generalProfile);

        return profiles;
    }
}
