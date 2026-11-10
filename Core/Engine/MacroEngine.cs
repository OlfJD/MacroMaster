using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;

namespace MacroMaster.Core.Engine;

public class MacroEngine : IDisposable
{
    // HUD accent colours (match the UI theme)
    private const string Success = "#3DD68C";
    private const string Muted = "#A3A6B1";
    private const string Danger = "#F2555A";
    private const string Warning = "#F5B547";
    private const string Accent = "#7C83FF";

    private readonly LowLevelKeyboardHook _keyboardHook = new();
    private readonly LowLevelMouseHook _mouseHook = new();
    private readonly InputSimulator _simulator = new();

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
    // Run-once / repeat macros currently executing, so a second press can't start an overlapping copy
    private readonly ConcurrentDictionary<string, byte> _singleRuns = new();
    // "While held" macros running right now → the key / mouse button whose release stops them
    private readonly ConcurrentDictionary<string, (int Vk, MacroDefinition Macro)> _holdRuns = new();

    private InputHookThread? _hookThread;

    /// <summary>
    /// Read-only copy of the profile/macro lists for the hook thread. The UI edits the real
    /// ObservableCollections; enumerating those from another thread mid-edit would throw, so any
    /// change to them swaps in a fresh snapshot instead. (Per-macro flags such as IsEnabled are
    /// still read live from the shared objects.)
    /// </summary>
    private volatile ProfileSnapshot[] _snapshot = Array.Empty<ProfileSnapshot>();
    private sealed record ProfileSnapshot(MacroProfile Profile, MacroDefinition[] Macros);

    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<MacroProfile> Profiles { get; set; } = new();

    /// <summary>The profile shown in the main window. Which profiles actually fire is <see cref="MacroProfile.IsOn"/>.</summary>
    public MacroProfile? SelectedProfile => Profiles.FirstOrDefault(p => p.IsActive) ?? Profiles.FirstOrDefault();

    // Events for UI & HUD
    public event Action<string, string, string, string>? OnHudNotification; // title, subtitle, iconKey, accentColor
    public event Action? OnStateRefreshed;
    /// <summary>The quick-open key was pressed (raised on the hook thread).</summary>
    public event Action? OnQuickOpenRequested;

    public InputSimulator Simulator => _simulator;

    public void Initialize(AppSettings settings, List<MacroProfile> profiles)
    {
        Settings = settings;
        Profiles = new ObservableCollection<MacroProfile>(profiles);

        Profiles.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (MacroProfile p in e.NewItems) p.Macros.CollectionChanged += (_, _) => RebuildSnapshot();
            RebuildSnapshot();
        };
        foreach (var p in Profiles) p.Macros.CollectionChanged += (_, _) => RebuildSnapshot();
        RebuildSnapshot();

        _keyboardHook.OnKeyDown += HandleKeyDown;
        _keyboardHook.OnKeyUp += HandleKeyUp;
        _mouseHook.OnMouseEvent += HandleMouseEvent;

        _hookThread = new InputHookThread(_keyboardHook, _mouseHook);
        _hookThread.Start();
    }

    /// <summary>True when both global hooks are installed, i.e. hotkeys can work at all.</summary>
    public bool HooksActive => _keyboardHook.IsInstalled && _mouseHook.IsInstalled;

    private void RebuildSnapshot()
    {
        _snapshot = Profiles.Select(p => new ProfileSnapshot(p, p.Macros.ToArray())).ToArray();
    }

    public void SetMasterEngineEnabled(bool enabled)
    {
        Settings.MasterEngineEnabled = enabled;
        if (!enabled)
        {
            StopAllRunningMacros();
            _simulator.ReleaseAllInputs();
            TriggerHud("Engine paused", "Hotkeys are ignored until you resume", "pause", Muted);
        }
        else
        {
            TriggerHud("Engine running", "Hotkeys are live", "power", Success);
        }
        OnStateRefreshed?.Invoke();
    }

    /// <summary>Turns a whole profile's hotkeys on or off. Each macro keeps its own switch. Returns false if nothing changed.</summary>
    public bool SetProfileOn(MacroProfile profile, bool on)
    {
        if (profile.IsOn == on) return false;
        profile.IsOn = on;

        if (!on)
        {
            // Stop anything this profile still has looping, so turning it off really silences it
            foreach (var m in profile.Macros.Where(m => _runningTasks.ContainsKey(m.Id)))
            {
                StopTask(m.Id);
                _holdRuns.TryRemove(m.Id, out _);
                m.IsRunning = false;
            }
            _simulator.ReleaseAllInputs();
        }

        TriggerHud(on ? $"{profile.Name} on" : $"{profile.Name} off",
                   on ? $"{profile.Macros.Count(m => m.IsEnabled)} macros ready" : "Its hotkeys are ignored",
                   "logo", on ? Accent : Muted);
        OnStateRefreshed?.Invoke();
        return true;
    }

    #region Hook Handling

    /// <summary>Profiles whose hotkeys are live right now (switched on, and their app is in front if they have one).</summary>
    private IEnumerable<ProfileSnapshot> LiveProfiles()
    {
        foreach (var s in _snapshot)
        {
            var p = s.Profile;
            if (!p.IsOn) continue;
            var targets = p.TargetProcesses;
            if (targets.Count > 0 && !targets.Any(ProcessWatcher.IsProcessActive)) continue;
            yield return s;
        }
    }

    private bool HandleKeyDown(int vkCode, bool isExtended)
    {
        // 1. Emergency killswitch works even when the engine is paused
        if (vkCode == KeyHelper.NormalizeToVk(Settings.EmergencyKillswitchKey))
        {
            EmergencyStop();
            return true;
        }

        // Quick open also works while paused: it opens the app, it doesn't run a macro
        if (!string.IsNullOrWhiteSpace(Settings.QuickOpenKey) && vkCode == KeyHelper.NormalizeToVk(Settings.QuickOpenKey))
        {
            OnQuickOpenRequested?.Invoke();
            return true;
        }

        if (!Settings.MasterEngineEnabled) return false;

        foreach (var profile in LiveProfiles())
        {
            // 2. Mode arming keys (e.g. PgUp arms Wellskate, PgDn arms Groundskate)
            foreach (var macro in profile.Macros)
            {
                if (!macro.IsEnabled || !macro.RequiresArming || string.IsNullOrWhiteSpace(macro.ArmingKey)) continue;
                if (vkCode != KeyHelper.NormalizeToVk(macro.ArmingKey)) continue;

                foreach (var other in profile.Macros)
                {
                    if (other.RequiresArming && other.ModeGroupKey == macro.ModeGroupKey)
                        other.IsCurrentlyArmed = other.Id == macro.Id;
                }

                TriggerHud($"Mode: {macro.ModeShortName}", $"Switched with {macro.ArmingKey.ToUpperInvariant()}", "circle-dot", Warning);
                OnStateRefreshed?.Invoke();
                return false; // the arming key still reaches the game
            }

            // 3. Macro triggers
            foreach (var macro in profile.Macros)
            {
                if (!macro.IsEnabled) continue;
                // Cheap checks first: this runs inside the low-level hook for every keystroke
                if (!KeyMatches(vkCode, KeyHelper.NormalizeToVk(macro.Trigger.PrimaryKey))) continue;
                if (!TriggerConditionsMet(macro)) continue;

                Trigger(macro, vkCode);
                return true; // swallow the trigger key
            }
        }

        return false;
    }

    private bool HandleKeyUp(int vkCode, bool isExtended) => StopHeldMacros(vkCode);

    /// <summary>A hotkey fired: "while held" macros also need to know which key to wait for.</summary>
    private void Trigger(MacroDefinition macro, int vk)
    {
        if (macro.Mode != ExecutionMode.HoldWhileDown)
        {
            ExecuteMacro(macro);
            return;
        }

        if (_runningTasks.ContainsKey(macro.Id)) return; // already running (e.g. a second trigger key)
        _holdRuns[macro.Id] = (vk, macro);
        StartLoop(macro, "Running while you hold the hotkey");
    }

    /// <summary>The key / button was let go: stop every "while held" macro it started. True if any stopped.</summary>
    private bool StopHeldMacros(int vk)
    {
        if (_holdRuns.IsEmpty) return false;

        bool any = false;
        foreach (var run in _holdRuns)
        {
            if (!KeyMatches(vk, run.Value.Vk)) continue;
            StopLoop(run.Value.Macro, announce: false);
            any = true;
        }
        return any;
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
        // Compare by virtual key: the editor stores mouse triggers as "Mouse 4" / "Left Click", and
        // comparing to the enum name ("Left") also matched the Left *arrow* key and hijacked clicks
        int buttonVk = MouseButtonToVk(button);

        if (!isDown) return StopHeldMacros(buttonVk);
        if (!Settings.MasterEngineEnabled) return false;

        foreach (var profile in LiveProfiles())
        {
            foreach (var macro in profile.Macros)
            {
                if (!macro.IsEnabled) continue;
                if (KeyHelper.NormalizeToVk(macro.Trigger.PrimaryKey) != buttonVk) continue;
                if (!TriggerConditionsMet(macro)) continue;

                Trigger(macro, buttonVk);
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Macro Execution

    /// <summary>Starts looping the macro's steps until <see cref="StopLoop"/> (or the kill switch).</summary>
    private void StartLoop(MacroDefinition macro, string hudSubtitle)
    {
        macro.IsRunning = true;
        TriggerHud(macro.Name, hudSubtitle, "repeat", Success);
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

    private void StopLoop(MacroDefinition macro, bool announce)
    {
        StopTask(macro.Id);
        _holdRuns.TryRemove(macro.Id, out _);
        macro.IsRunning = false;
        _simulator.ReleaseAllInputs();
        if (announce) TriggerHud(macro.Name, "Stopped", "square", Muted);
        OnStateRefreshed?.Invoke();
    }

    /// <summary>
    /// Runs a macro the way its hotkey would. Also used by the Run button and the editor's test
    /// run, where there is no key to let go of: a "while held" macro then behaves like a loop
    /// (Run starts it, Run again stops it).
    /// </summary>
    public void ExecuteMacro(MacroDefinition macro)
    {
        if (macro.Mode is ExecutionMode.Toggle or ExecutionMode.HoldWhileDown)
        {
            if (_runningTasks.ContainsKey(macro.Id))
            {
                StopLoop(macro, announce: true);
                return;
            }

            StartLoop(macro, "Looping — press again to stop");
        }
        else
        {
            // Pressing the trigger again while this macro is still playing would start a second copy
            // interleaved with the first (key downs/ups crossing, keys left stuck), so ignore it.
            // The trigger key is still swallowed by the caller, so the game doesn't see it either.
            if (!_singleRuns.TryAdd(macro.Id, 0)) return;

            TriggerHud(macro.Name, macro.Mode == ExecutionMode.RepeatCount ? $"Running ×{Math.Max(1, macro.RepeatCount)}" : "Running", "play", Accent);

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
                    _singleRuns.TryRemove(macro.Id, out _);
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
                    _simulator.KeyTap(action.Key, Math.Max(1, action.DelayMs), token);
                    break;
                case ActionType.MouseDown:
                    _simulator.MouseDown(action.MouseButton);
                    break;
                case ActionType.MouseUp:
                    _simulator.MouseUp(action.MouseButton);
                    break;
                case ActionType.MouseClick:
                    _simulator.MouseClick(action.MouseButton, Math.Max(1, action.DelayMs), token);
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
                case ActionType.Text:
                    _simulator.TypeText(action.TextContent, token);
                    break;
            }
        }
    }

    public void EmergencyStop()
    {
        StopAllRunningMacros();
        _simulator.ReleaseAllInputs();

        TriggerHud("Everything stopped", "All macros halted and inputs released", "octagon-x", Danger);
        OnStateRefreshed?.Invoke();
    }

    public void StopAllRunningMacros()
    {
        foreach (var kvp in _runningTasks)
        {
            kvp.Value.Cancel();
        }
        _runningTasks.Clear();
        _holdRuns.Clear();

        // Snapshot, not Profiles: this also runs on the hook thread (kill switch)
        foreach (var m in _snapshot.SelectMany(s => s.Macros))
        {
            m.IsRunning = false;
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
        _hookThread?.Dispose();   // ends the message loop, which uninstalls both hooks on their thread
        _hookThread = null;
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _simulator.Dispose();
        GC.SuppressFinalize(this);
    }
}
