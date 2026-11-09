using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MacroMaster.Core.Engine;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using MacroMaster.Core.Parsers;
using MacroMaster.Core.Presets;
using MacroMaster.UI;
using MacroMaster.UI.Windows;
using Brush = System.Windows.Media.Brush;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Color = System.Windows.Media.Color;

namespace MacroMaster;

public partial class MainWindow : Window
{
    private readonly MacroEngine _engine = new();
    private HudOverlayWindow? _hudOverlay;
    private TrayManager? _trayManager;
    private readonly string _profilesFilePath;
    private readonly string _settingsFilePath;
    private bool _isInitialized;
    private bool _isUpdatingProfiles;
    private ListCollectionView? _macroView;
    private string _runtimeSignature = string.Empty;

    public MainWindow()
    {
        string appData = MacroJsonSerializer.GetAppDataDirectory();
        _profilesFilePath = Path.Combine(appData, "profiles.json");
        _settingsFilePath = Path.Combine(appData, "settings.json");

        InitializeComponent();
        WindowHelper.Attach(this, RootGrid);
        StateChanged += (_, _) => MaximizeIcon.IconKey = WindowState == WindowState.Maximized ? "restore" : "maximize";

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = MacroJsonSerializer.LoadSettings(_settingsFilePath);
        var load = MacroJsonSerializer.LoadProfiles(_profilesFilePath);
        var profiles = load.Profiles;
        bool firstRun = profiles == null || profiles.Count == 0;
        if (firstRun)
        {
            profiles = DefaultPresets.CreateDefaultProfiles();
            settings.DataVersion = AppSettings.CurrentDataVersion;
        }
        MigrateData(settings, profiles!);
        if (firstRun) MacroJsonSerializer.SaveProfiles(profiles!, _profilesFilePath);

        _hudOverlay = new HudOverlayWindow();

        _engine.Initialize(settings, profiles!);
        _engine.OnHudNotification += Engine_OnHudNotification;
        _engine.OnStateRefreshed += Engine_OnStateRefreshed;
        if (!_engine.HooksActive)
        {
            Notify("Hotkeys couldn't start", "Windows refused the keyboard/mouse hook. Restart MacroMaster.", "octagon-x", "#F2555A", 8000);
        }

        _trayManager = new TrayManager(_engine, ShowAndRestore);

        _isInitialized = true;
        RefreshProfiles();
        RefreshMacrosList();
        SyncUiStates();

        AutoStartManager.SyncStartupRegistration(_engine.Settings.StartWithWindows);

        bool isAutostart = Environment.GetCommandLineArgs().Any(arg =>
            arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase));
        if (isAutostart)
        {
            Hide();
            WindowState = WindowState.Minimized;
        }

        if (load.BrokenFileBackup != null)
        {
            ConfirmDialog.Show(this, "Your profiles file couldn't be read",
                (load.Profiles != null
                    ? "MacroMaster restored the last good copy instead."
                    : "MacroMaster started with the default profiles instead.") +
                $" The unreadable file was kept as:\n{load.BrokenFileBackup}", "OK", cancelText: null);
        }
    }

    /// <summary>One-time upgrades of saved data from older versions.</summary>
    private static void MigrateData(AppSettings settings, List<MacroProfile> profiles)
    {
        if (settings.DataVersion < 2)
        {
            // Before profile switches existed only the selected profile's hotkeys were live,
            // so keep exactly that profile on and the others off.
            var selected = profiles.FirstOrDefault(p => p.IsActive) ?? profiles.FirstOrDefault();
            foreach (var p in profiles) p.IsOn = p == selected;
        }

        if (settings.DataVersion < 3 && settings.OverlayPosition == HudPosition.TopCenter)
        {
            // Popups moved from top center to the top-right corner (old default → new default)
            settings.OverlayPosition = HudPosition.TopRight;
        }

        // Exactly one profile is shown in the window
        var shown = profiles.FirstOrDefault(p => p.IsActive) ?? profiles.FirstOrDefault();
        foreach (var p in profiles) p.IsActive = p == shown;

        settings.DataVersion = AppSettings.CurrentDataVersion;
    }

    private void ShowAndRestore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Notify(string title, string subtitle, string icon, string color = "#7C83FF", int durationMs = 2200)
    {
        _hudOverlay?.ShowNotification(title, subtitle, icon, color, _engine.Settings.OverlayPosition, durationMs);
    }

    private void Engine_OnHudNotification(string title, string subtitle, string iconKey, string accentColor)
    {
        _hudOverlay?.ShowNotification(title, subtitle, iconKey, accentColor, _engine.Settings.OverlayPosition, _engine.Settings.HudDurationMs);
    }

    private void Engine_OnStateRefreshed()
    {
        // Raised from macro worker threads too; don't block them on the UI thread
        Dispatcher.BeginInvoke(SyncUiStates);
    }

    #region State → UI

    private Brush Res(string key) => (Brush)FindResource(key);

    private void SyncUiStates()
    {
        var s = _engine.Settings;
        bool on = s.MasterEngineEnabled;

        MasterEngineSwitchBox.IsChecked = on;
        EngineTitle.Text = on ? "Engine running" : "Engine paused";
        EngineSubtitle.Text = on ? "Hotkeys are live" : "All hotkeys ignored";
        EngineDot.Fill = Res(on ? "SuccessBrush" : "TextMutedBrush");
        EngineCard.Background = Res(on ? "SuccessSubtleBrush" : "SurfaceBrush");
        EngineCard.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0x1F, 0x42, 0x32)) : Res("BorderBrush");

        StatusDot.Fill = Res(on ? "SuccessBrush" : "TextMutedBrush");
        StatusEngineText.Text = on ? "Engine running" : "Engine paused";
        StatusKillKey.Text = string.IsNullOrWhiteSpace(s.EmergencyKillswitchKey) ? "F12" : s.EmergencyKillswitchKey;

        // Running / armed flags live on the models without change notification,
        // so re-render the rows only when that runtime state actually changed.
        string signature = string.Join("|", _engine.SelectedProfile?.Macros.Select(m => $"{m.IsRunning}{m.IsCurrentlyArmed}") ?? Enumerable.Empty<string>());
        if (signature != _runtimeSignature)
        {
            _runtimeSignature = signature;
            _macroView?.Refresh();
        }

        UpdateCounts();
    }

    private void UpdateCounts()
    {
        var profile = _engine.SelectedProfile;
        int total = profile?.Macros.Count ?? 0;
        int enabled = profile?.Macros.Count(m => m.IsEnabled) ?? 0;
        bool profileOn = profile?.IsOn == true;

        TxtProfileTitle.Text = profile?.Name ?? "No profile";
        TxtProfileSubtitle.Text = total == 0
            ? "No macros in this profile"
            : $"{total} macro{(total == 1 ? "" : "s")} · {enabled} enabled" + (profileOn ? "" : " · profile off");
        ProfileOffBanner.Visibility = profile != null && !profileOn ? Visibility.Visible : Visibility.Collapsed;

        // Status bar covers every profile, since several can be live at once
        var onProfiles = _engine.Profiles.Where(p => p.IsOn).ToList();
        int live = onProfiles.Sum(p => p.Macros.Count(m => m.IsEnabled));
        int running = _engine.Profiles.Sum(p => p.Macros.Count(m => m.IsRunning));
        string profilesText = onProfiles.Count switch
        {
            0 => "No profiles on",
            1 => $"{onProfiles[0].Name} on",
            _ => $"{onProfiles.Count} profiles on"
        };
        StatusCounts.Text = $"{profilesText} · {live} macro{(live == 1 ? "" : "s")} live" + (running > 0 ? $" · {running} running" : "");

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        int total = _engine.SelectedProfile?.Macros.Count ?? 0;
        int visible = _macroView?.Count ?? 0;

        if (visible > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Visible;
        if (total == 0)
        {
            EmptyIcon.IconKey = "inbox";
            EmptyTitle.Text = "No macros yet";
            EmptyBody.Text = "Create one from scratch, record your inputs, or drop an AutoHotkey script anywhere on this window.";
            EmptyAction.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyIcon.IconKey = "search";
            EmptyTitle.Text = "Nothing matches";
            EmptyBody.Text = "Try a different search or filter.";
            EmptyAction.Visibility = Visibility.Collapsed;
        }
    }

    #endregion

    #region Profiles

    private void RefreshProfiles()
    {
        _isUpdatingProfiles = true;
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _engine.Profiles;
        ProfilesList.SelectedItem = _engine.SelectedProfile;
        _isUpdatingProfiles = false;
    }

    private void ProfileSwitch_Changed(object sender, RoutedEventArgs e)
    {
        // Also fires when the binding sets the initial state; SetProfileOn ignores no-op changes
        if (!_isInitialized) return;
        if (sender is System.Windows.Controls.CheckBox { DataContext: MacroProfile profile } box &&
            _engine.SetProfileOn(profile, box.IsChecked == true))
        {
            SaveState();
        }
    }

    private void TurnProfileOn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.SelectedProfile is { } profile && _engine.SetProfileOn(profile, true))
        {
            SaveState();
        }
    }

    private void ActivateProfile(MacroProfile profile)
    {
        foreach (var p in _engine.Profiles)
        {
            p.IsActive = p.Id == profile.Id;
        }
        _engine.Settings.ActiveProfileId = profile.Id;
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdatingProfiles) return;
        if (ProfilesList.SelectedItem is MacroProfile profile)
        {
            ActivateProfile(profile);
            TxtSearch.Text = string.Empty;
            RefreshMacrosList();
            SaveState();
        }
    }

    private void NewProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewProfileDialog { Owner = this };
        if (dlg.ShowDialog() == true && dlg.CreatedProfile != null)
        {
            _engine.Profiles.Add(dlg.CreatedProfile);
            ActivateProfile(dlg.CreatedProfile);
            SaveState();
            RefreshProfiles();
            RefreshMacrosList();
            Notify("Profile created", dlg.CreatedProfile.Name, "logo");
        }
    }

    private static MacroProfile? ProfileFromMenu(object sender) =>
        (sender as FrameworkElement)?.DataContext as MacroProfile;

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileFromMenu(sender) is not { } profile) return;
        var dlg = new NewProfileDialog(profile) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SaveState();
            RefreshProfiles();
            UpdateCounts();
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileFromMenu(sender) is not { } profile) return;
        if (_engine.Profiles.Count <= 1)
        {
            ConfirmDialog.Show(this, "Can't delete the last profile", "MacroMaster needs at least one profile. Create another one first.", "OK", cancelText: null);
            return;
        }

        string count = profile.Macros.Count == 1 ? "1 macro" : $"{profile.Macros.Count} macros";
        if (!ConfirmDialog.Show(this, $"Delete \"{profile.Name}\"?", $"The profile and its {count} will be removed. This can't be undone.", "Delete profile", danger: true))
            return;

        bool wasActive = profile == _engine.SelectedProfile;
        _engine.Profiles.Remove(profile);
        if (wasActive && _engine.Profiles.Count > 0)
        {
            ActivateProfile(_engine.Profiles[0]);
        }
        SaveState();
        RefreshProfiles();
        RefreshMacrosList();
    }

    #endregion

    #region Macro list

    private void RefreshMacrosList()
    {
        var profile = _engine.SelectedProfile;
        if (profile == null)
        {
            MacrosListBox.ItemsSource = null;
            _macroView = null;
        }
        else
        {
            _macroView = new ListCollectionView(profile.Macros) { Filter = MacroFilter };
            MacrosListBox.ItemsSource = _macroView;
        }
        _runtimeSignature = string.Empty;
        UpdateCounts();
    }

    private bool MacroFilter(object obj)
    {
        if (obj is not MacroDefinition m) return false;

        if (FilterEnabled.IsChecked == true && !m.IsEnabled) return false;
        if (FilterDisabled.IsChecked == true && m.IsEnabled) return false;

        string q = TxtSearch.Text.Trim();
        if (q.Length == 0) return true;

        return Contains(m.Name, q) || Contains(m.Description, q) || Contains(m.Category, q) ||
               Contains(m.ProcessFilter, q) || Contains(m.Trigger.DisplayString, q);

        static bool Contains(string? hay, string needle) =>
            hay?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        _macroView?.Refresh();
        UpdateEmptyState();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _macroView?.Refresh();
        UpdateEmptyState();
    }

    private void MacroEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        SaveState();
        UpdateCounts();
        // If a filter is active the row may need to disappear
        if (FilterAll.IsChecked != true)
        {
            Dispatcher.BeginInvoke(() => { _macroView?.Refresh(); UpdateEmptyState(); });
        }
    }

    private void MacroRunNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            _engine.ExecuteMacro(macro);
        }
    }

    private void MacrosListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double clicks on the switch / buttons inside the row
        if (e.OriginalSource is DependencyObject src && FindAncestor<ButtonBase>(src) != null) return;
        if (MacrosListBox.SelectedItem is MacroDefinition macro)
        {
            EditMacro(macro);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private void MacroEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            EditMacro(macro);
        }
    }

    private IEnumerable<(MacroDefinition, MacroProfile)> AllMacros() =>
        _engine.Profiles.SelectMany(p => p.Macros.Select(m => (m, p))).ToList();

    private void EditMacro(MacroDefinition macro)
    {
        var editor = new MacroEditorDialog(macro, AllMacros()) { Owner = this };
        if (editor.ShowDialog() != true || _engine.SelectedProfile == null) return;

        int idx = _engine.SelectedProfile.Macros.IndexOf(macro);
        if (idx < 0) return;

        _engine.SelectedProfile.Macros[idx] = editor.Macro;
        SaveState();
        RefreshMacrosList();
        Notify("Macro saved", editor.Macro.Name, "logo");
    }

    private void NewMacroBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.SelectedProfile == null) return;
        var editor = new MacroEditorDialog(null, AllMacros()) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            _engine.SelectedProfile.Macros.Add(editor.Macro);
            SaveState();
            RefreshMacrosList();
            Notify("Macro created", editor.Macro.Name, "logo");
        }
    }

    private void MacroDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MacroDefinition macro }) return;
        if (!ConfirmDialog.Show(this, $"Delete \"{macro.Name}\"?", "This macro and its steps will be removed. This can't be undone.", "Delete", danger: true))
            return;

        _engine.SelectedProfile?.Macros.Remove(macro);
        SaveState();
        UpdateCounts();
    }

    #endregion

    #region AutoHotkey import (button + drag and drop)

    private int ImportAhkFiles(IEnumerable<string> files)
    {
        if (_engine.SelectedProfile == null) return 0;
        int count = 0;
        foreach (var file in files.Where(f => f.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var m in AhkScriptParser.ParseAhkFile(file))
            {
                _engine.SelectedProfile.Macros.Add(m);
                count++;
            }
        }

        if (count > 0)
        {
            SaveState();
            RefreshMacrosList();
            Notify($"Imported {count} macro{(count == 1 ? "" : "s")}", "Converted from AutoHotkey", "logo", "#7C83FF", 2500);
        }
        return count;
    }

    private void ImportAhkBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import AutoHotkey script",
            Filter = "AutoHotkey scripts (*.ahk)|*.ahk|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true)
        {
            ImportAhkFiles(dialog.FileNames);
        }
    }

    private static bool HasAhkFiles(System.Windows.DragEventArgs e) =>
        e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
        e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files &&
        files.Any(f => f.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase));

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (HasAhkFiles(e))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements – only hide when the cursor really left
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
        {
            ImportAhkFiles(files);
        }
    }

    #endregion

    #region Engine, settings, persistence

    private void MasterEngineSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        bool isChecked = MasterEngineSwitchBox.IsChecked == true;
        if (isChecked == _engine.Settings.MasterEngineEnabled) return;
        _engine.SetMasterEngineEnabled(isChecked);
        SaveState();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_engine.Settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SaveState();
            SyncUiStates();
            Notify("Settings saved", "Changes are live", "logo");
        }
    }

    private void SaveState()
    {
        if (!_isInitialized || string.IsNullOrEmpty(_profilesFilePath)) return;
        MacroJsonSerializer.SaveProfiles(_engine.Profiles.ToList(), _profilesFilePath);
        MacroJsonSerializer.SaveSettings(_engine.Settings, _settingsFilePath);
        AutoStartManager.SetStartup(_engine.Settings.StartWithWindows);
    }

    #endregion

    #region Window chrome

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Settings.MinimizeToTray)
            Hide();
        else
            WindowState = WindowState.Minimized;
    }

    private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Settings.CloseToTray)
            Hide();
        else
            Close();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
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
