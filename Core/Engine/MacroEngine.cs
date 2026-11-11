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
    private readonly InputSimulator _simulator;

    /// <summary>Is this key physically held? The real hook by default; tests swap in their own keyboard state.</summary>
    internal Func<int, bool> IsKeyDown;

    // Watches running macros and the foreground window a few times a second (see WatchdogTick)
    private System.Threading.Timer? _watchdog;
    private int _watchdogBusy;
    private uint _lastForegroundPid;

    public MacroEngine() : this(new InputSimulator()) { }

    /// <summary>Tests pass a simulator that records instead of sending real input.</summary>
    internal MacroEngine(InputSimulator simulator)
    {
        _simulator = simulator;
        IsKeyDown = _keyboardHook.IsKeyDown;
    }

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
    /// <summary>
    /// A hotkey changed something that is saved to disk: a mode group's active mode, or the engine
    /// switch (raised on the hook thread).
    /// </summary>
    public event Action? OnSaveRequested;

    /// <summary>
    /// Name of an app that runs as administrator while MacroMaster doesn't, seen in front during
    /// this session. Windows doesn't pass key presses made in such a window to a normal program,
    /// so hotkeys silently do nothing there. Null when that hasn't happened.
    /// </summary>
    public string? HotkeysBlockedBy { get; private set; }
    /// <summary>The quick-open key was pressed (raised on the hook thread).</summary>
    public event Action? OnQuickOpenRequested;

    public InputSimulator Simulator => _simulator;

    public void Initialize(AppSettings settings, List<MacroProfile> profiles) => Initialize(settings, profiles, installHooks: true);

    /// <summary><paramref name="installHooks"/> = false leaves the PC's keyboard and mouse alone (tests feed keys in directly).</summary>
    internal void Initialize(AppSettings settings, List<MacroProfile> profiles, bool installHooks)
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

        if (!installHooks) return;

        _keyboardHook.OnKeyDown += HandleKeyDown;
        _keyboardHook.OnKeyUp += HandleKeyUp;
        _mouseHook.OnMouseEvent += HandleMouseEvent;

        _hookThread = new InputHookThread(_keyboardHook, _mouseHook);
        _hookThread.Start();

        _watchdog = new System.Threading.Timer(_ =>
        {
            // Never let two ticks overlap (a slow one must not queue up behind itself)
            if (Interlocked.Exchange(ref _watchdogBusy, 1) == 1) return;
            try { WatchdogTick(); }
            catch { /* a failed check must never take the engine down */ }
            finally { Volatile.Write(ref _watchdogBusy, 0); }
        }, null, 250, 250);
    }

    /// <summary>
    /// Runs four times a second: stops macros whose app is no longer in front, and notices when an
    /// administrator window is in front (where hotkeys can't work).
    /// </summary>
    internal void WatchdogTick()
    {
        uint pid = ProcessWatcher.GetForegroundProcessId();
        if (pid == 0) return; // no foreground window for a moment (mid alt-tab): decide on the next tick

        StopRunsOutsideTheirApp();
        RefreshModeIndicator();

        if (pid == _lastForegroundPid) return;
        _lastForegroundPid = pid;

        if (!ProcessWatcher.IsForegroundBlockingHooks()) return;
        string app = ProcessWatcher.GetActiveProcessName();
        if (app.Length == 0) app = "this app";
        bool firstTime = HotkeysBlockedBy != app;
        HotkeysBlockedBy = app;
        // Only interrupt with a popup if some hotkey was actually meant to work in this app
        if (firstTime && Settings.MasterEngineEnabled && LiveProfiles().Any(s => s.Macros.Any(m => m.IsEnabled)))
        {
            TriggerHud($"Hotkeys can't work in {app}", "It runs as administrator. Restart MacroMaster as administrator.", "octagon-x", Warning);
        }
        if (firstTime) OnStateRefreshed?.Invoke();
    }

    /// <summary>
    /// What the always-visible mode indicator should say right now: the active mode of every mode
    /// group whose hotkey would work in the app in front ("Wellskate"), or null to hide it.
    /// </summary>
    public string? ModeIndicatorText { get; private set; }

    /// <summary>The indicator's text changed (null = hide). Raised on the watchdog or hook thread.</summary>
    public event Action<string?>? OnModeIndicatorChanged;

    /// <summary>Works out the indicator text again; call after anything that can change it.</summary>
    public void RefreshModeIndicator()
    {
        string? text = null;
        if (Settings.MasterEngineEnabled && Settings.ShowModeIndicator)
        {
            var modes = LiveProfiles()
                .SelectMany(s => s.Macros)
                .Where(m => m.IsEnabled && m.RequiresArming && m.IsCurrentlyArmed && ProcessWatcher.IsProcessActive(m.ProcessFilter))
                .Select(m => m.ModeShortName)
                .Distinct()
                .ToList();
            if (modes.Count > 0) text = string.Join("  ·  ", modes);
        }

        if (text == ModeIndicatorText) return;
        ModeIndicatorText = text;
        OnModeIndicatorChanged?.Invoke(text);
    }

    /// <summary>
    /// The app a macro is limited to is only checked when its hotkey is pressed, so without this a
    /// loop started in the game would carry on clicking on the desktop after alt-tabbing out.
    /// </summary>
    private void StopRunsOutsideTheirApp()
    {
        if (_runningTasks.IsEmpty) return;

        foreach (var s in _snapshot)
        {
            foreach (var m in s.Macros)
            {
                bool looping = _runningTasks.ContainsKey(m.Id);
                if (!looping && !_singleRuns.ContainsKey(m.Id)) continue;
                if (IsLive(s.Profile) && ProcessWatcher.IsProcessActive(m.ProcessFilter)) continue;

                if (looping)
                {
                    StopLoop(m, announce: false);
                    TriggerHud(m.Name, "Stopped — you left its app", "square", Muted);
                }
                else
                {
                    string prefix = m.Id + "#run#";
                    foreach (var key in _runningTasks.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        StopTask(key);
                    }
                    _simulator.ReleaseAllInputs();
                }
            }
        }
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
            string key = Settings.EngineToggleKey?.Trim() ?? string.Empty;
            TriggerHud("Engine off", key.Length > 0 ? $"Everything stopped. Press {key.ToUpperInvariant()} to turn it back on" : "Everything stopped, hotkeys are ignored", "octagon-x", Danger);
        }
        else
        {
            TriggerHud("Engine on", "Hotkeys are live", "power", Success);
        }
        RefreshModeIndicator();
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
        RefreshModeIndicator();
        OnStateRefreshed?.Invoke();
        return true;
    }

    #region Hook Handling

    /// <summary>Profiles whose hotkeys are live right now (switched on, and their app is in front if they have one).</summary>
    private IEnumerable<ProfileSnapshot> LiveProfiles()
    {
        foreach (var s in _snapshot)
        {
            if (IsLive(s.Profile)) yield return s;
        }
    }

    private static bool IsLive(MacroProfile p)
    {
        if (!p.IsOn) return false;
        var targets = p.TargetProcesses;
        return targets.Count == 0 || targets.Any(ProcessWatcher.IsProcessActive);
    }

    internal bool HandleKeyDown(int vkCode, bool isExtended)
    {
        // 1. Engine on/off key: the same switch as in the window. Turning the engine off stops every
        //    macro and releases everything they hold, and it has to work while the engine is off
        //    too, or you couldn't switch it back on.
        if (!string.IsNullOrWhiteSpace(Settings.EngineToggleKey) && vkCode == KeyHelper.NormalizeToVk(Settings.EngineToggleKey))
        {
            SetMasterEngineEnabled(!Settings.MasterEngineEnabled);
            OnSaveRequested?.Invoke();
            return true;
        }

        // Quick open also works while paused: it opens the app, it doesn't run a macro
        if (!string.IsNullOrWhiteSpace(Settings.QuickOpenKey) && vkCode == KeyHelper.NormalizeToVk(Settings.QuickOpenKey))
        {
            OnQuickOpenRequested?.Invoke();
            return true;
        }

        if (!Settings.MasterEngineEnabled) return false;

        // 2. Mode switch keys (e.g. PgUp = Wellskate, PgDn = Groundskate). These also work while
        //    MacroMaster itself is in front: that's where you look at the list and try the keys, and
        //    with only the profile's own app counting, pressing them there did nothing at all.
        bool ownWindow = ProcessWatcher.IsOwnWindowActive();
        bool switched = false;
        foreach (var profile in ownWindow ? _snapshot.Where(s => s.Profile.IsOn) : LiveProfiles())
        {
            foreach (var macro in profile.Macros)
            {
                if (!macro.IsEnabled || !macro.RequiresArming || string.IsNullOrWhiteSpace(macro.ArmingKey)) continue;
                if (!KeyMatches(vkCode, KeyHelper.NormalizeToVk(macro.ArmingKey))) continue;

                bool changed = !macro.IsCurrentlyArmed;
                foreach (var other in profile.Macros)
                {
                    if (other.RequiresArming && other.ModeGroupKey == macro.ModeGroupKey)
                        other.IsCurrentlyArmed = other.Id == macro.Id;
                }

                TriggerHud($"Mode: {macro.ModeShortName}", $"Switched with {macro.ArmingKey.ToUpperInvariant()}", "circle-dot", Warning);
                if (changed) OnSaveRequested?.Invoke();
                switched = true;
                break; // one mode per profile and key press
            }
        }
        if (switched)
        {
            RefreshModeIndicator();
            OnStateRefreshed?.Invoke();
            return false; // the switch key still reaches the game
        }

        foreach (var profile in LiveProfiles())
        {
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

    internal bool HandleKeyUp(int vkCode, bool isExtended) => StopHeldMacros(vkCode);

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

        var t = macro.Trigger;
        if (!ModifierMatches(t, 0x11, t.RequireCtrl)) return false;
        if (!ModifierMatches(t, 0x10, t.RequireShift)) return false;
        if (!ModifierMatches(t, 0x12, t.RequireAlt)) return false;
        if (!ModifierMatches(t, 0x5B, t.RequireWin)) return false;

        foreach (var chord in t.ChordKeys)
        {
            int chordVk = KeyHelper.NormalizeToVk(chord);
            if (chordVk != 0 && !IsKeyDown(chordVk)) return false;
        }

        return ProcessWatcher.IsProcessActive(macro.ProcessFilter);
    }

    /// <summary>
    /// Modifiers have to match exactly: a hotkey on L must not also fire on Ctrl+L or Shift+L
    /// (those are other shortcuts). A modifier that is itself the hotkey or one of its extra keys
    /// is of course allowed to be down.
    /// </summary>
    private bool ModifierMatches(MacroTrigger t, int modifierVk, bool required)
    {
        bool down = IsKeyDown(modifierVk);
        if (required) return down;
        if (!down) return true;
        if (ModifierGroup(KeyHelper.NormalizeToVk(t.PrimaryKey)) == modifierVk) return true;
        foreach (var chord in t.ChordKeys)
        {
            if (ModifierGroup(KeyHelper.NormalizeToVk(chord)) == modifierVk) return true;
        }
        return false;
    }

    /// <summary>Generic modifier a key belongs to (left/right Shift → Shift), or 0 for any other key.</summary>
    private static int ModifierGroup(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => 0x10,
        0x11 or 0xA2 or 0xA3 => 0x11,
        0x12 or 0xA4 or 0xA5 => 0x12,
        0x5B or 0x5C => 0x5B,
        _ => 0
    };

    private static int MouseButtonToVk(MouseButtonType button) => button switch
    {
        MouseButtonType.Left => 0x01,
        MouseButtonType.Right => 0x02,
        MouseButtonType.Middle => 0x04,
        MouseButtonType.XButton1 => 0x05,
        MouseButtonType.XButton2 => 0x06,
        _ => 0
    };

    internal bool HandleMouseEvent(MouseButtonType button, bool isDown)
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

    /// <summary>Starts looping the macro's steps until <see cref="StopLoop"/> (or the engine is switched off).</summary>
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
                // "Hold a key/button down" macros (nothing but presses): press once and wait.
                // Looping them re-sent the same press as fast as the CPU could, forever.
                if (macro.Actions.Count > 0 && macro.Actions.All(a => a.Type is ActionType.KeyDown or ActionType.MouseDown))
                {
                    ExecuteActionSequence(macro.Actions, cts.Token);
                    cts.Token.WaitHandle.WaitOne();
                    return;
                }

                long oneMs = System.Diagnostics.Stopwatch.Frequency / 1000;
                while (!cts.Token.IsCancellationRequested)
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    ExecuteActionSequence(macro.Actions, cts.Token);
                    if (macro.LoopDelayMs > 0)
                    {
                        InputSimulator.PreciseSleep(macro.LoopDelayMs, cts.Token);
                    }
                    else if (System.Diagnostics.Stopwatch.GetTimestamp() - started < oneMs)
                    {
                        // A pass with no waiting in it at all: yield for a moment so the loop
                        // can't pin a CPU core and flood the PC with thousands of inputs a second
                        cts.Token.WaitHandle.WaitOne(1);
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
    /// Runs a macro the way its hotkey would: loops toggle on/off, everything else plays once
    /// (or N times) and ignores presses while it is still playing.
    /// </summary>
    private void ExecuteMacro(MacroDefinition macro)
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

            // Registered under a unique key so switching the engine off can cancel long repeat runs too
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
        // Open "repeat" blocks: where each one starts and how many passes it still has to do
        var repeats = new Stack<(int Start, int Left)>();

        for (int i = 0; i < actions.Count; i++)
        {
            if (token.IsCancellationRequested) break;
            var action = actions[i];

            switch (action.Type)
            {
                case ActionType.RepeatStart:
                    repeats.Push((i, Math.Clamp(action.RepeatCount, 1, 100000)));
                    break;
                case ActionType.RepeatEnd:
                    // An "end" without a start is ignored; a start without an end repeats nothing
                    if (repeats.Count > 0)
                    {
                        var (start, left) = repeats.Pop();
                        if (left > 1)
                        {
                            repeats.Push((start, left - 1));
                            i = start; // the loop's i++ continues with the first step of the block
                        }
                    }
                    break;
                case ActionType.MouseMoveAbsolute:
                    _simulator.MouseMoveTo(action.MoveX, action.MoveY);
                    break;
                case ActionType.WaitForPixel:
                    // The colour never showed up: the steps after it were waiting for it, so skip them
                    if (!WaitForPixel(action, token)) return;
                    break;
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

    /// <summary>How far each colour channel may be off and still count (games dither and compress colours).</summary>
    internal const int PixelTolerance = 12;

    /// <summary>
    /// Waits until the screen pixel has the wanted colour. False when it gave up (DelayMs passed;
    /// 0 = never gives up), was cancelled, or the step has no valid colour.
    /// </summary>
    private bool WaitForPixel(MacroAction action, CancellationToken token)
    {
        if (!MacroAction.TryParseColor(action.TextContent, out int r, out int g, out int b)) return false;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!token.IsCancellationRequested)
        {
            int c = _simulator.ReadPixel(action.MoveX, action.MoveY);
            if (c >= 0 &&
                Math.Abs(((c >> 16) & 0xFF) - r) <= PixelTolerance &&
                Math.Abs(((c >> 8) & 0xFF) - g) <= PixelTolerance &&
                Math.Abs((c & 0xFF) - b) <= PixelTolerance)
            {
                return true;
            }
            if (action.DelayMs > 0 && sw.ElapsedMilliseconds >= action.DelayMs) return false;
            token.WaitHandle.WaitOne(10);
        }
        return false;
    }

    public void StopAllRunningMacros()
    {
        foreach (var kvp in _runningTasks)
        {
            kvp.Value.Cancel();
        }
        _runningTasks.Clear();
        _holdRuns.Clear();

        // Snapshot, not Profiles: this also runs on the hook thread (engine on/off key)
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
        _watchdog?.Dispose();
        _watchdog = null;
        StopAllRunningMacros();
        _simulator.ReleaseAllInputs(); // quitting must not leave a key or button held down
        _hookThread?.Dispose();  // ends the message loop, which uninstalls both hooks on their thread
        _hookThread = null;
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _simulator.Dispose();
        GC.SuppressFinalize(this);
    }
}
