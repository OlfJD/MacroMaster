using System.Collections.ObjectModel;

namespace MacroMaster.Core.Models;

public class MacroProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Profile";
    public string IconKey { get; set; } = "layers";
    public bool IsActive { get; set; } = true;
    public List<string> TargetProcesses { get; set; } = new(); // Auto-switch to profile when active
    public ObservableCollection<MacroDefinition> Macros { get; set; } = new();
}
