using System.Windows;
using System.Windows.Input;
using MacroMaster.Core.Models;

namespace MacroMaster.UI.Windows;

public partial class NewProfileDialog : Window
{
    public MacroProfile? CreatedProfile { get; private set; }

    public NewProfileDialog()
    {
        InitializeComponent();
        TxtProfileName.Focus();
        TxtProfileName.SelectAll();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CreateBtn_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtProfileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "New Profile";
        }

        string proc = TxtProcessFilter.Text.Trim();
        if (string.IsNullOrWhiteSpace(proc))
        {
            proc = "*";
        }

        CreatedProfile = new MacroProfile
        {
            Name = name,
            IconKey = "gamepad-2",
            IsActive = true,
            TargetProcesses = new List<string> { proc }
        };

        DialogResult = true;
        Close();
    }
}
