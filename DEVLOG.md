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
   - Macro timeline editor and live input recorder (`UI/Views/MacroEditorView.xaml`, a page inside the main window).
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

### Version 2.2.2 - Windows Registry Autostart Subsystem & Silent Background Launch (2026-09-20)
- **Windows Autostart Registration Engine (`Core/Native/AutoStartManager.cs`)**:
  - Implemented full Windows Registry integration (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MacroMaster`).
  - Added dynamic executable path detection via `GetExecutablePath()` with multi-level parent directory searching.
  - Added self-healing `SyncStartupRegistration()` on startup and setting changes to heal path mismatches when directories move.
- **Silent Background Launch Support (`--autostart`)**:
  - Added command-line parameter parsing in `MainWindow.xaml.cs` (`--autostart`, `-silent`, `-minimized`).
  - When Windows starts at boot, MacroMaster launches silently to the system tray without showing the main configuration window, while keeping the HUD overlay and hotkeys ready.
### Version 2.2.3 - Engine Reliability & Trigger Fixes (2026-11-04)
- **Runaway toggle macros fixed (`Core/Engine/MacroEngine.cs`)**: toggling a macro off and quickly on again let the old run's `finally` remove the *new* run from `_runningTasks`. That loop then kept running untracked: the next press started a second copy, and neither the toggle nor the F12 killswitch could stop it. Runs now only unregister themselves.
- **Killswitch now stops Once/Repeat macros**: they ran with `CancellationToken.None` and weren't registered, so a long repeat couldn't be halted. They're registered and cancellable now. Delays, loop delays and the auto-clicker/Minecraft loops wake up immediately on cancel instead of finishing their sleep.
- **"Once" mode ran N times** if a repeat count had been set before switching the mode back to Once. `RepeatCount` is now only used in Repeat mode.
- **Ctrl/Shift/Alt-modified triggers never fired**: low-level hooks report modifiers as left/right codes (0xA0–0xA5) but the engine checked the generic 0x10–0x12. `IsKeyDown` now maps generic to both sides, a modifier used as the primary trigger key matches either side, and `RequireWin` is checked as well.
- **Mouse-button triggers never fired / arrow keys hijacked clicks**: the editor saves mouse triggers as "Mouse 4" / "Left Click", but the engine compared against enum names ("XButton1", "Left"), so captured mouse triggers never matched. A *keyboard* trigger on the Left/Right arrow ("Left") also matched the left/right mouse button and swallowed every click. Matching now goes through virtual-key codes, and mouse triggers honour modifiers/chords like keyboard ones.
- **Mouse hook reacted to its own clicks**: injected events (auto clicker, macro actions) are ignored like the keyboard hook already did, so a mouse-bound macro can't re-trigger itself.
- **Held hotkeys no longer flip toggles on auto-repeat**: key auto-repeat doesn't re-fire handlers. Repeats and key-ups of a swallowed trigger are swallowed too, as is the release of a swallowed mouse button, so apps don't get orphaned key-up/button-up events.
- **Chord state can't be corrupted by our own input**: only physical keys are tracked as "held".
- **Killswitch / stop releases only what macros pressed**: `ReleaseAllInputs` used to send mouse-up and Shift/Ctrl/Alt-up unconditionally, which could cancel a button the user was physically holding while leaving keys pressed via `KeyDown` actions (and X buttons) stuck. The simulator now tracks held keys/buttons and releases exactly those.
- **Lower CPU, more accurate timing (`Core/Native/InputSimulator.cs`)**: `PreciseSleep` busy-spun the whole wait for anything ≤ 15 ms (a full core at high CPS) and could overshoot by a tick above that. It now waits on a high-resolution waitable timer and only spins the last ~1 ms. This also keeps working when Windows 11 ignores `timeBeginPeriod` for background/minimised apps.
- **Faster hook callbacks**: the process-filter lookup ran for every enabled macro on every keystroke *before* checking whether the key even matched. Key matching now comes first, the foreground process name is cached per PID, key-name parsing is cached, and mouse moves skip struct marshaling.
- **HUD no longer steals focus**: the overlay window is `ShowActivated="False"`, and notifications are posted with `BeginInvoke` so the input hook returns immediately.
- `Engine_OnStateRefreshed` uses `BeginInvoke` so macro worker threads don't block on the UI thread.
- Autostart entry is compared exactly, so an old entry without `--autostart` gets upgraded.

### Version 2.3.0 - App picker, visible mode groups, UI consistency (2026-11-10)
- **App picker instead of typing an exe name** (`UI/Editor/RunningApps.cs`, macro editor → More options): "Only while this app is in front" opens a searchable list of apps that currently have a window (icon, window title, exe), plus "Any app" and "Browse for an .exe…" for games that aren't running. Stored value is unchanged (`destiny2.exe`), so old macros keep working.
- **Mode groups are shown as one block in the macro list**: macros that share a hotkey and switch via a key (Wellskate / Groundskate) are grouped under a header with an accent rail, "N modes on one hotkey" and "Switch with [PgUp] Wellskate [PgDn] Groundskate"; the active mode is highlighted. Rows show "Active mode" or "Press PgDn to use" instead of the truncated yellow/grey label.
- **Mode group editor**: the switch key is captured like every other key (click, press) instead of typed; existing groups are offered as "Join …" chips; the editor lists the other modes in the group and warns when they use a different hotkey. Saving keeps exactly one active mode per group, and an empty mode name falls back to the macro name. Missing switch key blocks saving with a footer hint.
- Engine compares mode groups by the trimmed name (`ModeGroupKey`); popup reads "Mode: Wellskate".
- Editor sections are numbered in reading order (1 Hotkey, 2 How often, 3 Steps).
- Macro list "Mode" column is a plain pill ("Once", "Loop", "Repeat ×3"); the play icon there looked like a second Run button.
- Hard-coded engine-card border color moved into the theme (`SuccessBorderBrush`); new shared `PickerButton`, `PopupPanel`, `PickerItem` styles.

### Version 2.3.1 - UI consistency pass (2026-11-10)
- **House rules written into the theme header** (`UI/Themes/DarkTheme.xaml`): type scale 11/12/13/14 (+16/22/24 headings), three radii (`RadiusSm` 5, `RadiusMd` 7, `RadiusLg` 10), control heights 32 standard / 28 compact, 32 page gutter / 24 dialog gutter. All one-off font sizes (10.5, 11.5, 12.5, 13.5), radii and heights in the windows were snapped onto these.
- **Hover transitions**: every button, list row, menu item and dropdown item has a `Hover` layer that fades in (80 ms) and out (160 ms) via the shared `HoverIn` / `HoverOut` storyboards; row action buttons fade the same way.
- **Keyboard focus ring** (`FocusRing`) on buttons, switches, segments, option cards and pickers; disabled state (45 % opacity) on every button, switch and input.
- **Dialogs share one header**: 48 px high, 24 px gutter, `H2` title, icon-button close (editor, settings, new profile).
- **Popups share one look**: combo dropdowns and context menus now use `PopupPanel` like the app picker.
- New shared styles replace inline duplicates: `H3`, `RowTitle`, `Hint`, `Callout`, `Divider`, `IconTile` / `IconTileLg`, `SegmentGroup`, `GhostButtonSm`, `InputSm`, `KeycapSm`. New brushes replace hard-coded colors: `OnAccentBrush`, `OnDangerBrush`, `ScrimBrush`, `CaptionCloseBrush`.
- **Macro list**: long names ellipsize instead of pushing the "Active mode" / "Running" tags out of view, descriptions ellipsize instead of being cut off, mode-group titles are no longer black-on-dark (`PlainList` sets a foreground), the mode pill stays visible on hovered rows.

### Version 2.3.2 - In-window editor, bracket-linked mode groups, clean corners (2026-11-10)
- **Macro editor is a page in the main window** instead of a second window: `MacroEditorDialog` (Window) became `UI/Views/MacroEditorView` (UserControl), hosted in `MainWindow.EditorHost` by `OpenEditor`. It replaces the sidebar + macro list while open (they are hidden, so Tab can't reach them), has a "← Macros / New macro" breadcrumb in the caption strip, and reports back through the `Finished(bool saved)` event. The status bar and the window's caption buttons stay visible. Dropping .ahk files is ignored while the editor is open.
- **Mode groups**: the tinted block with the accent rail and the big icon tile is gone. A group is now a quiet one-line label in the name column (small icon, name, "N modes on one hotkey", switch chips) and a bracket on the left that links the group's switches. A group with one macro says "only mode so far".
- **Corner seams fixed** (`UI/Controls/SmoothBorder.cs`): WPF's `Border` draws outline and fill as two anti-aliased shapes, which left a dark notch at every rounded corner on bright fills (primary button, switches). `SmoothBorder` draws both as one rounded rectangle. Every evenly outlined rounded border in the theme and windows uses it; use it for new ones too.
- New `arrow-left` icon.

### Version 2.4.0 - Hold-to-run, more step types, test run, macro menu, profile files (2026-11-10)
- **"While held" mode works** (`Core/Engine/MacroEngine.cs`): `ExecutionMode.HoldWhileDown` existed in the model but the engine never ran it and the editor silently saved it as "Once". The hotkey now starts a loop (`StartLoop`) and letting go of that key or mouse button stops it (`_holdRuns`, `StopHeldMacros` from `HandleKeyUp` / mouse-up). From the Run button, where there is no key to release, it behaves like a loop. The editor has it as a fourth "How often" option and requires a hotkey for it.
- **Text and media steps**: the engine now executes `ActionType.Text` (`InputSimulator.TypeText`, Unicode input so it is layout-independent, Enter for line breaks, 6 ms between characters). The editor's step toolbar has a "More" menu with Move the mouse, Type text and Media key; each has an inline row editor (`TextStepTemplate`, `MediaStepTemplate`, `StepChoices.MediaCommands`).
- **Drag steps to reorder**: the step number is the handle. The list captures the mouse (not the row, which is re-created on every move); the dragged row is highlighted via `MacroAction.IsDragging`.
- **Macro row right-click menu** (`MainWindow`): Edit, Duplicate (the copy starts switched off because it has the same hotkey), Move up / Move down (within its mode group, or among ungrouped macros), Move to another profile (keeps one active mode per group on both sides), Delete.
- **Profile files**: right-click a profile → "Export to a file…" writes it as JSON in the same format as `profiles.json`. The "Import" button and drag-and-drop accept those files next to .ahk scripts; imported profiles get fresh ids, a unique name, and start switched off (`MacroJsonSerializer.ExportProfiles` / `ImportProfiles` / `CloneMacro`).

### Version 2.4.1 - Menus, discard check, quick open, group label (2026-11-10)
- **"Try it" removed** from the editor (button, countdown and `TestRequested` event).
- **Right-click menus restyled** (`UI/Themes/DarkTheme.xaml`): menu items have an icon column (`MenuItem.Icon`, an `AppIcon`), and menu separators finally use the theme. WPF looks separators inside a menu up under `MenuItem.SeparatorStyleKey`, not the implicit `Separator` style, so they had the system look (indented, running past the right edge). Macro, profile and the editor's "More" menu all carry icons; new `arrow-right` icon.
- **Unsaved changes**: leaving the editor (breadcrumb or Cancel) asks "Discard your changes?" when anything differs from what was loaded (`MacroEditorView.HasUnsavedChanges`, a flattened snapshot of every editable value). Really quitting the app with a half-edited macro asks too.
- **Quick open key** (Settings → Global keys, `AppSettings.QuickOpenKey`, off by default): a single key that brings the window in front of everything (`WindowHelper.ForceToFront`, via `AttachThreadInput` + `SetForegroundWindow`), e.g. to make a macro mid-game; pressed again while MacroMaster is in front it hides/minimizes again. Handled in the engine hook right after the kill switch, so it also works while the engine is paused. It can't be the same key as the kill switch.
- **Mode group label**: the small icon and grey text are gone; the group name is set like a macro name (same column, primary colour, semibold) followed by a count pill ("2 modes"). Bracket and "Switch with" chips unchanged.

### Version 2.4.2 - Framed mode groups, every key recognised, settings page (2026-11-10)
- **Mode groups are framed**: each group sits in its own rounded, outlined block with a darker fill (`SidebarBrush` + `BorderStrongBrush`), inset 6 px from the card. Rows inside are indented 10 px so the bracket keeps 8 px clear of the outline, and pulled back out on the right so Trigger / Mode / Steps stay in their columns. `SmoothBorder.ClipContent` clips row hover fills and dividers to the rounded inside.
- **Every key is recognised** (`Core/Native/KeyHelper.cs`): single characters that aren't letters or digits (+ # - , . < ^ …) are resolved through the keyboard layout (`VkKeyScan`), and `VkToString` now names numpad keys, Win, Apps, Pause, CapsLock, PrintScreen, NumLock, ScrollLock and F13–F24, shows punctuation keys as their character, and falls back to `VK_0xNN` (which parses back) so no key can come out unusable. Older saved names such as `OemPlus` still work.
- **Settings is a page in the main window** (`UI/Views/SettingsView`, was `SettingsDialog`), opened through `MainWindow.ShowPage` like the macro editor, with the same breadcrumb and a centred 720 px column.
