using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;

namespace MacroMaster.Core.Engine;

public class MacroEngine : IDisposable
{
    private readonly LowLevelKeyboardHook _keyboardHook = new();
    private readonly LowLevelMouseHook _mouseHook = new();
    private readonly InputSimulator _simulator = new();

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
    private readonly object _stateLock = new();

    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<MacroProfile> Profiles { get; set; } = new();
    public MacroProfile? ActiveProfile => Profiles.FirstOrDefault(p => p.IsActive) ?? Profiles.FirstOrDefault();

    // Events for UI & HUD
    public event Action<string, string, string, string>? OnHudNotification; // title, subtitle, iconKey, accentColor
    public event Action? OnStateRefreshed;

    public InputSimulator Simulator => _simulator;

    public void Initialize(AppSettings settings, List<MacroProfile> profiles)
    {
        Settings = settings;
        Profiles = new ObservableCollection<MacroProfile>(profiles);

        _keyboardHook.OnKeyDown += HandleKeyDown;
        _keyboardHook.OnKeyUp += HandleKeyUp;
        _mouseHook.OnMouseEvent += HandleMouseEvent;

        _keyboardHook.Install();
        _mouseHook.Install();
    }

    public void SetMasterEngineEnabled(bool enabled)
    {
        Settings.MasterEngineEnabled = enabled;
        if (!enabled)
        {
            StopAllRunningMacros();
            _simulator.ReleaseAllInputs();
            TriggerHud("ENGINE PAUSED", "All macros and triggers disabled", "pause", "#F87171");
        }
        else
        {
            TriggerHud("ENGINE ACTIVE", "Macros ready to trigger", "zap", "#34D399");
        }
        OnStateRefreshed?.Invoke();
    }

    #region Quick Actions

    public void ToggleHoldLeftClick(bool? explicitState = null)
    {
        lock (_stateLock)
        {
            bool newState = explicitState ?? !Settings.HoldLeftClickActive;
            Settings.HoldLeftClickActive = newState;

            if (newState)
            {
                _simulator.MouseDown(MouseButtonType.Left);
                TriggerHud("LEFT CLICK: HELD", "Mouse button locked down", "mouse", "#34D399");
            }
            else
            {
                _simulator.MouseUp(MouseButtonType.Left);
                TriggerHud("LEFT CLICK: RELEASED", "Mouse button unlocked", "mouse", "#94A3B8");
            }
        }
        OnStateRefreshed?.Invoke();
    }

    public void ToggleAutoClicker(bool? explicitState = null)
    {
        lock (_stateLock)
        {
            bool newState = explicitState ?? !Settings.AutoClickerActive;
            Settings.AutoClickerActive = newState;

            if (newState)
            {
                StartAutoClickerLoop();
                TriggerHud("AUTO CLICKER: ON", $"{Settings.AutoClickerCps} CPS ({Settings.AutoClickerButton})", "zap", "#38BDF8");
            }
            else
            {
                StopTask("QuickAction_AutoClicker");
                _simulator.MouseUp(Settings.AutoClickerButton);
                TriggerHud("AUTO CLICKER: OFF", "Spamming stopped", "zap", "#94A3B8");
            }
        }
        OnStateRefreshed?.Invoke();
    }

    private void StartAutoClickerLoop()
    {
        StopTask("QuickAction_AutoClicker");
        var cts = new CancellationTokenSource();
        _runningTasks["QuickAction_AutoClicker"] = cts;

        Task.Run(() =>
        {
            int intervalMs = Math.Max(1, 1000 / Math.Max(1, Settings.AutoClickerCps));
            while (!cts.Token.IsCancellationRequested)
            {
                _simulator.MouseClick(Settings.AutoClickerButton, Math.Min(10, intervalMs / 2));
                int waitMs = Math.Max(1, intervalMs - Math.Min(10, intervalMs / 2));
                InputSimulator.PreciseSleep(waitMs, cts.Token);
            }
        }, cts.Token);
    }

    public void ToggleMinecraftLoop(bool? explicitState = null)
    {
        lock (_stateLock)
        {
            bool newState = explicitState ?? !Settings.MinecraftLoopActive;
            Settings.MinecraftLoopActive = newState;

            if (newState)
            {
                StartMinecraftMiningLoop();
                TriggerHud("MINECRAFT: MINING", "Left Hold + Right Click Spam active", "crosshair", "#FBBF24");
            }
            else
            {
                StopTask("QuickAction_Minecraft");
                _simulator.MouseUp(MouseButtonType.Left);
                TriggerHud("MINECRAFT: STOPPED", "Mining loop stopped", "crosshair", "#94A3B8");
            }
        }
        OnStateRefreshed?.Invoke();
    }

    private void StartMinecraftMiningLoop()
    {
        StopTask("QuickAction_Minecraft");
        var cts = new CancellationTokenSource();
        _runningTasks["QuickAction_Minecraft"] = cts;

        Task.Run(() =>
        {
            _simulator.MouseDown(MouseButtonType.Left);
            while (!cts.Token.IsCancellationRequested)
            {
                _simulator.MouseClick(MouseButtonType.Right, 20);
                InputSimulator.PreciseSleep(100, cts.Token);
            }
            _simulator.MouseUp(MouseButtonType.Left);
        }, cts.Token);
    }

    #endregion

    #region Hook Handling

    private bool HandleKeyDown(int vkCode, bool isExtended)
    {
        // 1. Emergency Killswitch (default F12)
        int killswitchVk = KeyHelper.NormalizeToVk(Settings.EmergencyKillswitchKey);
        if (vkCode == killswitchVk)
        {
            EmergencyStop();
            return true;
        }

        if (!Settings.MasterEngineEnabled) return false;

        // 2. Check Quick Action Hotkeys
        if (Settings.HoldLeftClickHotkeyEnabled && vkCode == KeyHelper.NormalizeToVk(Settings.HoldLeftClickHotkey))
        {
            // Only trigger if no other modifier conflict
            ToggleHoldLeftClick();
            return true;
        }

        if (Settings.AutoClickerHotkeyEnabled && vkCode == KeyHelper.NormalizeToVk(Settings.AutoClickerHotkey))
        {
            ToggleAutoClicker();
            return true;
        }

        if (Settings.MinecraftLoopHotkeyEnabled && vkCode == KeyHelper.NormalizeToVk(Settings.MinecraftLoopHotkey))
        {
            ToggleMinecraftLoop();
            return true;
        }

        // 3. Check Mode Arming Keys (e.g. PgUp arms Wellskate, PgDn arms Groundskate)
        if (ActiveProfile != null)
        {
            foreach (var macro in ActiveProfile.Macros.Where(m => m.IsEnabled && m.RequiresArming && !string.IsNullOrWhiteSpace(m.ArmingKey)))
            {
                int armingVk = KeyHelper.NormalizeToVk(macro.ArmingKey);
                if (vkCode == armingVk)
                {
                    // Disarm other macros in the same arming group
                    foreach (var other in ActiveProfile.Macros.Where(m => m.RequiresArming && m.ArmingGroupName == macro.ArmingGroupName))
                    {
                        other.IsCurrentlyArmed = (other.Id == macro.Id);
                    }

                    TriggerHud($"ARMED: {macro.ArmingDisplayName}", $"Switched via {macro.ArmingKey.ToUpperInvariant()}", "rocket", "#34D399");
                    OnStateRefreshed?.Invoke();
                    return false; // Let key pass through if needed, or suppress
                }
            }
        }

        // 4. Check Macro Triggers
        if (ActiveProfile != null)
        {
            foreach (var macro in ActiveProfile.Macros.Where(m => m.IsEnabled))
            {
                // Cheap checks first: this runs inside the low-level hook for every keystroke
                if (!KeyMatches(vkCode, KeyHelper.NormalizeToVk(macro.Trigger.PrimaryKey))) continue;
                if (!TriggerConditionsMet(macro)) continue;

                // All trigger conditions met!
                ExecuteMacro(macro);
                return true; // Suppress trigger key
            }
        }

        return false;
    }

    private bool HandleKeyUp(int vkCode, bool isExtended)
    {
        return false;
    }

    /// <summary>Low-level hooks report left/right modifier codes; match them against generic ones.</summary>
    private static bool KeyMatches(int vkCode, int targetVk)
    {
        if (targetVk == 0) return false;
        if (vkCode == targetVk) return true;
        return targetVk switch
        {
            0x10 => vkCode is 0xA0 or 0xA1,
            0x11 => vkCode is 0xA2 or 0xA3,
            0x12 => vkCode is 0xA4 or 0xA5,
            0x5B => vkCode is 0x5C,
            _ => false
        };
    }

    /// <summary>Arming, modifiers, chord keys and process filter (checked last: it's the expensive one).</summary>
    private bool TriggerConditionsMet(MacroDefinition macro)
    {
        if (macro.RequiresArming && !macro.IsCurrentlyArmed) return false;

        if (macro.Trigger.RequireCtrl && !_keyboardHook.IsKeyDown(0x11)) return false;
        if (macro.Trigger.RequireShift && !_keyboardHook.IsKeyDown(0x10)) return false;
        if (macro.Trigger.RequireAlt && !_keyboardHook.IsKeyDown(0x12)) return false;
        if (macro.Trigger.RequireWin && !_keyboardHook.IsKeyDown(0x5B)) return false;

        // Chord keys (e.g. "ä", "ö")
        foreach (var chord in macro.Trigger.ChordKeys)
        {
            int chordVk = KeyHelper.NormalizeToVk(chord);
            if (chordVk != 0 && !_keyboardHook.IsKeyDown(chordVk)) return false;
        }

        return ProcessWatcher.IsProcessActive(macro.ProcessFilter);
    }

    private static int MouseButtonToVk(MouseButtonType button) => button switch
    {
        MouseButtonType.Left => 0x01,
        MouseButtonType.Right => 0x02,
        MouseButtonType.Middle => 0x04,
        MouseButtonType.XButton1 => 0x05,
        MouseButtonType.XButton2 => 0x06,
        _ => 0
    };

    private bool HandleMouseEvent(MouseButtonType button, bool isDown)
    {
        if (!isDown || !Settings.MasterEngineEnabled) return false;

        // Compare by virtual key: the editor stores mouse triggers as "Mouse 4" / "Left Click", and
        // comparing to the enum name ("Left") also matched the Left *arrow* key and hijacked clicks
        int buttonVk = MouseButtonToVk(button);

        if (ActiveProfile != null)
        {
            foreach (var macro in ActiveProfile.Macros.Where(m => m.IsEnabled))
            {
                if (KeyHelper.NormalizeToVk(macro.Trigger.PrimaryKey) != buttonVk) continue;
                if (!TriggerConditionsMet(macro)) continue;

                ExecuteMacro(macro);
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Macro Execution

    public void ExecuteMacro(MacroDefinition macro)
    {
        if (macro.Mode == ExecutionMode.Toggle)
        {
            if (_runningTasks.ContainsKey(macro.Id))
            {
                StopTask(macro.Id);
                macro.IsRunning = false;
                _simulator.ReleaseAllInputs();
                TriggerHud($"STOPPED: {macro.Name}", "Toggle deactivated", "pause", "#94A3B8");
                OnStateRefreshed?.Invoke();
                return;
            }

            macro.IsRunning = true;
            TriggerHud($"RUNNING: {macro.Name}", "Toggle active", "play", "#34D399");
            OnStateRefreshed?.Invoke();

            var cts = new CancellationTokenSource();
            _runningTasks[macro.Id] = cts;

            Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        ExecuteActionSequence(macro.Actions, cts.Token);
                        if (macro.LoopDelayMs > 0)
                        {
                            InputSimulator.PreciseSleep(macro.LoopDelayMs, cts.Token);
                        }
                    }
                }
                finally
                {
                    // Only remove our own registration: after a quick off->on toggle a newer run is
                    // already registered under the same id, and removing it would leave that loop
                    // untracked (and unstoppable, even by the killswitch).
                    _runningTasks.TryRemove(new KeyValuePair<string, CancellationTokenSource>(macro.Id, cts));
                    if (!_runningTasks.ContainsKey(macro.Id))
                    {
                        macro.IsRunning = false;
                    }
                    OnStateRefreshed?.Invoke();
                }
            }, cts.Token);
        }
        else
        {
            // Execute Once or Repeat N Times
            TriggerHud($"TRIGGERED: {macro.Name}", macro.Category, "zap", "#A78BFA");

            // Registered under a unique key so EmergencyStop can cancel long repeat runs too
            string runKey = $"{macro.Id}#run#{Guid.NewGuid():N}";
            var cts = new CancellationTokenSource();
            _runningTasks[runKey] = cts;

            Task.Run(() =>
            {
                try
                {
                    // RepeatCount is kept when switching mode back to Once, so only honour it in RepeatCount mode
                    int times = macro.Mode == ExecutionMode.RepeatCount ? Math.Max(1, macro.RepeatCount) : 1;
                    for (int i = 0; i < times && !cts.Token.IsCancellationRequested; i++)
                    {
                        ExecuteActionSequence(macro.Actions, cts.Token);
                        if (i < times - 1 && macro.LoopDelayMs > 0)
                        {
                            InputSimulator.PreciseSleep(macro.LoopDelayMs, cts.Token);
                        }
                    }
                }
                finally
                {
                    _runningTasks.TryRemove(runKey, out _);
                }
            });
        }
    }

    private void ExecuteActionSequence(ObservableCollection<MacroAction> actions, CancellationToken token)
    {
        foreach (var action in actions)
        {
            if (token.IsCancellationRequested) break;

            switch (action.Type)
            {
                case ActionType.KeyDown:
                    _simulator.KeyDown(action.Key);
                    break;
                case ActionType.KeyUp:
                    _simulator.KeyUp(action.Key);
                    break;
                case ActionType.KeyTap:
                    _simulator.KeyTap(action.Key, Math.Max(1, action.DelayMs));
                    break;
                case ActionType.MouseDown:
                    _simulator.MouseDown(action.MouseButton);
                    break;
                case ActionType.MouseUp:
                    _simulator.MouseUp(action.MouseButton);
                    break;
                case ActionType.MouseClick:
                    _simulator.MouseClick(action.MouseButton, Math.Max(1, action.DelayMs));
                    break;
                case ActionType.MouseMoveRelative:
                    _simulator.MouseMoveRelative(action.MoveX, action.MoveY);
                    break;
                case ActionType.MouseScroll:
                    _simulator.MouseScroll(action.MouseDelta);
                    break;
                case ActionType.Delay:
                    int delay = action.DelayMs;
                    if (action.RandomJitterMs > 0)
                    {
                        int jitter = Random.Shared.Next(-action.RandomJitterMs, action.RandomJitterMs + 1);
                        delay = Math.Max(1, delay + jitter);
                    }
                    InputSimulator.PreciseSleep(delay, token);
                    break;
                case ActionType.Media:
                    _simulator.SendMediaCommand(action.MediaCommand);
                    break;
            }
        }
    }

    public void EmergencyStop()
    {
        StopAllRunningMacros();
        _simulator.ReleaseAllInputs();

        Settings.HoldLeftClickActive = false;
        Settings.AutoClickerActive = false;
        Settings.MinecraftLoopActive = false;

        TriggerHud("KILLSWITCH ACTIVATED", "All macros halted & inputs released", "shield-alert", "#EF4444");
        OnStateRefreshed?.Invoke();
    }

    public void StopAllRunningMacros()
    {
        foreach (var kvp in _runningTasks)
        {
            kvp.Value.Cancel();
        }
        _runningTasks.Clear();

        if (ActiveProfile != null)
        {
            foreach (var m in ActiveProfile.Macros)
            {
                m.IsRunning = false;
            }
        }
    }

    private void StopTask(string key)
    {
        if (_runningTasks.TryRemove(key, out var cts))
        {
            cts.Cancel();
        }
    }

    private void TriggerHud(string title, string subtitle, string iconKey, string accentColor)
    {
        if (Settings.ShowHudOverlay)
        {
            OnHudNotification?.Invoke(title, subtitle, iconKey, accentColor);
        }
    }

    #endregion

    public void Dispose()
    {
        StopAllRunningMacros();
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _simulator.Dispose();
        GC.SuppressFinalize(this);
    }
}
