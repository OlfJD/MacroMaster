using System.Diagnostics;

namespace MacroMaster.Core.Native;

public class ProcessWatcher
{
    // Process.GetProcessById is comparatively expensive and this runs inside the low-level hook on
    // every trigger check, so remember the name for the last foreground PID.
    private static uint _cachedPid;
    private static string _cachedName = string.Empty;
    private static readonly object _cacheLock = new();

    public static string GetActiveProcessName()
    {
        try
        {
            IntPtr hWnd = Win32Api.GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return string.Empty;

            Win32Api.GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0) return string.Empty;

            lock (_cacheLock)
            {
                if (processId == _cachedPid) return _cachedName;
            }

            using var process = Process.GetProcessById((int)processId);
            string name = process.ProcessName.ToLowerInvariant(); // e.g. "destiny2", "javaw"

            lock (_cacheLock)
            {
                _cachedPid = processId;
                _cachedName = name;
            }
            return name;
        }
        catch
        {
            return string.Empty;
        }
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
}
