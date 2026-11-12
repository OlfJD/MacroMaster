using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using MacroMaster.UI;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace MacroMaster.UI.Views;

/// <summary>
/// Settings as a page inside the main window (see MainWindow.SettingsBtn_Click). Writes into the
/// AppSettings it was given only on Save, then raises <see cref="Finished"/>.
/// </summary>
public partial class SettingsView : System.Windows.Controls.UserControl
{
    /// <summary>True = saved, false = left without saving.</summary>
    public event Action<bool>? Finished;

    private readonly AppSettings _settings;
    private TextBox? _activeCaptureTextBox;
    private Button? _activeCaptureButton;
    private LowLevelKeyboardHook? _captureKeyboardHook;
    private LowLevelMouseHook? _captureMouseHook;

    // Custom spots being edited; written to the settings on Save
    private ScreenSpot _hudSpot = new();
    private ScreenSpot _indicatorSpot = new();
    private bool _loading;

    /// <summary>Order of the position lists (top row, bottom row, custom).</summary>
    private static readonly HudPosition[] PositionOrder =
    {
        HudPosition.TopLeft, HudPosition.TopCenter, HudPosition.TopRight,
        HudPosition.BottomLeft, HudPosition.BottomCenter, HudPosition.BottomRight,
        HudPosition.Custom
    };

    public SettingsView(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
        Unloaded += (_, _) => StopKeyCapture(); // never leave a keyboard hook behind
    }

    private void LoadSettings()
    {
        // Hotkeys & Triggers
        TxtEngineToggle.Text = _settings.EngineToggleKey;
        TxtQuickOpen.Text = _settings.QuickOpenKey;

        if (Environment.IsPrivilegedProcess)
        {
            TxtAdminHint.Text = "MacroMaster is running as administrator, so hotkeys also work in apps that run as administrator.";
            BtnRestartAsAdmin.Visibility = Visibility.Collapsed;
        }

        // HUD / Overlay
        _loading = true;
        _hudSpot = Copy(_settings.OverlayCustomSpot);
        _indicatorSpot = Copy(_settings.ModeIndicatorCustomSpot);
        FillPositions(CmbHudPosition, HudPosition.TopRight, _settings.OverlayPosition);
        FillPositions(CmbIndicatorPosition, HudPosition.TopLeft, _settings.ModeIndicatorPosition);
        ChkShowOverlay.IsChecked = _settings.ShowHudOverlay;
        TxtHudDuration.Text = _settings.HudDurationMs.ToString();
        ChkModeIndicator.IsChecked = _settings.ShowModeIndicator;
        _loading = false;

        // Display
        double auto = UiScale.AutoFor(new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow).Handle);
        foreach (int percent in UiScale.Choices)
        {
            CmbUiScale.Items.Add(new ComboBoxItem
            {
                Content = percent == 0 ? $"Auto ({auto * 100:0} % on this screen)" : $"{percent} %",
                Tag = percent
            });
        }
        int current = Array.IndexOf(UiScale.Choices, _settings.UiScalePercent);
        CmbUiScale.SelectedIndex = Math.Max(0, current);

        // System
        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
        ChkStartAsAdmin.IsChecked = _settings.StartAsAdmin;
        UpdateStartAsAdminRow();
        ChkAutoUpdate.IsChecked = _settings.AutoUpdate;
        TxtUpdateHint.Text = $"You have version {AppUpdater.CurrentVersion.ToString(3)}. New versions from GitHub are installed quietly in the background.";
        ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
        ChkCloseToTray.IsChecked = _settings.CloseToTray;
    }

    #region Key & Mouse Capture Logic

    private void BtnCaptureEngineToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtEngineToggle, BtnCaptureEngineToggle, allowMouse: false);
    }

    /// <summary>
    /// "Show preview": the main window shows the mode indicator for a few seconds at the position
    /// picked here (saved or not), so it can be judged without starting the game.
    /// </summary>
    public event Action<HudPosition, ScreenSpot>? ModeIndicatorPreviewRequested;

    /// <summary>"Show preview" next to notifications: an example notification at the position picked here.</summary>
    public event Action<HudPosition, ScreenSpot>? NotificationPreviewRequested;

    private void BtnPreviewIndicator_Click(object sender, RoutedEventArgs e)
        => ModeIndicatorPreviewRequested?.Invoke(SelectedPosition(CmbIndicatorPosition), _indicatorSpot);

    private void BtnPreviewHud_Click(object sender, RoutedEventArgs e)
        => NotificationPreviewRequested?.Invoke(SelectedPosition(CmbHudPosition), _hudSpot);

    #region Positions

    private static ScreenSpot Copy(ScreenSpot? s) => new() { X = s?.X ?? 0.5, Y = s?.Y ?? 0.1 };

    private static string PositionName(HudPosition p) => p switch
    {
        HudPosition.TopLeft => "Top left",
        HudPosition.TopCenter => "Top center",
        HudPosition.TopRight => "Top right",
        HudPosition.BottomLeft => "Bottom left",
        HudPosition.BottomCenter => "Bottom center",
        HudPosition.BottomRight => "Bottom right",
        _ => "Custom (drag it anywhere)"
    };

    private static void FillPositions(ComboBox box, HudPosition defaultPosition, HudPosition current)
    {
        box.Items.Clear();
        foreach (var p in PositionOrder)
        {
            box.Items.Add(new ComboBoxItem
            {
                Content = PositionName(p) + (p == defaultPosition ? " (default)" : ""),
                Tag = p
            });
        }
        box.SelectedIndex = Math.Max(0, Array.IndexOf(PositionOrder, current));
    }

    private static HudPosition SelectedPosition(ComboBox box)
        => box.SelectedItem is ComboBoxItem { Tag: HudPosition p } ? p : HudPosition.TopRight;

    private void CmbHudPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => PositionChanged(CmbHudPosition, BtnPlaceHud, BtnPlaceHud_Click);

    private void CmbIndicatorPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => PositionChanged(CmbIndicatorPosition, BtnPlaceIndicator, BtnPlaceIndicator_Click);

    /// <summary>Custom shows the "Place it…" button; picking Custom in the list opens the picker straight away.</summary>
    private void PositionChanged(ComboBox box, Button placeButton, RoutedEventHandler place)
    {
        if (placeButton == null) return; // during InitializeComponent
        bool custom = SelectedPosition(box) == HudPosition.Custom;
        placeButton.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (!custom || _loading || !IsLoaded) return;

        // Only once the drop-down has closed, or it would take the focus from the picker
        void Open() => Dispatcher.BeginInvoke(() => place(placeButton, new RoutedEventArgs()), System.Windows.Threading.DispatcherPriority.Background);
        if (!box.IsDropDownOpen) { Open(); return; }
        void Closed(object? s, EventArgs e) { box.DropDownClosed -= Closed; Open(); }
        box.DropDownClosed += Closed;
    }

    private void BtnPlaceHud_Click(object sender, RoutedEventArgs e)
    {
        var spot = Windows.PositionPickerWindow.Pick("Notifications show up here", _hudSpot, Window.GetWindow(this));
        if (spot != null) _hudSpot = spot;
    }

    private void BtnPlaceIndicator_Click(object sender, RoutedEventArgs e)
    {
        var spot = Windows.PositionPickerWindow.Pick("The mode indicator sits here", _indicatorSpot, Window.GetWindow(this));
        if (spot != null) _indicatorSpot = spot;
    }

    #endregion

    #region Start with Windows

    private void ChkStartWithWindows_Changed(object sender, RoutedEventArgs e) => UpdateStartAsAdminRow();

    /// <summary>"As administrator" belongs to "Start with Windows" and only matters while that is on.</summary>
    private void UpdateStartAsAdminRow()
    {
        if (StartAsAdminRow == null) return;
        bool on = ChkStartWithWindows.IsChecked == true;
        StartAsAdminRow.IsEnabled = on;
        StartAsAdminRow.Opacity = on ? 1 : 0.45;
    }

    #endregion

    /// <summary>The main window does the restart (it owns the engine and the tray icon).</summary>
    public event Action? RestartAsAdminRequested;

    private void BtnRestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        StopKeyCapture();
        RestartAsAdminRequested?.Invoke();
    }

    private void BtnCaptureQuickOpen_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtQuickOpen, BtnCaptureQuickOpen, allowMouse: false);
    }

    private void ToggleCapture(TextBox targetBox, Button targetButton, bool allowMouse)
    {
        if (_activeCaptureButton == targetButton)
        {
            StopKeyCapture();
        }
        else
        {
            StartKeyCapture(targetBox, targetButton, allowMouse);
        }
    }

    private void StartKeyCapture(TextBox targetBox, Button targetButton, bool allowMouse)
    {
        StopKeyCapture();

        _activeCaptureTextBox = targetBox;
        _activeCaptureButton = targetButton;
        _activeCaptureButton.Content = "Press a key…";
        _activeCaptureButton.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");

        _captureKeyboardHook = new LowLevelKeyboardHook();
        _captureKeyboardHook.OnKeyDown += (vk, isExt) =>
        {
            if (_activeCaptureTextBox == null) return false;
            string keyName = KeyHelper.VkToString((uint)vk);
            Dispatcher.BeginInvoke(() =>
            {
                if (_activeCaptureTextBox != null)
                {
                    _activeCaptureTextBox.Text = keyName;
                }
                StopKeyCapture();
            });
            return true;
        };
        _captureKeyboardHook.Install();

        if (allowMouse)
        {
            _captureMouseHook = new LowLevelMouseHook();
            _captureMouseHook.OnMouseEvent += (btn, isDown) =>
            {
                if (_activeCaptureTextBox == null || !isDown) return false;
                string btnName = btn switch
                {
                    MouseButtonType.Left => "Left Click",
                    MouseButtonType.Right => "Right Click",
                    MouseButtonType.Middle => "Middle Click",
                    MouseButtonType.XButton1 => "Mouse 4",
                    MouseButtonType.XButton2 => "Mouse 5",
                    _ => btn.ToString()
                };

                Dispatcher.BeginInvoke(() =>
                {
                    if (_activeCaptureTextBox != null)
                    {
                        _activeCaptureTextBox.Text = btnName;
                    }
                    StopKeyCapture();
                });
                return true;
            };
            _captureMouseHook.Install();
        }
    }

    private void StopKeyCapture()
    {
        if (_activeCaptureButton != null)
        {
            _activeCaptureButton.Content = "Capture";
            _activeCaptureButton.ClearValue(Button.BorderBrushProperty);
            _activeCaptureButton = null;
        }
        _activeCaptureTextBox = null;

        _captureKeyboardHook?.Dispose();
        _captureKeyboardHook = null;
        _captureMouseHook?.Dispose();
        _captureMouseHook = null;
    }

    #endregion

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        StopKeyCapture();
        Finished?.Invoke(false);
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        StopKeyCapture();

        // One key can't do both jobs: the engine key is checked first and would always win
        string quickOpen = TxtQuickOpen.Text.Trim();
        string engineKey = TxtEngineToggle.Text.Trim();
        if (engineKey.Length > 0 && KeyHelper.NormalizeToVk(engineKey) == 0)
        {
            Windows.ConfirmDialog.Show(Window.GetWindow(this)!, "That engine key isn't recognised", $"“{engineKey}” isn't a key MacroMaster knows. Use Capture and press the key.", "OK", cancelText: null);
            return;
        }
        if (quickOpen.Length > 0)
        {
            int quickVk = KeyHelper.NormalizeToVk(quickOpen);
            if (quickVk == 0)
            {
                Windows.ConfirmDialog.Show(Window.GetWindow(this)!,"That quick-open key isn't recognised", $"“{quickOpen}” isn't a key MacroMaster knows. Use Capture and press the key.", "OK", cancelText: null);
                return;
            }
            if (quickVk == KeyHelper.NormalizeToVk(engineKey))
            {
                Windows.ConfirmDialog.Show(Window.GetWindow(this)!,"Pick a different quick-open key", "It's the same key as the engine on / off key, which would always win.", "OK", cancelText: null);
                return;
            }
        }
        _settings.QuickOpenKey = quickOpen;
        _settings.EngineToggleKey = engineKey;

        // HUD / Overlay
        _settings.ShowHudOverlay = ChkShowOverlay.IsChecked == true;
        _settings.OverlayPosition = SelectedPosition(CmbHudPosition);
        _settings.OverlayCustomSpot = _hudSpot;
        if (int.TryParse(TxtHudDuration.Text, out int dur))
        {
            _settings.HudDurationMs = Math.Max(500, dur);
        }

        _settings.ShowModeIndicator = ChkModeIndicator.IsChecked == true;
        _settings.ModeIndicatorPosition = SelectedPosition(CmbIndicatorPosition);
        _settings.ModeIndicatorCustomSpot = _indicatorSpot;

        // Display
        if (CmbUiScale.SelectedItem is ComboBoxItem { Tag: int percent }) _settings.UiScalePercent = percent;

        // System
        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.StartAsAdmin = ChkStartAsAdmin.IsChecked == true;
        _settings.AutoUpdate = ChkAutoUpdate.IsChecked == true;
        _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
        _settings.CloseToTray = ChkCloseToTray.IsChecked == true;

        Finished?.Invoke(true);
    }
}
