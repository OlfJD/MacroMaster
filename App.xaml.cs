using System.IO;
using System.Windows;
using MacroMaster.Core.Native;
using MacroMaster.Core.Parsers;

namespace MacroMaster;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    // Held for the whole run: while it exists, this copy owns the data folder
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool autostart = e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("-minimized", StringComparison.OrdinalIgnoreCase));

        // "Restart as administrator" starts this copy while the old one is still closing: wait for it
        int replaces = Array.IndexOf(e.Args, "--replaces");
        if (replaces >= 0 && replaces + 1 < e.Args.Length && int.TryParse(e.Args[replaces + 1], out int oldPid))
        {
            try { using var old = System.Diagnostics.Process.GetProcessById(oldPid); old.WaitForExit(10000); }
            catch (ArgumentException) { } // already gone
        }

        // One copy per data folder: two copies each saved their own list over the other's (macros made
        // in one window were lost) and both ran every hotkey. Before the main window exists, which
        // would install the hooks.
        _instance = SingleInstance.Claim(MacroJsonSerializer.GetAppDataDirectory());
        if (!_instance.IsFirst)
        {
            if (autostart)
            {
                // Started by Windows at sign-in while a copy already runs (e.g. both the old normal and
                // the new administrator start fired): the running copy wins
                AutoStartManager.Log("started at sign-in, but MacroMaster already runs: this copy quits");
            }
            else if (!_instance.AskRunningCopyToShow())
            {
                System.Windows.MessageBox.Show("MacroMaster is already running. Open it from its icon in the notification area of the taskbar.",
                                               "MacroMaster", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Environment.Exit(0);
        }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true;
        };

        // Started at sign-in: only the tray icon, the window stays hidden until opened from there
        var window = new MainWindow();
        MainWindow = window;
        if (autostart) window.StartHidden();
        else window.Show();

        // Started again (Start menu, desktop icon, "Launch MacroMaster.bat"): show this copy instead
        _instance.ListenForShowRequests(() => Dispatcher.BeginInvoke(window.ShowFromAnotherLaunch));
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string crashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.AppendAllText(crashPath, $"[{DateTime.Now}] UNHANDLED EXCEPTION:\n{ex}\n\n");
            System.Windows.MessageBox.Show($"MacroMaster encountered an error:\n\n{ex.Message}\n\nCheck crash.log for details.", "MacroMaster Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}
