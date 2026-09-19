namespace MacroMaster.Core.Models;

public enum TriggerType
{
    Press,          // Trigger once on key down
    Release,        // Trigger on key release
    Toggle,         // Toggle on first press, toggle off on next press
    HoldToLoop      // Run continuously while key is held down
}

public class MacroTrigger
{
    public string PrimaryKey { get; set; } = string.Empty; // e.g. "l", "PgDn", "F6", "XButton1"
    public int PrimaryVirtualKey { get; set; }
    
    // Chord keys that must also be held down (e.g. "ä", "ö" for the Destiny 2 rocket combo)
    public List<string> ChordKeys { get; set; } = new();
    
    // Standard modifiers
    public bool RequireCtrl { get; set; }
    public bool RequireShift { get; set; }
    public bool RequireAlt { get; set; }
    public bool RequireWin { get; set; }
    
    // Trigger action type
    public TriggerType Type { get; set; } = TriggerType.Press;

    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            if (RequireCtrl) parts.Add("Ctrl");
            if (RequireShift) parts.Add("Shift");
            if (RequireAlt) parts.Add("Alt");
            if (RequireWin) parts.Add("Win");
            
            foreach (var chord in ChordKeys)
            {
                if (!string.IsNullOrWhiteSpace(chord) && !parts.Contains(chord))
                {
                    parts.Add(chord.ToUpperInvariant());
                }
            }

            if (!string.IsNullOrWhiteSpace(PrimaryKey))
            {
                parts.Add(PrimaryKey.ToUpperInvariant());
            }

            return parts.Count > 0 ? string.Join(" + ", parts) : "None";
        }
    }
}
