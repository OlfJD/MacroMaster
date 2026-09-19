using System.IO;
using System.Text.Json;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Parsers;

public static class MacroJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetAppDataDirectory()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroMaster");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    public static void SaveProfiles(List<MacroProfile> profiles, string filePath)
    {
        string json = JsonSerializer.Serialize(profiles, Options);
        File.WriteAllText(filePath, json);
    }

    public static List<MacroProfile>? LoadProfiles(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<MacroProfile>>(json, Options);
    }

    public static void SaveSettings(AppSettings settings, string filePath)
    {
        string json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(filePath, json);
    }

    public static AppSettings LoadSettings(string filePath)
    {
        if (!File.Exists(filePath)) return new AppSettings();
        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
