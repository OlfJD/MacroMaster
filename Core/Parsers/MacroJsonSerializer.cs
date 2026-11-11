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

    /// <summary>
    /// Where profiles and settings live. Set the MACROMASTER_DATA_DIR environment variable to use
    /// another folder (a portable copy, or a throw-away one for trying things out).
    /// </summary>
    public static string GetAppDataDirectory()
    {
        string? custom = Environment.GetEnvironmentVariable("MACROMASTER_DATA_DIR");
        string path = !string.IsNullOrWhiteSpace(custom)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroMaster");
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

    #region Single macros

    /// <summary>What an importable .json file holds.</summary>
    public enum ImportKind { Unknown, Profiles, Macros }

    /// <summary>
    /// Tells a profile file from a macro file by the one property each always has
    /// (a profile has "Macros", a macro has "Actions"). Unknown for anything else, unreadable files included.
    /// </summary>
    public static ImportKind DetectImportKind(string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement };
            if (elements.Count == 0 || elements.Any(el => el.ValueKind != JsonValueKind.Object)) return ImportKind.Unknown;

            bool Has(JsonElement el, string name) => el.EnumerateObject().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (elements.All(el => Has(el, nameof(MacroProfile.Macros)))) return ImportKind.Profiles;
            if (elements.All(el => Has(el, nameof(MacroDefinition.Actions)))) return ImportKind.Macros;
            return ImportKind.Unknown;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ImportKind.Unknown;
        }
    }

    /// <summary>Writes one or more macros to a file the user picked, to share or back up on their own.</summary>
    public static void ExportMacros(IEnumerable<MacroDefinition> macros, string filePath)
        => File.WriteAllText(filePath, JsonSerializer.Serialize(macros.ToList(), Options));

    /// <summary>
    /// Reads macros from an exported macro file (a list, or a single macro object). Each gets a new
    /// id and comes in switched off and not as the active mode: its hotkey may already be in use
    /// here, and a file from someone else shouldn't go live the moment it's opened.
    /// Throws <see cref="InvalidDataException"/> if the file isn't a macro file.
    /// </summary>
    public static List<MacroDefinition> ImportMacros(string filePath)
    {
        if (DetectImportKind(filePath) != ImportKind.Macros) throw new InvalidDataException("This isn't a MacroMaster macro file.");

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        var elements = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        var macros = new List<MacroDefinition>();
        foreach (var el in elements)
        {
            MacroDefinition? m;
            try { m = el.Deserialize<MacroDefinition>(Options); }
            catch (JsonException ex) { throw new InvalidDataException("This isn't a MacroMaster macro file.", ex); }
            if (m == null) continue;

            m.Id = Guid.NewGuid().ToString("N");
            m.Trigger ??= new MacroTrigger();
            m.Actions ??= new();
            m.IsEnabled = false;
            m.IsCurrentlyArmed = false;
            macros.Add(m);
        }
        if (macros.Count == 0) throw new InvalidDataException("This file contains no macros.");
        return macros;
    }

    #endregion

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
