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

    /// <summary>
    /// Writes to a temp file first and then swaps it in, so a crash or power cut mid-save
    /// can never leave a half-written (unreadable) profiles file behind.
    /// </summary>
    private static void WriteAtomic(string filePath, string contents)
    {
        string tmp = filePath + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(filePath))
            File.Replace(tmp, filePath, filePath + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tmp, filePath);
    }

    public static void SaveProfiles(List<MacroProfile> profiles, string filePath)
        => WriteAtomic(filePath, JsonSerializer.Serialize(profiles, Options));

    public static void SaveSettings(AppSettings settings, string filePath)
        => WriteAtomic(filePath, JsonSerializer.Serialize(settings, Options));

    /// <summary>Result of loading profiles: <see cref="Profiles"/> is null when there is no usable file.</summary>
    public sealed record ProfileLoadResult(List<MacroProfile>? Profiles, string? BrokenFileBackup);

    /// <summary>
    /// Loads profiles. An unreadable file is moved aside (never overwritten) and the previous
    /// good copy (.bak) is used instead if there is one.
    /// </summary>
    public static ProfileLoadResult LoadProfiles(string filePath)
    {
        if (!File.Exists(filePath)) return new(null, null);

        try
        {
            return new(JsonSerializer.Deserialize<List<MacroProfile>>(File.ReadAllText(filePath), Options), null);
        }
        catch (Exception)
        {
            string broken = Path.Combine(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileNameWithoutExtension(filePath)}.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(filePath, broken); } catch { broken = filePath; }

            string backup = filePath + ".bak";
            try
            {
                if (File.Exists(backup))
                    return new(JsonSerializer.Deserialize<List<MacroProfile>>(File.ReadAllText(backup), Options), broken);
            }
            catch { }

            return new(null, broken);
        }
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
