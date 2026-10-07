# FEATURES.md — Feature specification (0.7.0-alpha; implemented — see the Status sections)

**This file is the spec** — authored for 0.7.0-alpha and fully implemented since (every section below carries a Status line). The "Status after …" section at the top lists what is done and what remains — implement the remaining items on top of the shipped 0.11.3 code in this bundle. Where this file and PROMPT.md conflict, this file wins for *features*; PROMPT.md's hard constraints (no `ES3.Save`, no `vc.input.*` writes, allocation-free steering prefix, hidden-runner survival architecture, one Graphic per GameObject, mouse-only panel, BepInEx config the only persistence, unchanged GUID) always win.

You have no terminal. The user runs `bash verify/run.sh` (needs .NET SDK 8+; globs **all** `plugin/**/*.cs`, so every new file must compile against the stubs — extend them, §7). Reason through it carefully.

Version: `plugin/PluginInfo.cs` → `"0.7.0"` ✅. README title → "v0.7.0-alpha" + a new "## Changes in 0.7.0-alpha" section ✅. Suite before this round: **504 tests (486 logic + 18 prefix)**; after: **610 (592 + 18)**, all passing. Keep them green, add the §7 tests, and remove `GearboxSettings.ComingSoon` (release gate from 0.6.0) only when the §1 shifting subsystem is done — until then the gate stays.

## Status after 0.9.0-alpha (what is done, what remains)

**Everything in this spec (§1–§5, §6 config, §7 harness, §10 crash hardening, §11 telemetry) is implemented, harness-tested and shipped** through 0.9.0-alpha, plus the post-0.7.1 rounds: 0.7.2 pin-overlap fix, 0.7.3 12-gear launch fix (ShiftDelegateGuard), 0.7.4 per-tab Reset fix + pins replaced by the telemetry readout list, 0.7.5 gearbox-at-load diagnostics, 0.7.6 the gearbox-at-load fix, 0.7.7 quiet release logging (`[Gearbox] DebugHooks` off), 0.8.0 max steering angle + save tracking (ResetOnSaveSwitch, default flipped true in 0.9.0), and **0.9.0 per-vehicle tunes** (`[PerVehicle] Tunes` blob, `PresetBook<T>` store, BasedOn round-trip, per-vehicle apply picks — README "Changes in 0.9.0-alpha"), plus **0.10.0 the Weight tab** (front/rear ballast kg — real mass, centre of mass, inertia — or negative-kg balloon lift; springs re-scaled with the mass at the time — README "Changes in 0.10.0-alpha"), **0.11.0 the weight fix + wider range + coarse/trim sliders** (balloon lift re-homed into the mod's own `FixedUpdate` — the 0.10.0 module tick never ran; uncapped lift; range −10 000…+20 000 kg; coarse 100-kg-snap + trim ±100-kg slider pairs per axle — README "Changes in 0.11.0-alpha"), and **0.11.1 the ballast-springs fix** (ballast leaves the springs stock so the body visibly squats — the 0.10.0 rescale held the ride height constant, so positive ballast had zero visible suspension actuation; `RescaleSpringsForMass` deleted, `RestoreWeight` resets the last axle split so a re-apply after OFF re-runs the mass writes — README "Changes in 0.11.1-alpha"), and **0.11.2 the trim-ratchet fix** (the ±100-kg trim range vs the [−50, +50] trim domain let a drag flip the coarse bucket per event and ratchet the weight toward ±10000 kg; the sliders now run ±50 kg (`WeightMath.TrimLimit`) and the setter clamps through `WeightMath.ApplyTrim` — README "Changes in 0.11.2-alpha"), and **0.11.3 the two-numbers display fix** (the coarse row's getter read the whole float, so a trim drag rewrote the coarse readout and nudged its handle; both rows now read only their own part — `CoarseOf`/`TrimOf` — and the applied weight is the two added; display-only, no math/config/persistence changes — README "Changes in 0.11.3-alpha"). Harness: **691 tests (672 logic + 19 prefix)**, green.

**Remains** (flagged gaps, documented in README §8 — the repo is maintenance/frozen mode, SPO is the future):
- Panel UI for per-vehicle save/remove (0.9.0 shipped the blob config-level only; `SettingsPanel.cs` unmodified).
- `ShiftController.cs:287` decides shifts from the global `GearboxSettings.ActivePreset` while gearbox apply picks per-vehicle — needs a decision.
- In-game checks: README §10 items 29–37 (0.6.x), 47–50 (0.8.0/0.9.0), 51–61 (0.10.0–0.11.1), 62–63 (0.11.2) and 64–65 (0.11.3) remain individually unconfirmed (38–46 were covered by the user's playtests).

## Status after 0.7.1-alpha (what is done, what remains)

**Every §1–§5 item is implemented and harness-tested** (`bash verify/run.sh`: 610 tests, 592 logic + 18 prefix, all passing on .NET SDK 8.0.131). README §10 items 38–46 (0.7.0) were later covered by the user's playtests of 0.7.1/0.7.6+; 29–37 remain individually unconfirmed. The full changelog, deviations and negative controls are in README "Changes in 0.7.0-alpha".

**0.7.1 (panel reorg, user request):** 8 tabs grouped by car area — Steering · Suspension · **Wheels** (alignment + grip) · **Drivetrain** (engine/diffs/layout + gearbox) · Brakes · Assists · Aero · Settings. 6x6 template button removed (parser keeps accepting multi-axle text); stored `LastTab` 8/9 clamps to the last tab (the one config-continuity exception). See README "Changes in 0.7.1-alpha".

### Checklist

| § | Item | Done | Harness-tested | Negative control | In-game check |
|---|---|---|---|---|---|
| §1 | Mod-owned shifting (`Runtime/ShiftController.cs`) — installed as NWH's `shiftDelegate`; the game's `input.ShiftInto/ShiftUp/ShiftDown` requests are read, never written; **no SetProperty patch** (deviation, README) | ✅ | ✅ | ✅ hook reverted: 11 fail | §10 38–41 |
| §1 | Automatic: per-gear shift points from the tuned ratios, landing-RPM hysteresis (no hunting), kickdown, creep hold, NWH DNR rules | ✅ | ✅ (525-case no-hunting grid; 0→60→0 m/s drive through 12 gears, 0 reversals) | ✅ landing raise reverted: 3 fail | §10 38 |
| §1 | Manual: the game's shift requests, clamped to the tuned count (vehicle's own ManualShift on Manual-type cars) | ✅ | ✅ | (covered by the hook control) | §10 39 |
| §1 | CVT/External untouched; Stock & clutch-only presets keep NWH's own shifting | ✅ | ✅ | — | §10 41 |
| §1 | Restore byte-for-byte (same delegate instance, gears, clutch; type never written); re-hook after the game changes the type; stale-runner and fault safety | ✅ | ✅ | (hook control) | §10 40 |
| §1 | `[Gearbox.Custom] ShiftUpFactor / ShiftDownFactor / KickdownScale` | ✅ | ✅ | — | §10 46 |
| §1 | `GearboxSettings.ComingSoon` and `AnyGearboxSkipped` removed (code + tests) | ✅ | ✅ | — | — |
| §2 | Custom drivetrain layout (0.6.2, config) | ✅ 0.6.2 | ✅ | ✅ (0.6.2) | §10 30–33 |
| §2 | Panel UI for the layout: switch, 5 templates, copy/paste via clipboard, status line | ✅ | ✅ (templates parse) | — | §10 42 |
| §2 | Centre-diff mode (`[Drivetrain.Custom] DiffCenterMode`), bias slider enabled only with a centre diff, per-axle torque-split readout (live wiring), one-axle note | ✅ | ✅ | — | §10 42 |
| §3 | Truck preset (12 gears, factors, clutch, ShiftUpFactor 0.9, 3+3 grid) — **ratios spread over the stock range** (`SpreadRatios`, deviation, README) | ✅ | ✅ | ✅ spread reverted: 2 fail | §10 38 |
| §4 | Telemetry pins: Pin button on every category slider, `Category.ConfigKey` registry, cap 12, `[Telemetry] Pins`, unknown keys dropped, dynamic strip rows | ✅ | ✅ | ✅ normalise reverted: 1 fail | §10 43 |
| §5 | Tighter UI (50/40/66/32/110, fonts +1..2) via `PanelLayout` constants; font fitting; 3 tab rows below 360 px; geometry at 300/400/460/800/1000 px | ✅ | ✅ | ✅ tab-font fit reverted: 4 fail | §10 44 |
| §6 | Config additions only (6 keys); a real 0.6.4 file keeps all 133 keys/values | ✅ | ✅ | — | §10 46 |
| §7 | Stubs mirror the new API; pure shift math; negative controls documented | ✅ | ✅ | ✅ | — |
| §10 | Crash hardening (0.6.3), re-reviewed for 0.7.0 (controller faults contained inside NWH's ForwardStep; no new spawn-wave work) | ✅ | ✅ | ✅ (0.6.3) | §10 35 |
| §11 | Telemetry pick (0.6.3/0.6.4); strip geometry now pure and tested with pins | ✅ | ✅ | ✅ (0.6.4) | §10 37, 43 |
| audit | Game-changed drivetrain/clutch values adopted (drift), telemetry reset defaults, selected-vehicle overwrite, 300 px min width, tab-label overflow, digit keys vs shift keys, per-pass Last-driven pick | ✅ | ✅ | ✅ (1–5 fail each; the pass cache is performance-only) | §10 39, 45 |

Known unknowns still to verify in-game: whether the game's saves serialise drivetrain wiring (0.6.2 hash hygiene is defensive) and gear lists (turn Gearbox off before saving); game diff-lock FSMs acting on bypassed diffs; how often `CheckTag` rewrites transmission fields (the drift adoption handles any rate); the HUD gear display lag.

### Status after 0.6.4-alpha (historical)

| § | Item | State |
|---|---|---|
| §2 core | Custom drivetrain layouts, config-only (`[Drivetrain.Layout]`) | ✅ done 0.6.2, harness-tested, negative controls |
| — | Curve-editor / gear-graph grab fix (press-position picking), allocation-free apply path | ✅ done 0.6.2, harness-tested |
| §10 | Crash hardening: post-load quiet window + spawn-jump deferral; per-category exception guards | ✅ done 0.6.3, harness-tested, negative controls |
| §11 | Telemetry driven-vehicle pick + liveness gate | ✅ done 0.6.3 / 0.6.4, harness-tested, negative controls |
| §1–§5 | (open at 0.6.4) | ✅ done 0.7.0 — see the checklist above |

## 1. Mod-owned gearbox subsystem (unlock Gearbox for every transmission)

**Why this exists**: the game's PlayMaker shift logic (the `Wrapper` driving FSM + the `INPUT_GearChange`/`INPUT_ShiftUpDown` input FSMs) assumes each vehicle's stock gear count and shifts the box through its own variables. Any resized gear list on an automatic transmission makes the car stuck in gear/neutral (0.6.x experience: engine revs, wheels don't move). The 0.6.0 category is therefore release-gated. 0.7.0 replaces the game's shifting with the mod's own controller while the Gearbox category is ON.

**Known game facts (verified; encode as load-bearing)**:
- The vehicle's input FSMs use GetButtonDown/GetButton with InsaneSystems names: `ShiftIntoR1`, `ShiftInto1..ShiftInto8`, `ShiftUp`, `ShiftDown` (the game's InputManager axis list also has `Shift-Up`/`Shift-Down`); the FSM dump shows per-vehicle `INPUT_*` GameObjects under the vehicle prefab with one action per gear (this is also how `VehicleTuner.StockGearCountFromFsms` reads the stock count — reuse it).
- The game's FSMs write `vc.input.*` every frame via PlayMaker `SetProperty` reflection (README §2.2) and control the automatic box through FSM variables (`_Shift`, `_AutomaticGearbox`, `Throttle`, `Normalized` seen in the `Wrapper` FSM). The game setting `automatic_gearbox` (ES3, read-only import like the others) decides which FSM branch runs.
- NWH2: `TransmissionComponent` (gamecode/TransmissionComponent.cs) — `gears` list layout `[reverse negatives…, 0 neutral, forward positives…]`, `Gear` setter is synchronous (`gearIndex = GearToIndex(value)`), `ShiftInto` refuses during the post-shift ban / in-flight shifts / full damage, `forwardGearCount/reverseGearCount` recomputed each tick, `transmissionType` re-assigns the shift delegate live, `GearName` gives "R1/N/1..12" strings, `isShifting`, `shiftProgress`. `ClutchComponent` (gamecode/ClutchComponent.cs) exposes engagement knobs; `PowertrainComponent.OutputRPM` (gamecode/PowertrainComponent.cs, in the bundle) is the engine RPM.

**Design (implement this)**:
1. **Suppress the game's shift writes while Gearbox is ON.** The tuner (or a new `ShiftController` on the hidden runner) must ensure the game's FSMs cannot write `transmission.Gear` / `input.GearShift` while the category is applied. Implement by Harmony-patching PlayMaker's `SetProperty` action (Assembly-CSharp, class `SetProperty`) with an instance-aware prefix: skip when the target object is a `VehicleController` (or its `powertrain.transmission` / `input` member) and the property name is `Gear`/`GearShift` — inspect the real action's fields (targetObject/targetProperty, cached FieldInfos) in the game code before writing the patch; the stubs must mirror what you use. Also patch (or reuse the InputBlocker layer-A whitelist) the `ShiftIntoN`/`ShiftUp`/`ShiftDown` reads so the shift REQUEST still reaches the mod while the game's own shift APPLICATION does not. Suppression must be scoped to Gearbox-ON only, and OFF must restore the game's behavior byte-for-byte (the hidden-runner + applied-flags restore model).
2. **`Runtime/ShiftController.cs`** — the mod's own shifting, run from the hidden runner every fixed/2s tick while Gearbox is ON and the vehicle is targeted:
   - **Automatic mode** (the vehicle's own transmissionType is Automatic or the preset's mode is Automatic): compute the target gear from `OutputRPM` against per-gear shift points derived from the tuned ratio list — upshift when RPM > `shiftUpRpm` (a preset knob, default scaled from the vehicle's captured stock upshift RPM and the new gear ratio step: `upRpm[i] = clamp(stockUpshiftRpm * ratio[i+1]/ratio[i] * userFactor, 0.4*idle…, 0.97*revLimiter)`), downshift when RPM < `downRpm[i]` with hysteresis; respect `vc.input.Throttle` (kickdown: >0.8 throttle lowers the upshift point by ~15%), never shift while `isShifting`, and skip below 2 m/s with a creep gear hold. Write `transmission.Gear = target` directly.
   - **Manual mode**: read the `ShiftIntoN`/`ShiftUp`/`ShiftDown` requests yourself (the names above; reuse `InputBlocker.RouteControllerAction`-style whitelist reading or poll `InputController` through the same layer-A patch surface) and shift; clamp to the tuned count.
   - **CVT/External**: the shift controller does nothing (their own logic; the gearbox category keeps its CVT clutch-only and External-untouched rules).
3. **Gear display sync**: the game's HUD/FSM shows its own gear variable. Cosmetic mismatch is acceptable for 0.7.0 — document it; do NOT attempt HUD patching (scene FSM, no compiled code to patch).
4. **Restore**: Gearbox OFF → suppression lifted, shift controller stops, captured stock (gears/type/clutch) restored, and the transmission left in a valid gear (`ClampGear` as today).
5. **Unlock**: with the shift controller in, remove the `ComingSoon` gate (tab shows the full UI; `ApplyGearbox` keeps its mode-first order but automatics are now tunable because the mod owns shifting — delete the `AnyGearboxSkipped` skip and the associated note).

**Config keys (additions)**: `[Gearbox.Custom] ShiftUpFactor` (0.5–1.5, default 1 — scales the computed upshift RPMs), `ShiftDownFactor` (0.5–1.5, default 1), `KickdownScale` (0.5–2, default 1). Keep everything else.

> **Status (0.7.0, done + harness-tested):** implemented as NWH's `shiftDelegate` instead of a `SetProperty` patch (the FSMs never write `Gear`; their `input.Shift*` writes are the requests the controller reads). Shift points come from the landing-RPM rule rather than the formula in design point 2 (that formula lowers the upshift point on wide steps, which causes hunting), and kickdown raises the points. The controller hooks only for presets that change shifting. Details and reasons: README "Changes in 0.7.0-alpha".

## 2. AWD / 4WD and center-diff control (Drivetrain)

**Scope honestly.** NWH2's powertrain differentials are wired at init (`DifferentialComponent.OutputA/OutputB`; gamecode/Powertrain.cs + DifferentialComponent.cs — read both). A live RWD→AWD conversion means rewiring the front diff's input source, which NWH does not support at runtime. Therefore:
- **Deliver for 0.7.0**: center-diff mode (Stock/Open/Locked/LSD) and center-diff bias + stiffness (the existing DiffBiasScale/DiffStiffnessScale already apply to centre diffs — extend the panel with explicit centre-diff controls and per-axle torque-split readout), plus a **"4WD strength" per-axle torque factor** implemented through the existing differential bias where the wiring allows (front/rear bias on AWD vehicles; on RWD/FWD the slider is disabled with the note "this vehicle drives one axle — AWD conversion needs NWH wiring changes").
- **If**, after reading Powertrain.cs/DifferentialComponent.cs, a safe live axle-attach mechanism exists (e.g. re-pointing a centre diff's outputs), implement the AWD toggle for it — otherwise ship the above and document exactly why conversion is out of scope. Do not guess.

> **Status (0.6.2, done + harness-tested):** the premise above is wrong — live rewiring IS supported. NWH steps the powertrain through `_output`/`_outputB` references every tick and the public `Output`/`OutputB` setters relink live (README §2.14). 0.6.2 ships a config-only **custom drivetrain layout** (`[Drivetrain.Layout]`, README "Changes in 0.6.2-alpha") that covers RWD↔AWD conversion, transfer cases (Open/Locked/LSD with split/stiffness) and multi-axle trucks. Still open for 0.7.0: panel UI for it (the RWD/FWD "disabled slider" note above is obsolete — a layout can convert), explicit centre-diff panel controls and the per-axle torque-split readout.

## 3. Truck gearbox preset (12 gears)

With §1 in, add a **"Truck"** Gearbox preset: GearCount 12, per-gear scales authored for a wide-ratio spread (gear 1 +20%, gears 2-6 +10%, 7-10 stock, 11-12 −10/−15% — verify feel in-game), clutch Street-style (grip ×1.1, range ×1.15), ShiftUpFactor 0.9 (shifts earlier, truck-style lazy revving), TransmissionMode Stock. Update the preset grid (perRow fits: 6 presets, 3+3).

> **Status (0.7.0, done + harness-tested):** the Truck preset uses the new `SpreadRatios`. Continuing a 5-speed to 12 gears spans about 29:1 with an unusable top end, so the 12 gears are spread progressively over the vehicle's own 1st-to-top range instead (5.7:1). The clutch values follow the spec (×1.1 / ×1.15), so the clutch-type row shows "Custom" for Truck.

## 4. Telemetry pinning ("add to telemetry" boxes)

Each slider (all categories) gains a small **pin toggle** (a 26×26 button next to the per-slider Reset) that adds the value to a **tracked telemetry list**: `[Telemetry] Pins` (string, `;`-separated stable keys like `Steering.RateMultiplier`, `Suspension.SpringFront`, … — define the key scheme in `UiStrings`-free code, e.g. a `TelemetryPin` registry). The strip renders one cell per pin (dynamic cell count, the existing `Width=440` grows or wraps to a second row; keep click-through, one Graphic per GO). Pinned values sample from the same 4 Hz tick as the existing four cells. Config round-trips; unknown pin keys are dropped on load. The existing speed/RPM/gear/slip cells stay always-on when telemetry is enabled.

## 5. Tighter UI ("bigger text, smaller boxes")

- Reduce block paddings ~15% (row heights 58→50, preset buttons 44→40, master row 76→66, section titles 38→32, footer 124→110) and raise font sizes +1..2 on labels/hints (13→14/15, 14→15/16, 15→17, 17→19, 21→23) — via the existing `PanelLayout` constants so the harness layout tests can assert the new geometry. Keep the 0.6.0 width-adaptive insets; verify no overlap at 300/460/1000 px in the layout tests.
- Keep the two-row tab strip and the curve-editor/gear-graph header bands; re-run their harness layout assertions with the new constants.

## 6. Config schema + migration

- **Additions only** (§1 shift knobs, §4 `[Telemetry] Pins`). No key renamed, removed, or default-changed; a 0.6.0 cfg loads as-is (harness test).
- `Save()`/`PushAllToRuntime`/`WireAll` extended; string parsing follows the `ParseDiffMode` pattern.

## 7. Harness expectations

- **Stubs**: extend with whatever §1 needs (PlayMaker `SetProperty`-shaped action with targetObject/targetProperty fields; any Powertrain/Differential member the AWD work reads) — mirror real signatures from gamecode/. ShiftController logic must be **pure static functions** (gear target computation, kickdown, hysteresis, clamp) so the harness tests them without Unity time.
- **Tests**: shift-point math (upshift RPM scaling with ratio step, kickdown, hysteresis, creep hold), manual-mode request mapping + clamping, suppression scoping (Gearbox-OFF restore byte-for-byte), truck preset values, centre-diff controls + RWD/FWD disabled state, pin registry round-trip + unknown-key dropping, layout geometry at 300/460/1000 px with the new constants, 0.6.0 cfg load. **Negative control**: state which new checks fail against the untouched 0.6.0 plugin.
- `ComingSoon` gate: remove it in code AND tests only as part of §1; until then `TestGearbox` flips it off (existing pattern).

## 8. Acceptance criteria — "done" means

1. `bash verify/run.sh` green; negative control documented with counts in README "Changes in 0.7.0-alpha".
2. In-game (§10 items 21+): with Gearbox ON and a 12-gear Truck preset on an AUTOMATIC car, the car drives and shifts through all 12 gears by itself; manual mode responds to the game's ShiftInto keys; disabling Gearbox restores the game's own shifting exactly; AWD cars show live centre-diff/bias controls, RWD/FWD show the disabled note; pinned telemetry values appear in the strip and persist; the panel is visibly tighter at the same widths; a 0.6.0 cfg loads unchanged.
3. README "Changes in 0.7.0-alpha" (features, behavior changes, new keys, allocation notes — the ShiftController's per-tick path must be allocation-free — and the "Not changed" §2-facts paragraph); strings.md sweep regenerated; version bumps (`PluginInfo` "0.7.0", README title, §6b tag, release/README.txt).

## 9. Risks with recommended answers

1. Shift suppression too broad → scope strictly to `SetProperty` writes targeting the transmission/input of a VehicleController while Gearbox is ON; restore is tested byte-for-byte.
2. ShiftController fights the game's FSM variables → we only suppress WRITES; the FSM reads (if any) are untouched.
3. AWD conversion impossible at runtime → ship centre-diff + bias + the honest disabled note (decide after reading Powertrain.cs/DifferentialComponent.cs).
4. 12-gear auto shift feel → authored defaults in §3; expose ShiftUp/DownFactor so it is tunable live.
5. Pin list growth → cap at 12 pins; unknown keys dropped; strip wraps to a second row.
6. Tighter UI regressions → the harness layout tests at three widths are the gate.


## 10. Crash report (investigate)

`docs/crash-2026-10-04.md` documents a native crash the user hit while loading a save with 0.6.0 (all categories OFF, mod inert — see the report). Include its two hardening items in your work: (1) pause the tuner's 2 s scan during the post-scene-load spawn wave (~5 s after `sceneLoaded`, plus a one-tick defer when the tracked-vehicle count jumps), (2) keep every capture/apply path exception-guarded per category so a half-initialised mid-spawn vehicle can never take the tuner down. Note in your changelog that you reviewed the report.


## 11. Telemetry display (user-reported, resolved in 0.6.3 + 0.6.4)

**Status:** resolved. 0.6.3 fixed the driven-vehicle pick (idling engines counted as input); the user's follow-up report ("still shows a parked car's zeros until I steer") traced to parked cars freezing their FSM-written input at exit values — fixed in 0.6.4 with the liveness gate (README "Changes in 0.6.4-alpha", §11b in the status table; harness negative control = 5 failures when reverted). The `[Telemetry] DebugPick` config key logs the pick for any future report. The strip's render math (scale factor `PanelLayout.ScaleFactor(Screen.height, TelemetryScale)`, ConstantPixelSize; corner anchoring `CornerAnchor`/`CornerOffset`/pivot/sizeDelta; cell insets; text sizes) was checked and left unchanged. Remaining for §4: in-game check README §10 item 37, plus the planned telemetry pins.
