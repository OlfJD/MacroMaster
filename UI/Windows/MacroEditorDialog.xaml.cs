using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;

namespace MacroMaster.UI.Windows;

public partial class MacroEditorDialog : Window
{
    public MacroDefinition Macro { get; private set; }
    public ObservableCollection<MacroAction> Actions { get; private set; } = new();

    private bool _isCapturingTrigger = false;
    private LowLevelKeyboardHook? _captureKeyboardHook;
    private LowLevelMouseHook? _captureMouseHook;

    private bool _isCapturingActionKey = false;
    private LowLevelKeyboardHook? _actionKeyCaptureHook;

    private bool _isRecording = false;
    private Stopwatch? _recordStopwatch;
    private LowLevelKeyboardHook? _recordKeyboardHook;
    private LowLevelMouseHook? _recordMouseHook;
    private readonly Dictionary<MouseButtonType, long> _mouseDownTimestamps = new();
    private readonly Dictionary<int, long> _keyDownTimestamps = new();

    private bool _isUpdatingInspector = false;

    public MacroEditorDialog(MacroDefinition? existingMacro = null)
    {
        InitializeComponent();

        Macro = existingMacro != null ? CloneMacro(existingMacro) : new MacroDefinition();
        Actions = new ObservableCollection<MacroAction>(Macro.Actions);
        ActionsList.ItemsSource = Actions;

        LoadMacroData();

        if (Actions.Count > 0)
        {
            ActionsList.SelectedIndex = 0;
        }
        else
        {
            UpdateInspectorUI();
        }
    }

    private void LoadMacroData()
    {
        TxtName.Text = Macro.Name;
        TxtDescription.Text = Macro.Description;
        TxtCategory.Text = Macro.Category;
        TxtProcess.Text = Macro.ProcessFilter;

        TxtTriggerKey.Text = Macro.Trigger.PrimaryKey;
        TxtChordKeys.Text = string.Join(", ", Macro.Trigger.ChordKeys);
        ChkCtrl.IsChecked = Macro.Trigger.RequireCtrl;
        ChkShift.IsChecked = Macro.Trigger.RequireShift;
        ChkAlt.IsChecked = Macro.Trigger.RequireAlt;

        CmbMode.SelectedIndex = Macro.Mode switch
        {
            ExecutionMode.Once => 0,
            ExecutionMode.Toggle => 1,
            ExecutionMode.RepeatCount => 2,
            _ => 0
        };
        TxtRepeatCount.Text = Macro.RepeatCount.ToString();

        ChkRequiresArming.IsChecked = Macro.RequiresArming;
        TxtArmingKey.Text = Macro.ArmingKey;
        TxtArmingName.Text = Macro.ArmingDisplayName;
        TxtArmingGroup.Text = Macro.ArmingGroupName;
        ArmingConfigPanel.Visibility = Macro.RequiresArming ? Visibility.Visible : Visibility.Collapsed;
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
        StopRecording();
        StopTriggerCapture();
        StopActionKeyCapture();
        DialogResult = false;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        StopTriggerCapture();
        StopActionKeyCapture();
        DialogResult = false;
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        StopTriggerCapture();
        StopActionKeyCapture();

        Macro.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Custom Macro" : TxtName.Text.Trim();
        Macro.Description = TxtDescription.Text.Trim();
        Macro.Category = string.IsNullOrWhiteSpace(TxtCategory.Text) ? "General" : TxtCategory.Text.Trim();
        Macro.ProcessFilter = string.IsNullOrWhiteSpace(TxtProcess.Text) ? "*" : TxtProcess.Text.Trim();

        Macro.Trigger.PrimaryKey = TxtTriggerKey.Text.Trim();
        Macro.Trigger.ChordKeys = TxtChordKeys.Text
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        Macro.Trigger.RequireCtrl = ChkCtrl.IsChecked == true;
        Macro.Trigger.RequireShift = ChkShift.IsChecked == true;
        Macro.Trigger.RequireAlt = ChkAlt.IsChecked == true;

        Macro.Mode = CmbMode.SelectedIndex switch
        {
            0 => ExecutionMode.Once,
            1 => ExecutionMode.Toggle,
            2 => ExecutionMode.RepeatCount,
            _ => ExecutionMode.Once
        };

        if (int.TryParse(TxtRepeatCount.Text, out int rep))
        {
            Macro.RepeatCount = Math.Max(1, rep);
        }

        Macro.RequiresArming = ChkRequiresArming.IsChecked == true;
        Macro.ArmingKey = TxtArmingKey.Text.Trim();
        Macro.ArmingDisplayName = TxtArmingName.Text.Trim();
        Macro.ArmingGroupName = TxtArmingGroup.Text.Trim();

        Macro.Actions = new ObservableCollection<MacroAction>(Actions);

        DialogResult = true;
        Close();
    }

    #region Action Step Inspector / Live Parameter Editing

    private MacroAction? SelectedAction => ActionsList.SelectedItem as MacroAction;

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInspectorUI();
    }

    private void UpdateInspectorUI()
    {
        var action = SelectedAction;
        if (action == null)
        {
            StepInspectorBorder.Visibility = Visibility.Collapsed;
            return;
        }

        StepInspectorBorder.Visibility = Visibility.Visible;
        int stepIdx = Actions.IndexOf(action) + 1;
        TxtInspectorHeader.Text = $"EDIT STEP #{stepIdx}: {action.Type}";

        _isUpdatingInspector = true;

        PanelKeyAction.Visibility = Visibility.Collapsed;
        PanelMouseAction.Visibility = Visibility.Collapsed;
        PanelDelayAction.Visibility = Visibility.Collapsed;
        PanelMoveAction.Visibility = Visibility.Collapsed;

        switch (action.Type)
        {
            case ActionType.KeyTap or ActionType.KeyDown or ActionType.KeyUp:
                PanelKeyAction.Visibility = Visibility.Visible;
                CmbKeyType.SelectedIndex = action.Type switch
                {
                    ActionType.KeyTap => 0,
                    ActionType.KeyDown => 1,
                    ActionType.KeyUp => 2,
                    _ => 0
                };
                TxtActionKey.Text = action.Key;
                TxtKeyHoldDuration.Text = action.DelayMs.ToString();
                PanelKeyHoldDuration.Visibility = action.Type == ActionType.KeyTap ? Visibility.Visible : Visibility.Collapsed;
                break;

            case ActionType.MouseClick or ActionType.MouseDown or ActionType.MouseUp or ActionType.MouseScroll:
                PanelMouseAction.Visibility = Visibility.Visible;
                CmbMouseType.SelectedIndex = action.Type switch
                {
                    ActionType.MouseClick => 0,
                    ActionType.MouseDown => 1,
                    ActionType.MouseUp => 2,
                    ActionType.MouseScroll => 3,
                    _ => 0
                };
                CmbMouseButton.SelectedIndex = action.MouseButton switch
                {
                    MouseButtonType.Left => 0,
                    MouseButtonType.Right => 1,
                    MouseButtonType.Middle => 2,
                    MouseButtonType.XButton1 => 3,
                    MouseButtonType.XButton2 => 4,
                    _ => 0
                };
                TxtMouseHoldDuration.Text = action.DelayMs.ToString();
                PanelMouseHoldDuration.Visibility = action.Type == ActionType.MouseClick ? Visibility.Visible : Visibility.Collapsed;
                break;

            case ActionType.Delay:
                PanelDelayAction.Visibility = Visibility.Visible;
                TxtDelayDuration.Text = action.DelayMs.ToString();
                TxtDelayJitter.Text = action.RandomJitterMs.ToString();
                break;

            case ActionType.MouseMoveRelative or ActionType.MouseMoveAbsolute:
                PanelMoveAction.Visibility = Visibility.Visible;
                TxtMoveX.Text = action.MoveX.ToString();
                TxtMoveY.Text = action.MoveY.ToString();
                break;
        }

        _isUpdatingInspector = false;
    }

    private void CmbKeyType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        SelectedAction.Type = CmbKeyType.SelectedIndex switch
        {
            0 => ActionType.KeyTap,
            1 => ActionType.KeyDown,
            2 => ActionType.KeyUp,
            _ => ActionType.KeyTap
        };
        PanelKeyHoldDuration.Visibility = SelectedAction.Type == ActionType.KeyTap ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtActionKey_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        SelectedAction.Key = TxtActionKey.Text.Trim();
    }

    private void TxtKeyHoldDuration_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        if (int.TryParse(TxtKeyHoldDuration.Text, out int ms))
        {
            SelectedAction.DelayMs = Math.Max(1, ms);
        }
    }

    private void BtnCaptureActionKey_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturingActionKey)
        {
            StopActionKeyCapture();
            return;
        }

        _isCapturingActionKey = true;
        BtnCaptureActionKey.Content = "Press...";

        _actionKeyCaptureHook = new LowLevelKeyboardHook();
        _actionKeyCaptureHook.OnKeyDown += (vk, isExt) =>
        {
            if (!_isCapturingActionKey) return false;
            string keyName = KeyHelper.VkToString((uint)vk);
            Dispatcher.BeginInvoke(() =>
            {
                TxtActionKey.Text = keyName;
                StopActionKeyCapture();
            });
            return true;
        };
        _actionKeyCaptureHook.Install();
    }

    private void StopActionKeyCapture()
    {
        _isCapturingActionKey = false;
        BtnCaptureActionKey.Content = "Set";
        _actionKeyCaptureHook?.Dispose();
        _actionKeyCaptureHook = null;
    }

    private void CmbMouseType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        SelectedAction.Type = CmbMouseType.SelectedIndex switch
        {
            0 => ActionType.MouseClick,
            1 => ActionType.MouseDown,
            2 => ActionType.MouseUp,
            3 => ActionType.MouseScroll,
            _ => ActionType.MouseClick
        };
        PanelMouseHoldDuration.Visibility = SelectedAction.Type == ActionType.MouseClick ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CmbMouseButton_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        SelectedAction.MouseButton = CmbMouseButton.SelectedIndex switch
        {
            0 => MouseButtonType.Left,
            1 => MouseButtonType.Right,
            2 => MouseButtonType.Middle,
            3 => MouseButtonType.XButton1,
            4 => MouseButtonType.XButton2,
            _ => MouseButtonType.Left
        };
    }

    private void TxtMouseHoldDuration_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        if (int.TryParse(TxtMouseHoldDuration.Text, out int ms))
        {
            SelectedAction.DelayMs = Math.Max(1, ms);
        }
    }

    private void TxtDelayDuration_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        if (int.TryParse(TxtDelayDuration.Text, out int ms))
        {
            SelectedAction.DelayMs = Math.Max(1, ms);
        }
    }

    private void TxtDelayJitter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        if (int.TryParse(TxtDelayJitter.Text, out int jitter))
        {
            SelectedAction.RandomJitterMs = Math.Max(0, jitter);
        }
    }

    private void TxtMoveX_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        if (int.TryParse(TxtMoveX.Text, out int x))
        {
            SelectedAction.MoveX = x;
        }
    }

    private void TxtMoveY_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInspector || SelectedAction == null) return;
        if (int.TryParse(TxtMoveY.Text, out int y))
        {
            SelectedAction.MoveY = y;
        }
    }

    #endregion

    #region Trigger Key & Mouse Button Capture

    private void BtnCaptureKey_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturingTrigger)
        {
            StopTriggerCapture();
            return;
        }

        StartTriggerCapture();
    }

    private void StartTriggerCapture()
    {
        _isCapturingTrigger = true;
        BtnCaptureKey.Content = "Press Key / Click...";

        _captureKeyboardHook = new LowLevelKeyboardHook();
        _captureKeyboardHook.OnKeyDown += (vk, isExt) =>
        {
            if (!_isCapturingTrigger) return false;

            // Handle modifiers if pressed alone
            if (vk is 0x10 or 0xA0 or 0xA1) // Shift
            {
                Dispatcher.BeginInvoke(() => ChkShift.IsChecked = true);
                return true;
            }
            if (vk is 0x11 or 0xA2 or 0xA3) // Ctrl
            {
                Dispatcher.BeginInvoke(() => ChkCtrl.IsChecked = true);
                return true;
            }
            if (vk is 0x12 or 0xA4 or 0xA5) // Alt
            {
                Dispatcher.BeginInvoke(() => ChkAlt.IsChecked = true);
                return true;
            }

            string keyName = KeyHelper.VkToString((uint)vk);
            Dispatcher.BeginInvoke(() =>
            {
                TxtTriggerKey.Text = keyName;
                StopTriggerCapture();
            });
            return true;
        };
        _captureKeyboardHook.Install();

        _captureMouseHook = new LowLevelMouseHook();
        _captureMouseHook.OnMouseEvent += (btn, isDown) =>
        {
            if (!_isCapturingTrigger || !isDown) return false;

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
                TxtTriggerKey.Text = btnName;
                StopTriggerCapture();
            });
            return true;
        };
        _captureMouseHook.Install();
    }

    private void StopTriggerCapture()
    {
        _isCapturingTrigger = false;
        BtnCaptureKey.Content = "Capture";

        _captureKeyboardHook?.Dispose();
        _captureKeyboardHook = null;
        _captureMouseHook?.Dispose();
        _captureMouseHook = null;
    }

    #endregion

    private void CmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RepeatOptionsPanel != null)
        {
            RepeatOptionsPanel.Visibility = CmbMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ChkRequiresArming_Checked(object sender, RoutedEventArgs e)
    {
        if (ArmingConfigPanel != null)
        {
            ArmingConfigPanel.Visibility = ChkRequiresArming.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #region Action Steps Controls

    private void AddKeyTap_Click(object sender, RoutedEventArgs e)
    {
        var action = new MacroAction { Type = ActionType.KeyTap, Key = "j", DelayMs = 25 };
        Actions.Add(action);
        ActionsList.SelectedItem = action;
    }

    private void AddMouseClick_Click(object sender, RoutedEventArgs e)
    {
        var action = new MacroAction { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left, DelayMs = 20 };
        Actions.Add(action);
        ActionsList.SelectedItem = action;
    }

    private void AddDelay_Click(object sender, RoutedEventArgs e)
    {
        var action = new MacroAction { Type = ActionType.Delay, DelayMs = 50 };
        Actions.Add(action);
        ActionsList.SelectedItem = action;
    }

    private void AddMouseMove_Click(object sender, RoutedEventArgs e)
    {
        var action = new MacroAction { Type = ActionType.MouseMoveRelative, MoveX = 0, MoveY = 26 };
        Actions.Add(action);
        ActionsList.SelectedItem = action;
    }

    private void StepMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroAction action })
        {
            int index = Actions.IndexOf(action);
            if (index > 0)
            {
                Actions.Move(index, index - 1);
                ActionsList.SelectedItem = action;
            }
        }
    }

    private void StepMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroAction action })
        {
            int index = Actions.IndexOf(action);
            if (index >= 0 && index < Actions.Count - 1)
            {
                Actions.Move(index, index + 1);
                ActionsList.SelectedItem = action;
            }
        }
    }

    private void StepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroAction action })
        {
            Actions.Remove(action);
            UpdateInspectorUI();
        }
    }

    #endregion

    #region Live Action Recorder

    private void BtnRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        RecordText.Text = "Stop";
        RecordIcon.IconKey = "pause";
        _mouseDownTimestamps.Clear();
        _keyDownTimestamps.Clear();
        _recordStopwatch = Stopwatch.StartNew();

        _recordKeyboardHook = new LowLevelKeyboardHook();
        _recordKeyboardHook.OnKeyDown += (vk, isExt) =>
        {
            if (!_isRecording) return false;
            if (!_keyDownTimestamps.ContainsKey(vk))
            {
                RecordElapsedDelay();
                _keyDownTimestamps[vk] = Stopwatch.GetTimestamp();
            }
            return false;
        };

        _recordKeyboardHook.OnKeyUp += (vk, isExt) =>
        {
            if (!_isRecording) return false;
            string key = KeyHelper.VkToString((uint)vk);
            int holdMs = 25;
            if (_keyDownTimestamps.TryGetValue(vk, out long downTs))
            {
                holdMs = (int)Stopwatch.GetElapsedTime(downTs).TotalMilliseconds;
                _keyDownTimestamps.Remove(vk);
            }
            holdMs = Math.Clamp(holdMs, 10, 5000);

            Dispatcher.BeginInvoke(() =>
            {
                var action = new MacroAction
                {
                    Type = ActionType.KeyTap,
                    Key = key,
                    DelayMs = holdMs
                };
                Actions.Add(action);
                ActionsList.SelectedItem = action;
            });

            _recordStopwatch?.Restart();
            return false;
        };

        _recordKeyboardHook.Install();

        _recordMouseHook = new LowLevelMouseHook();
        _recordMouseHook.OnMouseEvent += (btn, isDown) =>
        {
            if (!_isRecording) return false;

            // Ignore initial click that started recording
            if (_recordStopwatch != null && _recordStopwatch.ElapsedMilliseconds < 150)
            {
                return false;
            }

            if (isDown)
            {
                if (!_mouseDownTimestamps.ContainsKey(btn))
                {
                    RecordElapsedDelay();
                    _mouseDownTimestamps[btn] = Stopwatch.GetTimestamp();
                }
            }
            else
            {
                int holdMs = 20;
                if (_mouseDownTimestamps.TryGetValue(btn, out long downTs))
                {
                    holdMs = (int)Stopwatch.GetElapsedTime(downTs).TotalMilliseconds;
                    _mouseDownTimestamps.Remove(btn);
                }
                holdMs = Math.Clamp(holdMs, 10, 5000);

                Dispatcher.BeginInvoke(() =>
                {
                    var action = new MacroAction
                    {
                        Type = ActionType.MouseClick,
                        MouseButton = btn,
                        DelayMs = holdMs
                    };
                    Actions.Add(action);
                    ActionsList.SelectedItem = action;
                });

                _recordStopwatch?.Restart();
            }

            return false;
        };
        _recordMouseHook.Install();
    }

    private void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        RecordText.Text = "Record";
        RecordIcon.IconKey = "zap";
        _recordKeyboardHook?.Dispose();
        _recordKeyboardHook = null;
        _recordMouseHook?.Dispose();
        _recordMouseHook = null;

        // Flush any keys or mouse buttons that were still held down
        foreach (var kvp in _keyDownTimestamps)
        {
            string key = KeyHelper.VkToString((uint)kvp.Key);
            int holdMs = Math.Clamp((int)Stopwatch.GetElapsedTime(kvp.Value).TotalMilliseconds, 10, 5000);
            var action = new MacroAction
            {
                Type = ActionType.KeyTap,
                Key = key,
                DelayMs = holdMs
            };
            Actions.Add(action);
        }
        _keyDownTimestamps.Clear();

        foreach (var kvp in _mouseDownTimestamps)
        {
            int holdMs = Math.Clamp((int)Stopwatch.GetElapsedTime(kvp.Value).TotalMilliseconds, 10, 5000);
            var action = new MacroAction
            {
                Type = ActionType.MouseClick,
                MouseButton = kvp.Key,
                DelayMs = holdMs
            };
            Actions.Add(action);
        }
        _mouseDownTimestamps.Clear();

        _recordStopwatch?.Stop();
    }

    private void RecordElapsedDelay()
    {
        if (_recordStopwatch != null)
        {
            int elapsed = (int)_recordStopwatch.ElapsedMilliseconds;
            if (elapsed >= 10)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var action = new MacroAction { Type = ActionType.Delay, DelayMs = elapsed };
                    Actions.Add(action);
                    ActionsList.SelectedItem = action;
                });
            }
            _recordStopwatch.Restart();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopRecording();
        StopTriggerCapture();
        StopActionKeyCapture();
    }

    #endregion

    private static MacroDefinition CloneMacro(MacroDefinition src)
    {
        var clone = new MacroDefinition
        {
            Id = src.Id,
            Name = src.Name,
            Description = src.Description,
            Category = src.Category,
            IsEnabled = src.IsEnabled,
            Mode = src.Mode,
            RepeatCount = src.RepeatCount,
            LoopDelayMs = src.LoopDelayMs,
            RequiresArming = src.RequiresArming,
            ArmingKey = src.ArmingKey,
            ArmingGroupName = src.ArmingGroupName,
            ArmingDisplayName = src.ArmingDisplayName,
            IsCurrentlyArmed = src.IsCurrentlyArmed,
            ProcessFilter = src.ProcessFilter,
            ImportedFromAhk = src.ImportedFromAhk,
            OriginalAhkScript = src.OriginalAhkScript
        };

        clone.Trigger = new MacroTrigger
        {
            PrimaryKey = src.Trigger.PrimaryKey,
            ChordKeys = new List<string>(src.Trigger.ChordKeys),
            RequireCtrl = src.Trigger.RequireCtrl,
            RequireShift = src.Trigger.RequireShift,
            RequireAlt = src.Trigger.RequireAlt,
            RequireWin = src.Trigger.RequireWin,
            Type = src.Trigger.Type
        };

        clone.Actions = new ObservableCollection<MacroAction>(src.Actions.Select(a => new MacroAction
        {
            Type = a.Type,
            Key = a.Key,
            VirtualKeyCode = a.VirtualKeyCode,
            ScanCode = a.ScanCode,
            MouseButton = a.MouseButton,
            MouseDelta = a.MouseDelta,
            MoveX = a.MoveX,
            MoveY = a.MoveY,
            DelayMs = a.DelayMs,
            RandomJitterMs = a.RandomJitterMs,
            TextContent = a.TextContent,
            MediaCommand = a.MediaCommand
        }));

        return clone;
    }
}
