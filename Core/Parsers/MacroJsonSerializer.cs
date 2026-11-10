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

    /// <summary>
    /// Writes profiles to a file the user picked (backup / sharing). Same format as the app's own
    /// profiles.json, so that file or one of its backups can be imported as well.
    /// </summary>
    public static void ExportProfiles(IEnumerable<MacroProfile> profiles, string filePath)
        => File.WriteAllText(filePath, JsonSerializer.Serialize(profiles.ToList(), Options));

    /// <summary>
    /// Reads profiles from an exported file (a list, or a single profile object). Every profile and
    /// macro gets a fresh id so importing the same file twice, or a copy of a profile that still
    /// exists, can't collide. Throws <see cref="InvalidDataException"/> if the file isn't a profile file.
    /// </summary>
    public static List<MacroProfile> ImportProfiles(string filePath)
    {
        string json = File.ReadAllText(filePath);
        List<MacroProfile?>? list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement };

            // Any JSON object would deserialize into an (empty) profile, so insist on the one
            // property every profile file has before accepting it
            bool looksLikeProfiles = elements.Count > 0 && elements.All(el =>
                el.ValueKind == JsonValueKind.Object &&
                el.EnumerateObject().Any(p => p.Name.Equals(nameof(MacroProfile.Macros), StringComparison.OrdinalIgnoreCase)));
            if (!looksLikeProfiles) throw new InvalidDataException("This isn't a MacroMaster profile file.");

            list = elements.Select(el => el.Deserialize<MacroProfile>(Options)).ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This isn't a MacroMaster profile file.", ex);
        }

        var profiles = (list ?? new()).Where(p => p != null && p.Macros != null).Select(p => p!).ToList();
        if (profiles.Count == 0) throw new InvalidDataException("This file contains no profiles.");

        foreach (var p in profiles)
        {
            p.Id = Guid.NewGuid().ToString("N");
            p.IsActive = false;
            foreach (var m in p.Macros)
            {
                m.Id = Guid.NewGuid().ToString("N");
                m.Trigger ??= new MacroTrigger();
                m.Actions ??= new();
            }
        }
        return profiles;
    }

    /// <summary>An independent copy of a macro with a new id (used by "Duplicate").</summary>
    public static MacroDefinition CloneMacro(MacroDefinition macro)
    {
        var copy = JsonSerializer.Deserialize<MacroDefinition>(JsonSerializer.Serialize(macro, Options), Options)!;
        copy.Id = Guid.NewGuid().ToString("N");
        return copy;
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
