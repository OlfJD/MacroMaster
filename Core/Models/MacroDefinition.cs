using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace MacroMaster.Core.Models;

public enum ExecutionMode
{
    Once,           // Execute action sequence once
    Toggle,         // Start looping/holding when triggered; stop when triggered again
    HoldWhileDown,  // Run while trigger key is held down
    RepeatCount     // Repeat sequence N times
}

public class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Macro";
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // Gaming, Destiny 2, Minecraft, Utility
    
    // Status
    public bool IsEnabled { get; set; } = true;
    
    // Trigger
    public MacroTrigger Trigger { get; set; } = new();
    
    // Execution Mode
    public ExecutionMode Mode { get; set; } = ExecutionMode.Once;
    public int RepeatCount { get; set; } = 1;
    public int LoopDelayMs { get; set; } = 0; // Delay between loops if looping
    
    // Mode Arming (For multi-mode setups like Wellskate / Groundskate)
    public bool RequiresArming { get; set; } = false;
    public string ArmingKey { get; set; } = string.Empty; // e.g. "PgUp" or "PgDn"
    public string ArmingGroupName { get; set; } = string.Empty; // e.g. "Destiny Skate Modes"
    public string ArmingDisplayName { get; set; } = string.Empty; // e.g. "Wellskate (PgUp Mode)"
    public bool IsCurrentlyArmed { get; set; } = false;
    
    // Game / Process Filtering
    public string ProcessFilter { get; set; } = "*"; // e.g. "destiny2.exe", "javaw.exe", "*" for global
    
    // Actions Sequence
    public ObservableCollection<MacroAction> Actions { get; set; } = new();

    // Source tracking
    public bool ImportedFromAhk { get; set; } = false;
    public string OriginalAhkScript { get; set; } = string.Empty;

    // Runtime tracking (not serialized)
    [JsonIgnore]
    public bool IsRunning { get; set; } = false;

    [JsonIgnore]
    public string StatusBadgeText
    {
        get
        {
            if (!IsEnabled) return "Disabled";
            if (IsRunning) return "Active";
            if (RequiresArming && IsCurrentlyArmed) return "Armed";
            return "Ready";
        }
    }
}
