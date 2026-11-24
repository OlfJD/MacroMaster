using MacroMaster.Core.Models;
using MacroMaster.Core.Parsers;
using Xunit;

namespace MacroMaster.Tests;

/// <summary>Dropping an .ahk script on the window: the common AutoHotkey v1 and v2 forms come through as steps.</summary>
public class AhkImportTests
{
    private static string Steps(MacroDefinition m) => string.Join(", ", m.Actions.Select(a => a.DisplayText));

    [Fact]
    public void Version_2_blocks_with_braced_keys_are_read_completely()
    {
        var macros = AhkScriptParser.ParseAhkContent("""
            F7::
            {
                Send "{q down}"
                Sleep 30
                Send "{q up}"
                Send "{v}"
            }
            """, "Test");

        var m = Assert.Single(macros);
        Assert.Equal("F7", m.Trigger.PrimaryKey);
        Assert.Equal("Key Down: Q, Wait: 30 ms, Key Up: Q, Key Tap: V (Hold 50ms)", Steps(m));
    }

    [Fact]
    public void Version_1_hotkeys_with_commas_and_return_are_read()
    {
        var macros = AhkScriptParser.ParseAhkContent("""
            $F8::
            Send, {Esc}
            Sleep, 50
            Click
            return
            """, "Test");

        var m = Assert.Single(macros);
        Assert.Equal("F8", m.Trigger.PrimaryKey);
        Assert.Equal("Key Tap: ESC (Hold 50ms), Wait: 50 ms, Mouse Click: Left (Hold 50ms)", Steps(m));
    }

    [Fact]
    public void Several_keys_in_one_send_become_several_steps()
    {
        var m = Assert.Single(AhkScriptParser.ParseAhkContent("""F9::Send "{v}{Esc}" """, "Test"));
        Assert.Equal("Key Tap: V (Hold 50ms), Key Tap: ESC (Hold 50ms)", Steps(m));
    }

    [Fact]
    public void Modifiers_repeats_text_and_comments_in_a_send()
    {
        var m = Assert.Single(AhkScriptParser.ParseAhkContent("""
            ^+F10::
            {
                Send "^c"            ; copy
                Send "{Tab 2}"
                SendText "gg wp"
            }
            """, "Test"));

        Assert.True(m.Trigger.RequireCtrl);
        Assert.True(m.Trigger.RequireShift);
        Assert.Equal("Key Down: CTRL, Key Tap: C (Hold 50ms), Key Up: CTRL, Key Tap: TAB (Hold 50ms), Key Tap: TAB (Hold 50ms), Type: \"gg wp\"", Steps(m));
    }

    [Fact]
    public void A_two_key_combination_becomes_a_chord()
    {
        var m = Assert.Single(AhkScriptParser.ParseAhkContent("""a & b::Send "{x}" """, "Test"));
        Assert.Equal("b", m.Trigger.PrimaryKey);
        Assert.Equal(new[] { "a" }, m.Trigger.ChordKeys);
    }

    [Fact]
    public void Skate_mode_scripts_keep_braced_keys_inside_their_modes()
    {
        var macros = AhkScriptParser.ParseAhkContent("""
            #HotIf WinActive("ahk_exe destiny2.exe") && GetKeyState("ä", "P") && GetKeyState("ö", "P")
            l::
            {
                if (currentMacro == 1) {
                    Send "{j down}"
                    Sleep 20
                    Send "{j up}"
                }
                else if (currentMacro == 2) {
                    Click "Right Down"
                    Sleep 40
                    Click "Right Up"
                }
            }
            #HotIf
            PgUp::currentMacro := 2
            PgDn::currentMacro := 1
            """, "Rocket");

        var ground = Assert.Single(macros, x => x.ArmingKey == "PgDn");
        var well = Assert.Single(macros, x => x.ArmingKey == "PgUp");
        Assert.Equal("l", ground.Trigger.PrimaryKey);
        Assert.Equal(new[] { "ä", "ö" }, ground.Trigger.ChordKeys);
        Assert.Equal("destiny2.exe", ground.ProcessFilter);
        Assert.Equal("Key Down: J, Wait: 20 ms, Key Up: J", Steps(ground));
        Assert.Equal("Mouse Down: Right, Wait: 40 ms, Mouse Up: Right", Steps(well));
    }
}
