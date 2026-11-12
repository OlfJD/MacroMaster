namespace MacroMaster.Core.Models;

/// <summary>Where a popup sits on the screen. Values are saved as numbers: only add at the end.</summary>
public enum HudPosition
{
    TopCenter,
    TopRight,
    BottomRight,
    BottomCenter,
    TopLeft,
    BottomLeft,
    /// <summary>Wherever the user dragged it (Settings → "Place it"), see <see cref="ScreenSpot"/>.</summary>
    Custom
}

/// <summary>
/// A spot on a monitor as fractions of its work area (0 = left/top edge, 1 = right/bottom edge) for
/// the popup's centre, so it lands in the same place at any resolution and on any monitor.
/// </summary>
public class ScreenSpot
{
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.1;
}

public class AppSettings
{
    /// <summary>Bumped when saved data needs a one-time upgrade on load (see MainWindow.MigrateData).</summary>
    public const int CurrentDataVersion = 5;
    public int DataVersion { get; set; } = 0;

    public bool MasterEngineEnabled { get; set; } = true;

    /// <summary>
    /// Key that switches the whole engine off and on again, like the switch in the window. Off
    /// stops every macro and releases everything they hold. "" = no key.
    /// </summary>
    public string EngineToggleKey { get; set; } = "F12";

    /// <summary>
    /// Only read from settings files of older versions, which had a separate kill switch key
    /// instead; <see cref="UpgradeLegacyKeys"/> moves it over. Never written again.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("EmergencyKillswitchKey")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyKillswitchKey { get; set; }

    /// <summary>The old kill switch key becomes the engine on/off key, so the key you're used to keeps stopping everything.</summary>
    public void UpgradeLegacyKeys()
    {
        if (!string.IsNullOrWhiteSpace(LegacyKillswitchKey)) EngineToggleKey = LegacyKillswitchKey.Trim();
        LegacyKillswitchKey = null;
    }

    /// <summary>Key that brings the MacroMaster window in front of everything (and sends it back again). "" = off.</summary>
    public string QuickOpenKey { get; set; } = string.Empty;

    // HUD / OSD Overlay Settings
    public bool ShowHudOverlay { get; set; } = true;
    public HudPosition OverlayPosition { get; set; } = HudPosition.TopRight;
    public ScreenSpot OverlayCustomSpot { get; set; } = new() { X = 0.85, Y = 0.08 };
    public int HudDurationMs { get; set; } = 2200;
    public bool PlayAudioChime { get; set; } = false;

    /// <summary>
    /// A small label that stays on screen while a mode group's app is in front and says which mode
    /// is active ("Wellskate"), so you don't have to remember it.
    /// </summary>
    public bool ShowModeIndicator { get; set; } = true;
    public HudPosition ModeIndicatorPosition { get; set; } = HudPosition.TopLeft;
    public ScreenSpot ModeIndicatorCustomSpot { get; set; } = new() { X = 0.1, Y = 0.05 };

    /// <summary>
    /// Size of the interface: 0 = Auto (grows with the screen, see UI.UiScale), otherwise a fixed
    /// percentage such as 125.
    /// </summary>
    public int UiScalePercent { get; set; } = 0;

    // Window & System
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Start with Windows as administrator (a scheduled task with the highest rights instead of the
    /// Run key), so hotkeys also work in games / apps that run as administrator. Windows asks for
    /// permission once when this is set up.
    /// </summary>
    public bool StartAsAdmin { get; set; } = true;

    /// <summary>Quietly download and install new versions from GitHub (UI.Updater).</summary>
    public bool AutoUpdate { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string ActiveProfileId { get; set; } = string.Empty;
}
