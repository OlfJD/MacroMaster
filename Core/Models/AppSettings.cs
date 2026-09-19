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
    public bool MasterEngineEnabled { get; set; } = true;
    public string EmergencyKillswitchKey { get; set; } = "F12";
    
    // Quick Actions State
    public bool HoldLeftClickActive { get; set; } = false;
    public string HoldLeftClickHotkey { get; set; } = "PgDn";
    public bool HoldLeftClickHotkeyEnabled { get; set; } = true;

    public bool AutoClickerActive { get; set; } = false;
    public string AutoClickerHotkey { get; set; } = "F6";
    public bool AutoClickerHotkeyEnabled { get; set; } = false;
    public int AutoClickerCps { get; set; } = 15; // Clicks per second
    public MouseButtonType AutoClickerButton { get; set; } = MouseButtonType.Left;

    public bool MinecraftLoopActive { get; set; } = false;
    public string MinecraftLoopHotkey { get; set; } = "F7";
    public bool MinecraftLoopHotkeyEnabled { get; set; } = false;

    // HUD / OSD Overlay Settings
    public bool ShowHudOverlay { get; set; } = true;
    public HudPosition OverlayPosition { get; set; } = HudPosition.TopCenter;
    public int HudDurationMs { get; set; } = 2200;
    public bool PlayAudioChime { get; set; } = false;

    // Window & System
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string ActiveProfileId { get; set; } = string.Empty;
}
