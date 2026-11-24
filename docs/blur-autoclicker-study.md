# What MacroMaster can learn from Blur-AutoClicker

Study of [Blur009/Blur-AutoClicker](https://github.com/Blur009/Blur-AutoClicker) at commit `bfabfa9` ("v3.10.2 release", 2026-11-06), compared with MacroMaster 1.3.0 (`9b46c4b`). Written 2026-11-24.

**How this was done.** I read the reference's Rust backend (`src-tauri/src/`) and its build files. I did not build or run it; it was only read, in a separate folder outside our repo. File and function names below are theirs, line numbers are at that commit. For our app I measured the real engine through the test harness (Release build, i5-1135G7, 8 threads) and the running app's idle CPU and memory. No reference code was copied; nothing in our repo changed for this study except this file.

**Licence.** Blur-AutoClicker is GPL-3.0. Everything recommended here is a general technique (Windows API usage, scheduling, failsafes) that we would write ourselves. Section 5 lists the few places where their exact formulas or constants are so specific that we should design our own instead of re-typing theirs.

---

## 1. Phase 1: how Blur-AutoClicker works

### 1.1 Click loop and timing

- **One dedicated thread per run.** `start_clicker_run` (`engine/worker.rs:267`) builds an immutable `ClickerConfig` from the settings, then `std::thread::spawn`s the loop (`worker.rs:345`) that runs `start_clicker` (`worker.rs:1149`).
- **Timer resolution only while running.** `TimerResolutionGuard` (`worker.rs:91–113`) calls the undocumented `NtSetTimerResolution(10000, …)` (1 ms, declared in `engine/mod.rs:121–128`) when the run starts and releases it in `Drop` when the run ends. Nothing raises the timer resolution while idle.
- **Absolute deadlines, so no drift.** `LoopState.next_batch_time` (`worker.rs:818`) is advanced by the batch duration each pass (`next_batch_time += batch_duration`, `worker.rs:976`), and the loop sleeps until that instant (`worker.rs:1061–1064`). Time spent sending input or checking failsafes doesn't add up. If the loop falls more than one batch behind, the deadline is reset to "now" (`worker.rs:977–979`), so it doesn't fire a catch-up burst.
- **Sleep, no spin.** Waits go through `sleep_interruptible` (`worker.rs:1372–1388`): `std::thread::sleep` in slices of at most 10 ms, checking for stop and failsafes between slices. There is no spin-wait anywhere in the click path; the only `spin_loop` is a 5 ms CPU-clock calibration (`measure_cycle_freq`, `worker.rs:71–89`). How precise `std::thread::sleep` is on Windows depends on the Rust standard library, not on this repo. As far as I know, current Rust uses a high-resolution waitable timer there, but that is outside the code I read.
- **Batching at high speed.** `ClickerContext::new` (`worker.rs:739–792`) sends 2 clicks per scheduling slot from 50 CPS and 3 above 500 CPS, when double-click is off. At those speeds the duty cycle is capped (≤ 98 % at 50–100 CPS, ≤ 70 % at 100–200, ≤ 30 % at 200–500, ≤ 1 % above 500). Once the hold time rounds to 0 ms, a whole batch goes out in **one `SendInput` call** (`send_batch`, `mouse.rs:244–268`). The trade-off: from 50 CPS clicks come in pairs (from 500 CPS in triples) rather than evenly spaced. The average rate is right; the spacing isn't.
- **Accuracy at high CPS** (from the code, not measured): the average rate is held by the absolute deadline; spacing within a batch is irregular by design; scheduling jitter is the OS sleep granularity (roughly sub-millisecond with a 1 ms timer). The maximum is 1000 CPS (`settings/mod.rs`, `sanitize` → `limits::CLICK_SPEED`; the test at `settings/mod.rs:539` clamps 1e9 to 1000).

### 1.2 Input injection

- **`SendInput` everywhere, never the legacy `mouse_event`/`keybd_event`.** Clicks: `send_mouse_event` (`mouse.rs:233–240`) and the batched `send_batch` (`mouse.rs:244–268`). Moves: `move_mouse` (`mouse.rs:157–166`) uses `MOUSEEVENTF_ABSOLUTE | MOVE | VIRTUALDESK`, normalised to the virtual desktop (`VirtualScreenRect::normalize_x/y`, `mouse.rs:51–60`), so games reading raw input see it. `SetCursorPos` isn't used.
- **Return values are checked.** When `SendInput` accepts fewer events than were passed (`sent != expected`, `mouse.rs:258`; keyboard `keyboard.rs:173`), the run stops with "Windows accepted only part of the simulated input (secure desktop, elevated target, or session isolation)" (`worker.rs:42–43`, `worker.rs:1047–1051`). The press that might still be held is released first (`send_clicks`, `mouse.rs:270–283`; `send_key_presses`, `keyboard.rs:197–211`).
- **Keyboard by scan code.** `make_keyboard_input` (`keyboard.rs:42–45`) maps the virtual key with `MapVirtualKeyW(…, MAPVK_VK_TO_VSC_EX)` (`vk_to_scan`, `keyboard.rs:14–22`), sends `KEYEVENTF_SCANCODE`, and sets `KEYEVENTF_EXTENDEDKEY` when the high byte of that lookup says so. Their comment lists the arrows, Ins/Del/Home/End/PgUp/PgDn and numpad Enter as covered. *Correction found while implementing our version:* on this PC that lookup returns the non-extended numpad codes for exactly the arrows, Home/End, PgUp/PgDn and Ins/Del (e.g. `0x004B` for Left), so the lookup alone flags right Ctrl/Alt, Win and numpad "/" but not those keys. Not tested in their app.
- **Own input is tagged.** Every event carries `dwExtraInfo = AUTOCLICKER_EXTRA_INFO` (`engine/mod.rs:11`, `mouse.rs:226`, `keyboard.rs:35`). The hooks ignore exactly those events (`hotkeys.rs:496`, `hotkeys.rs:535`), not all injected input.
- **Upper case.** For letters it holds Shift according to the Caps Lock state (`shift_needed_for_case`, `keyboard.rs:65–71`).
- **Key-hold mode** (100 % duty on a key) imitates Windows' own auto-repeat, using the user's repeat delay and rate from `SystemParametersInfoW(SPI_GETKEYBOARDDELAY / SPI_GETKEYBOARDSPEED)` (`get_keyboard_repeat_settings`, `worker.rs:698–736`; loop `worker.rs:1177–1295`).

### 1.3 Threading

- **Threads:** the click thread (one per run, `worker.rs:345`); a hotkey thread that owns the low-level hooks and a 16 ms loop (`start_hotkey_listener`, `hotkeys.rs:659`); a 1 s overlay auto-hide thread (`lib.rs:453–461`); a 100 ms "start zone" monitor (`lib.rs:463–…`); a 30 s WebView2 memory trimmer (`window_lifecycle.rs:93–101`); and the Tauri main thread for windows.
- **How the UI reaches the engine:** Tauri commands `start_clicker` / `stop_clicker` / `toggle_clicker` (`ui_commands.rs:231–245`) call into `worker.rs`. Run state is two atomics, `running` and `run_generation` (`begin_run` / `end_run`, `worker.rs:226–257`, serialised by `RUN_TRANSITION`). Each run gets a generation number; a thread from an older run sees `is_active()` turn false (`RunControl`, `worker.rs:115–142`) and can't clear a newer run's flags (`RunFlagsGuard`, `worker.rs:147–197`, which also cleans up on a panic).
- **Stopping:** `stop_clicker_inner` flips the atomics (`worker.rs:375–400`). The click thread notices at the next check, at worst after one 10 ms sleep slice.
- **Talking back to the UI:** `emit_status` hops to the main thread (`worker.rs:620–626`); progress updates are capped at one per 250 ms (`PROGRESS_EMIT_INTERVAL`, `worker.rs:915–928`).

### 1.4 Hotkeys

- **Low-level hooks that only record.** `WH_KEYBOARD_LL` and `WH_MOUSE_LL` (`install_hooks`, `hotkeys.rs:548–571`) store "this physical key is down/up" in a 256-entry atomic table and always pass the event on (`keyboard_ll_proc`, `hotkeys.rs:532–546`; `mouse_ll_proc`, `hotkeys.rs:493–530`). They never swallow a key and do almost no work, so Windows can't time them out.
- **The hotkey itself is decided by polling.** The hotkey thread wakes every 16 ms (`MsgWaitForMultipleObjectsEx` with `POLL_INTERVAL_MS = 16`, `hotkeys.rs:35`, `hotkeys.rs:676–688`), compares the binding with the recorded key table (`is_hotkey_binding_pressed_physical`, `hotkeys.rs:1000–1030`) and reacts to press/release edges (`hotkeys.rs:889–912`). This adds up to 16 ms of delay, and the hotkey still reaches the game.
- **Hold vs toggle:** `handle_hotkey_pressed` starts (Hold) or toggles (Toggle); `handle_hotkey_released` stops in Hold mode (`hotkeys.rs:1090–1130`).
- **Self-repair** (every 400 ms, `hotkeys.rs:691–714`):
  - It checks whether the input desktop changed, i.e. the lock screen or a UAC prompt (`input_desktop_visible`, `hotkeys.rs:611–625`). After coming back it clears stale key states and **reinstalls the hooks** (`repair_after_desktop_switch`, `replace_hooks`).
  - If the hooks went quiet and a key recorded as down is actually up per `GetAsyncKeyState`, it fixes the table and reinstalls the hooks (`resync_physical_key_state`, `hotkeys.rs:641–657`). This also catches hooks that Windows removed silently.
- **Modifiers:**
  - "Strict" mode decides whether extra modifiers block a hotkey (`modifiers_match`, `hotkeys.rs:1202`).
  - Before a run starts on a hotkey like Ctrl+F6, the held modifiers are released, so the first clicks aren't Ctrl+clicks (`release_trigger_modifiers`, `hotkeys.rs:1079–1088`).
  - A keyboard auto-press key that would trigger its own hotkey is refused (`worker.rs:279–307`).

### 1.5 Randomisation and duty cycle

- **RNG:** a small xorshift generator (`SmallRng`, `engine/rng.rs`), seeded from time and PID, with Box–Muller normal samples (`next_gaussian`, `rng.rs:33–44`).
- **Speed variation:** each batch's period is drawn from a **normal distribution** with mean = the interval and standard deviation = `variation %` of it (`run_batch`, `worker.rs:953–960`), floored at 1 ms. The default is 35 % (`settings/mod.rs:223–224`).
- **Duty cycle:** the hold time is a percentage of the cycle (default 45 %, `settings/mod.rs:220–221`), so press length scales with speed (`plans_for_cycle`, `worker.rs:795–808`).
- **Double clicks:** both presses must fit inside the system double-click time (`GetDoubleClickTime() × 0.9`, `max_double_click_gap_ms`, `worker.rs:427–430`), and the pair must fit in the cycle (`ClickCyclePlan::double`, `engine/cycle.rs:29–41`, with thorough unit tests).
- **Position:**
  - Click points can have a radius for uniform-in-disc jitter (`update_target`, `worker.rs:862–913`).
  - A 2 px "nudge" with 21.6 % chance (`worker.rs:517–518`) makes games re-read the cursor.
  - Optional human-like moves along a random cubic Bézier with easing, a mid-way wobble and overshoot (`smooth_move_inner`, `mouse.rs:380–483`).

### 1.6 Safety stops

- **Corner and edge failsafes** (`engine/failsafe.rs:29–117`), checked per monitor before every batch. On by default: corners 50 px, edges 40 px (`settings/mod.rs:235–242`).
- **Custom zones** that stop, pause or start the clicker (`detect_stop_zones`, `failsafe.rs:7–27`).
- If the cursor position can't be read for 500 ms, the run stops (`failsafe.rs:119–188`), so a failsafe never silently stops working.
- **Click limit and time limit** (`worker.rs:1299–1330`), and "stop when all click points are done".
- **App filter:** whitelist or blacklist of apps, checked every batch (`check_process_list`, `engine/process.rs:531–568`). The foreground name is cached by window handle and PID with a 1 s expiry (`ForegroundCache`, `process.rs:357–391`). An app that can't be identified for 250 ms counts as "not allowed" in whitelist mode.
- **Alt+Tab / task view stop** (window class check plus Alt+Tab key state, `is_task_switcher_active`, `process.rs:570–595`). On by default.
- **Stop when Windows blocks the input** (section 1.2) and a master on/off key (`hotkeys.rs:790–839`).

### 1.7 CPU and memory

- **CPU:**
  - No spin-wait and no busy loop; 10 ms sleep slices.
  - Timer resolution raised only during a run.
  - The status sent to the UI is throttled to 4/s.
  - Measured average CPU per run is shown to the user (`QueryThreadCycleTime` per thread, `worker.rs:49–63`, `cpu_usage`, `worker.rs:1098–1111`).
- **Memory:** the WebView2 child processes' working sets are trimmed every 30 s and when the window hides (`SetProcessWorkingSetSize(-1,-1)`, `window_lifecycle.rs:13–106`). That is a WebView-specific problem; we use WPF, not WebView2.
- **Hooks:** cheap by design (section 1.4).

### 1.8 Build and release

- **Cargo profile:** `[profile.release]` `opt-level = 3`, `lto = "thin"`, `codegen-units = 1`, `strip = "symbols"`, `debug = "line-tables-only"`, `panic = "unwind"` (`Cargo.toml:67–74`). That's full optimisation with readable crash traces; unwinding is what lets `RunFlagsGuard` clean up after a panic.
- **Release pipeline** (`.github/workflows/release.yml`):
  - signed Windows builds (`scripts/sign-windows.ps1`, `docs/windows-release-trust.md`)
  - a portable package (`scripts/package-portable.ps1`)
  - Crashpad crash reports with a pinned hash (`Cargo.toml:48–50`)
  - winget publishing (`.github/wingetcreate.sha256`)

---

## 2. Measurements of our engine (MacroMaster 1.3.0)

A loop of "click (hold *h* ms) + wait *w* ms" run for 3 s through the real engine with the recording simulator: nothing reaches the PC, so this isolates scheduling. "Drift" is where the last click lands compared with an ideal schedule.

| Target | Period set | Period measured (mean / std dev / max) | Rate | Drift | CPU (process, % of one core) |
|---|---|---|---|---|---|
| 10 CPS | 100 ms | 100.015 / 0.003 / 100.026 ms | 10.0 | +0.4 ms over 24 clicks | 2 % |
| 15.4 CPS | 65 ms | 65.018 / 0.009 / 65.049 ms | 15.4 | +0.7 ms over 41 | 3 % |
| 50 CPS | 20 ms | 20.017 / 0.010 / 20.108 ms | 50.0 | +2.4 ms over 144 | **9 %** |
| 100 CPS | 10 ms | 10.015 / 0.009 / 10.122 ms | 99.8 | +4.5 ms over 294 | **18 %** |
| 500 CPS | 2 ms | 2.002 / 0.003 / 2.070 ms | 499.5 | +2.9 ms over 1494 | **100 %** |
| 1000 CPS | 1 ms | 1.006 / 0.172 / **10.071** ms | 994.5 | +16.6 ms over 2977 | **109 %** |

Idle, hidden in the tray (Release build, after start-up): 0–0.5 % of one core, 87 MB working set (23 MB private), 16 threads. With the window open: about 0.2–1.3 %, 147 MB (70 MB private).

**What this shows:**
- Our intervals are extremely regular (standard deviation around 0.01 ms), because `PreciseSleep` spins the last ~1 ms of every wait.
- That spin is also our main cost: CPU grows with speed and reaches a whole core from about 500 CPS.
- Drift is small (about +15 µs per cycle, below 0.2 %), but real, because each wait is relative to the end of the previous step.
- At 1000 CPS there are occasional 10 ms stalls.

I did not run Blur-AutoClicker, so there are no measured numbers for it. Expected from its code: much lower CPU (no spin); about sub-millisecond jitter per interval instead of ~0.01 ms; no drift; uneven spacing at ≥ 50 CPS because of batching.

---

## 3. Phase 2: comparison, area by area

| Area | MacroMaster today | Blur-AutoClicker | Better / worse | Expected impact of changing |
|---|---|---|---|---|
| **1. Loop and timing** | Each run on its own thread (`MacroEngine.StartRun`, `MacroEngine.cs:574`). Steps run one after another (`ExecuteActionSequence`, `MacroEngine.cs:668`). Every wait is relative: `PreciseSleep` = high-res waitable timer for *n−1* ms, then spin to the end (`InputSimulator.cs:304`, `:340–361`). A loop pass with no wait yields 1 ms (`MacroEngine.cs:543`). | Absolute deadline per batch; sleep only; batching from 50 CPS; timer resolution only while running. | **Ours:** far more even intervals and instant stop (cancellation token, no 10 ms slices). **Theirs:** no drift, a fraction of the CPU at high speed, real 1000 CPS without burning a core. | Removing most of the spin: up to ~10× less CPU at 50–100 CPS, at the cost of some per-click evenness. Absolute deadlines: removes the 0.15 % drift (minor). |
| **Timer resolution** | `timeBeginPeriod(1)` in the `InputSimulator` constructor (`InputSimulator.cs:20`), i.e. for the app's **whole lifetime**, even idle in the tray. | Raised only during a run, released afterwards. | Theirs. | Less power use and fewer timer wake-ups on laptops while MacroMaster idles in the tray (which is most of the time). Our waits use a high-res timer anyway, so precision should be unchanged; this needs re-measuring. |
| **2. Input injection** | Mouse buttons via legacy `mouse_event` (`InputSimulator.cs:253`), no return value. Keys via `SendInput` with the VK plus a scan code from `MapVirtualKey(vk, 0)`, **without** `KEYEVENTF_SCANCODE` and **without** `KEYEVENTF_EXTENDEDKEY` (`InputSimulator.cs:255–268`). Absolute moves via `SetCursorPos` (`InputSimulator.cs:139`). Typed text as Unicode (`InputSimulator.cs:194–227`, good for chat, invisible to games). Return values never checked. Own input recognised by the generic `LLKHF_INJECTED` flag. | `SendInput` for everything, results checked, scan codes with the extended flag, absolute moves by `SendInput`, own input tagged via `dwExtraInfo`. | **Theirs, clearly.** Concrete bug in ours: arrows, Home/End, PgUp/PgDn, Ins/Del, right Ctrl/Alt and numpad Enter are sent without the extended flag, so games that read scan codes can see them as numpad keys. `SetCursorPos` moves are ignored by raw-input games. We never notice when Windows drops our input (admin window, lock screen). | Fixes key steps in games, makes "move to" work in raw-input games, and lets a run stop with a clear reason instead of silently doing nothing. |
| **3. Threading** | Hooks on a dedicated thread (`InputHookThread.cs:41–53`); one thread per run (`StartRun`, since 1.3.0); 250 ms watchdog timer (`MacroEngine.cs:118`); UI updates via `Dispatcher.BeginInvoke`. Stop = cancel token, wakes waits at once. | Similar shape: atomics plus a generation counter, 10 ms stop slices, UI events capped at 4/s. | About equal. Ours stops faster; theirs has the tidy generation guard (we solved the same problem with per-run objects in 1.3.0). | Nothing important to change. |
| **4. Hotkeys** | Matching and **swallowing inside the hook** (`LowLevelKeyboardHook.HookCallback`, `:101–156`; `MacroEngine.HandleKeyDown`, `:301`): zero added delay, and the game never sees the hotkey. Auto-repeat detection by our own key table (`:116`). **No self-repair**: no reinstall when Windows drops a hook, no resync after the lock screen / UAC; a missed key-up makes the next press of that key count as auto-repeat and get ignored. | Record-only hooks plus a 16 ms poll; the key reaches the game; self-repair every 400 ms; releases trigger modifiers. | **Ours** for latency and for multi-step game macros. **Theirs** for robustness (self-repair). | Adding self-repair (resync stale keys with `GetAsyncKeyState`, reinstall hooks after a desktop switch or a silent removal) fixes "the hotkey did nothing the first time" after unlocking or alt-tabbing out of an admin window, without giving up our zero-latency model. |
| **5. Randomisation / duty** | Per wait step, **uniform** ±*N* ms ("give or take", `MacroEngine.cs:726–733`). Hold times are fixed ms per step. No notion of CPS or duty cycle: an auto clicker is "click *x* ms + wait *y* ms" worked out by hand (the old sample promised 15 CPS and delivered 5.5). | Normal distribution on the period; duty % of the cycle; caps at high speed; double-click aware of the system setting. | Theirs for auto clicking; ours is more general (any sequence). | A rate-based "auto clicker" mode (CPS or per minute/hour + duty + variation) would remove the hand calculation. A normal distribution looks more human than uniform. |
| **6. Safety stops** | Engine on/off key (F12), stop on leaving the app (watchdog, 250 ms, `StopRunsThatShouldEnd`, `MacroEngine.cs:202`), admin-window warning, stop on switch-off/delete/edit, release of held keys. App name cached by PID only, never expires (`ProcessWatcher.cs:10–56`). | Corner/edge failsafe, custom zones, click and time limits, Alt+Tab stop, whitelist/blacklist, blocked-input stop, unreadable-cursor stop. | Theirs has more. | Corner failsafe and "stop after N clicks / T seconds" are cheap and useful. An app-leave check before each step (not only every 250 ms) stops a loop within one click when alt-tabbing. Caching by window + PID with an expiry removes a rare wrong-app match after Windows reuses a PID. |
| **7. CPU / memory** | Idle is cheap (above). Running cost is dominated by the spin. WPF has no WebView, so the memory trick doesn't apply. | No spin, timer released when idle, throttled UI events, WebView trimming. | Theirs while running; idle comparable on CPU. | See area 1. |
| **8. Build / release** | `dotnet publish -c Release`, framework-dependent, unsigned zip, quiet updater that doesn't check the download's integrity. | Fully optimised native build, signed releases, crash reports, winget. | Theirs. | **ReadyToRun**: no JIT at start-up, so a faster tray start at sign-in and no slow "first run" of a macro. **Signing**: no SmartScreen warning, but it needs a certificate (cost). **Checksum** on update downloads: integrity. |

---

## 4. Phase 3: ranked recommendations (waiting for your choice)

Effort: S = under a day, M = 1–3 days, L = more. All of them fit our stack (C#/.NET 10, WPF, Win32 P/Invoke); none needs a new dependency.

| # | Improvement | Benefit | Effort | Risk | Notes |
|---|---|---|---|---|---|
| 1 | **Keys by scan code with the extended flag** (keep the VK path for keys without a scan code, e.g. media keys) | Correctness in games: arrows, Home/End, PgUp/PgDn, Ins/Del, right Ctrl/Alt, numpad Enter arrive as the right key in DirectInput / raw-input games | S | Low–medium: must be checked in a real game, since behaviour differs per game | Plain Win32 practice (`MapVirtualKey` + `KEYEVENTF_SCANCODE/EXTENDEDKEY`); our code will differ structurally from theirs |
| 2 | **`SendInput` for the mouse, check every result, stop a run when Windows blocks it** (and absolute "move to" via `SendInput` instead of `SetCursorPos`) | Reliability and UX: no silent do-nothing runs in admin windows or on the lock screen; "move to" works in raw-input games | S–M | Low; absolute coordinates on multi-monitor setups need testing | Replaces deprecated `mouse_event` |
| 3 | **Cheaper waits**: shrink the spin to a measured margin (e.g. ~0.2–0.3 ms instead of ~1 ms) or skip it for long waits, and add absolute deadlines for loop passes | Performance: CPU at 50–100 CPS roughly from 9–18 % to a few %; no drift | M | Medium: timing changes; verify with the benchmark from section 2 (worth keeping as a test) | Could be a setting ("Highest timing precision" = today's behaviour) |
| 4 | **Timer resolution only while a macro runs** (or drop `timeBeginPeriod` entirely, since we use high-res waitable timers) | Power and battery while idling in the tray | S | Low; re-run the benchmark | — |
| 5 | **Hook self-repair**: every few hundred ms, resync "pressed" keys with `GetAsyncKeyState`; reinstall the hooks after the lock screen / UAC or when they stop receiving events | Reliability: no ignored first press after unlocking or after a missed key-up; recovers when Windows silently drops a hook | M | Medium: touches the hook thread; needs careful testing | Keeps our zero-latency, swallowing model |
| 6 | **Safety stops**: optional screen-corner failsafe, "stop after N clicks / T seconds" for loops, app-leave check before each step (not only every 250 ms) | Safety and features | S–M | Low; corner stop should be **opt-in** (top-right is the Close button in most apps) | Generic idea (PyAutoGUI has the same failsafe) |
| 7 | **Rate-based auto clicker**: per second/minute/hour + duty % + variation %, the engine computes hold and wait; optional double click within the system double-click time | UX / features: no hand-calculated auto clickers | M–L | Medium (model, editor UI, migration) | Design our own model; see section 5 |
| 8 | **Normal-distributed "give or take"** instead of uniform | More human timing | S | Low | Textbook method |
| 9 | **App cache by window + PID with an expiry** | Correctness in a rare case (PID reuse) | S | Low | — |
| 10 | **ReadyToRun publish** (and later: release signing, a checksum check in the updater) | Faster cold start at sign-in, no first-run JIT stall; trust and integrity | S (R2R) / M (checksum) / needs a certificate (signing) | Low | Bigger zip with R2R |
| 11 | **Release trigger modifiers before a run** (Ctrl+F6 shouldn't make Ctrl+clicks) | Correctness for modifier hotkeys | S | Medium: sending key-ups for keys the user physically holds can surprise | Low priority |
| 12 | **Batching above ~500 CPS** (one `SendInput` with several clicks) | Performance at extreme speeds | M | Medium: clicks become bursty | Only worth it if very high CPS matters to your users |
| 13 | **Human-like mouse paths, click statistics** | Features | M | Low | Nice-to-have; design our own curves |

**My suggestion for a first batch:** 1, 2, 4 and 9, all small with real correctness wins; then 3 and 5; then 6 and 7 as features.

**Status (2026-11-24):** 1, 2, 4 and 9 are implemented (DEVLOG 2.7.6). Notes from doing them:
- #1 keeps the virtual key and adds the right scan code and extended flag, rather than sending by scan code alone: numpad keys would otherwise turn into Insert / End / … depending on Num Lock. The extended flag comes from Windows' list of extended keys, because the `MapVirtualKey` lookup doesn't report the navigation keys (see 1.2).
- #9: the old name lookup turned out to be as fast as the new one (about 0.04 ms either way on .NET 10), so the gain is only the corrected cache.

The other items are in DEVLOG.md under To-do.

---

## 5. Licence check: ideas too specific to re-create by copying

These would come out nearly identical if we "re-implemented" them by porting, so we should design our own instead:

- **Double-click planning** (`ClickCyclePlan::double`, `engine/cycle.rs:29–41`): the exact min/saturating formula for hold and gap. We should write our double-click rule from the requirement ("both presses inside `GetDoubleClickTime`, pair inside the cycle"), with our own structure and tests.
- **Duty caps per speed band** (`worker.rs:758–768`: 98 / 70 / 30 / 1 % at 50 / 100 / 200 / 500 CPS) and **batch sizes** (2 / 3 at 50 / 500 CPS): if we do this, our thresholds should come from our own measurements.
- **Human-like mouse paths** (`smooth_move_inner`, `mouse.rs:380–483`): the control-point ranges (0.20–0.48, 0.55–0.79), the 0.29 × distance / 76 px offset, the 37 % wobble and 47 % overshoot chances are distinctive. Use our own curve model and parameters.
- **Cursor nudge** constants (2 px, 21.6 %) and the `dwExtraInfo` tag value `0x800DA5A5`: pick our own values.

Everything else in section 4 (scan codes, `SendInput` result checks, deadline scheduling, scoped timer resolution, hook resync, corner failsafe, normal-distributed jitter) is standard Windows / timing practice, and our versions would naturally look different.

---

## 6. What already changed in MacroMaster

**From this study (2.7.6, not released yet):** extended-key flag on key steps, mouse through `SendInput` with absolute "move to", runs stop when Windows refuses their input, the 1 ms timer only while a macro runs, foreground app cached per window + process for at most a second. Tests 97 → 116.

**Before the study (1.3.0, released)**

These are not from this study, but they cover several of the same areas: runs stop when a macro is switched off, deleted or edited; no key is left held after a stop (a race between "stop" and "press" closed); runs get their own thread (no thread-pool delay); one copy per data folder; saves survive locked files; errors are logged; the editor validates values; AutoHotkey import reads v1/v2 syntax; blank first start. Details are in `DEVLOG.md` (version 2.7.5).
