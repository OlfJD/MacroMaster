using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace MacroMaster.Core.Native;

/// <summary>
/// Starting MacroMaster when the user signs in. Two ways:
/// <list type="bullet">
/// <item>Normal: the <c>HKCU\...\Run</c> registry value. Starts without administrator rights.</item>
/// <item>As administrator: a scheduled task "MacroMaster" with the highest rights and a logon
/// trigger. That is the only way Windows starts a program elevated at sign-in without asking every
/// time. Setting it up needs administrator rights once (one Windows permission prompt).</item>
/// </list>
/// Only one of the two is ever registered, so MacroMaster never starts twice.
/// </summary>
public static class AutoStartManager
{
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MacroMaster";
    internal static string TaskName = "MacroMaster"; // tests use another name

    public enum Outcome
    {
        /// <summary>Everything is set up as asked.</summary>
        Done,
        /// <summary>Administrator start wanted, but no permission (prompt declined or not allowed to ask): it starts normally instead.</summary>
        StartsWithoutAdmin,
        /// <summary>Something went wrong; the previous registration may still be in place.</summary>
        Failed
    }

    public static string GetExecutablePath()
    {
        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(procPath) && Path.GetExtension(procPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(procPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return procPath;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string directExe = Path.Combine(baseDir, "MacroMaster.exe");
        if (File.Exists(directExe)) return directExe;

        DirectoryInfo? parent = Directory.GetParent(baseDir);
        while (parent != null)
        {
            string candidate = Path.Combine(parent.FullName, "MacroMaster.exe");
            if (File.Exists(candidate)) return candidate;
            parent = parent.Parent;
        }

        return directExe;
    }

    public static bool IsElevated
    {
        get
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Makes the sign-in start match the settings. Slow-ish (runs schtasks.exe): call it off the UI
    /// thread. <paramref name="mayAskForPermission"/> = false never shows a Windows prompt; then an
    /// administrator start that isn't set up yet falls back to a normal start (unless this process
    /// already is elevated, which needs no prompt).
    /// </summary>
    public static Outcome Sync(bool enable, bool asAdmin, bool mayAskForPermission)
    {
        try
        {
            string exe = GetExecutablePath();

            if (!enable)
            {
                SetRunKey(null);
                if (TaskExists()) DeleteTask(mayAskForPermission);
                return Outcome.Done;
            }

            if (!asAdmin)
            {
                SetRunKey(exe);
                if (TaskExists() && !DeleteTask(mayAskForPermission))
                {
                    // Can't remove the elevated task without permission: keep only it, so we don't start twice
                    SetRunKey(null);
                    return Outcome.Failed;
                }
                return Outcome.Done;
            }

            // As administrator
            if (TaskPointsTo(exe))
            {
                SetRunKey(null);
                return Outcome.Done;
            }
            if (IsElevated || mayAskForPermission)
            {
                if (CreateTask(exe, elevatedTask: true, runElevated: !IsElevated))
                {
                    SetRunKey(null);
                    return Outcome.Done;
                }
            }
            // No permission: at least start normally
            SetRunKey(exe);
            return Outcome.StartsWithoutAdmin;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error configuring startup: {ex.Message}");
            return Outcome.Failed;
        }
    }

    #region Run key

    private static void SetRunKey(string? exe)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
        if (key == null) return;
        if (exe == null)
        {
            if (key.GetValue(AppName) != null) key.DeleteValue(AppName, false);
            return;
        }
        string expected = $"\"{exe}\" --autostart";
        // Exact compare so an entry without the --autostart flag (older versions) gets updated too
        if (!string.Equals(key.GetValue(AppName)?.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            key.SetValue(AppName, expected);
    }

    public static bool IsRunKeySet()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return !string.IsNullOrWhiteSpace(key?.GetValue(AppName)?.ToString());
        }
        catch { return false; }
    }

    #endregion

    #region Scheduled task

    /// <summary>The task's definition, or null when there is none (or it can't be read).</summary>
    private static string? QueryTaskXml()
    {
        var (code, output) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML", elevated: false);
        return code == 0 ? output : null;
    }

    public static bool TaskExists() => QueryTaskXml() != null || (TaskName == "MacroMaster" && GetTaskMarker() != null);

    /// <summary>
    /// The task exists, starts this exe and runs with the highest rights. A normal (not elevated)
    /// copy may not be allowed to read an elevated task, so a marker written when the task was set
    /// up counts too; without it that copy would add the Run key and MacroMaster would start twice.
    /// </summary>
    private static bool TaskPointsTo(string exe)
    {
        string? xml = QueryTaskXml();
        if (xml != null)
        {
            bool match = xml.Contains(SecurityElement.Escape(exe)!, StringComparison.OrdinalIgnoreCase)
                      && xml.Contains("HighestAvailable", StringComparison.OrdinalIgnoreCase);
            SetTaskMarker(match ? exe : null);
            return match;
        }
        if (IsElevated)
        {
            SetTaskMarker(null); // elevated, we could read it if it existed: it's gone
            return false;
        }
        return string.Equals(GetTaskMarker(), exe, StringComparison.OrdinalIgnoreCase);
    }

    private const string MarkerKey = @"Software\MacroMaster";
    private const string MarkerValue = "AdminAutostartTask";

    private static string? GetTaskMarker()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MarkerKey, false);
            return key?.GetValue(MarkerValue)?.ToString();
        }
        catch { return null; }
    }

    private static void SetTaskMarker(string? exe)
    {
        if (TaskName != "MacroMaster") return; // tests
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(MarkerKey, true);
            if (exe == null) key.DeleteValue(MarkerValue, false);
            else key.SetValue(MarkerValue, exe);
        }
        catch { }
    }

    /// <param name="elevatedTask">Run with the highest rights (false only for tests: needs no permission).</param>
    /// <param name="runElevated">Run schtasks itself elevated (shows the Windows permission prompt).</param>
    internal static bool CreateTask(string exe, bool elevatedTask, bool runElevated)
    {
        // Written by this (normal) process so the task belongs to the signed-in user, not to
        // whichever administrator account the permission prompt was answered with
        string user = WindowsIdentity.GetCurrent().Name;
        string xmlPath = Path.Combine(Path.GetTempPath(), $"MacroMaster-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exe, user, elevatedTask), Encoding.Unicode);
            var (code, _) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", runElevated);
            if (code == 0 && elevatedTask) SetTaskMarker(exe);
            return code == 0;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    /// <summary>Deletes the task; asks for permission if needed and allowed.</summary>
    internal static bool DeleteTask(bool mayAskForPermission)
    {
        bool deleted = RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevated: false).Code == 0
                    || (!IsElevated && mayAskForPermission && RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevated: true).Code == 0);
        if (deleted) SetTaskMarker(null);
        return deleted;
    }

    internal static string BuildTaskXml(string exe, string user, bool elevated)
    {
        string e(string s) => SecurityElement.Escape(s)!;
        string dir = Path.GetDirectoryName(exe) ?? "";
        // ExecutionTimeLimit PT0S: the default would end MacroMaster after 3 days. Priority 4 =
        // normal; the default (7) runs the task below normal priority, bad for input timing.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts MacroMaster in the tray when you sign in{(elevated ? ", with administrator rights so hotkeys also work in games that run as administrator" : "")}. Managed by MacroMaster (Settings → Start with Windows).</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{e(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{e(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{(elevated ? "HighestAvailable" : "LeastPrivilege")}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{e(exe)}"</Command>
                  <Arguments>--autostart</Arguments>
                  <WorkingDirectory>{e(dir)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) RunSchtasks(string args, bool elevated)
    {
        string exe = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        try
        {
            if (elevated)
            {
                // The permission prompt needs ShellExecute, which can't capture output
                using var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (p == null) return (-1, "");
                p.WaitForExit(30000);
                return (p.HasExited ? p.ExitCode : -1, "");
            }

            using var proc = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            });
            if (proc == null) return (-1, "");
            string output = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);
            return (proc.HasExited ? proc.ExitCode : -1, output);
        }
        catch (Win32Exception)
        {
            return (-1, ""); // "No" in the permission prompt
        }
    }

    #endregion
}
