using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroMaster.Core.Native;

public class ProcessWatcher
{
    // This runs inside the low-level hook on every trigger check, so the name of the app in front is
    // remembered. Keyed by window and process id, and for a second at most: a process id that Windows
    // hands to a new program could otherwise keep the old program's name.
    private static readonly ForegroundNameCache _cache = new();

    /// <summary>
    /// Tests only: pretend this is what's in front instead of asking Windows.
    /// Thread-static, so tests running side by side can't see each other's fake.
    /// </summary>
    [ThreadStatic] internal static ForegroundInfo? TestForeground;

    /// <param name="ProcessName">Lower case, without ".exe".</param>
    internal sealed record ForegroundInfo(string ProcessName, bool IsOwnWindow = false, bool BlocksHooks = false, uint Pid = 1);

    /// <summary>Process id of the window in front, or 0 when there is none (e.g. in the middle of an alt-tab).</summary>
    public static uint GetForegroundProcessId()
    {
        if (TestForeground is { } fake) return fake.Pid;

        IntPtr hWnd = Win32Api.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return 0;
        Win32Api.GetWindowThreadProcessId(hWnd, out uint processId);
        return processId;
    }

    public static string GetActiveProcessName()
    {
        if (TestForeground is { } fake) return fake.ProcessName;

        try
        {
            IntPtr window = Win32Api.GetForegroundWindow();
            if (window == IntPtr.Zero) return string.Empty;
            Win32Api.GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0) return string.Empty;

            long now = Stopwatch.GetTimestamp();
            if (_cache.TryGet(window, processId, now, out string cached)) return cached;

            string name = ProcessNameOf(processId); // e.g. "destiny2", "javaw"
            if (name.Length > 0) _cache.Store(window, processId, name, now);
            return name;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// A process's name, lower case and without ".exe", read straight from Windows with only the
    /// limited query right (granted for most games, anti-cheat protected ones included). Falls back to
    /// Process.ProcessName. "" if Windows won't say.
    /// </summary>
    internal static string ProcessNameOf(uint processId)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process != IntPtr.Zero)
        {
            try
            {
                var path = new System.Text.StringBuilder(1024);
                int size = path.Capacity;
                if (QueryFullProcessImageNameW(process, 0, path, ref size))
                    return System.IO.Path.GetFileNameWithoutExtension(path.ToString()).ToLowerInvariant();
            }
            finally { CloseHandle(process); }
        }

        try
        {
            using var p = Process.GetProcessById((int)processId); // the old way, as a fallback
            return p.ProcessName.ToLowerInvariant();
        }
        catch { return string.Empty; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);

    /// <summary>True while one of MacroMaster's own windows is in front.</summary>
    public static bool IsOwnWindowActive()
    {
        if (TestForeground is { } fake) return fake.IsOwnWindow;
        return GetForegroundProcessId() == (uint)Environment.ProcessId;
    }

    public static bool IsProcessActive(string processFilter)
    {
        if (string.IsNullOrWhiteSpace(processFilter) || processFilter == "*")
            return true;

        string cleanFilter = processFilter.Trim().ToLowerInvariant();
        if (cleanFilter.EndsWith(".exe"))
        {
            cleanFilter = cleanFilter.Substring(0, cleanFilter.Length - 4);
        }

        string activeProc = GetActiveProcessName();
        return string.Equals(activeProc, cleanFilter, StringComparison.OrdinalIgnoreCase);
    }

    #region Administrator windows

    /// <summary>
    /// True when the window in front belongs to a program running as administrator while MacroMaster
    /// itself doesn't. Windows doesn't hand key presses made in such a window to normal programs,
    /// so every hotkey silently does nothing there.
    /// </summary>
    public static bool IsForegroundBlockingHooks()
    {
        if (TestForeground is { } fake) return fake.BlocksHooks;
        if (Environment.IsPrivilegedProcess) return false;

        uint pid = GetForegroundProcessId();
        return pid != 0 && pid != (uint)Environment.ProcessId && IsProcessElevated(pid) == true;
    }

    /// <summary>
    /// Whether a process runs as administrator; null when Windows won't say (protected processes
    /// such as anti-cheat guarded games). Callers must treat null as "don't know", not as "yes".
    /// </summary>
    public static bool? IsProcessElevated(uint pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token)) return null;
            try
            {
                return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) ? elevated != 0 : null;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}

/// <summary>The last foreground window's process name, for one window and process id, for up to a second.</summary>
internal sealed class ForegroundNameCache
{
    public static readonly long MaxAge = Stopwatch.Frequency; // one second, in Stopwatch ticks

    private readonly object _lock = new();
    private IntPtr _window;
    private uint _processId;
    private string _name = string.Empty;
    private long _storedAt;
    private bool _hasEntry;

    public bool TryGet(IntPtr window, uint processId, long now, out string name)
    {
        lock (_lock)
        {
            if (_hasEntry && _window == window && _processId == processId && now - _storedAt < MaxAge)
            {
                name = _name;
                return true;
            }
        }
        name = string.Empty;
        return false;
    }

    public void Store(IntPtr window, uint processId, string name, long now)
    {
        lock (_lock)
        {
            _window = window;
            _processId = processId;
            _name = name;
            _storedAt = now;
            _hasEntry = true;
        }
    }
}
