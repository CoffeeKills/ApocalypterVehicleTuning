# FEATURES.md — Feature specification for v0.6.0-alpha

## Implementation status (0.6.0-alpha) — checklist

Legend: **[x] done** · **H** = covered by `verify/` (harness-tested) · **G** = needs the in-game check (README §10 item) · **Δ** = implemented differently from the spec text below, reason in README "Changes in 0.6.0-alpha → Deviations".

**§0 Scope**
- [x] `PluginInfo` "0.6.0", README title / Changes section / §6b tag — H (compile)
- [x] gamecode gap listed: `PowertrainComponent.cs` should be copied into `gamecode/` (stub mirrors only `OutputRPM`)

**§1 Alignment** — `AlignmentPreset.cs`, `AlignmentSettings.cs`, `VehicleTuner.Alignment.cs`, tab 8
- [x] Presets Stock/Street/Sport/Race/Off-road/Stance/Custom; per-axle + "Per-wheel (advanced)" — H
- [x] Camber via `WheelUAPI.Camber` (stock + offset) — H, G item 24
- [x] CamberController + solid-axle detect / skip / warn (never disables game components) — H
- [x] Caster/toe per axle via `WheelGroup.CasterAngle/ToeAngle`; gates opened only while needed, restored on OFF; L/R toe mirroring; euler Z preserved — H
- [x] Position via the wheel transform (never `Wheel.localPosition`); |x| ≥ 1 cm, side kept — H
- [x] Stale wheelbase/trackWidth warned, not recomputed — H (flag), G
- [x] Exact restore (camber, caster/toe, gates, localPosition, localEulerAngles); OFF→ON baseline refresh; rescan re-apply — H
- [x] Reference-vehicle stock readouts (`ReferenceWheelStock` / `ReferenceGroupStock`); rows disabled without a vehicle — H (accessors), G
- [x] Δ values are offsets on each vehicle's geometry (Stock = as shipped); PosX is outward
- [x] Δ per-wheel caster/toe dropped (no fields/keys for it in this spec)
- [ ] G sign directions (camber, PosX, toe) — README §10 item 24

**§2 Gearbox** — `GearboxPreset.cs`, `GearboxSettings.cs`, `VehicleTuner.Gearbox.cs`, `GearGraph.cs`, tab 9
- [x] Gear count 0..12 (0 = own); continuation `r[n] = r[n-1]²/r[n-2]`; truncation; reverse + neutral untouched — H
- [x] Δ re-shift guard writes `Gear` (ShiftInto refuses during ban/in-flight shift); shrink deferred while `isShifting`; mid-shift restore keeps placeholders, then trims — H
- [x] CVT (and External / non-standard lists) refused for count/ratios; clutch still applies; mode Stock only — H
- [x] Per-gear factors 0.5..1.5 with absolute ratio readouts — H (math), G
- [x] Clutch types Stock/Street/Sport/Race/Custom over slipTorque (≥1) / engagementRange (≥1) / engagementRPM; honest panel note — H
- [x] Δ engagementRPM never pushed below min(stock, 1.1 × idle) — H
- [x] Transmission mode Stock/Manual/Automatic (live-safe) — H
- [x] No field shared with Drivetrain — H
- [x] GearGraph: bars vs stock outline, header band + public constants, pure `RouteDrag`/`RouteClick`, scroll forwarding, `canEdit` — H (routing, bands), G item 23/25
- [ ] G 6-gear add, 4-gear cut, Race clutch feel, CVT untouched — README §10 item 25

**§3 Live panel + selective input blocking** — `InputBlocker.cs`, `SettingsPanelManager.cs`
- [x] Layer A: InputController name whitelist (6 statics patched), fail-closed, once-per-name log (max 64) — H (routing)
- [x] Layer B: class patch set incl. OnEnter — Δ forks routed by name, OnEnter gated by `everyFrame` (skip + Finish for one-shots) — H
- [x] `SetInputBlocked` / `SetFreeze` split; live never touches timeScale; Freeze = 0.5.0 incl. one-frame-late restore of a pause-menu 0 — H (state), G item 22
- [x] Swallow-nothing click model in live mode; dim + click-outside only with Freeze — G
- [x] Selection cleared every frame (Submit re-fire hazard) — G item 21
- [ ] G whitelist names verified in game; no unknown-action log lines for driving controls — README §10 item 21

**§4 Docking / width / scale / transparency** — `PanelLayout.cs`, Panel tab
- [x] Docked right, full height, `PanelWidth` 400..800 (default 460), capped to the canvas — H (EffectiveWidth)
- [x] Full fixed-px inventory width-adaptive via `Relayout(width)` (no rebuild) — H (bands at 400/460/800)
- [x] `ConstantPixelSize`, scaleFactor = Screen.height/1080 × PanelScale — H
- [x] CanvasGroup transparency — G
- [x] 10 tabs, two-row 5+5 strip — H (sizes)
- [ ] G every tab at 400/460/800 × 0.5/1/2 — README §10 item 23

**§5 Expert values + wider ranges**
- [x] Brake-torque N·m readout (`MeanBaseline(Readout.BrakeTorque)`), blank readouts at baseline 0 — H (baseline)
- [x] Smart toggles: Per-wheel, Keep vehicle's gear count, Transmission mode, Freeze — G
- [x] Widened Limits drive sliders and `AcceptableValueRange`; shift RPMs / diff stiffness not widened; out-of-range values clamp on load — H

**§6 Telemetry strip** — `TelemetryStrip.cs`
- [x] Own canvas (31000), click-through, hidden with panel / no vehicle / disabled, 4 Hz — G item 26
- [x] `TryGetTelemetry`, UiStrings templates, corner parse — H
- [x] Δ slip shown as |LateralSlip| × 90 (approximate degrees)

**§7 Preset export/import** — `PresetCodec.cs`
- [x] Format, sorted keys, invariant floats; import into Custom only; BasedOn resolution — H
- [x] Byte-identical round trip for all nine books (Δ nine, not ten — Panel has no book), garbage rejected, clamping, unknown-key skip — H
- [x] Footer Copy/Paste + status line, clipboard in try/catch — H (status strings), G item 27

**§8 Small extras**
- [x] Digit hotkeys 1..0 (open panel only) — H (mapping), G
- [x] "Turn everything off" two-click — H
- [x] Alignment stock readouts — H (accessors)
- [x] LastTab remembered — H (parse/persist)

**§9 Config** — additions only, no migrations, 0.5.0 cfg loads as is, `Save()` covers all books, `WireAll` extended — H

**§10 Harness** — stubs extended (see README), 135 new logic tests, prefix suite unchanged, negative control documented — H

**§11 Acceptance** — run.sh green (355 + 18); README Changes / §10 items 21–28; strings.md updated — H

---

**This file is the authoritative spec for the next version.** Implement everything below on top of the 0.5.0 code in this bundle. Where this file and PROMPT.md conflict, this file wins for *features*; PROMPT.md's hard constraints (no `ES3.Save`, no `vc.input.*` writes, allocation-free steering prefix, hidden-runner survival architecture, one Graphic per GameObject, mouse-only panel, BepInEx config the only persistence, unchanged GUID) always win.

You have no terminal. The user runs `bash verify/run.sh` (needs .NET SDK 8+; globs **all** `plugin/**/*.cs`, so every new file must compile against the stubs — extend them, §10). Reason through it carefully.

## 0. Scope, version, prerequisites

- `plugin/PluginInfo.cs` → `"0.6.0"` (BepInEx 5 parses System.Version: numeric-only). README title → "v0.6.0-alpha" + a new "## Changes in 0.6.0-alpha" section (§11).
- `gamecode/` now includes `ClutchComponent.cs` and `Powertrain.cs` (added to this bundle). If you reference any other game type not present, ADD the file from the user's decompiled tree (ApocalypterSource) and list it in your changelog so the user can copy it — never leave the bundle uncompilable.
- The current suite is 238 tests (220 logic + 18 prefix), all passing. Your changes must keep it green and add the §10 tests.

## 1. Wheel geometry — "Alignment" (new tab, 9th category)

**Feature**: move each wheel and change camber, caster, toe, position. Per-axle modes for everything; an advanced **"Per-wheel (advanced)" toggle** for per-wheel camber/caster/toe/position. Physically-plausible-but-wide ranges: camber −12..+12°, caster −10..+12°, toe −5..+5°, position ±30 cm per axis.

**New files** (follow existing patterns exactly):
- `plugin/Settings/AlignmentPreset.cs` — `ITunablePreset` (mirror `SuspensionPreset.cs`). Fields (absolute units, defaults 0): `CamberFL/FR/RL/RR` (deg), `CasterF/CasterR` (deg), `ToeF/ToeR` (deg), `PosXFL/PosXFR/PosXRL/PosXRR`, `PosY…`, `PosZ…` (cm). Presets: **Stock** (identity), **Street** (CamberF −1.0 / CamberR −0.5, ToeF +0.05 / ToeR −0.05, CasterF +1.0), **Sport** (CamberF −2.0 / −1.2, ToeF +0.10 / +0.02, CasterF +3.0, PosY −2), **Race** (CamberF −3.5 / −2.5, ToeF +0.15 / +0.05, CasterF +5.0, PosY −3), **Off-road** (Camber 0, ToeF +0.20, PosY +5, PosX ±6 mirrored), **Stance** (Camber −8 / −8, PosY −6, PosX ∓4), **Custom**. Verify sign directions in-game (README §10 items).
- `plugin/Settings/AlignmentSettings.cs` — mirror `SuspensionSettings.cs`: `PresetBook<AlignmentPreset>`, `Enabled=false`, `PerWheel=false`, `BeginEdit/Reference/Shown/ResetAll`, axle helpers writing both wheels.
- Apply/restore in `plugin/Runtime/VehicleTuner.Systems.cs` (`ApplyAlignment/RestoreAlignment`); capture in `VehicleTuner.cs` (extend `WheelData`/`GroupData`, add `Category.Alignment` to the enum + `RefreshBaselines`, `_alignmentApplied` flag through `ApplyLive`/`RestoreAll`).
- `BuildAlignment` tab builder in `SettingsPanel.cs` (tabs go to 10 — §4).

**NWH/game API constraints + hazards (encode these exactly)**:
- **Camber**: `WheelUAPI.Camber` — abstract on `WheelUAPI` (gamecode/WheelUAPI.cs:39), no cast needed. `WheelController`'s override clamps to ±16° in the setter (gamecode/WheelController.cs:292-312) and re-applies every fixed tick to visual + collider + friction basis, side sign auto-flipped from `transform.localPosition.x` (WheelController.cs:843-845). Just write the value.
- **Hazard — `CamberController` component** on the wheel GO overwrites Camber from a curve every FixedUpdate (gamecode/CamberController.cs:18-21). Detect at capture (`wheelUAPI.GetComponent<CamberController>()` — the `HasTyreWearComponent` pattern), flag the record, **skip writing camber on those wheels, show a warning note**. Never disable game components.
- **Hazard — solid axles**: `WheelGroup.Update()` overwrites both wheels' Camber every tick when `isSolid && count==2 && trackWidth != 0` (gamecode/WheelGroup.cs:159-167). Flag `isSolid` groups; skip per-wheel camber there + warn.
- **Caster/Toe are per-AXLE**: `WheelGroup.CasterAngle/ToeAngle` setters call `ApplyGeometryValues()` immediately (no runtime clamp; the editor Range ±8 doesn't apply) (gamecode/WheelGroup.cs:67-91). `ApplyGeometryValues` writes `transform.localEulerAngles`: X = −caster (both sides), Y = toe negated for `localPosition.x >= 0` else positive (L/R mirrored), **Z preserved**; gated by `applyCasterAngle/applyToeAngle` bools (WheelGroup.cs:188-205). If a captured group has a gate false and the user requests nonzero, set the gate true while applied and **restore both gates on OFF**.
- **Per-wheel caster/toe (advanced mode)**: direct `transform.localEulerAngles` writes (X/Y only, never Z). These are clobbered by any later `ApplyGeometryValues()` or group re-init — re-apply on the tuner's 2 s rescan and say so in the panel note.
- **Position**: no API — move the WheelController GameObject's `transform.localPosition` (honored live). **Never use `wheel.localPosition.x/z`** (the `Wheel` struct fields; prefab visual offsets are erased at `Wheel.Initialize`). Hazards: `vc.wheelbase`/`wheelGroup.trackWidth` are computed only at init → stale after moves (Ackermann + solid-axle camber degrade; warn, don't recompute). **Clamp final `localPosition.x` to |x| ≥ 1 cm** — crossing x=0 flips every side convention.
- **Restore** (OFF / OnDisable / OnDestroy via the existing `RestoreAll` path): write back captured `Camber`, group `CasterAngle/ToeAngle` + both gate bools, captured `transform.localPosition` + `localEulerAngles`.
- **Baseline refresh**: `RefreshBaselines(Category.Alignment)` re-reads the above on OFF→ON.
- **UI reference vehicle**: first tracked vehicle's wheel roles (FL = `IsFront && localPosition.x < 0`, etc., existing mean-Z axle detection). No vehicle → rows disabled + standard status note.
- **Readouts**: sliders in degrees/cm; value box shows e.g. "−3.0°" plus a second line "stock −1.2°" via new public `VehicleTuner` accessors (`ReferenceWheelStock(WheelRole)` / `ReferenceGroupStock(bool front)` — public readonly structs; keep `_records` private, same shape as `MeanBaseline`).
- **Cadence**: `ApplyLive()` per slider tick + 2 s scan + `ReapplyNow` on open/toggle. Allocation-free apply/restore loops.

**Config keys** (`ModConfig` + `Limits`): `[Alignment] Enabled` (false), `Preset` ("Stock"), `PerWheel` (false); `[Alignment.Custom] BasedOn`, `CamberFL/FR/RL/RR` (−12..12), `CasterFront/CasterRear` (−10..12), `ToeFront/ToeRear` (−5..5), `PosXFL…PosZRR` (12 keys, −30..30). Limits: `AlignmentCamberMin/Max = −12/12`, `AlignmentCasterMin/Max = −10/12`, `AlignmentToeMin/Max = −5/5`, `AlignmentPosMin/Max = −30/30`.

**Acceptance**: harness tests for capture/apply/restore incl. sign mirroring, x=0 clamp, solid-axle + CamberController skip, gate-bool restore, per-wheel Z preservation; in-game: camber visibly tilts wheels; caster/toe change steering feel; moved wheels stay moved and restore exactly on OFF; per-wheel mode survives a rescan; warned vehicles keep stock behavior.

## 2. Gearbox customization (new tab, 10th category)

**Feature**: gear count, clutch "type", per-gear ratios on a graph + sliders (car-tuning-game style).

**Game-side caveats (state these flatly — do not guess)**:
- **NWH2 has NO clutch-type model.** `ClutchComponent` (now in gamecode/) exposes: `ClutchControlType {Automatic, UserInput, Manual}`, `engagementRPM`, `engagementRange`, `throttleEngagementOffsetRPM`, `slipTorque` (max transferable torque), `creepTorque`, `creepSpeedLimit`, `engagementCurve`, `clutchInput`, `Engagement`. **"Clutch type" = named presets over these knobs**: Stock (vehicle's own), Street single (slipTorque ×0.9, engagementRange ×1.2), Sport single (×1.2 / ×0.85), Race twin-disc emulated (×1.8 / ×0.6, engagementRPM −200), Custom (three sliders). Panel note must say: "NWH2 has no real clutch model — clutch type is emulated through clutch capacity and engagement speed."
- **Gear count live-resize**: `transmission.gears` is `List<float>` laid out `[reverse (negative)…, 0 (neutral), forward (positive)…]` (gamecode/TransmissionComponent.cs:50); `forwardGearCount/reverseGearCount` recomputed per tick; `GearToIndex(g) = g + reverseGearCount`. **`CalculateTotalGearRatio()` does `gears[gearIndex]` UNGUARDED** (:311-322) — after resizing, re-shift into a valid gear immediately (`ShiftInto(min(currentForward, newForwardCount), instant: true)` or `Gear = 0`). Never alter the reverse section or remove the neutral 0. Added gears: geometric continuation `r[n] = r[n-1] × r[n-1]/r[n-2]`. Truncation drops the top gears. `GearCount = 0` = keep each vehicle's own count (default — one config serves all vehicles).
- **CVT**: `VC_Validate` requires exactly 3 gears for `TransmissionShiftType.CVT` (:277-280). Refuse count/ratio changes on CVT (skip + panel note); clutch knobs + transmission mode still apply.
- **No field sharing with Drivetrain** (the RefreshBaselines invariant): Gearbox must NOT touch `shiftDuration`, `UpshiftRPM/DownshiftRPM`, or `finalGearRatio` — shift *feel* comes from clutch engagement + slip torque only.
- `transmissionType` (Manual/Automatic/CVT) is live-safe (`ForwardStep` re-assigns the shift delegate on change, :401-409). Offer Stock/Manual/Automatic (CVT vehicles: Stock only).
- `GearName` (:196-216) gives "R1/N/1..12" strings — use for graph axis + telemetry.

**New files**:
- `plugin/Settings/GearboxPreset.cs` — `ITunablePreset`: `GearCount` (int 0..12, 0 = vehicle's own), `Gear1Scale..Gear12Scale` (0.5..1.5, factor on each vehicle's own ratio), `ClutchGripScale` (0.5..2 → slipTorque), `ClutchRangeScale` (0.4..2 → engagementRange), `ClutchRpmOffset` (−500..+500 → engagementRPM), `TransmissionMode` (Stock/Manual/Automatic). Presets: Stock, Comfort (×0.9/×1.2), Sport (×1.2/×0.85), Race (×1.8/×0.6, offset −200), Custom.
- `plugin/Settings/GearboxSettings.cs` — book delegate, `Enabled=false`, `BeginEdit/Reference/ResetAll`, `PerGearScale(i)`.
- `plugin/Runtime/GearGraph.cs` — a **sibling** of `CurveEditor.cs` (bars, not a curve): MaskableGraphic (one Graphic per GO) drawing N bars (current fill vs stock outline), the same header-band layout with public layout constants, pure `RouteDrag(bool canEdit, int bar, bool hasScroll)` / `RouteClick(...)` helpers for the harness, non-handle drags forwarded to the parent ScrollRect, `canEdit` predicate (`GearboxSettings.Enabled`), click selects a gear (highlights its slider row). Reuse `UiKit.Place/Top/Label/MakeButton` + the CurveEditor inset conventions.
- Apply/restore in `VehicleTuner.Systems.cs` (`ApplyGearbox/RestoreGearbox`), `CaptureGearbox` (gears deep copy, count, clutch fields, transmissionType) + `Category.Gearbox` in RefreshBaselines. Apply order: ratios → count resize (with the re-shift guard) → clutch fields (clamp `slipTorque ≥ 1`, `engagementRPM = stock + offset`, `engagementRange ≥ 1`) → transmissionType (skip CVT except Stock). Restore reverses, then re-shifts into a valid gear (restore captured gear if still valid).
- `BuildGearbox` tab: master switch → preset row → description → "Gear count" slider (0..12, 0 = "Vehicle's own") → GearGraph row → per-gear slider rows (1..max(GearCount, reference count); each shows the absolute ratio `stock × factor` of the reference vehicle as readout) → "Clutch type" preset grid (Stock/Street/Sport/Race/Custom → the three sliders) → "Transmission mode" row → status note (CVT warning line included).

**Config keys**: `[Gearbox] Enabled` (false), `Preset` ("Stock"); `[Gearbox.Custom] BasedOn`, `GearCount` (0..12), `Gear1Scale..Gear12Scale` (0.5..1.5), `ClutchGripScale` (0.5..2), `ClutchRangeScale` (0.4..2), `ClutchRpmOffset` (−500..500), `TransmissionMode` (name-only parse, Stock fallback — the `ParseDiffMode` pattern). Limits: `GearCountMin/Max = 0/12`, `GearRatioMin/Max = 0.5/1.5`, `ClutchGripMin/Max = 0.5/2`, `ClutchRangeMin/Max = 0.4/2`, `ClutchRpmMin/Max = −500/500`.

**Acceptance**: harness — resize math (reverse+neutral preserved, continuation, truncation), re-shift logic, CVT refusal, factor application, restore completeness; in-game — 5-speed → 6 gears works without exceptions, → 4 gears shifts correctly at once, Race clutch launches harder/shifts crisper, Stock restores exactly, CVT untouched.

## 3. Live-driving panel + selective input blocking

**Feature**: panel open = game keeps running (throttle/brake/steering/handbrake live), mouse free for the UI. Default live; `Freeze game while open` reproduces the old freeze exactly. Esc closes; the game never sees the closing key; the pause menu never opens behind the panel.

**Two-layer blocker rework (`plugin/Runtime/InputBlocker.cs`)**:
- **Layer A — parameter-value whitelist on `InsaneSystems.InputManager.InputController`** (Assembly-CSharp). The game's driving actions are HutongGames forks routing through it: `GetButton → GetKeyActionIsActive(buttonName.Value)`, `GetButtonDown/Up → GetKeyActionIsDown/IsUp`, `GetAxis`/`GetAxisKeyAxis → GetAnyAxisActionValue(axisName.Value)`. **Patch the four static methods with a `__0` string argument** and whitelist by name. Whitelist (from the FSM dump): `Throttle, Brakes, Handbrake, Clutch, Horn, Headlight, ShiftUp, ShiftDown, Cruise Control`; axes: `input`, `Steering Keyboard` (verify exact names in-game, §11). Whitelisted → original runs; anything else → skip (false/0f). Skipping is also the NRE guard (`InputStorage.GetKeyByName` throws on unknown names — gamecode/InputStorage.cs:62-72), so unknown names must never reach the original. **Fail-closed + discovery logging**: log each unknown name once (rate-limited) so the in-game check harvests missing driving names into the whitelist.
- **Layer B — keep the existing class-name regex patch set** for direct-Input actions that must stay dead while the panel is open: `GetAxisOrig` ("Mouse X"/"Mouse Y"/scroll), `GetMouseButton*`, `GetMouseX/GetMouseY`, `MouseLook`, `MouseLook2`, `MousePick*`, `AnyKey`, `GetTouch*`, `GetKey*` direct-Input classes, `Input`/`Mouse`-prefixed classes (current regex on OnUpdate/OnFixedUpdate/OnLateUpdate; keeping the GetButton/GetAxis/GetAxisKeyAxis class entries is redundant-but-harmless defense in depth). Also patch `OnEnter` for the same class set (one-shot `everyFrame=false` actions).
- **Freeze split**: replace `Set(bool)` with `SetInputBlocked(bool)` (Active flag gating both layers) + `SetFreeze(bool)` (the exact save/restore incl. pause-menu timeScale 0). Live mode: `SetInputBlocked(true)`, **never touch `Time.timeScale`** (`KeyAxisAction` ramps with `Time.deltaTime` — gamecode/KeyAxisAction.cs:36-47). Freeze ON: also `SetFreeze(true)`. Close: existing deferred one-frame unblock + `SetFreeze(false)` restoring the saved scale. Panel opened from the pause menu: live mode never writes timeScale, so the game stays at its pause-menu 0 — documented (the toggle only ever *adds* freezing, never unpauses).
- **Manager** (`SettingsPanelManager.cs`): replace `InputBlocker.Set(true)` (:532) with the split calls driven by `UI.FreezeWhileOpen`; `OnDisable` completes pending unblock + `SetFreeze(false)`; keep the per-frame cursor re-free loop and EventSystem find-or-create (unlocked cursor required to raycast).
- **Click model**: remove the full-screen dim click-catcher (`SettingsPanel.cs:102-110`) — **swallow-nothing: no dim, raycast only the window**. Accepted risk: opened from the pause menu, outside clicks can hit game menu buttons (uGUI onClick isn't blocker-gated) — document; with Freeze ON, restore the dim layer + click-outside-to-close (the old modal behavior).
- **Blocker routing must be pure static functions** (e.g. `InputBlocker.RouteControllerAction(string name)` + the whitelist set) so the harness tests them without Harmony.

**Acceptance**: in-game — driving with panel open: all four controls live, normal steering ramp; mouse free; Esc closes, no pause-menu bleed; log shows no unknown-action warnings for driving actions; Freeze ON reproduces 0.5.0 exactly. Harness: whitelist routing tests.

## 4. Panel docking, width, scale, transparency ("Panel" tab — 10 tabs total)

**Feature**: narrower window docked RIGHT, full height; game visible beside it; panel transparency + custom interface size (the game has NO UI-scale setting — `PanelScale` is fully mod-owned).

- Window (`SettingsPanel.cs:112-117`): `anchorMin=(1,0.02)`, `anchorMax=(1,0.98)`, `pivot=(1,0.5)`, `anchoredPosition=(-10,0)`, width = `PanelWidth` (400–800, default 460).
- **Fixed-px inventory — every one becomes width-adaptive** (per-instance computed insets from `win.rect.width` + a `Relayout(width)` re-applying them to stored RectTransforms so width changes apply live; rebuild-in-place without toggling blocker state is the documented fallback): AddSlider label insets 16/316 + left 328, `sliderRight 236/172`, value widths 150/86 + inset 82, reset 66/12; AddOption right insets 120 + switch 86; AddMasterSwitch right insets 160 + switch 120; footer LeftBox 320 / RightBox 180; page insets `Pad, 88, Pad−10, 158`; tab-bar font 16. CurveEditor constants (Side 16, ReadoutWidth 150, ReadoutRight 96, HintTop 38) fit at 440+ px — keep.
- **Scale**: CanvasScaler → `ConstantPixelSize` with `scaleFactor = (Screen.height / 1080f) × PanelScale` (reproduces 0.5.0 rendering at 1 — match=1 was already height-only); applied live, no rebuild.
- **Transparency**: CanvasGroup on the window root, `alpha = PanelAlpha` (0.4–1, default 1), live.
- **Tabs**: 10 (Steering, Suspension, Aero, Brakes, Grip, Drivetrain, Assists, Alignment, Gearbox, Panel) → **two-row strip (5+5)**, height ~26 px rows, font 14; arrays sized 10; per-row underlines.
- **Panel tab content**: Freeze toggle (default OFF), Transparency slider, Size slider (0.5–2), Width slider (400–800) + "changes apply immediately" note. All wired through ModConfig + OnSettingsChanged.
- Subtitle (lines 137-139): Esc-only close in live mode (no click-outside).

**Config keys**: `[UI] FreezeWhileOpen` (false), `PanelScale` (0.5–2, 1), `PanelWidth` (400–800, 460), `PanelAlpha` (0.4–1, 1), `LastTab` (int 0..9, 0). `ToggleKey` unchanged.

**Acceptance**: panel docks right, game visible/playable; width/scale/alpha apply live; at every width 400–800 × scale 0.5–2 nothing overlaps/unreachable across all 10 tabs (both curve editors + gear graph); Freeze reproduces 0.5.0; harness — layout-constant computation exposed as pure static functions with public constants (CurveEditor-bands precedent), tested at 400/460/800.

## 5. Expert values + wider ranges

- **Absolute readouts** (extend the existing conversion layer, `SettingsPanel.AddAxleFactor` + `VehicleTuner.MeanBaseline`): Alignment is absolute (deg/cm, §1); Gearbox ratios display absolute per-gear values (§2); add brake-torque Nm readout via a new `MeanBaseline(Readout.BrakeTorque)`; suspension already done. Guard baseline == 0.
- **Smart toggles** (bounded): Alignment "Per-wheel (advanced)"; Gearbox "Keep vehicle's gear count" + "Transmission mode"; Panel "Freeze while open". Existing steering/drivetrain toggles stay.
- **Wider physically-justified `Limits`** (widening never invalidates stored values — no migration): suspension factors `0.25/3` (suspension-only, replaces `FactorMin/Max` there), `FinalDrive 0.5/2`, `Power 0.25/3.5`, boost/loss `0.25/3`, `Grip 0.1/3`, `BrakeTorque 0.25/3`. **Do NOT widen**: shift RPMs 0.8–1.2 (lock-up guard depends on it), diff stiffness (oscillates above `max(1, stock)` — README fix 7).
- **Acceptance**: harness — Limits drive both slider bounds and config `AcceptableValueRange` (assert on `ConfigEntry.Description`); out-of-range hand-edited values clamp on load. In-game — suspension ×0.25 visibly soft, ×3 rock-hard.

## 6. Telemetry strip

New `plugin/Runtime/TelemetryStrip.cs` MonoBehaviour on the hidden runner (survives scene loads). Own canvas (sortingOrder 31000, below the panel), **all children raycastTarget=false** (click-through; one Graphic per GO: background Image root + label/value Text children). Hidden while the panel is open. Reads (null-guarded, `Vehicle.ActiveVehicle` — gamecode/Vehicle.cs:47): speed = `vc.Speed × 3.6` km/h; RPM = `engine.OutputRPM`; gear = `transmission.GearName`; front slip = mean |LateralSlip| over front wheels (new public `VehicleTuner.TryGetTelemetry(out …)` reading the active vehicle's record). 4 Hz update (allocations fine there; physics hot paths stay allocation-free). Dynamic strings via new UiStrings templates (`TelemetrySpeedFmt "{0} km/h"`, `TelemetryRpmFmt "{0} rpm"`, `TelemetryGearFmt "Gear {0}"`, `TelemetrySlipFmt "Slip {0}°"`); static captions plain literals. No active vehicle → hidden.

**Config**: `[Telemetry] Enabled` (bool, **true** — passive UI, not a vehicle change; justify the opt-in-convention deviation in README §8), `Scale` (0.5–2, 1), `Position` (name-only parse with fallback: TopLeft/TopRight/BottomLeft/BottomRight, default BottomLeft).

## 7. Preset export/import (copy/paste as text)

New `plugin/Settings/PresetCodec.cs` (pure static, fully harness-testable): format `AVT1|Category|PresetName|BasedOn=<Name>|Key=Value|…` (sorted, invariant culture, `;`-joined). `Serialize(category, preset)` covers all **ten** books (nine tuning categories + Steering; Panel/UI/Telemetry excluded — bounded). `Parse(text)`: validate header (wrong tag/category → failure with reason); unknown keys skipped + counted; known keys clamped to `Limits`; missing keys keep Custom's current value; on success apply into the category's **Custom slot** (`CopyValuesFrom` + BasedOn = source name when it resolves to that category's built-in, else "") and return a status string. Built-ins are never overwritten (export of a built-in carries `BasedOn=<Name>` so import forks Custom(Name)).

Panel: per-tab footer "Copy preset" / "Paste preset" buttons acting on the active tab's book + a status note line (`PresetCopiedFmt/PresetPastedFmt/PresetPasteFailedFmt` UiStrings templates). Clipboard = `GUIUtility.systemCopyBuffer` wrapped in try/catch (no clipboard → failure note, never a throw).

**Acceptance**: harness — Serialize→Parse→Serialize byte-identical for every category (incl. 12 position + 12 gear keys), garbage rejected, clamping verified, unknown keys skipped. In-game — copy Race suspension → reset → paste → Custom (Race) identical.

## 8. Small extras (bounded)

1. Digit hotkeys `1..0` jump to tabs 1–10 while the panel is open only (dual-input poll; digits aren't driving keys). Document in the subtitle.
2. "Turn everything off" footer button — two-click arm pattern (`OnResetClicked`); second click sets all nine `Enabled=false` + `ReapplyNow()`.
3. Alignment stock readouts (counted in §1).
4. Panel remembers the last tab (`[UI] LastTab`, `ShowTab(saved)` on open).

## 9. Config schema + migration rules

- **NO existing key renamed, removed, or default-changed.** Additions only: `[Alignment]`, `[Gearbox]`, `[Telemetry]`, `[UI] FreezeWhileOpen/PanelScale/PanelWidth/PanelAlpha/LastTab`. A 0.5.0 cfg loads as-is (harness test required).
- **Deliberate behavior-default changes (document in README Changes)**: `FreezeWhileOpen=false` (live by default), docked right at 460, `Telemetry.Enabled=true`.
- **No one-time migrations** (only widened AcceptableValueRanges). Follow the `BindRange`/`Wire`/`PushAllToRuntime`/`Save` quartet + `_syncing` guard + one-write-per-save; new string enums name-only parsed with Stock fallback; `Save()` covers all ten books; `WireAll` extended. Apocasetter picks the new sections up automatically.

## 10. Harness expectations (stub additions + tests + negative control)

**Stubs to extend** (mirror real signatures from gamecode/ — the compile breaks without them):
- `verify/stubs/GameStubs.cs`: `WheelUAPI.Camber {get;set;}` + `LateralSlip {get;}`; `WheelGroup` — `CasterAngle/ToeAngle` (settable), `applyCasterAngle/applyToeAngle/isSolid/trackWidth`, `ApplyGeometryValues()`, `Update()` with the solid-axle camber overwrite; `Powertrain.clutch` + `ClutchComponent` (controlType, engagementRPM, engagementRange, slipTorque, creepTorque, engagementCurve, clutchInput, Engagement); `TransmissionComponent` — `gears List<float>`, `forwardGearCount`, `Gear {get;set;}`, `GearName`, `ShiftInto(int,bool)`, `transmissionType`, `isShifting`, `shiftProgress`, `postShiftBan`, `variableShiftIntensity`; `PowertrainComponent.OutputRPM`; `Vehicle.ActiveVehicle`; `CamberController` (NWH.WheelController3D).
- `verify/stubs/UnityStubs.cs`: `GUIUtility.systemCopyBuffer {get;set;}`; the layout/telemetry additions the new widgets need.
- New blocker block: `InsaneSystems.InputManager.InputController` (the four statics), `InputStorage` (throwing getters), `FsmString`, and minimal `GetButton/GetAxis/GetAxisKeyAxis/GetAxisOrig/AnyKey/MouseLook` FsmStateAction subclasses with public `buttonName/axisName` fields.
- Blocker logic as pure static functions — tested without Harmony.

**Tests** (same `Check`/`Near`/`MakeCar` style; the steering prefix suite must NOT change — the patch stays as-is): alignment capture/apply/restore (sign mirroring, x=0 clamp, solid-axle + CamberController skip, gate restore, Z preservation); gearbox (resize hazards, CVT refusal, continuation, clutch clamps); whitelist routing (driving names pass; Mouse X / GetAxisOrig / AnyKey / unknown block); codec round-trips + garbage + clamp + unknown-skip; config defaults + 0.5.0-cfg load + range clamping; panel layout constants at 400/460/800; telemetry formatting + position parse; LastTab parse; two-click "all off" arming. **Negative control**: state which new checks fail (or fail to compile — the 0.5.0 convention) against the untouched 0.5.0 plugin; `run.sh` itself unchanged.

## 11. Acceptance criteria — "done" means

1. `bash verify/run.sh` green (stubs compile, all logic tests pass, prefix suite passes); negative control documented with counts in README "Changes in 0.6.0-alpha" → "Verification harness".
2. README §10 gains the new in-game items (numbered 21+): live driving with panel open (all four controls live, normal steering ramp, no pause-menu bleed, Esc clean, no unknown-action log warnings); freeze toggle reproduces 0.5.0 incl. pause-menu-0 restore; docked panel at widths 400/460/800 × scales 0.5/1/2 with all 10 tabs fully visible/clickable (both curve editors + gear graph); transparency slider; alignment visible geometry + exact restore + solid-axle/CamberController warnings; gearbox 6-gear add + 4-gear cut + Race clutch feel + CVT untouched + exact restore; telemetry live + click-through + hidden with panel; copy/paste round-trip; 0.5.0 cfg loads with all legacy keys intact; Apocasetter lists the new sections.
3. README "Changes in 0.6.0-alpha" (features, behavior changes, new keys, migration statement, allocation notes, "Not changed" §2-facts paragraph); `docs/strings.md` sweep regenerated (new preset classes are under `Settings/` so the documented grep already covers them; new UiStrings templates inventoried; a CurveEditor-style note for GearGraph/telemetry concatenated readouts); version bumps (`PluginInfo` "0.6.0", README title, §6b tag reference, release/README.txt if present).

## 12. Risks — use these answers (encode each in code/notes)

1. Clutch types don't exist in NWH2 → emulated presets + honest panel note.
2. Gear-resize IndexOutOfRange → mandatory re-shift guard; never touch reverse/neutral.
3. CVT 3-gear invariant → refuse count/ratio changes on CVT.
4. Solid axle / CamberController overwrite camber → detect, skip, warn; never disable game components.
5. Stale wheelbase/trackWidth after moves → warn; don't recompute.
6. x=0 crossing flips side conventions → clamp |x| ≥ 1 cm.
7. Pause-menu buttons behind the live panel can be clicked → accepted + documented; Freeze-ON keeps the modal dim.
8. Unknown driving action names → fail-closed whitelist + once-per-name logging; harvest in-game.
9. timeScale untouched in live mode → pause-menu-open stays paused; documented.
10. 10 tabs at 440 px → two-row strip; the full fixed-px inventory goes width-adaptive.
11. Per-wheel UI needs a vehicle → reference = first tracked record; disabled until one spawns.
12. Width relayout fragility → live `Relayout(width)`; fallback = rebuild in place.
13. Telemetry default ON deviates from opt-in → it is passive UI; documented.
