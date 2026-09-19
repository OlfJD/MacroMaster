<div align="center">

# ⚡ MacroMaster

**Pro Gaming & Automation Macro Suite for Windows with OSD HUD & AHK Auto-Conversion**

[![Download Latest Release](https://img.shields.io/badge/Download-Latest_Release_(Windows)-10B981?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/OlfJD/MacroMaster/releases/latest/download/MacroMaster-v1.0.0-win-x64.zip)
[![GitHub Release](https://img.shields.io/github/v/release/OlfJD/MacroMaster?style=for-the-badge&color=10B981)](https://github.com/OlfJD/MacroMaster/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows_10_%2F_11-3B82F6?style=for-the-badge&logo=windows)](https://github.com/OlfJD/MacroMaster)

### 📥 [👉 Click Here to Download MacroMaster (.zip)](https://github.com/OlfJD/MacroMaster/releases/latest/download/MacroMaster-v1.0.0-win-x64.zip)

*(No installation required — just extract and run!)*

</div>

---

## ⚡ Quick Start for Users

1. **[Download MacroMaster (.zip)](https://github.com/OlfJD/MacroMaster/releases/latest/download/MacroMaster-v1.0.0-win-x64.zip)**.
2. **Extract** the ZIP folder anywhere on your computer.
3. Run **`MacroMaster.exe`** (or `Launch MacroMaster.bat`).
4. Toggle the **Master Switch** on, activate quick actions (like **Hold Left Click** or **CPS Clicker**), or drag and drop any `.ahk` script into the window!

---

## ✨ Features

- **Zero-Script Quick Action Toggles**:
  - **Hold Left Click**: 1-click toggle on the dashboard or hotkey (`PgDn`) to lock Left Click down without holding the physical mouse button.
  - **Auto Clicker / CPS Spammer**: Automated high-speed spam clicking (configurable CPS, Left/Right/Middle mouse buttons, hotkey `F6`).
  - **Minecraft Mining Loop**: Locks Left Click Down while pulsing Right Click every 100ms for continuous mining and placing (`F7`).

- **Drag & Drop AutoHotkey (.ahk) Auto-Converter**:
  - Drag and drop any AutoHotkey (`.ahk`) script directly onto the MacroMaster window.
  - The built-in parser automatically converts hotkeys, multi-key conditionals (`#HotIf`, `GetKeyState`), timing delays, clicks, sends, and hardware-level mouse movements into visual, editable MacroMaster timeline steps.

- **Floating Neon OSD / HUD Overlay**:
  - Replaces basic OS tooltips with a translucent, animated floating glass HUD banner.
  - Displays instant mode changes (e.g., `ARMED: Wellskate`, `ARMED: Groundskate`), quick toggle states, and safety killswitch alerts with crisp Lucide vector icons.

- **Destiny 2 Movement Tech Presets**:
  - Pre-loaded with Destiny 2 movement tech presets (Wellskate & Groundskate).
  - Multi-key execution with 1ms multimedia timer precision.
  - Game/process filtering ensures macros only trigger when the target game is active.

- **Visual Action Timeline Builder & Live Recorder**:
  - Visual step-by-step macro editor: Key Down, Key Up, Key Tap, Mouse Clicks, Delays, and Relative Mouse Movements.
  - Built-in live recorder to capture keypresses and mouse clicks in real time with exact millisecond timings.

- **Safety & Performance**:
  - **Emergency Killswitch (`F12`)**: Pressing `F12` immediately halts all running loops and releases all held mouse buttons and keys.
  - **Multimedia High-Frequency Timer**: Activates Windows `timeBeginPeriod(1)` to eliminate standard 15.6ms OS timer jitter.
  - **System Tray Background App**: Runs silently in the Windows taskbar notification area with ~8-12 MB RAM footprint.
