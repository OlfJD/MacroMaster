using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MacroMaster.Core.Engine;
using MacroMaster.Core.Models;

namespace MacroMaster.UI.Windows;

public partial class ActiveScriptsDialog : Window
{
    private readonly MacroEngine _engine;
    public ObservableCollection<MacroDefinition> ActiveMacros { get; private set; } = new();

    public ActiveScriptsDialog(MacroEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        LoadActiveScripts();
    }

    private void LoadActiveScripts()
    {
        ActiveMacros.Clear();

        if (_engine.ActiveProfile != null)
        {
            foreach (var m in _engine.ActiveProfile.Macros.Where(m => m.IsEnabled))
            {
                ActiveMacros.Add(m);
            }
        }

        ActiveScriptsListBox.ItemsSource = ActiveMacros;
        TxtSummary.Text = $"{ActiveMacros.Count} Active Script{(ActiveMacros.Count == 1 ? "" : "s")} Armed in Profile: {_engine.ActiveProfile?.Name ?? "None"}";
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
        DialogResult = true;
        Close();
    }
}
