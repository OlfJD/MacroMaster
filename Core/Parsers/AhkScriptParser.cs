using System.IO;
using System.Text.RegularExpressions;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Parsers;

public static class AhkScriptParser
{
    public static List<MacroDefinition> ParseAhkFile(string filePath)
    {
        if (!File.Exists(filePath)) return new List<MacroDefinition>();
        string scriptText = File.ReadAllText(filePath);
        string filename = Path.GetFileNameWithoutExtension(filePath);
        return ParseAhkContent(scriptText, filename);
    }

    public static List<MacroDefinition> ParseAhkContent(string scriptText, string defaultName = "Imported Macro")
    {
        var result = new List<MacroDefinition>();
        if (string.IsNullOrWhiteSpace(scriptText)) return result;

        // 1. Detect Game / Process filter
        string processFilter = "*";
        var exeMatch = Regex.Match(scriptText, @"(?:GameEXE\s*:=\s*[""']([^""']+)[""']|WinActive\([^""']*[""']ahk_exe\s+([^""']+)[""']|#IfWinActive\s+(?:ahk_exe\s+)?([^\r\n]+))", RegexOptions.IgnoreCase);
        if (exeMatch.Success)
        {
            processFilter = (exeMatch.Groups[1].Value + exeMatch.Groups[2].Value + exeMatch.Groups[3].Value).Trim();
        }

        // 2. Check if script has armed modes (like Rocket.ahk with currentMacro == 1 / 2, PgUp/PgDn)
        if (scriptText.Contains("currentMacro") && (scriptText.Contains("PgUp") || scriptText.Contains("PgDn")))
        {
            var armedMacros = ParseMultiModeSkateScript(scriptText, defaultName, processFilter);
            if (armedMacros.Count > 0)
            {
                result.AddRange(armedMacros);
            }
        }

        // 3. If no armed modes were found or there are additional standalone hotkeys, parse blocks
        if (result.Count == 0 || HasStandaloneHotkeys(scriptText))
        {
            var standardMacros = ParseStandardHotkeys(scriptText, defaultName, processFilter, result);
            result.AddRange(standardMacros);
        }

        // If nothing was parsed as structured hotkeys, wrap the whole script as an action sequence
        if (result.Count == 0)
        {
            var fallback = new MacroDefinition
            {
                Name = defaultName,
                ProcessFilter = processFilter,
                ImportedFromAhk = true,
                OriginalAhkScript = scriptText,
                Mode = ExecutionMode.Once
            };
            fallback.Trigger.PrimaryKey = "F6";
            fallback.Actions = new System.Collections.ObjectModel.ObservableCollection<MacroAction>(ParseActionLines(scriptText));
            result.Add(fallback);
        }

        return result;
    }

    private static bool HasStandaloneHotkeys(string scriptText)
    {
        // Check for secondary keys like "End::", "PgUp::", "F6::"
        return Regex.IsMatch(scriptText, @"(?:\r?\n|^)\s*(?:End|\*?Pg[Up|Dn]|F\d+|[a-zA-Z0-9_]+)\s*::", RegexOptions.IgnoreCase);
    }

    private static List<MacroDefinition> ParseMultiModeSkateScript(string scriptText, string baseName, string processFilter)
    {
        var list = new List<MacroDefinition>();

        // Find chord keys (e.g. GetKeyState("ä", "P") and GetKeyState("ö", "P"))
        var chordKeys = new List<string>();
        var chordMatches = Regex.Matches(scriptText, @"GetKeyState\(\s*[""']([^""']+)[""']\s*,\s*[""']P[""']\s*\)", RegexOptions.IgnoreCase);
        foreach (Match m in chordMatches)
        {
            string k = m.Groups[1].Value.Trim();
            if (!chordKeys.Contains(k)) chordKeys.Add(k);
        }

        // Find main trigger key (e.g. l::)
        string mainTrigger = "l";
        var trigMatch = Regex.Match(scriptText, @"(?:\r?\n|^)\s*([a-zA-Z0-9_]+)\s*::\s*\{[^}]*currentMacro", RegexOptions.IgnoreCase);
        if (trigMatch.Success)
        {
            mainTrigger = trigMatch.Groups[1].Value.Trim();
        }

        // Extract Mode 2 (Wellskate / PgUp)
        var mode2Block = Regex.Match(scriptText, @"else\s+if\s*\(\s*currentMacro\s*==\s*2\s*\)\s*\{([^}]+)\}", RegexOptions.IgnoreCase);
        if (mode2Block.Success)
        {
            var wellskate = new MacroDefinition
            {
                Name = "Destiny 2 - Wellskate",
                Description = "Heavy weapon right click cancel into jump and super skate",
                Category = "Destiny 2",
                RequiresArming = true,
                ArmingKey = "PgUp",
                ArmingGroupName = "Skate Modes",
                ArmingDisplayName = "Wellskate (PgUp Mode)",
                IsCurrentlyArmed = true,
                ProcessFilter = processFilter,
                ImportedFromAhk = true,
                OriginalAhkScript = mode2Block.Groups[1].Value
            };
            wellskate.Trigger.PrimaryKey = mainTrigger;
            wellskate.Trigger.ChordKeys = new List<string>(chordKeys);
            wellskate.Actions = new System.Collections.ObjectModel.ObservableCollection<MacroAction>(ParseActionLines(mode2Block.Groups[1].Value));
            list.Add(wellskate);
        }

        // Extract Mode 1 (Groundskate / PgDn)
        var mode1Block = Regex.Match(scriptText, @"if\s*\(\s*currentMacro\s*==\s*1\s*\)\s*\{([^}]+)\}", RegexOptions.IgnoreCase);
        if (mode1Block.Success)
        {
            var groundskate = new MacroDefinition
            {
                Name = "Destiny 2 - Groundskate",
                Description = "Jump, light attack, jump, super quick ground skate sequence",
                Category = "Destiny 2",
                RequiresArming = true,
                ArmingKey = "PgDn",
                ArmingGroupName = "Skate Modes",
                ArmingDisplayName = "Groundskate (PgDn Mode)",
                IsCurrentlyArmed = false,
                ProcessFilter = processFilter,
                ImportedFromAhk = true,
                OriginalAhkScript = mode1Block.Groups[1].Value
            };
            groundskate.Trigger.PrimaryKey = mainTrigger;
            groundskate.Trigger.ChordKeys = new List<string>(chordKeys);
            groundskate.Actions = new System.Collections.ObjectModel.ObservableCollection<MacroAction>(ParseActionLines(mode1Block.Groups[1].Value));
            list.Add(groundskate);
        }

        return list;
    }

    private static List<MacroDefinition> ParseStandardHotkeys(string scriptText, string defaultName, string defaultProcess, List<MacroDefinition> existing)
    {
        var list = new List<MacroDefinition>();

        // Check for chord keys defined in #HotIf
        var chordKeys = new List<string>();
        var chordMatches = Regex.Matches(scriptText, @"GetKeyState\(\s*[""']([^""']+)[""']\s*,\s*[""']P[""']\s*\)", RegexOptions.IgnoreCase);
        foreach (Match m in chordMatches)
        {
            string k = m.Groups[1].Value.Trim();
            if (!chordKeys.Contains(k)) chordKeys.Add(k);
        }

        // Match hotkey blocks: Key:: { ... } or Key:: ...
        var hotkeyMatches = Regex.Matches(scriptText, @"(?:\r?\n|^)\s*([*~^!+#]*)([a-zA-Z0-9_]+)\s*::\s*(\{([^}]+)\}|[^\r\n]+)", RegexOptions.Multiline);

        foreach (Match match in hotkeyMatches)
        {
            string modifiers = match.Groups[1].Value;
            string key = match.Groups[2].Value.Trim();
            string body = match.Groups[4].Success ? match.Groups[4].Value : match.Groups[3].Value;

            // Skip safety killswitches like F12::ExitApp()
            if (body.Contains("ExitApp", StringComparison.OrdinalIgnoreCase)) continue;

            // Skip arming hotkeys that were already processed
            if (body.Contains("currentMacro :=") || body.Contains("currentMacro:=")) continue;

            // Skip if this trigger key was already parsed in existing skate macros
            if (existing.Any(e => e.Trigger.PrimaryKey.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;

            string macroName = GenerateMacroName(key, body, defaultName);
            var mode = ExecutionMode.Once;

            // Check if toggle script (e.g. Leftclick.ahk with holding := !holding)
            bool isToggle = Regex.IsMatch(body, @"holding\s*:=\s*!holding|active\s*:=\s*!active|Toggle", RegexOptions.IgnoreCase);
            if (isToggle)
            {
                mode = ExecutionMode.Toggle;
            }

            var macro = new MacroDefinition
            {
                Name = macroName,
                ProcessFilter = defaultProcess,
                ImportedFromAhk = true,
                OriginalAhkScript = body,
                Mode = mode
            };

            macro.Trigger.PrimaryKey = key;
            if (chordKeys.Count > 0 && !key.Equals("End", StringComparison.OrdinalIgnoreCase) && !key.Equals("PgUp", StringComparison.OrdinalIgnoreCase) && !key.Equals("PgDn", StringComparison.OrdinalIgnoreCase))
            {
                macro.Trigger.ChordKeys = new List<string>(chordKeys);
            }

            if (modifiers.Contains("^")) macro.Trigger.RequireCtrl = true;
            if (modifiers.Contains("+")) macro.Trigger.RequireShift = true;
            if (modifiers.Contains("!")) macro.Trigger.RequireAlt = true;

            var actions = ParseActionLines(body);
            macro.Actions = new System.Collections.ObjectModel.ObservableCollection<MacroAction>(actions);
            list.Add(macro);
        }

        return list;
    }

    private static string GenerateMacroName(string key, string body, string fallback)
    {
        if (body.Contains("Left Down") && body.Contains("SpamRightClick")) return "Minecraft Mine & Place Loop";
        if (body.Contains("Click \"Down\"") || body.Contains("Click(\"Down\")") || body.Contains("Click \"Left Down\"")) return "Toggle Left Click Hold";
        if (body.Contains("Media_Play_Pause")) return "Media Play / Pause";
        if (body.Contains("mouse_event")) return "Rocket Recoil Pull-Down";
        if (body.Contains("{q down}")) return "Lumina Quick Shot & Ability";
        if (body.Contains("{v}") && body.Contains("{Esc}")) return "V & Escape Quick Action";
        return $"{fallback} ({key.ToUpperInvariant()})";
    }

    public static List<MacroAction> ParseActionLines(string text)
    {
        var actions = new List<MacroAction>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith(";") || string.IsNullOrWhiteSpace(line)) continue;

            // 1. Sleep / Delays
            var sleepMatch = Regex.Match(line, @"Sleep\s*\(?\s*(\d+)\s*\)?", RegexOptions.IgnoreCase);
            if (sleepMatch.Success && int.TryParse(sleepMatch.Groups[1].Value, out int ms))
            {
                actions.Add(new MacroAction { Type = ActionType.Delay, DelayMs = ms });
                continue;
            }

            // 2. Hardware Mouse Movement (mouse_event)
            var mouseMoveMatch = Regex.Match(line, @"mouse_event[""'\s,]+(\d+)[""'\s,]+(-?\d+)[""'\s,]+(-?\d+)", RegexOptions.IgnoreCase);
            if (mouseMoveMatch.Success)
            {
                int dx = int.Parse(mouseMoveMatch.Groups[2].Value);
                int dy = int.Parse(mouseMoveMatch.Groups[3].Value);
                actions.Add(new MacroAction { Type = ActionType.MouseMoveRelative, MoveX = dx, MoveY = dy });
                continue;
            }

            // 3. Mouse Clicks
            if (Regex.IsMatch(line, @"Click\s*\(?\s*[""']?Right\s+Down[""']?\s*\)?", RegexOptions.IgnoreCase))
            {
                actions.Add(new MacroAction { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Right });
                continue;
            }
            if (Regex.IsMatch(line, @"Click\s*\(?\s*[""']?Right\s+Up[""']?\s*\)?", RegexOptions.IgnoreCase))
            {
                actions.Add(new MacroAction { Type = ActionType.MouseUp, MouseButton = MouseButtonType.Right });
                continue;
            }
            if (Regex.IsMatch(line, @"Click\s*\(?\s*[""']?(?:Left\s+Down|Down)[""']?\s*\)?", RegexOptions.IgnoreCase))
            {
                actions.Add(new MacroAction { Type = ActionType.MouseDown, MouseButton = MouseButtonType.Left });
                continue;
            }
            if (Regex.IsMatch(line, @"Click\s*\(?\s*[""']?(?:Left\s+Up|Up)[""']?\s*\)?", RegexOptions.IgnoreCase))
            {
                actions.Add(new MacroAction { Type = ActionType.MouseUp, MouseButton = MouseButtonType.Left });
                continue;
            }
            if (Regex.IsMatch(line, @"Click\s*\(?\s*[""']?Right[""']?\s*\)?", RegexOptions.IgnoreCase))
            {
                actions.Add(new MacroAction { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Right });
                continue;
            }
            if (Regex.IsMatch(line, @"Click\s*\(?\s*[""']?(?:Left)?[""']?\s*\)?", RegexOptions.IgnoreCase))
            {
                actions.Add(new MacroAction { Type = ActionType.MouseClick, MouseButton = MouseButtonType.Left });
                continue;
            }

            // 4. Key Sends (Send, SendEvent, SendInput)
            var sendMatch = Regex.Match(line, @"(?:Send|SendEvent|SendInput)\s*\(?\s*[""']\{?([^{}""']+)[""']\s*\)?", RegexOptions.IgnoreCase);
            if (sendMatch.Success)
            {
                string keyToken = sendMatch.Groups[1].Value.Trim().Trim('{', '}');

                if (keyToken.Equals("Media_Play_Pause", StringComparison.OrdinalIgnoreCase))
                {
                    actions.Add(new MacroAction { Type = ActionType.Media, MediaCommand = "PlayPause" });
                    continue;
                }

                if (keyToken.EndsWith(" down", StringComparison.OrdinalIgnoreCase))
                {
                    string k = keyToken.Substring(0, keyToken.Length - 5).Trim();
                    actions.Add(new MacroAction { Type = ActionType.KeyDown, Key = k });
                }
                else if (keyToken.EndsWith(" up", StringComparison.OrdinalIgnoreCase))
                {
                    string k = keyToken.Substring(0, keyToken.Length - 3).Trim();
                    actions.Add(new MacroAction { Type = ActionType.KeyUp, Key = k });
                }
                else
                {
                    actions.Add(new MacroAction { Type = ActionType.KeyTap, Key = keyToken });
                }
            }
        }

        return actions;
    }
}
