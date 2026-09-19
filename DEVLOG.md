# MacroMaster - Developer Log & Technical Specification

## Project Overview
MacroMaster is a high-performance, ultra-low latency desktop macro management engine for Windows, built with C# and WPF on .NET 10. It features vector Lucide icons, live On-Screen Display (OSD) HUD overlay, multi-mode gaming execution engines (Destiny 2 Wellskate / Groundskate), hardware-level input simulation, and AutoHotkey (.ahk) script parsing.

---

## Architecture & Subsystems

1. **Input Engine & Low-Level Hooks (`Core/Native/`)**:
   - `LowLevelKeyboardHook.cs`: `WH_KEYBOARD_LL` hook capturing global keyboard events with injected synthetic input filtering (`LLKHF_INJECTED = 0x10`).
   - `LowLevelMouseHook.cs`: `WH_MOUSE_LL` hook supporting Left, Right, Middle, Mouse 4 (XButton1), and Mouse 5 (XButton2) buttons.
   - `InputSimulator.cs`: `SendInput` and `mouse_event` wrapper with high-resolution multimedia timer (`timeBeginPeriod(1)`) and hybrid coarse-sleep + spin-wait for sub-millisecond precision.
   - `KeyHelper.cs`: Virtual key code normalization, key strings, extended key mappings, and layout conversion.
   - `ProcessWatcher.cs`: Foreground window filtering via `GetForegroundWindow` and `GetWindowThreadProcessId`.

2. **Core Macro Engine (`Core/Engine/MacroEngine.cs`)**:
   - Multi-mode macro scheduler: Single-Shot (`Once`), Looping (`Toggle`), and N-Repeat (`RepeatCount`).
   - Mode arming state machine (e.g. `PgUp` arms Wellskate, `PgDn` arms Groundskate).
   - Chord key evaluation (simultaneous multi-key conditions e.g. `ä + ö + l`).
   - Quick action loops: Left Click Hold, Auto Clicker (CPS spammer), and Minecraft Mining loop.
   - Safety Killswitch (`F12` default) halting all loops and releasing all held inputs.

3. **AutoHotkey Converter (`Core/Parsers/AhkScriptParser.cs`)**:
   - Regex-based AST parsing for AHK v1/v2 hotkeys, `#HotIf`, `GetKeyState`, `mouse_event`, `Click`, `Send`, and `Sleep`.

4. **UI & Vector Visual System (`UI/`)**:
   - Deep Obsidian stealth theme with Vivid Emerald, Cyan, and Violet accents (`UI/Themes/DarkTheme.xaml`).
   - Vector Lucide icon renderer (`UI/Controls/LucideIcon.cs`, `UI/Icons/LucideIcons.xaml`).
   - Floating glass HUD notification banner (`UI/Windows/HudOverlayWindow.xaml`).
   - Macro timeline editor and live input recorder (`UI/Windows/MacroEditorDialog.xaml`).
   - Active scripts inspector and profile management modal.

---

## Changelog & Evolution

### Version 2.0.0
- **UI Clean-up**:
  - Removed obsolete brand icon cube and engine tags from title bar for a minimal stealth aesthetic.
  - Fixed rounded corner background bleed through OpacityMask geometry clipping on container borders.
  - Increased line strokes and border thicknesses globally by +2px (2.5px - 3.5px) to eliminate rendering artifacts.
  - Enforced ClearType display formatting and crisp text rendering across high-DPI displays.
- **Typography & Alignment**:
  - Centered and enlarged 'Active Profile' selector typography.
  - Scaled up Emergency Killswitch footer badge and status indicators.
  - Centered execution mode display inside macro cards and editor.
- **Layout & Grid Uniformity**:
  - Re-architected macro list cards into a uniform grid layout where every macro container has identical dimensions and vertically locked action buttons.
- **Bug Fixes**:
  - Fixed `PreciseSleep` double-wait bug where stopwatch was initialized after coarse sleep.
  - Fixed trigger key and input recorder to capture mouse buttons (Left, Right, Middle, X1, X2) and extended keys (PgDn, PgUp, Home, End, F-keys).
  - Added full Profile Creation and Profile Switching workflow.
- **New Features**:
  - Interactive Active Scripts button & modal displaying all currently active scripts with live state badges and quick disable toggles.

### Version 2.1.0
- **Macro Card Layout Modernization**:
  - Grouped `Mode` directly next to `Trigger` on the left side (e.g., `Trigger: [ä + ö + l]  Mode: [Play Once]`) and moved `Steps` count to the right.
  - Eliminated floating mode columns for a cohesive, scannable card layout.
- **Embedded Profile Creation in Dropdown**:
  - Moved `➕ Create New Profile...` directly into the `Active Profile` ComboBox dropdown list.
  - Selecting it immediately launches the profile creation dialog and selects the newly created profile seamlessly.
  - Centered, enlarged (`16.5px`, `Bold`), and styled active profile text with balanced margins to eliminate dropdown chevron overlap.
- **Macro Editor Dimensions & "Move" Action**:
  - Expanded `MacroEditorDialog` width to `1160px` so top timeline buttons never overlap or collide with header titles.
  - Renamed "Recoil" button and actions to "Move" for intuitive mouse movement control.
- **Action Step Parameter Inspector & Live Editing**:
  - Added a dedicated parameter inspector below the action timeline allowing instant editing of keys, mouse buttons, hold times, delays, jitter, and move coordinates at any time.
  - Implemented `INotifyPropertyChanged` on `MacroAction` for instant, real-time list updates when changing action values.
- **Single-Action Key & Mouse Hold Timings**:
  - `Key Tap` and `Mouse Click` now support single-action hold durations (e.g. `Key Tap: 'J' (Hold 25ms)`, `Mouse Click: Left (Hold 20ms)`) eliminating the need for 3 separate actions.
- **Selection Anti-Blurriness Fix**:
  - Removed WPF `DropShadowEffect` bitmap caching on `ListBoxItem` selection that previously disabled ClearType antialiasing, switching to crisp 3px vector border lighting.

### Version 2.2.0
- **Hold-and-Release Mouse & Key Recording**:
  - Live recorder now captures mouse clicks as single-action `MouseClick` (Hold & Release) with measured sub-millisecond hold duration instead of separate down/hold actions.
  - Keyboard recording similarly captures `KeyTap` (Hold & Release) with measured hold durations, keeping action timelines clean and human-readable.
- **Direct Key Capture in Settings**:
  - Implemented interactive key capture engine (`LowLevelKeyboardHook` / `LowLevelMouseHook`) in Settings.
  - Added dedicated "Capture" buttons for Emergency Killswitch, Hold Left Click, Auto Clicker, and Minecraft Mining loop hotkeys to effortlessly press and assign keys.
- **Top Bar Alignment Modernization**:
  - Re-anchored `Active Profile` selector and the interactive `Active Scripts` badge button to the left side of the top toolbar.
  - Action buttons (`Import .ahk Script` and `New Macro`) remain aligned on the right, providing an intuitive, balanced workflow.
- **Crisp Rounded Corners & Dark Border System**:
  - Eliminated outer margin drop shadow artifacts that caused black corners in WPF layered windows (`AllowsTransparency="True"`).
  - Replaced bright slate outer outline with a deep obsidian border (`#20293D`, `1.5px`) with matching inner corner radiuses across `MainWindow`, `MacroEditorDialog`, `SettingsDialog`, `ActiveScriptsDialog`, and `NewProfileDialog`.
- **Macro Description Editing in Macro Editor**:
  - Added a dedicated multi-line `Description / Notes` input field in `MacroEditorDialog` under General Configuration.
  - Fully bound to `MacroDefinition.Description` on load, edit, and save, enabling instant editing of explanatory macro notes.
- **New Vector Lucide Executable Icon**:
  - Designed and generated a multi-resolution `.ico` icon (`256x256`, `128x128`, `64x64`, `48x48`, `32x32`, `16x16`) featuring the signature Lucide `zap` lightning bolt with neon emerald glow on a deep obsidian squircle.
  - Configured `<ApplicationIcon>` in [`MacroMaster.csproj`](file:///C:/Users/Levi/Desktop/MacroMaster/MacroMaster.csproj), bound window icons across all dialogs, and integrated it into the system tray manager.

### Version 2.2.1
- **System Tray & Taskbar Icon Prominence Enhancement**:
  - Maximized squircle usable area by reducing transparent outer padding from 80px (16% wasted canvas) to **18px** (~97% canvas coverage).
  - Scaled up the Lucide `zap` lightning bolt geometry by **+38%** (`scale = 36.5`), enlarged stroke to 14px, and boosted multi-tier ambient emerald glows for brilliant visibility.
  - Added dedicated pre-resampled **`20x20` (125% DPI)** and **`24x24` (150% DPI)** raster frames to the multi-resolution `.ico` asset.
  - Upgraded `TrayManager.cs` to request `SystemInformation.SmallIconSize`, ensuring pixel-perfect native DPI scaling and matching the prominent size of standard Windows tray icons.