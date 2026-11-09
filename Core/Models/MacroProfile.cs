using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MacroMaster.Core.Models;

public class MacroProfile : INotifyPropertyChanged
{
    private string _name = "Default Profile";
    private bool _isOn = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string IconKey { get; set; } = "layers";

    /// <summary>The profile currently shown in the main window (exactly one).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this profile's hotkeys are live. Any number of profiles can be on at once; inside a
    /// profile only the macros whose own switch is on will run, so turning a profile off and on
    /// again brings back exactly the macros that were enabled before.
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set { _isOn = value; OnPropertyChanged(); }
    }

    /// <summary>Optional: only fire this profile's macros while one of these apps is in front ("*" = everywhere).</summary>
    public List<string> TargetProcesses { get; set; } = new();

    public ObservableCollection<MacroDefinition> Macros { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
