using System.Collections.ObjectModel;
using MacroMaster.Core.Models;

namespace MacroMaster.Core.Engine;

/// <summary>
/// List edits the main window makes to profiles and macros, kept free of UI so they can be tested:
/// deleting with undo, and reordering by dragging.
/// </summary>
public static class ProfileEdits
{
    /// <summary>
    /// Removes a macro and returns the action that puts it back exactly where it was. If it was its
    /// mode group's active mode, the group's next mode takes over so the shared hotkey keeps working.
    /// </summary>
    public static Action RemoveMacro(MacroProfile profile, MacroDefinition macro)
    {
        int index = profile.Macros.IndexOf(macro);
        if (index < 0) return () => { };

        // Who was the active mode before, so undo can restore it
        var group = macro.RequiresArming
            ? profile.Macros.Where(m => m.RequiresArming && m.ModeGroupKey == macro.ModeGroupKey).Select(m => (Macro: m, Armed: m.IsCurrentlyArmed)).ToList()
            : new();

        profile.Macros.RemoveAt(index);

        var remaining = group.Where(g => g.Macro != macro).Select(g => g.Macro).ToList();
        if (macro.RequiresArming && macro.IsCurrentlyArmed && remaining.Count > 0 && !remaining.Any(m => m.IsCurrentlyArmed))
        {
            remaining[0].IsCurrentlyArmed = true;
        }

        return () =>
        {
            if (profile.Macros.Contains(macro)) return;
            profile.Macros.Insert(Math.Min(index, profile.Macros.Count), macro);
            foreach (var (m, armed) in group) m.IsCurrentlyArmed = armed;
        };
    }

    /// <summary>Removes a profile and returns the action that puts it back at its old position.</summary>
    public static Action RemoveProfile(ObservableCollection<MacroProfile> profiles, MacroProfile profile)
    {
        int index = profiles.IndexOf(profile);
        if (index < 0) return () => { };

        profiles.RemoveAt(index);
        return () =>
        {
            if (!profiles.Contains(profile)) profiles.Insert(Math.Min(index, profiles.Count), profile);
        };
    }

    /// <summary>
    /// Dragging: puts <paramref name="dragged"/> in the place of the row it is held over. Only within
    /// the same block of the list (a mode group, or the macros outside any group), because the list
    /// shows each block together. False if nothing moved.
    /// </summary>
    public static bool MoveOnto(ObservableCollection<MacroDefinition> macros, MacroDefinition dragged, MacroDefinition? target)
    {
        if (target == null || ReferenceEquals(dragged, target)) return false;
        if (dragged.ModeGroupKey != target.ModeGroupKey) return false;

        int from = macros.IndexOf(dragged), to = macros.IndexOf(target);
        if (from < 0 || to < 0) return false;

        macros.Move(from, to);
        return true;
    }
}
