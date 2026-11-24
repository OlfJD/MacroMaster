using System.IO;

namespace MacroMaster.Core;

/// <summary>
/// errors.log in the data folder: problems nobody would see otherwise, such as a save that failed or
/// a macro that stopped on an error. One line each (plus the exception), the newer half kept once
/// the file passes 64 KB, like autostart.log.
/// </summary>
public static class AppLog
{
    /// <summary>Null = no log (tests).</summary>
    internal static string? LogPath = Path.Combine(Parsers.MacroJsonSerializer.GetAppDataDirectory(), "errors.log");

    private static readonly object Lock = new();

    public static void Error(string what, Exception? ex = null)
    {
        if (LogPath == null) return;
        try
        {
            lock (Lock)
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 64 * 1024)
                {
                    var lines = File.ReadAllLines(LogPath);
                    File.WriteAllLines(LogPath, lines.Skip(lines.Length / 2)); // keep the newer half
                }
                var version = typeof(AppLog).Assembly.GetName().Version;
                string detail = ex == null ? "" : $"{Environment.NewLine}    {ex.GetType().Name}: {ex.Message}{Environment.NewLine}    {ex.StackTrace?.Replace(Environment.NewLine, Environment.NewLine + "    ")}";
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  v{version?.ToString(3)}  {what}{detail}{Environment.NewLine}");
            }
        }
        catch { /* logging must never cause a problem of its own */ }
    }
}
