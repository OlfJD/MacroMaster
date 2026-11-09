using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace MacroMaster.UI.Windows;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private TextBox? _activeCaptureTextBox;
    private Button? _activeCaptureButton;
    private LowLevelKeyboardHook? _captureKeyboardHook;
    private LowLevelMouseHook? _captureMouseHook;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        WindowHelper.Attach(this, RootGrid);
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Hotkeys & Triggers
        TxtKillswitch.Text = _settings.EmergencyKillswitchKey;

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
        DialogResult = false;
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        StopKeyCapture();

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

        DialogResult = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopKeyCapture();
    }
}
