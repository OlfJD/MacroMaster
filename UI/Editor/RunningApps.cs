using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacroMaster.UI.Editor;

/// <summary>An app with a visible window, as offered in the "Only while this app is in front" picker.</summary>
public sealed class RunningApp
{
    /// <summary>What the macro stores and the engine matches, e.g. "destiny2.exe".</summary>
    public string ExeName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Path { get; init; }
    public ImageSource? Icon { get; init; }

    /// <summary>Window title when there is one, otherwise the exe name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ExeName : Title;
}

public static class RunningApps
{
    /// <summary>
    /// Apps that currently have a main window, one entry per exe, sorted by title.
    /// Slow-ish (icons, process queries): call it off the UI thread. Icons are frozen.
    /// </summary>
    public static List<RunningApp> List()
    {
        int self = Environment.ProcessId;
        var apps = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == IntPtr.Zero) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    string exe = p.ProcessName.ToLowerInvariant() + ".exe";
                    if (apps.ContainsKey(exe) || Hidden.Contains(exe)) continue;

                    string? path = ImagePath(p.Id);
                    apps[exe] = new RunningApp { ExeName = exe, Title = title.Trim(), Path = path, Icon = IconFor(path) };
                }
                catch
                {
                    // Process exited or access denied: skip it
                }
            }
        }

        return apps.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Shell / system windows nobody wants a macro for.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "applicationframehost.exe", "textinputhost.exe", "shellexperiencehost.exe",
        "searchhost.exe", "startmenuexperiencehost.exe", "systemsettings.exe", "lockapp.exe"
    };

    /// <summary>Icon of an .exe as a frozen WPF image, or null.</summary>
    public static ImageSource? IconFor(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full path of a process' exe. Works for most elevated processes too, unlike Process.MainModule.</summary>
    private static string? ImagePath(int pid)
    {
        IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
