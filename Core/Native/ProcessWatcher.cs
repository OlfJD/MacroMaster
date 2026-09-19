using System.Diagnostics;

namespace MacroMaster.Core.Native;

public class ProcessWatcher
{
    public static string GetActiveProcessName()
    {
        try
        {
            IntPtr hWnd = Win32Api.GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return string.Empty;

            Win32Api.GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0) return string.Empty;

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.ToLowerInvariant(); // e.g. "destiny2", "javaw"
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
