using System.Collections.Concurrent;
using MacroMaster.Core.Engine;
using MacroMaster.Core.Models;
using MacroMaster.Core.Native;

namespace MacroMaster.Tests;

/// <summary>An input simulator that writes down what it would have sent instead of sending it.</summary>
internal sealed class RecordingSimulator : InputSimulator
{
    public ConcurrentQueue<string> Sent { get; } = new();

    public int Count(string what) => Sent.Count(s => s == what);

    protected override void SendKeyboardInput(ushort vkCode, bool isKeyUp)
        => Sent.Enqueue($"key {(isKeyUp ? "up" : "down")} {KeyHelper.VkToString(vkCode)}");

    /// <summary>What the fake screen shows at a position (0xRRGGBB). Black everywhere by default.</summary>
    public Func<int, int, int> Screen { get; set; } = (_, _) => 0x000000;

    public override int ReadPixel(int x, int y) => Screen(x, y);

    protected override void SetPointerPosition(int x, int y) => Sent.Enqueue($"mouse to {x},{y}");

    protected override void SendUnicodeInput(char c, bool isKeyUp)
    {
        if (!isKeyUp) Sent.Enqueue($"char {c}");
    }

    protected override void SendMouseEvent(uint flags, int dx, int dy, uint data) => Sent.Enqueue(flags switch
    {
        Win32Api.MOUSEEVENTF_LEFTDOWN => "mouse down Left",
        Win32Api.MOUSEEVENTF_LEFTUP => "mouse up Left",
        Win32Api.MOUSEEVENTF_RIGHTDOWN => "mouse down Right",
        Win32Api.MOUSEEVENTF_RIGHTUP => "mouse up Right",
        Win32Api.MOUSEEVENTF_MOVE => $"mouse move {dx},{dy}",
        _ => $"mouse 0x{flags:X}"
    });
}

/// <summary>
/// A real <see cref="MacroEngine"/> with a fake keyboard, a fake "which app is in front" and a
/// recording simulator. No hooks are installed, so tests can't see or affect the PC's own input.
/// </summary>
internal sealed class EngineHarness : IDisposable
{
    public const int F12 = 0x7B, PgUp = 0x21, PgDn = 0x22, L = 'L', F6 = 0x75, Ctrl = 0xA2, Shift = 0xA0;

    public RecordingSimulator Sim { get; } = new();
    public MacroEngine Engine { get; }
    public AppSettings Settings { get; } = new() { ShowHudOverlay = true };
    public List<string> Hud { get; } = new();
    public int SaveRequests;

    private readonly HashSet<int> _down = new();

    public EngineHarness(params MacroProfile[] profiles)
    {
        InApp("explorer");
        Engine = new MacroEngine(Sim)
        {
            // Same matching as the real hook: generic modifiers match either side
            IsKeyDown = vk => vk switch
            {
                0x10 => _down.Contains(0x10) || _down.Contains(0xA0) || _down.Contains(0xA1),
                0x11 => _down.Contains(0x11) || _down.Contains(0xA2) || _down.Contains(0xA3),
                0x12 => _down.Contains(0x12) || _down.Contains(0xA4) || _down.Contains(0xA5),
                0x5B => _down.Contains(0x5B) || _down.Contains(0x5C),
                _ => _down.Contains(vk)
            }
        };
        Engine.Initialize(Settings, profiles.ToList(), installHooks: false);
        Engine.OnHudNotification += (title, subtitle, _, _) => { lock (Hud) Hud.Add($"{title} | {subtitle}"); };
        Engine.OnSaveRequested += () => Interlocked.Increment(ref SaveRequests);
    }

    /// <summary>Pretend this app's window is in front (process name without ".exe").</summary>
    public void InApp(string processName, bool admin = false) =>
        ProcessWatcher.TestForeground = new(processName, IsOwnWindow: false, BlocksHooks: admin, Pid: (uint)processName.GetHashCode() | 1);

    public void InMacroMaster() => ProcessWatcher.TestForeground = new("macromaster", IsOwnWindow: true, Pid: 7);

    /// <summary>Key goes down. Returns true if the engine swallowed it (the app in front never sees it).</summary>
    public bool Press(int vk)
    {
        _down.Add(vk);
        return Engine.HandleKeyDown(vk, false);
    }

    public bool Release(int vk)
    {
        _down.Remove(vk);
        return Engine.HandleKeyUp(vk, false);
    }

    public bool Tap(int vk)
    {
        bool swallowed = Press(vk);
        Release(vk);
        return swallowed;
    }

    /// <summary>Waits until the condition holds (macros run on background threads). False on timeout.</summary>
    public static bool WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    public void Dispose()
    {
        Engine.Dispose();
        ProcessWatcher.TestForeground = null;
    }

    #region Building blocks

    public static MacroProfile Profile(string name, string[] targets, params MacroDefinition[] macros)
    {
        var p = new MacroProfile { Name = name, IsOn = true, TargetProcesses = targets.ToList() };
        foreach (var m in macros) p.Macros.Add(m);
        return p;
    }

    public static MacroDefinition Macro(string name, string key, ExecutionMode mode = ExecutionMode.Once, string process = "*", params MacroAction[] actions)
    {
        var m = new MacroDefinition { Name = name, Mode = mode, ProcessFilter = process };
        m.Trigger.PrimaryKey = key;
        foreach (var a in actions) m.Actions.Add(a);
        return m;
    }

    public static MacroDefinition Mode(string name, string hotkey, string switchKey, bool active, string marker)
    {
        var m = Macro(name, hotkey, ExecutionMode.Once, "destiny2.exe", Type(marker));
        m.RequiresArming = true;
        m.ArmingKey = switchKey;
        m.ArmingGroupName = "Skate modes";
        m.IsCurrentlyArmed = active;
        return m;
    }

    /// <summary>A step that leaves a recognisable mark in <see cref="RecordingSimulator.Sent"/>.</summary>
    public static MacroAction Type(string text) => new() { Type = ActionType.Text, TextContent = text };
    public static MacroAction MouseDown() => new() { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Left };
    public static MacroAction Wait(int ms) => new() { Type = ActionType.Delay, DelayMs = ms };

    #endregion
}
