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

    private static bool HasStandaloneHotkeys(string scriptText) => FindHotkeys(scriptText).Any();

    /// <summary>
    /// A hotkey line: modifier symbols ($ ~ * ^ ! + # &lt; &gt;), the key (or "a &amp; b", a two-key
    /// combination), "::" and whatever follows on the same line.
    /// </summary>
    private static readonly Regex HotkeyLine = new(@"^\s*(?<mods>[*~$^!+#<>]*)(?<first>[\p{L}\p{N}_]+)(?:\s*&\s*(?<second>[\p{L}\p{N}_]+))?\s*::(?<rest>.*)$");

    private sealed record AhkHotkey(string Modifiers, string Key, string? ChordKey, string Body);

    /// <summary>
    /// Every hotkey with its body, in the forms AutoHotkey allows: on the same line ("F9::Send ..."),
    /// a v2 block in braces (the closing brace is found by counting, so braces inside Send strings
    /// such as "{q down}" don't end it), or v1 lines up to "return".
    /// </summary>
    private static IEnumerable<AhkHotkey> FindHotkeys(string scriptText)
    {
        var lines = scriptText.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = HotkeyLine.Match(lines[i]);
            if (!m.Success) continue;

            string rest = m.Groups["rest"].Value.Trim();
            string first = m.Groups["first"].Value, second = m.Groups["second"].Value;
            string key = second.Length > 0 ? second : first;
            string? chord = second.Length > 0 ? first : null;
            string body;

            if (rest.StartsWith('{') || (rest.Length == 0 && NextCodeLine(lines, i + 1) is { } open && lines[open].TrimStart().StartsWith('{')))
            {
                // Block: from the opening brace to its partner
                int start = rest.Length > 0 ? i : NextCodeLine(lines, i + 1)!.Value;
                body = BlockContent(lines, start, rest.Length > 0 ? rest : lines[start].TrimStart(), out int endLine);
                i = endLine;
            }
            else if (rest.Length > 0)
            {
                body = rest; // one line
            }
            else
            {
                // v1: the following lines up to "return" (or the next hotkey)
                var collected = new List<string>();
                int line = i + 1;
                for (; line < lines.Length; line++)
                {
                    string t = lines[line].Trim();
                    if (t.Equals("return", StringComparison.OrdinalIgnoreCase) || HotkeyLine.IsMatch(lines[line])) break;
                    collected.Add(lines[line]);
                }
                body = string.Join("\n", collected);
                i = line < lines.Length && !HotkeyLine.IsMatch(lines[line]) ? line : line - 1;
            }

            yield return new AhkHotkey(m.Groups["mods"].Value, key, chord, body);
        }
    }

    /// <summary>
    /// What's inside the block that opens at the start of <paramref name="fromBrace"/> (line
    /// <paramref name="line"/>), up to its partner brace, which may be lines further down.
    /// </summary>
    private static string BlockContent(string[] lines, int line, string fromBrace, out int endLine)
    {
        var block = new System.Text.StringBuilder();
        int depth = 0;
        string text = fromBrace;
        while (true)
        {
            int end = ScanBraces(text, ref depth);
            if (end >= 0) { block.Append(text[..end]); break; }
            block.Append(text).Append('\n');
            if (++line >= lines.Length) break;
            text = lines[line];
        }
        endLine = Math.Min(line, lines.Length - 1);
        string inside = block.ToString().Trim();
        return (inside.StartsWith('{') ? inside[1..] : inside).Trim();
    }

    /// <summary>The body of the block right after <paramref name="header"/> (e.g. "if (currentMacro == 1)"), or null.</summary>
    private static string? BlockAfter(string scriptText, string header)
    {
        string text = scriptText.Replace("\r\n", "\n");
        var m = Regex.Match(text, header + @"\s*\{", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        int brace = m.Index + m.Length - 1;
        int line = text.AsSpan(0, brace).Count('\n');
        int lineStart = text.LastIndexOf('\n', Math.Max(0, brace - 1)) + 1;
        var lines = text.Split('\n');
        return BlockContent(lines, line, lines[line][(brace - lineStart)..], out _);
    }

    /// <summary>Index of the next line that isn't empty or a comment, or null.</summary>
    private static int? NextCodeLine(string[] lines, int from)
    {
        for (int i = from; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length > 0 && !t.StartsWith(';')) return i;
        }
        return null;
    }

    /// <summary>
    /// Counts braces outside "…" / '…' strings. Returns the index where the depth drops back to 0
    /// (the block's closing brace), or -1 if it stays open past this text.
    /// </summary>
    private static int ScanBraces(string text, ref int depth)
    {
        char quote = '\0'; // strings end with their line
        for (int k = 0; k < text.Length; k++)
        {
            char c = text[k];
            if (quote != '\0')
            {
                if (c == quote && (k == 0 || text[k - 1] != '`')) quote = '\0';
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == ';' && (k == 0 || char.IsWhiteSpace(text[k - 1]))) return -1; // the rest of the line is a comment
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return k;
        }
        return -1;
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

        // Find main trigger key (e.g. l::): the hotkey that reads currentMacro rather than setting it
        string mainTrigger = "l";
        var trigger = FindHotkeys(scriptText).FirstOrDefault(h => h.Body.Contains("currentMacro") && !Regex.IsMatch(h.Body, @"currentMacro\s*:="));
        if (trigger != null)
        {
            mainTrigger = trigger.Key;
        }

        // Extract Mode 2 (Wellskate / PgUp). The block ends at its own closing brace, not at the
        // first "}" (which is often inside a Send such as "{j down}").
        string? mode2Body = BlockAfter(scriptText, @"else\s+if\s*\(\s*currentMacro\s*==\s*2\s*\)");
        if (mode2Body != null)
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
                OriginalAhkScript = mode2Body
            };
            wellskate.Trigger.PrimaryKey = mainTrigger;
            wellskate.Trigger.ChordKeys = new List<string>(chordKeys);
            wellskate.Actions = new System.Collections.ObjectModel.ObservableCollection<MacroAction>(ParseActionLines(mode2Body));
            list.Add(wellskate);
        }

        // Extract Mode 1 (Groundskate / PgDn)
        string? mode1Body = BlockAfter(scriptText, @"if\s*\(\s*currentMacro\s*==\s*1\s*\)");
        if (mode1Body != null)
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
                OriginalAhkScript = mode1Body
            };
            groundskate.Trigger.PrimaryKey = mainTrigger;
            groundskate.Trigger.ChordKeys = new List<string>(chordKeys);
            groundskate.Actions = new System.Collections.ObjectModel.ObservableCollection<MacroAction>(ParseActionLines(mode1Body));
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

        // Hotkeys with their bodies: Key:: ..., Key:: { ... } (v2) or Key:: ... return (v1)
        foreach (var hotkey in FindHotkeys(scriptText))
        {
            string modifiers = hotkey.Modifiers;
            string key = hotkey.Key;
            string body = hotkey.Body;

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
            if (hotkey.ChordKey != null)
            {
                macro.Trigger.ChordKeys = new List<string> { hotkey.ChordKey }; // "a & b::" = hold a, press b
            }
            else if (chordKeys.Count > 0 && !key.Equals("End", StringComparison.OrdinalIgnoreCase) && !key.Equals("PgUp", StringComparison.OrdinalIgnoreCase) && !key.Equals("PgDn", StringComparison.OrdinalIgnoreCase))
            {
                macro.Trigger.ChordKeys = new List<string>(chordKeys);
            }

            if (modifiers.Contains('^')) macro.Trigger.RequireCtrl = true;
            if (modifiers.Contains('+')) macro.Trigger.RequireShift = true;
            if (modifiers.Contains('!')) macro.Trigger.RequireAlt = true;
            if (modifiers.Contains('#')) macro.Trigger.RequireWin = true;

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
            string line = StripComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 1. Sleep / Delays: "Sleep 30", "Sleep(30)", v1 "Sleep, 30"
            var sleepMatch = Regex.Match(line, @"\bSleep\s*[,(]?\s*(\d+)", RegexOptions.IgnoreCase);
            if (sleepMatch.Success && int.TryParse(sleepMatch.Groups[1].Value, out int ms))
            {
                actions.Add(new MacroAction { Type = ActionType.Delay, DelayMs = ms });
                continue;
            }

            // Key sends come before the click checks below, which would also match "Send "{Click}""
            var sendMatch = Regex.Match(line, @"^(?<cmd>SendInput|SendEvent|SendPlay|SendText|Send)\b\s*(?:,\s*|\(\s*)?(?<arg>.*)$", RegexOptions.IgnoreCase);
            if (sendMatch.Success)
            {
                string keys = SendArgument(sendMatch.Groups["arg"].Value);
                if (sendMatch.Groups["cmd"].Value.Equals("SendText", StringComparison.OrdinalIgnoreCase))
                {
                    if (keys.Length > 0) actions.Add(new MacroAction { Type = ActionType.Text, TextContent = keys });
                }
                else
                {
                    actions.AddRange(SendSteps(keys));
                }
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
        }

        return actions;
    }

    /// <summary>Drops a "; comment" at the end of a line (a semicolon after a space, outside quotes).</summary>
    private static string StripComment(string line)
    {
        char quote = '\0';
        for (int k = 0; k < line.Length; k++)
        {
            char c = line[k];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == ';' && (k == 0 || char.IsWhiteSpace(line[k - 1]))) return line[..k];
        }
        return line;
    }

    /// <summary>What a Send line sends: the quoted string of v2 ("{q down}", ("{v}")), or the plain rest of a v1 line.</summary>
    private static string SendArgument(string arg)
    {
        arg = arg.Trim();
        if (arg.Length > 0 && arg[0] is '"' or '\'')
        {
            int close = arg.LastIndexOf(arg[0]);
            return close > 0 ? arg[1..close] : arg[1..];
        }
        return arg.TrimEnd(')').TrimEnd();
    }

    /// <summary>
    /// AutoHotkey's Send syntax as steps: {Esc} a key, {q down} / {q up} hold and let go, {Tab 3}
    /// three times, ^ ! + # Ctrl / Alt / Shift / Win for the next key, {Click} a click. A single
    /// plain character is a key press, longer plain text is typed as text.
    /// </summary>
    private static IEnumerable<MacroAction> SendSteps(string keys)
    {
        var steps = new List<MacroAction>();
        var modifiers = new List<string>();
        var text = new System.Text.StringBuilder();

        void FlushText()
        {
            if (text.Length == 1) steps.Add(new MacroAction { Type = ActionType.KeyTap, Key = text.ToString() });
            else if (text.Length > 1) steps.Add(new MacroAction { Type = ActionType.Text, TextContent = text.ToString() });
            text.Clear();
        }

        void Add(IEnumerable<MacroAction> keySteps)
        {
            // Modifiers wrap the key: Ctrl down, key, Ctrl up
            foreach (var m in modifiers) steps.Add(new MacroAction { Type = ActionType.KeyDown, Key = m });
            steps.AddRange(keySteps);
            for (int i = modifiers.Count - 1; i >= 0; i--) steps.Add(new MacroAction { Type = ActionType.KeyUp, Key = modifiers[i] });
            modifiers.Clear();
        }

        for (int k = 0; k < keys.Length; k++)
        {
            char c = keys[k];
            int close = c == '{' ? keys.IndexOf('}', k + 2) : -1; // k + 2: "{}}" sends a "}"
            if (c == '{' && close > 0)
            {
                FlushText();
                Add(BraceSteps(keys[(k + 1)..close]));
                k = close;
            }
            else if (c is '^' or '!' or '+' or '#')
            {
                FlushText();
                modifiers.Add(c switch { '^' => "Ctrl", '!' => "Alt", '+' => "Shift", _ => "Win" });
            }
            else if (modifiers.Count > 0)
            {
                Add(new[] { new MacroAction { Type = ActionType.KeyTap, Key = c.ToString() } });
            }
            else
            {
                text.Append(c);
            }
        }
        FlushText();
        return steps;
    }

    /// <summary>One {…} of a Send: "Esc", "q down", "q up", "Tab 3", "Click", "Media_Play_Pause".</summary>
    private static IEnumerable<MacroAction> BraceSteps(string inside)
    {
        var parts = inside.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) yield break;
        string name = parts[0];
        string option = parts.Length > 1 ? parts[1] : string.Empty;

        if (name.Equals("Click", StringComparison.OrdinalIgnoreCase))
        {
            var button = option.Contains("right", StringComparison.OrdinalIgnoreCase) ? MouseButtonType.Right
                       : option.Contains("middle", StringComparison.OrdinalIgnoreCase) ? MouseButtonType.Middle
                       : MouseButtonType.Left;
            var type = option.Contains("down", StringComparison.OrdinalIgnoreCase) ? ActionType.MouseDown
                     : option.Contains("up", StringComparison.OrdinalIgnoreCase) ? ActionType.MouseUp
                     : ActionType.MouseClick;
            yield return new MacroAction { Type = type, MouseButton = button };
            yield break;
        }

        string? media = name.ToLowerInvariant() switch
        {
            "media_play_pause" => "PlayPause",
            "media_next" => "Next",
            "media_prev" => "Prev",
            "media_stop" => "Stop",
            "volume_up" => "VolumeUp",
            "volume_down" => "VolumeDown",
            "volume_mute" => "Mute",
            _ => null
        };
        if (media != null)
        {
            yield return new MacroAction { Type = ActionType.Media, MediaCommand = media };
            yield break;
        }

        if (option.Equals("down", StringComparison.OrdinalIgnoreCase) || option.Equals("downtemp", StringComparison.OrdinalIgnoreCase))
            yield return new MacroAction { Type = ActionType.KeyDown, Key = name };
        else if (option.Equals("up", StringComparison.OrdinalIgnoreCase))
            yield return new MacroAction { Type = ActionType.KeyUp, Key = name };
        else
        {
            int times = int.TryParse(option, out int n) ? Math.Clamp(n, 1, 100) : 1;
            for (int i = 0; i < times; i++) yield return new MacroAction { Type = ActionType.KeyTap, Key = name };
        }
    }
}
