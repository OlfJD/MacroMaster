using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MacroMaster.Core.Engine;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using MacroMaster.Core.Parsers;
using MacroMaster.Core.Presets;
using MacroMaster.UI.Windows;

namespace MacroMaster;

public partial class MainWindow : Window
{
    private readonly MacroEngine _engine = new();
    private HudOverlayWindow? _hudOverlay;
    private TrayManager? _trayManager;
    private readonly string _profilesFilePath;
    private readonly string _settingsFilePath;
    private bool _isInitialized = false;

    public MainWindow()
    {
        string appData = MacroJsonSerializer.GetAppDataDirectory();
        _profilesFilePath = Path.Combine(appData, "profiles.json");
        _settingsFilePath = Path.Combine(appData, "settings.json");

        InitializeComponent();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Load Profiles & Settings
        var settings = MacroJsonSerializer.LoadSettings(_settingsFilePath);
        var profiles = MacroJsonSerializer.LoadProfiles(_profilesFilePath);
        if (profiles == null || profiles.Count == 0)
        {
            profiles = DefaultPresets.CreateDefaultProfiles();
            MacroJsonSerializer.SaveProfiles(profiles, _profilesFilePath);
        }

        // 2. Initialize HUD Overlay Window
        _hudOverlay = new HudOverlayWindow();

        // 3. Initialize Macro Engine
        _engine.Initialize(settings, profiles);
        _engine.OnHudNotification += Engine_OnHudNotification;
        _engine.OnStateRefreshed += Engine_OnStateRefreshed;

        // 4. Initialize System Tray
        _trayManager = new TrayManager(_engine, ShowAndRestore);

        // 5. Setup UI Bindings
        _isInitialized = true;
        RefreshProfilesComboBox();
        RefreshMacrosList();
        SyncUiStates();
    }

    private void ShowAndRestore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Engine_OnHudNotification(string title, string subtitle, string iconKey, string accentColor)
    {
        _hudOverlay?.ShowNotification(title, subtitle, iconKey, accentColor, _engine.Settings.OverlayPosition, _engine.Settings.HudDurationMs);
    }

    private void Engine_OnStateRefreshed()
    {
        Dispatcher.Invoke(SyncUiStates);
    }

    private void SyncUiStates()
    {
        MasterEngineSwitchBox.IsChecked = _engine.Settings.MasterEngineEnabled;
        MasterStatusLabel.Text = _engine.Settings.MasterEngineEnabled ? "ACTIVE" : "PAUSED";
        MasterStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource(_engine.Settings.MasterEngineEnabled ? "NeonEmeraldGlowBrush" : "AlertRedBrush");
        UpdateActiveScriptsCount();
    }

    private bool _isUpdatingProfileCombo = false;

    private void RefreshProfilesComboBox()
    {
        _isUpdatingProfileCombo = true;

        var items = new List<ProfileComboItem>();
        ProfileComboItem? selectedItem = null;

        foreach (var profile in _engine.Profiles)
        {
            var item = new ProfileComboItem
            {
                Name = profile.Name,
                Profile = profile,
                IsCreateNewAction = false
            };
            items.Add(item);
            if (profile.IsActive || profile == _engine.ActiveProfile)
            {
                selectedItem = item;
            }
        }

        // Add Create New Profile action at bottom of dropdown
        items.Add(new ProfileComboItem
        {
            Name = "➕ Create New Profile...",
            Profile = null,
            IsCreateNewAction = true
        });

        CmbProfiles.ItemsSource = items;
        CmbProfiles.DisplayMemberPath = "Name";

        if (selectedItem != null)
        {
            CmbProfiles.SelectedItem = selectedItem;
        }
        else if (items.Count > 1)
        {
            CmbProfiles.SelectedIndex = 0;
        }

        _isUpdatingProfileCombo = false;
    }

    private void RefreshMacrosList()
    {
        if (_engine.ActiveProfile != null)
        {
            MacrosListBox.ItemsSource = null;
            MacrosListBox.ItemsSource = _engine.ActiveProfile.Macros;
        }

        UpdateActiveScriptsCount();
    }

    private void UpdateActiveScriptsCount()
    {
        int activeCount = _engine.ActiveProfile?.Macros.Count(m => m.IsEnabled) ?? 0;
        TxtActiveScriptsCount.Text = $"Active Scripts ({activeCount})";
    }

    private void SaveState()
    {
        if (!_isInitialized || string.IsNullOrEmpty(_profilesFilePath)) return;
        MacroJsonSerializer.SaveProfiles(_engine.Profiles.ToList(), _profilesFilePath);
        MacroJsonSerializer.SaveSettings(_engine.Settings, _settingsFilePath);
    }

    #region Drag and Drop AutoHotkey (.ahk) Conversion

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropZoneBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("NeonEmeraldGlowBrush");
            DropZoneBorder.Background = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString("#152422")!;
            e.Handled = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropZoneBorder.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#33436B")!;
        DropZoneBorder.Background = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString("#111726")!;

        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            int importedCount = 0;

            foreach (string file in files)
            {
                if (file.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
                {
                    var convertedMacros = AhkScriptParser.ParseAhkFile(file);
                    if (_engine.ActiveProfile != null)
                    {
                        foreach (var m in convertedMacros)
                        {
                            _engine.ActiveProfile.Macros.Add(m);
                            importedCount++;
                        }
                    }
                }
            }

            if (importedCount > 0)
            {
                SaveState();
                RefreshMacrosList();
                _hudOverlay?.ShowNotification(
                    $"IMPORTED {importedCount} MACROS",
                    "Auto-converted from AutoHotkey script",
                    "file-code",
                    "#34D399",
                    _engine.Settings.OverlayPosition,
                    2500);
            }
        }
    }

    #endregion

    #region Master Controls

    private void MasterEngineSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        bool isChecked = MasterEngineSwitchBox.IsChecked == true;
        _engine.SetMasterEngineEnabled(isChecked);
        SaveState();
    }

    #endregion

    #region Profile & Macro Management

    private void CmbProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdatingProfileCombo) return;

        if (CmbProfiles.SelectedItem is ProfileComboItem item)
        {
            if (item.IsCreateNewAction)
            {
                // Open New Profile Dialog
                var dlg = new NewProfileDialog
                {
                    Owner = this
                };

                if (dlg.ShowDialog() == true && dlg.CreatedProfile != null)
                {
                    foreach (var p in _engine.Profiles)
                    {
                        p.IsActive = false;
                    }

                    dlg.CreatedProfile.IsActive = true;
                    _engine.Profiles.Add(dlg.CreatedProfile);
                    SaveState();
                    RefreshProfilesComboBox();
                    RefreshMacrosList();
                    _hudOverlay?.ShowNotification("PROFILE CREATED", dlg.CreatedProfile.Name, "layers", "#34D399");
                }
                else
                {
                    // Restores selection to active profile if user canceled
                    RefreshProfilesComboBox();
                }
            }
            else if (item.Profile != null)
            {
                foreach (var p in _engine.Profiles)
                {
                    p.IsActive = (p.Id == item.Profile.Id);
                }
                RefreshMacrosList();
                SaveState();
            }
        }
    }

    private void BtnActiveScripts_Click(object sender, RoutedEventArgs e)
    {
        var activeDlg = new ActiveScriptsDialog(_engine)
        {
            Owner = this
        };
        activeDlg.ShowDialog();
        RefreshMacrosList();
        SaveState();
    }

    private void ImportAhkBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import AutoHotkey Script",
            Filter = "AutoHotkey Scripts (*.ahk)|*.ahk|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            int count = 0;
            foreach (var file in dialog.FileNames)
            {
                var converted = AhkScriptParser.ParseAhkFile(file);
                if (_engine.ActiveProfile != null)
                {
                    foreach (var m in converted)
                    {
                        _engine.ActiveProfile.Macros.Add(m);
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                SaveState();
                RefreshMacrosList();
                _hudOverlay?.ShowNotification(
                    $"IMPORTED {count} MACROS",
                    "Converted from AutoHotkey script",
                    "file-code",
                    "#34D399",
                    _engine.Settings.OverlayPosition,
                    2500);
            }
        }
    }

    private void NewMacroBtn_Click(object sender, RoutedEventArgs e)
    {
        var editor = new MacroEditorDialog
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            _engine.ActiveProfile?.Macros.Add(editor.Macro);
            SaveState();
            RefreshMacrosList();
            _hudOverlay?.ShowNotification("MACRO CREATED", editor.Macro.Name, "plus", "#34D399");
        }
    }

    private void MacroEnable_Changed(object sender, RoutedEventArgs e)
    {
        UpdateActiveScriptsCount();
        SaveState();
    }

    private void MacroRunNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            _engine.ExecuteMacro(macro);
        }
    }

    private void MacroEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            var editor = new MacroEditorDialog(macro)
            {
                Owner = this
            };

            if (editor.ShowDialog() == true)
            {
                int idx = _engine.ActiveProfile?.Macros.IndexOf(macro) ?? -1;
                if (idx >= 0 && _engine.ActiveProfile != null)
                {
                    _engine.ActiveProfile.Macros[idx] = editor.Macro;
                    SaveState();
                    RefreshMacrosList();
                    _hudOverlay?.ShowNotification("MACRO UPDATED", editor.Macro.Name, "edit", "#A78BFA");
                }
            }
        }
    }

    private void MacroDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            var res = System.Windows.MessageBox.Show($"Are you sure you want to delete \"{macro.Name}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _engine.ActiveProfile?.Macros.Remove(macro);
                SaveState();
                RefreshMacrosList();
            }
        }
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var settingsDlg = new SettingsDialog(_engine.Settings)
        {
            Owner = this
        };

        if (settingsDlg.ShowDialog() == true)
        {
            SaveState();
            _hudOverlay?.ShowNotification("SETTINGS SAVED", "Overlay and system updated", "settings", "#38BDF8");
        }
    }

    #endregion

    #region Window Chrome

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Settings.MinimizeToTray)
        {
            Hide();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Settings.CloseToTray)
        {
            Hide();
        }
        else
        {
            Close();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_engine.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _trayManager?.Dispose();
            _hudOverlay?.Close();
            _engine.Dispose();
            System.Windows.Application.Current.Shutdown();
        }
    }

    #endregion
}

public class ProfileComboItem
{
    public string Name { get; set; } = string.Empty;
    public MacroProfile? Profile { get; set; }
    public bool IsCreateNewAction { get; set; }

    public override string ToString() => Name;
}