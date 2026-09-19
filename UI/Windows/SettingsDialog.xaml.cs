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
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Hotkeys & Triggers
        TxtKillswitch.Text = _settings.EmergencyKillswitchKey;
        
        ChkHoldLeftClick.IsChecked = _settings.HoldLeftClickHotkeyEnabled;
        TxtHoldLeftClick.Text = _settings.HoldLeftClickHotkey;

        ChkAutoClicker.IsChecked = _settings.AutoClickerHotkeyEnabled;
        TxtAutoClicker.Text = _settings.AutoClickerHotkey;
        TxtAutoClickerCps.Text = _settings.AutoClickerCps.ToString();

        ChkMinecraftLoop.IsChecked = _settings.MinecraftLoopHotkeyEnabled;
        TxtMinecraftLoop.Text = _settings.MinecraftLoopHotkey;

        // HUD / Overlay
        ChkShowOverlay.IsChecked = _settings.ShowHudOverlay;
        CmbHudPosition.SelectedIndex = (int)_settings.OverlayPosition;
        TxtHudDuration.Text = _settings.HudDurationMs.ToString();

        // System
        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
        ChkCloseToTray.IsChecked = _settings.CloseToTray;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    #region Key & Mouse Capture Logic

    private void BtnCaptureKillswitch_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtKillswitch, BtnCaptureKillswitch, allowMouse: false);
    }

    private void BtnCaptureHoldLeftClick_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtHoldLeftClick, BtnCaptureHoldLeftClick, allowMouse: true);
    }

    private void BtnCaptureAutoClicker_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtAutoClicker, BtnCaptureAutoClicker, allowMouse: true);
    }

    private void BtnCaptureMinecraftLoop_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture(TxtMinecraftLoop, BtnCaptureMinecraftLoop, allowMouse: true);
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
        _activeCaptureButton.Content = "Press Key...";
        _activeCaptureButton.BorderBrush = (System.Windows.Media.Brush)FindResource("NeonEmeraldGlowBrush");

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

        _settings.HoldLeftClickHotkeyEnabled = ChkHoldLeftClick.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(TxtHoldLeftClick.Text))
        {
            _settings.HoldLeftClickHotkey = TxtHoldLeftClick.Text.Trim();
        }

        _settings.AutoClickerHotkeyEnabled = ChkAutoClicker.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(TxtAutoClicker.Text))
        {
            _settings.AutoClickerHotkey = TxtAutoClicker.Text.Trim();
        }
        if (int.TryParse(TxtAutoClickerCps.Text, out int cps))
        {
            _settings.AutoClickerCps = Math.Clamp(cps, 1, 100);
        }

        _settings.MinecraftLoopHotkeyEnabled = ChkMinecraftLoop.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(TxtMinecraftLoop.Text))
        {
            _settings.MinecraftLoopHotkey = TxtMinecraftLoop.Text.Trim();
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
