using System.IO;
using System.Windows;

namespace MacroMaster;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Started by Windows at sign-in while a copy is already running (e.g. both the old normal
        // and the new administrator start fired once): the running copy wins.
        if (e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)))
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            if (System.Diagnostics.Process.GetProcessesByName(me.ProcessName).Any(p => p.Id != me.Id))
            {
                Environment.Exit(0); // before the main window exists (it would install the hooks)
            }
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
