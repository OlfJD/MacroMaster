using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;
using Brush = System.Windows.Media.Brush;

namespace MacroMaster.UI.Windows;

/// <summary>
/// Create / edit a macro in three numbered parts: 1 Hotkey, 2 Steps, 3 How often
/// (+ collapsed "More options"). Steps are edited inline in their rows.
/// </summary>
public partial class MacroEditorDialog : Window
{
    public MacroDefinition Macro { get; private set; }
    public ObservableCollection<MacroAction> Actions { get; } = new();

    /// <summary>Every other macro (with its profile), for hotkey-conflict warnings.</summary>
    private readonly List<(MacroDefinition Macro, MacroProfile Profile)> _others;

    // Hotkey being edited (written to Macro.Trigger on save)
    private string _primaryKey = string.Empty;
    private List<string> _chordKeys = new();
    private bool _ctrl, _shift, _alt, _win;

    // Hotkey capture
    private bool _capturingHotkey;
    private LowLevelKeyboardHook? _hkKeyboard;
    private LowLevelMouseHook? _hkMouse;
    private readonly List<int> _hkHeld = new();     // keys currently down, in press order
    private readonly List<int> _hkPeak = new();     // every key that was down during this combination
    private int _hkLastNonModifier;

    // Step key capture
    private MacroAction? _stepCapture;
    private LowLevelKeyboardHook? _stepHook;

    // Recording
    private bool _isRecording;
    private Stopwatch? _recordStopwatch;
    private LowLevelKeyboardHook? _recordKeyboardHook;
    private LowLevelMouseHook? _recordMouseHook;
    private readonly Dictionary<MouseButtonType, long> _mouseDownTimestamps = new();
    private readonly Dictionary<int, long> _keyDownTimestamps = new();

    public MacroEditorDialog(MacroDefinition? existingMacro = null,
                             IEnumerable<(MacroDefinition Macro, MacroProfile Profile)>? allMacros = null)
    {
        InitializeComponent();
        WindowHelper.Attach(this, RootGrid);

        Macro = existingMacro != null ? CloneMacro(existingMacro) : new MacroDefinition { Name = string.Empty, Category = string.Empty };
        _others = (allMacros ?? Enumerable.Empty<(MacroDefinition, MacroProfile)>())
            .Where(x => x.Macro.Id != Macro.Id).ToList();

        TxtCaption.Text = existingMacro != null ? "Edit macro" : "New macro";

        foreach (var a in Macro.Actions) Actions.Add(a);
        ActionsList.ItemsSource = Actions;
        foreach (var a in Actions) a.PropertyChanged += Action_PropertyChanged;
        Actions.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (MacroAction a in e.NewItems) a.PropertyChanged += Action_PropertyChanged;
            if (e.OldItems != null) foreach (MacroAction a in e.OldItems) a.PropertyChanged -= Action_PropertyChanged;
            UpdateSummary();
        };

        LoadMacroData();
        UpdateSummary();

        TxtName.TextChanged += (_, _) => UpdateFooter();

        // Stop listening for keys when the user switches away mid-capture
        Deactivated += (_, _) =>
        {
            StopHotkeyCapture();
            StopStepKeyCapture();
        };

        if (existingMacro == null)
        {
            Loaded += (_, _) => TxtName.Focus();
        }
    }

    #region Load / save

    private void LoadMacroData()
    {
        TxtName.Text = Macro.Name;
        TxtDescription.Text = Macro.Description;
        TxtCategory.Text = Macro.Category == "General" ? string.Empty : Macro.Category;
        TxtProcess.Text = Macro.ProcessFilter is "*" or "" ? string.Empty : Macro.ProcessFilter;

        _primaryKey = Macro.Trigger.PrimaryKey;
        _chordKeys = new List<string>(Macro.Trigger.ChordKeys);
        _ctrl = Macro.Trigger.RequireCtrl;
        _shift = Macro.Trigger.RequireShift;
        _alt = Macro.Trigger.RequireAlt;
        _win = Macro.Trigger.RequireWin;

        RadioOnce.IsChecked = Macro.Mode is ExecutionMode.Once or ExecutionMode.HoldWhileDown;
        RadioRepeat.IsChecked = Macro.Mode == ExecutionMode.RepeatCount;
        RadioLoop.IsChecked = Macro.Mode == ExecutionMode.Toggle;
        TxtRepeatCount.Text = Math.Max(2, Macro.RepeatCount).ToString();

        ChkRequiresArming.IsChecked = Macro.RequiresArming;
        TxtArmingKey.Text = Macro.ArmingKey;
        TxtArmingName.Text = Macro.ArmingDisplayName;
        TxtArmingGroup.Text = Macro.ArmingGroupName;
        ArmingConfigPanel.Visibility = Macro.RequiresArming ? Visibility.Visible : Visibility.Collapsed;

        // Open "More options" straight away if this macro actually uses any of them
        MoreToggle.IsChecked = Macro.RequiresArming || !string.IsNullOrWhiteSpace(Macro.Description) ||
                               !string.IsNullOrWhiteSpace(TxtProcess.Text) || !string.IsNullOrWhiteSpace(TxtCategory.Text);

        ShowHotkey();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        StopEverything();
        if (Actions.Count == 0) return; // button is disabled in this state anyway

        Macro.Name = !string.IsNullOrWhiteSpace(TxtName.Text) ? TxtName.Text.Trim()
                   : !string.IsNullOrEmpty(_primaryKey) ? $"{BuildTrigger().DisplayString} macro"
                   : "Untitled macro";
        Macro.Description = TxtDescription.Text.Trim();
        Macro.Category = string.IsNullOrWhiteSpace(TxtCategory.Text) ? "General" : TxtCategory.Text.Trim();
        Macro.ProcessFilter = string.IsNullOrWhiteSpace(TxtProcess.Text) ? "*" : TxtProcess.Text.Trim();

        Macro.Trigger = BuildTrigger();

        Macro.Mode = RadioLoop.IsChecked == true ? ExecutionMode.Toggle
                   : RadioRepeat.IsChecked == true ? ExecutionMode.RepeatCount
                   : ExecutionMode.Once;
        if (int.TryParse(TxtRepeatCount.Text, out int rep)) Macro.RepeatCount = Math.Clamp(rep, 1, 10000);

        Macro.RequiresArming = ChkRequiresArming.IsChecked == true;
        Macro.ArmingKey = TxtArmingKey.Text.Trim();
        Macro.ArmingDisplayName = TxtArmingName.Text.Trim();
        Macro.ArmingGroupName = TxtArmingGroup.Text.Trim();

        Macro.Actions = new ObservableCollection<MacroAction>(Actions);

        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        StopEverything();
        DialogResult = false;
    }

    private void StopEverything()
    {
        StopRecording();
        StopHotkeyCapture();
        StopStepKeyCapture();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopEverything();
    }

    #endregion

    #region 1 · Hotkey

    private MacroTrigger BuildTrigger() => new()
    {
        PrimaryKey = _primaryKey,
        ChordKeys = new List<string>(_chordKeys),
        RequireCtrl = _ctrl,
        RequireShift = _shift,
        RequireAlt = _alt,
        RequireWin = _win,
        Type = Macro.Trigger.Type
    };

    private void ShowHotkey(IEnumerable<string>? liveKeys = null)
    {
        var caps = liveKeys?.ToList() ??
                   (string.IsNullOrEmpty(_primaryKey) ? new List<string>() : BuildTrigger().DisplayString.Split(" + ").ToList());

        HotkeyCaps.ItemsSource = caps;
        bool empty = caps.Count == 0;
        HotkeyPrompt.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        HotkeyPrompt.Text = _capturingHotkey ? "Press your hotkey now…  (Esc to cancel)" : "Click here, then press your hotkey";
        HotkeyPrompt.Foreground = Res(_capturingHotkey ? "AccentTextBrush" : "TextMutedBrush");
        HotkeyBox.BorderBrush = Res(_capturingHotkey ? "AccentBrush" : "BorderStrongBrush");
        BtnClearHotkey.Visibility = !_capturingHotkey && !string.IsNullOrEmpty(_primaryKey) ? Visibility.Visible : Visibility.Collapsed;

        UpdateConflict();
        UpdateFooter();
    }

    private void HotkeyBox_Click(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey) { StopHotkeyCapture(); return; }
        StopStepKeyCapture();
        StartHotkeyCapture();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        _primaryKey = string.Empty;
        _chordKeys.Clear();
        _ctrl = _shift = _alt = _win = false;
        ShowHotkey();
    }

    private static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;

    private static string KeyName(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => "Shift",
        0x11 or 0xA2 or 0xA3 => "Ctrl",
        0x12 or 0xA4 or 0xA5 => "Alt",
        0x5B or 0x5C => "Win",
        _ => KeyHelper.VkToString((uint)vk)
    };

    private void StartHotkeyCapture()
    {
        _capturingHotkey = true;
        _hkHeld.Clear();
        _hkPeak.Clear();
        _hkLastNonModifier = 0;
        ShowHotkey(Enumerable.Empty<string>());

        // Low-level hooks so every key works (F-keys, Alt combos, Ä/Ö...) and nothing reaches other apps
        _hkKeyboard = new LowLevelKeyboardHook();
        _hkKeyboard.OnKeyDown += (vk, _) =>
        {
            if (!_capturingHotkey) return false;
            if (vk == 0x1B && _hkHeld.Count == 0)
            {
                Dispatcher.BeginInvoke(StopHotkeyCapture);
                return true;
            }
            if (!_hkHeld.Contains(vk)) _hkHeld.Add(vk);
            if (!_hkPeak.Contains(vk)) _hkPeak.Add(vk);
            if (!IsModifier(vk)) _hkLastNonModifier = vk;

            var live = _hkHeld.Select(KeyName).Distinct().ToList();
            Dispatcher.BeginInvoke(() => ShowHotkey(live));
            return true;
        };
        _hkKeyboard.OnKeyUp += (vk, _) =>
        {
            if (!_capturingHotkey) return false;
            _hkHeld.Remove(vk);
            if (_hkPeak.Count > 0)
            {
                // The combination is complete as soon as the first key is let go
                var peak = _hkPeak.ToList();
                int primary = _hkLastNonModifier != 0 ? _hkLastNonModifier : vk;
                Dispatcher.BeginInvoke(() => FinishHotkeyCapture(primary, peak, null));
            }
            return true;
        };
        _hkKeyboard.Install();

        _hkMouse = new LowLevelMouseHook();
        _hkMouse.OnMouseEvent += (btn, isDown) =>
        {
            if (!_capturingHotkey || !isDown) return false;
            // A plain left click means "never mind" (binding left click would hijack every click)
            if (btn == MouseButtonType.Left)
            {
                Dispatcher.BeginInvoke(StopHotkeyCapture);
                return false;
            }
            var held = _hkHeld.ToList();
            Dispatcher.BeginInvoke(() => FinishHotkeyCapture(0, held, btn));
            return true;
        };
        _hkMouse.Install();
    }

    private void FinishHotkeyCapture(int primaryVk, List<int> keys, MouseButtonType? mouse)
    {
        if (!_capturingHotkey) return;

        _ctrl = keys.Any(k => k is 0x11 or 0xA2 or 0xA3);
        _shift = keys.Any(k => k is 0x10 or 0xA0 or 0xA1);
        _alt = keys.Any(k => k is 0x12 or 0xA4 or 0xA5);
        _win = keys.Any(k => k is 0x5B or 0x5C);

        if (mouse.HasValue)
        {
            _primaryKey = mouse.Value switch
            {
                MouseButtonType.Right => "Right Click",
                MouseButtonType.Middle => "Middle Click",
                MouseButtonType.XButton1 => "Mouse 4",
                MouseButtonType.XButton2 => "Mouse 5",
                _ => "Left Click"
            };
            _chordKeys = keys.Where(k => !IsModifier(k)).Select(KeyName).Distinct().ToList();
        }
        else
        {
            _primaryKey = KeyName(primaryVk);
            // A lone modifier as the hotkey isn't also a "required modifier"
            if (IsModifier(primaryVk))
            {
                if (_primaryKey == "Ctrl") _ctrl = false;
                if (_primaryKey == "Shift") _shift = false;
                if (_primaryKey == "Alt") _alt = false;
                if (_primaryKey == "Win") _win = false;
            }
            _chordKeys = keys.Where(k => k != primaryVk && !IsModifier(k)).Select(KeyName).Distinct().ToList();
        }

        StopHotkeyCapture();
    }

    private void StopHotkeyCapture()
    {
        if (_hkKeyboard == null && _hkMouse == null && !_capturingHotkey) return;
        _capturingHotkey = false;
        _hkKeyboard?.Dispose();
        _hkKeyboard = null;
        _hkMouse?.Dispose();
        _hkMouse = null;
        ShowHotkey();
    }

    /// <summary>Same hotkey as another enabled macro? (Mode-switching macros may share on purpose.)</summary>
    private void UpdateConflict()
    {
        if (string.IsNullOrEmpty(_primaryKey) || _capturingHotkey)
        {
            ConflictBox.Visibility = Visibility.Collapsed;
            return;
        }

        string mine = Signature(BuildTrigger());
        bool iArm = ChkRequiresArming.IsChecked == true;
        var clash = _others.FirstOrDefault(o =>
            o.Macro.IsEnabled && Signature(o.Macro.Trigger) == mine && !(iArm && o.Macro.RequiresArming));

        if (clash.Macro == null)
        {
            ConflictBox.Visibility = Visibility.Collapsed;
            return;
        }

        ConflictText.Text = $"“{clash.Macro.Name}” in {clash.Profile.Name}{(clash.Profile.IsOn ? "" : " (off)")} already uses this hotkey. " +
                            "If both are on, only one of them will run.";
        ConflictBox.Visibility = Visibility.Visible;
    }

    private static string Signature(MacroTrigger t)
    {
        int primary = KeyHelper.NormalizeToVk(t.PrimaryKey);
        if (primary == 0) return string.Empty;
        var chords = t.ChordKeys.Select(KeyHelper.NormalizeToVk).Where(v => v != 0).OrderBy(v => v);
        return $"{primary}|{t.RequireCtrl}{t.RequireShift}{t.RequireAlt}{t.RequireWin}|{string.Join(",", chords)}";
    }

    #endregion

    #region 2 · Steps

    private void Action_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MacroAction.DelayMs) or nameof(MacroAction.Type) or nameof(MacroAction.Key))
            UpdateSummary();

        // Switching a mouse step to "Scroll" with no amount yet: start at one notch
        if (e.PropertyName == nameof(MacroAction.Type) && sender is MacroAction { Type: ActionType.MouseScroll, MouseDelta: 0 } a)
            a.MouseDelta = 120;
    }

    private void UpdateSummary()
    {
        int count = Actions.Count;
        StepsEmptyState.Visibility = count == 0 && !_isRecording ? Visibility.Visible : Visibility.Collapsed;

        if (count == 0)
        {
            TxtTimelineSummary.Text = "What it does, top to bottom.";
        }
        else
        {
            long ms = Actions.Where(a => a.Type is ActionType.Delay or ActionType.KeyTap or ActionType.MouseClick).Sum(a => (long)a.DelayMs);
            string duration = ms >= 1000 ? $"{ms / 1000.0:0.##} s" : $"{ms} ms";
            TxtTimelineSummary.Text = $"{count} step{(count == 1 ? "" : "s")} · takes about {duration}";
        }
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        string hint;
        bool warn = false;
        if (Actions.Count == 0)
        {
            hint = "Add at least one step to save.";
        }
        else if (Actions.Any(a => a.Type is ActionType.KeyTap or ActionType.KeyDown or ActionType.KeyUp && string.IsNullOrWhiteSpace(a.Key)))
        {
            hint = "A key step has no key yet — click its “Set key” button.";
            warn = true;
        }
        else if (string.IsNullOrEmpty(_primaryKey))
        {
            hint = "No hotkey yet — you can still start it with the Run button.";
        }
        else
        {
            hint = string.Empty;
        }

        TxtFooterHint.Text = hint;
        TxtFooterHint.Foreground = Res(warn ? "WarningBrush" : "TextMutedBrush");
        FooterIcon.Foreground = TxtFooterHint.Foreground;
        FooterIcon.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        BtnSave.IsEnabled = Actions.Count > 0 && !warn;
    }

    private void AddStep(MacroAction action)
    {
        Actions.Add(action);
        ActionsList.UpdateLayout();
        if (ActionsList.ItemContainerGenerator.ContainerFromItem(action) is FrameworkElement row)
            row.BringIntoView();
    }

    private void AddKeyTap_Click(object sender, RoutedEventArgs e)
    {
        var action = new MacroAction { Type = ActionType.KeyTap, Key = string.Empty, DelayMs = 25 };
        AddStep(action);
        StartStepKeyCapture(action); // ask for the key right away instead of inventing one
    }

    private void AddMouseClick_Click(object sender, RoutedEventArgs e)
        => AddStep(new MacroAction { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left, DelayMs = 20 });

    private void AddDelay_Click(object sender, RoutedEventArgs e)
        => AddStep(new MacroAction { Type = ActionType.Delay, DelayMs = 50 });

    private void AddMouseMove_Click(object sender, RoutedEventArgs e)
        => AddStep(new MacroAction { Type = ActionType.MouseMoveRelative, MoveX = 0, MoveY = 10 });

    // Starters
    private void TemplateAutoClicker_Click(object sender, RoutedEventArgs e)
    {
        Actions.Add(new MacroAction { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left, DelayMs = 15 });
        Actions.Add(new MacroAction { Type = ActionType.Delay, DelayMs = 50 });
        RadioLoop.IsChecked = true;
        if (string.IsNullOrWhiteSpace(TxtName.Text)) TxtName.Text = "Auto clicker";
    }

    private void TemplateKeySpam_Click(object sender, RoutedEventArgs e)
    {
        var key = new MacroAction { Type = ActionType.KeyTap, Key = string.Empty, DelayMs = 25 };
        Actions.Add(key);
        Actions.Add(new MacroAction { Type = ActionType.Delay, DelayMs = 50 });
        RadioLoop.IsChecked = true;
        if (string.IsNullOrWhiteSpace(TxtName.Text)) TxtName.Text = "Key spam";
        StartStepKeyCapture(key);
    }

    private static MacroAction? StepOf(object sender) => (sender as FrameworkElement)?.DataContext as MacroAction;

    private void StepKey_Click(object sender, RoutedEventArgs e)
    {
        if (StepOf(sender) is not { } action) return;
        if (_stepCapture == action) { StopStepKeyCapture(); return; }
        StartStepKeyCapture(action);
    }

    private void StartStepKeyCapture(MacroAction action)
    {
        StopStepKeyCapture();
        StopHotkeyCapture();
        _stepCapture = action;
        action.IsCapturingKey = true;

        _stepHook = new LowLevelKeyboardHook();
        _stepHook.OnKeyDown += (vk, _) =>
        {
            if (_stepCapture == null) return false;
            string name = KeyName(vk);
            Dispatcher.BeginInvoke(() =>
            {
                if (_stepCapture != null) _stepCapture.Key = name;
                StopStepKeyCapture();
            });
            return true;
        };
        _stepHook.OnKeyUp += (_, _) => _stepCapture != null;
        _stepHook.Install();
    }

    private void StopStepKeyCapture()
    {
        if (_stepCapture != null) _stepCapture.IsCapturingKey = false;
        _stepCapture = null;
        _stepHook?.Dispose();
        _stepHook = null;
    }

    private void RenumberSteps() => ActionsList.Items.Refresh(); // AlternationIndex doesn't follow Move()

    private void StepMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (StepOf(sender) is not { } a) return;
        int i = Actions.IndexOf(a);
        if (i > 0) { Actions.Move(i, i - 1); RenumberSteps(); }
    }

    private void StepMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (StepOf(sender) is not { } a) return;
        int i = Actions.IndexOf(a);
        if (i >= 0 && i < Actions.Count - 1) { Actions.Move(i, i + 1); RenumberSteps(); }
    }

    private void StepDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (StepOf(sender) is not { } a) return;
        Actions.Insert(Actions.IndexOf(a) + 1, CloneAction(a));
        RenumberSteps();
    }

    private void StepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (StepOf(sender) is not { } a) return;
        if (_stepCapture == a) StopStepKeyCapture();
        Actions.Remove(a);
        RenumberSteps();
    }

    #endregion

    #region 3 · How often / more options

    private void TxtRepeatCount_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => RadioRepeat.IsChecked = true;

    private void MoreToggle_Changed(object sender, RoutedEventArgs e)
        => MorePanel.Visibility = MoreToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void ChkRequiresArming_Checked(object sender, RoutedEventArgs e)
    {
        ArmingConfigPanel.Visibility = ChkRequiresArming.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) UpdateConflict();
    }

    #endregion

    #region Recording

    private void BtnRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording) StartRecording();
        else StopRecording();
    }

    private void StartRecording()
    {
        StopHotkeyCapture();
        StopStepKeyCapture();

        _isRecording = true;
        RecordText.Text = "Stop recording";
        RecordIcon.IconKey = "square";
        BtnRecord.Background = Res("DangerSubtleBrush");
        BtnRecord.BorderBrush = Res("DangerBrush");
        RecordingBanner.Visibility = Visibility.Visible;
        UpdateSummary();

        _mouseDownTimestamps.Clear();
        _keyDownTimestamps.Clear();
        _recordStopwatch = Stopwatch.StartNew();

        _recordKeyboardHook = new LowLevelKeyboardHook();
        _recordKeyboardHook.OnKeyDown += (vk, _) =>
        {
            if (!_isRecording) return false;
            if (!_keyDownTimestamps.ContainsKey(vk))
            {
                RecordElapsedDelay();
                _keyDownTimestamps[vk] = Stopwatch.GetTimestamp();
            }
            return false;
        };
        _recordKeyboardHook.OnKeyUp += (vk, _) =>
        {
            if (!_isRecording) return false;
            string key = KeyName(vk);
            int holdMs = 25;
            if (_keyDownTimestamps.TryGetValue(vk, out long downTs))
            {
                holdMs = (int)Stopwatch.GetElapsedTime(downTs).TotalMilliseconds;
                _keyDownTimestamps.Remove(vk);
            }
            holdMs = Math.Clamp(holdMs, 10, 5000);
            Dispatcher.BeginInvoke(() => Actions.Add(new MacroAction { Type = ActionType.KeyTap, Key = key, DelayMs = holdMs }));
            _recordStopwatch?.Restart();
            return false;
        };
        _recordKeyboardHook.Install();

        _recordMouseHook = new LowLevelMouseHook();
        _recordMouseHook.OnMouseEvent += (btn, isDown) =>
        {
            if (!_isRecording) return false;
            // Clicks on this window (e.g. "Stop recording") are not part of the macro
            if (IsCursorOverThisWindow()) return false;

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
                Dispatcher.BeginInvoke(() => Actions.Add(new MacroAction { Type = ActionType.MouseClick, MouseButton = btn, DelayMs = holdMs }));
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
        RecordIcon.IconKey = "circle-dot";
        BtnRecord.ClearValue(BackgroundProperty);
        BtnRecord.ClearValue(BorderBrushProperty);
        RecordingBanner.Visibility = Visibility.Collapsed;

        _recordKeyboardHook?.Dispose();
        _recordKeyboardHook = null;
        _recordMouseHook?.Dispose();
        _recordMouseHook = null;

        // Flush keys or buttons that were still held down
        foreach (var kvp in _keyDownTimestamps)
        {
            int holdMs = Math.Clamp((int)Stopwatch.GetElapsedTime(kvp.Value).TotalMilliseconds, 10, 5000);
            Actions.Add(new MacroAction { Type = ActionType.KeyTap, Key = KeyName(kvp.Key), DelayMs = holdMs });
        }
        _keyDownTimestamps.Clear();

        foreach (var kvp in _mouseDownTimestamps)
        {
            int holdMs = Math.Clamp((int)Stopwatch.GetElapsedTime(kvp.Value).TotalMilliseconds, 10, 5000);
            Actions.Add(new MacroAction { Type = ActionType.MouseClick, MouseButton = kvp.Key, DelayMs = holdMs });
        }
        _mouseDownTimestamps.Clear();

        _recordStopwatch?.Stop();
        UpdateSummary();
    }

    private void RecordElapsedDelay()
    {
        if (_recordStopwatch == null) return;
        int elapsed = (int)_recordStopwatch.ElapsedMilliseconds;
        // The gap before the very first input is just "time to switch to the game", not part of the macro
        bool first = Actions.Count == 0 && _keyDownTimestamps.Count == 0 && _mouseDownTimestamps.Count == 0;
        if (elapsed >= 10 && !first)
        {
            Dispatcher.BeginInvoke(() => Actions.Add(new MacroAction { Type = ActionType.Delay, DelayMs = elapsed }));
        }
        _recordStopwatch.Restart();
    }

    private bool IsCursorOverThisWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return GetCursorPos(out POINT p) && GetWindowRect(hwnd, out RECT r) &&
               p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    #endregion

    private Brush Res(string key) => (Brush)FindResource(key);

    private static MacroAction CloneAction(MacroAction a) => new()
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
    };

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
            OriginalAhkScript = src.OriginalAhkScript,
            Trigger = new MacroTrigger
            {
                PrimaryKey = src.Trigger.PrimaryKey,
                ChordKeys = new List<string>(src.Trigger.ChordKeys),
                RequireCtrl = src.Trigger.RequireCtrl,
                RequireShift = src.Trigger.RequireShift,
                RequireAlt = src.Trigger.RequireAlt,
                RequireWin = src.Trigger.RequireWin,
                Type = src.Trigger.Type
            }
        };
        clone.Actions = new ObservableCollection<MacroAction>(src.Actions.Select(CloneAction));
        return clone;
    }
}
