using System.Runtime.InteropServices;

namespace MacroMaster.Core.Native;

/// <summary>
/// Hosts the global keyboard and mouse hooks on a dedicated thread with its own message loop.
///
/// Windows calls a low-level hook on the thread that installed it, and every keystroke on the
/// whole PC waits for that call to return. When the hooks lived on the UI thread, anything that
/// kept the window busy (layout, dialogs opening, saving) delayed every key press system-wide,
/// and Windows silently removes hooks that keep timing out — the hotkeys would just stop working
/// until restart. This thread does nothing but answer the hooks, so that can't happen.
/// </summary>
public sealed class InputHookThread : IDisposable
{
    private readonly LowLevelKeyboardHook _keyboard;
    private readonly LowLevelMouseHook _mouse;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;

    public InputHookThread(LowLevelKeyboardHook keyboard, LowLevelMouseHook mouse)
    {
        _keyboard = keyboard;
        _mouse = mouse;
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "MacroMaster input hooks",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _keyboard.Install();
        _mouse.Install();
        _ready.Set();

        // Hooks are delivered through this thread's message queue
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        _keyboard.Uninstall();
        _mouse.Uninstall();
    }

    public void Dispose()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _ready.Dispose();
    }

    #region Win32

    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    #endregion
}
