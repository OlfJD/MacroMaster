using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

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
        TxtKillswitch.Text = _settings.EmergencyKillswitchKey;
        TxtQuickOpen.Text = _settings.QuickOpenKey;

        // HUD / Overlay
        ChkShowOverlay.IsChecked = _settings.ShowHudOverlay;
        CmbHudPosition.SelectedIndex = (int)_settings.OverlayPosition;
        TxtHudDuration.Text = _settings.HudDurationMs.ToString();

        // System
        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
        ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
        ChkCloseToTray.IsChecked = _settings.CloseToTray;
    }

    #region Key & Mouse Capture Logic

    private void BtnCaptureKillswitch_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtKillswitch, BtnCaptureKillswitch, allowMouse: false);
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

        // One key can't do both jobs: the kill switch is checked first and would always win
        string quickOpen = TxtQuickOpen.Text.Trim();
        string killSwitch = string.IsNullOrWhiteSpace(TxtKillswitch.Text) ? _settings.EmergencyKillswitchKey : TxtKillswitch.Text.Trim();
        if (quickOpen.Length > 0)
        {
            int quickVk = KeyHelper.NormalizeToVk(quickOpen);
            if (quickVk == 0)
            {
                Windows.ConfirmDialog.Show(Window.GetWindow(this)!,"That quick-open key isn't recognised", $"“{quickOpen}” isn't a key MacroMaster knows. Use Capture and press the key.", "OK", cancelText: null);
                return;
            }
            if (quickVk == KeyHelper.NormalizeToVk(killSwitch))
            {
                Windows.ConfirmDialog.Show(Window.GetWindow(this)!,"Pick a different quick-open key", "It's the same key as the kill switch, which would always win.", "OK", cancelText: null);
                return;
            }
        }
        _settings.QuickOpenKey = quickOpen;

        // Hotkeys & Triggers
        if (!string.IsNullOrWhiteSpace(TxtKillswitch.Text))
        {
            _settings.EmergencyKillswitchKey = TxtKillswitch.Text.Trim();
        }

        // HUD / Overlay
        _settings.ShowHudOverlay = ChkShowOverlay.IsChecked == true;
        _settings.OverlayPosition = (HudPosition)Math.Max(0, CmbHudPosition.SelectedIndex);
        if (int.TryParse(TxtHudDuration.Text, out int dur))
        {
            _settings.HudDurationMs = Math.Max(500, dur);
        }

        // System
        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
        _settings.CloseToTray = ChkCloseToTray.IsChecked == true;

        Finished?.Invoke(true);
    }
}
