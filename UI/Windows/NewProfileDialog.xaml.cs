using System.Windows;
using MacroMaster.Core.Models;

namespace MacroMaster.UI.Windows;

/// <summary>Creates a new profile, or renames an existing one when constructed with it.</summary>
public partial class NewProfileDialog : Window
{
    private readonly MacroProfile? _existing;

    public MacroProfile? CreatedProfile { get; private set; }

    public NewProfileDialog(MacroProfile? existing = null)
    {
        InitializeComponent();
        WindowHelper.Attach(this);
        _existing = existing;

        if (existing != null)
        {
            TxtHeader.Text = "Edit profile";
            BtnCreate.Content = "Save";
            TxtProfileName.Text = existing.Name;
            var proc = existing.TargetProcesses.FirstOrDefault();
            TxtProcessFilter.Text = proc == "*" ? string.Empty : proc ?? string.Empty;
        }

        Loaded += (_, _) =>
        {
            TxtProfileName.Focus();
            TxtProfileName.SelectAll();
        };
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CreateBtn_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtProfileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "New profile";

        string proc = TxtProcessFilter.Text.Trim();
        if (string.IsNullOrWhiteSpace(proc)) proc = "*";

        if (_existing != null)
        {
            _existing.Name = name;
            _existing.TargetProcesses = new List<string> { proc };
            CreatedProfile = _existing;
        }
        else
        {
            CreatedProfile = new MacroProfile
            {
                Name = name,
                IconKey = "gamepad-2",
                IsActive = true,
                TargetProcesses = new List<string> { proc }
            };
        }

        DialogResult = true;
    }
}
