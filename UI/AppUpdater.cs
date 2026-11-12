using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using MacroMaster.Core.Engine;

namespace MacroMaster.UI;

/// <summary>
/// Silent updates from GitHub Releases (like SmartNotes and the YouTube downloader, but without
/// asking): checks the latest release when MacroMaster starts and every few hours, downloads the
/// release .zip in the background, and installs it at a quiet moment — no macro running, the
/// window not in use. Installing = a small hidden batch file waits for MacroMaster to exit, copies
/// the new files over the app folder and starts it again in the same state (tray or window). Your
/// macros and settings live in %AppData% and are not touched.
///
/// Releases must be tagged with a version higher than the csproj &lt;Version&gt; (e.g. v1.2.0) and
/// carry a .zip whose files include MacroMaster.exe. Dev builds (Debug) never update themselves.
/// </summary>
public static class AppUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/OlfJD/MacroMaster/releases/latest";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ApplyRetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>The running version (csproj &lt;Version&gt;), three parts.</summary>
    public static readonly Version CurrentVersion = ReadVersion();

    private static readonly HttpClient Http = CreateClient();
    private static DispatcherTimer? _checkTimer, _applyTimer;
    private static MacroEngine? _engine;
    private static Func<bool>? _userBusy, _windowShown;
    private static Action? _shutdown;
    private static string? _readyFolder; // extracted new version waiting to be installed
    private static bool _checking;

    private static Version ReadVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MacroMaster-Updater", CurrentVersion.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <param name="engine">For the settings (AutoUpdate) and to see whether a macro is running.</param>
    /// <param name="userBusy">True while the user is using the window (editor open, window active).</param>
    /// <param name="windowShown">Whether the window is on screen, so the new version opens the same way.</param>
    /// <param name="shutdown">Saves and quits MacroMaster (the batch file takes over from there).</param>
    public static void Start(MacroEngine engine, Func<bool> userBusy, Func<bool> windowShown, Action shutdown)
    {
        if (IsDevBuild) return; // never overwrite a dev build folder with a release
        _engine = engine;
        _userBusy = userBusy;
        _windowShown = windowShown;
        _shutdown = shutdown;

        _checkTimer = new DispatcherTimer { Interval = FirstCheckDelay };
        _checkTimer.Tick += async (_, _) =>
        {
            _checkTimer!.Interval = CheckInterval;
            await CheckAsync();
        };
        _checkTimer.Start();
    }

    private static bool IsDevBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    private static async Task CheckAsync()
    {
        if (_checking || _readyFolder != null || _engine?.Settings.AutoUpdate != true) return;
        _checking = true;
        try
        {
            var (version, zipUrl) = await GetLatestAsync();
            if (version == null || zipUrl == null || version <= CurrentVersion) return;
            if (!CanWrite(AppContext.BaseDirectory)) return; // e.g. installed under Program Files without admin

            string folder = await Task.Run(() => DownloadAndExtract(version, zipUrl));
            _readyFolder = folder;

            _applyTimer ??= new DispatcherTimer { Interval = ApplyRetryInterval };
            _applyTimer.Tick -= ApplyTimer_Tick;
            _applyTimer.Tick += ApplyTimer_Tick;
            _applyTimer.Start();
            ApplyTimer_Tick(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}"); // offline etc.: try again next time
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Latest release's version and its .zip, from the GitHub API.</summary>
    internal static async Task<(Version? Version, string? ZipUrl)> GetLatestAsync()
    {
        using var response = await Http.GetAsync(LatestReleaseApi);
        if (!response.IsSuccessStatusCode) return (null, null);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var version = ParseVersion(tag);

        string? zip = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || url.Length == 0) continue;
                // Prefer the Windows build if a release ever carries several zips
                if (zip == null || name.Contains("win", StringComparison.OrdinalIgnoreCase)) zip = url;
            }
        }
        return (version, zip);
    }

    /// <summary>"v1.2" / "1.2.3" / "V1.2.3-beta" → 1.2.0 / 1.2.3 / 1.2.3.</summary>
    internal static Version? ParseVersion(string tag)
    {
        string s = tag.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) s = s[..cut];
        var parts = s.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor)) return null;
        int build = parts.Length > 2 && int.TryParse(parts[2], out int b) ? b : 0;
        return new Version(major, minor, build);
    }

    private static string DownloadAndExtract(Version version, string zipUrl)
    {
        string root = Path.Combine(Path.GetTempPath(), "MacroMaster-update");
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        Directory.CreateDirectory(root);

        string zipPath = Path.Combine(root, $"MacroMaster-{version}.zip");
        using (var stream = Http.GetStreamAsync(zipUrl).GetAwaiter().GetResult())
        using (var file = File.Create(zipPath))
        {
            stream.CopyTo(file);
        }

        string extracted = Path.Combine(root, "files");
        ZipFile.ExtractToDirectory(zipPath, extracted, overwriteFiles: true); // refuses paths outside the folder

        // The zip may hold the files directly or inside one folder
        string? exe = Directory.EnumerateFiles(extracted, "MacroMaster.exe", SearchOption.AllDirectories)
                               .OrderBy(p => p.Length).FirstOrDefault();
        if (exe == null) throw new InvalidDataException("The release zip has no MacroMaster.exe");
        return Path.GetDirectoryName(exe)!;
    }

    private static void ApplyTimer_Tick(object? sender, EventArgs e)
    {
        if (_readyFolder == null || _engine == null) { _applyTimer?.Stop(); return; }
        if (!_engine.Settings.AutoUpdate) return;

        // Only at a quiet moment: never in the middle of a running macro or while the user works in the window
        bool macroRunning = _engine.Profiles.SelectMany(p => p.Macros).Any(m => m.IsRunning);
        if (macroRunning || _userBusy?.Invoke() == true) return;

        _applyTimer?.Stop();
        try
        {
            LaunchInstaller(_readyFolder, reopenWindow: _windowShown?.Invoke() == true);
            _shutdown?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update install failed: {ex.Message}");
            _readyFolder = null;
        }
    }

    /// <summary>A hidden batch file: wait for this process to end, copy the new files in, start again.</summary>
    private static void LaunchInstaller(string newFiles, bool reopenWindow)
    {
        string appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string exe = Path.Combine(appDir, "MacroMaster.exe");
        int pid = Environment.ProcessId;
        string args = reopenWindow ? "" : "--autostart"; // --autostart = start in the tray
        string updateRoot = Path.GetDirectoryName(newFiles.TrimEnd('\\'))!;
        while (!string.Equals(Path.GetFileName(updateRoot), "MacroMaster-update", StringComparison.OrdinalIgnoreCase)
               && Path.GetDirectoryName(updateRoot) is { } parent)
        {
            updateRoot = parent;
        }

        string script = Path.Combine(Path.GetTempPath(), $"MacroMaster-update-{pid}.cmd");
        // Waits up to ~30 s for a clean exit (settings and profiles are saved on the way out), only then force-quits
        string content = $"""
            @echo off
            chcp 65001 >nul
            set /a tries=0
            :wait
            tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul
            if errorlevel 1 goto copy
            set /a tries+=1
            if %tries% geq 30 (
                taskkill /F /PID {pid} >nul 2>&1
                timeout /t 1 /nobreak >nul
                goto copy
            )
            timeout /t 1 /nobreak >nul
            goto wait
            :copy
            xcopy /Y /E /I /Q /R "{newFiles}\*" "{appDir}\" >nul
            start "" "{exe}" {args}
            rmdir /s /q "{updateRoot}" >nul 2>&1
            (goto) 2>nul & del "%~f0"
            """;
        File.WriteAllText(script, content, new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        });
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
