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
    public const int CurrentDataVersion = 3;
    public int DataVersion { get; set; } = 0;

    public bool MasterEngineEnabled { get; set; } = true;
    public string EmergencyKillswitchKey { get; set; } = "F12";

    // HUD / OSD Overlay Settings
    public bool ShowHudOverlay { get; set; } = true;
    public HudPosition OverlayPosition { get; set; } = HudPosition.TopRight;
    public int HudDurationMs { get; set; } = 2200;
    public bool PlayAudioChime { get; set; } = false;

    // Window & System
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string ActiveProfileId { get; set; } = string.Empty;
}
