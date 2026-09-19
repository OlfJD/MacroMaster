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
