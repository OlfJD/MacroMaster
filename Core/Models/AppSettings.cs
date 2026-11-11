namespace MacroMaster.Core.Models;

public enum HudPosition
{
    TopCenter,
    TopRight,
    BottomRight,
    BottomCenter,
    TopLeft
}

public class AppSettings
{
    /// <summary>Bumped when saved data needs a one-time upgrade on load (see MainWindow.MigrateData).</summary>
    public const int CurrentDataVersion = 4;
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
    public int HudDurationMs { get; set; } = 2200;
    public bool PlayAudioChime { get; set; } = false;

    /// <summary>
    /// A small label that stays on screen while a mode group's app is in front and says which mode
    /// is active ("Wellskate"), so you don't have to remember it.
    /// </summary>
    public bool ShowModeIndicator { get; set; } = true;
    public HudPosition ModeIndicatorPosition { get; set; } = HudPosition.TopLeft;

    // Window & System
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string ActiveProfileId { get; set; } = string.Empty;
}
