# MacroMaster - Pro Gaming & Automation Macro Engine

A high-performance, ultra-low latency desktop macro management suite for Windows, engineered with a modern Razer / Corsair stealth dark aesthetic, vector Lucide icons, live On-Screen Display (OSD) HUD overlay, and automatic AutoHotkey (.ahk) script conversion.

---

### Features

- **Zero-Script Quick Action Toggles**:
  - **Hold Left Click**: 1-click toggle on the dashboard or hotkey (`PgDn`) to lock Left Click down without holding the physical mouse button.
  - **Auto Clicker / CPS Spammer**: Automated high-speed spam clicking (configurable CPS, Left/Right/Middle mouse buttons, hotkey `F6`).
  - **Minecraft Mining Loop**: Locks Left Click Down while pulsing Right Click every 100ms for continuous mining and placing (`F7`).

- **Drag & Drop AutoHotkey (.ahk) Auto-Converter**:
  - Drag and drop any AutoHotkey (`.ahk`) script directly onto the MacroMaster window.
  - The built-in parser automatically converts hotkeys, multi-key conditionals (`#HotIf`, `GetKeyState`), timing delays, clicks, sends, and hardware-level mouse movements (`mouse_event` recoil compensation) into visual, editable MacroMaster timeline steps.

- **Floating Neon OSD / HUD Overlay**:
  - Replaces basic white OS tooltips with a translucent, animated floating glass HUD banner.
  - Displays instant mode changes (e.g., `ARMED: Wellskate (PgUp Mode)`, `ARMED: Groundskate (PgDn Mode)`), quick toggle states, and safety killswitch alerts with crisp Lucide vector icons and smooth slide/fade animations.

- **Destiny 2 Wellskate & Groundskate Multi-Mode Engine**:
  - Pre-loaded with Destiny 2 movement tech presets.
  - `PgUp` arms **Wellskate** mode; `PgDn` arms **Groundskate** mode.
  - Pressing `ä + ö + l` executes the currently armed sequence with 1ms multimedia timer precision.
  - Game/process filtering ensures macros only trigger when `destiny2.exe` is the foreground window.

- **Visual Action Timeline Builder & Live Recorder**:
  - Visual step-by-step macro editor: Key Down, Key Up, Key Tap, Mouse Clicks, Delays, and Relative Mouse Movements (Recoil compensation).
  - Built-in live recorder to capture keypresses and mouse clicks in real time with exact millisecond timings.

- **Safety & Performance**:
  - **Emergency Killswitch (`F12`)**: Pressing `F12` immediately halts all running loops and releases all held mouse buttons and keys.
  - **Multimedia High-Frequency Timer**: Activates Windows `timeBeginPeriod(1)` to eliminate standard 15.6ms OS timer jitter.
  - **System Tray Background App**: Runs silently in the Windows taskbar notification area with ~8-12 MB RAM footprint.

---

### How to Run

1. Double-click [**`MacroMaster.exe`**](file:///C:/Users/Levi/Desktop/MacroMaster/MacroMaster.exe) or run [**`Launch MacroMaster.bat`**](file:///C:/Users/Levi/Desktop/MacroMaster/Launch%20MacroMaster.bat).
2. Use the **Master Engine Switch** at the top to toggle all macro triggers globally.
3. Toggle any quick action (such as **Hold Left Click**) or drag and drop any `.ahk` files into the window to import them instantly.
4. Press `F12` at any time for emergency input release.
