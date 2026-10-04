# Plan: ApocalypterSteeringMod v3.2 — Suspension Slider Rework + Vehicle Tuning Expansion

## Context

v3.1.0 (reviewed, tested, installed) works. The user wants v3.2:
1. **Suspension sliders like steering**: sliders show the active preset's factor values; moving one copies preset → Custom ("Custom (Race)") with BasedOn tracking; per-slider Reset returns to origin. The separate user-multiplier layer is removed (preset factor IS the slider value).
2. **Absolute readouts** next to factors (e.g. "×1.18 / 38 000 N") computed from tracked vehicles' stock values (user chose multipliers + readout).
3. **New categories, presets everywhere**: Aero, Brakes, Grip, Drivetrain, Assists (ABS + TCS) — each with master switch + presets + sliders (user chose full scope + presets).
4. **Steering.Enabled default = false** (opt-in; existing cfg files keep their saved values — BepInEx file-value-wins).

All API claims verified against the decompiled source: `ManagerVehicleComponent.AddAndOnboardNewComponent` (ManagerVehicleComponent.cs:30), AeroModule fields (dimensions/frontalCd/sideCd/simulateDrag/simulateDownforce/maxDownforceSpeed/downforcePoints), `Brakes.brakeTorqueModifiers` (List<BrakeTorqueModifier> delegate), `EngineComponent.powerModifiers` (List<PowerModifier>), `TransmissionComponent.UpshiftRPM/DownshiftRPM` properties, `WheelUAPI.LongitudinalFrictionGrip/LateralFrictionGrip` abstract, `DiffFrontType` indexes `differentials[0]` unguarded (VehicleController.cs:158-166) — **tuner must use `powertrain.differentials[i]` with Count guards, never the convenience properties**.

## Architecture (three structural changes)

### 1. `Settings/PresetBook.cs` (new) — generic preset semantics for all 7 categories
```csharp
public interface ITunablePreset { string Name; string Label; string BasedOn; bool CanEdit; void CopyValuesFrom(ITunablePreset src); }
public sealed class PresetBook<T> where T : class, ITunablePreset {
    T[] Presets; T Identity; T Custom; T Defaults; T NotFound; T Active;
    Func<string,string> legacyName (optional, e.g. Street→Stock);
    void SetByName(string); void Select(T); T BeginEdit(); T Reference(); T FindBuiltIn(string); void ResetCustom();
}
```
- `BeginEdit()`: Custom active → Custom; Identity with `CanEdit == false` (steering Vanilla only) → null; else copy active into Custom (BasedOn = active.Name, "" when copying Identity), return Custom. Uniform across all categories.
- `Reference()`: built-in → itself; Custom → FindBuiltIn(BasedOn) ?? Defaults.
- `SteeringPreset`/`SteeringSettings` keep their public surface but delegate to a book (`ActivePreset` property over `Book.Active`; ResetAll sets Enabled=false). Patch untouched.
- `SuspensionPreset` gains Custom (all 1.0) + Defaults + BasedOn; 5 existing factor tables unchanged; Presets = {Stock, Comfort, Sport, Off-road, Race, Custom}. `SuspensionSettings`: delete the 10 user multipliers; `Spring(front)` etc. return the active preset factor directly; `LinkRearToFront()` must `BeginEdit()` first; legacy "Street"→"Stock" mapping kept.

### 2. New per-category preset/settings classes (each `XPreset.cs` + `XSettings.cs`, all factors on captured stock)
- **AeroPreset**: DownforceScale (0–2), DragScale (0–2), MaxDownforceSpeedScale (0.5–2). Presets: Stock, Street (0.8/1.0/1.0), Sport (1.2/1.1/1.1), Off-road (0.6/1.05/0.85), Race (1.6/1.35/1.25), Custom.
- **BrakesPreset**: TorqueScale (0.5–2), FrontBrakeScale/RearBrakeScale (0–2), HandbrakeScale (0–2), ActuationScale (0.5–2). Presets: Stock, Sport (1.1/1.1/1.05/1.0/0.9), Race (1.3/1.15/1.1/0.8/0.75), Off-road (1.05/0.9/0.9/1.2/1.1), Drift (1.0/0.95/0.95/1.6/1.0), Custom.
- **GripPreset**: LongitudinalScale/LateralScale/StiffnessScale (0.25–2). Presets: Stock, Sport (1.05/1.1/1.0), Race (1.1/1.25/1.1), Off-road (0.8/0.85/0.8), Drift (0.95/0.55/0.8), Custom.
- **DrivetrainPreset**: PowerScale (0.5–2.5), RevLimiterScale (0.8–1.2), LossScale, BoostScale, FinalDriveScale (0.7–1.5), UpshiftScale/DownshiftScale (0.8–1.2), ShiftDurationScale, DiffFrontMode/DiffRearMode (enum DiffMode {Stock, Open, Locked, LimitedSlip}), DiffStiffnessScale, DiffBiasScale. Presets: Stock, Street, Off-road (Locked front/LSD rear), Sport, Race (both Locked), Drift (rear Locked, front Open), Custom.
- **AssistsPreset** (delegate-only, no vehicle fields): AbsEnabled/AbsSlipThreshold (0.02–0.5, 0.1)/AbsCutoffSpeed (0–5, 1)/AbsCutMultiplier (0–1, 0.01); Tcs* same (defaults 0.1/2/0.01). Presets: Off (identity), Standard (both on, NWH defaults), Sport (0.12/0.08), Off-road (ABS only), Race (ABS only), Custom.
- `Limits.cs`: add the factor/scale ranges above (single source for config AcceptableValueRange + sliders).

### 3. `Runtime/VehicleTuner.cs` replaces `SuspensionApplier.cs`
Single vehicle registry + one capture pass (mean-Z axle detection, now also reading grip/brake/drivetrain/aero baselines, each system null-guarded independently); per-system `ApplyX/RestoreX` driven by per-category `Enabled` with applied-flags; allocation-free hot paths; 2 s unscaled scan + `ReapplyNow` + cheap `ApplyLive`; `OnDestroy → RestoreAll`; records kept until vehicle destroyed. Files: `VehicleTuner.cs` (core), `VehicleTuner.Systems.cs`, `VehicleTuner.Assists.cs`.
- **Aero**: use the real `AerodynamicsModule` — find in `vc.moduleManager.Components`; absent → onboard via `AddAndOnboardNewComponent(new AerodynamicsModule())` + `VC_Enable(false)` (module stays in Components for reuse; `FillComponentList` only re-scans when list null — no duplicates). Deep-copy fields+points as baseline; apply Cd/points scaled; **no downforce-point synthesis** (vehicles without points get drag tuning only — readout shows "—"); restore fields + `VC_Disable` if onboarded.
- **Brakes**: `maxTorque ×`, per-group `brakeCoefficient` (clamp 0..1) / `handbrakeCoefficient` (clamp 0..2) ×, `actuationTime ×`.
- **Grip**: per-wheel `LongitudinalFrictionGrip/LateralFrictionGrip` (+Stiffness pair) ×; GroundDetection overwrites FrictionPreset/rolling resistance only (not these — verified). If `TyreWear` MonoBehaviour present on a wheel (never referenced by game code, but possible on prefabs), dim grip tab with note (cheap detection at capture).
- **Drivetrain**: apply `revLimiterRPM` first, then shift RPMs; `engineLossPercent` clamp 0..1; `powerGainMultiplier` clamp 1..3 (inert on EVs — hint note); diffs via `powertrain.differentials[i]` with Count guard (NEVER `vc.DiffFrontType` — unguarded indexer, verified).
- **Assists**: register once per vehicle (flag-guarded, same delegate instance reused) into `vc.brakes.brakeTorqueModifiers` / `vc.powertrain.engine.powerModifiers`; delegate bodies read live preset fields each tick (allocation-free, mirror NWH's ABSModule/TCSModule slip-check semantics with cutoff speed + cut multiplier); removal on disable via `list.Remove(instance)`; no removal needed on vehicle destruction (whole cycle unreachable when record dropped).
- **Readout API**: `MeanBaseline(Readout kind, bool front)` (mean of tracked vehicles' axle stock values; 0 when none) + `TrackedVehicles` — panel shows "×1.18" / "38 000 N".

## Config schema + migration (ModConfig.cs)

- New sections: `Aero`, `Brakes`, `Grip`, `Drivetrain`, `Assists` (each `Enabled=false` + `Preset="Stock"/"Off"` + `Custom.*` with BasedOn + factor keys, ranges from Limits); `Suspension.Custom.*` (10 factor keys 0.5–2, BasedOn); `Steering.Enabled` default **false**; `UI.ToggleKey` unchanged. Keep bind/wire/push/save trio pattern, `_syncing` guard, one write per save.
- **v3.1 migration (one-time)**: after PushAllToRuntime — (1) `Suspension.Preset="Street"`→"Stock" (kept); (2) if any of the 10 bound legacy `Suspension.User` values ≠ 1.0: fold `Custom_i = Clamp(presetFactor_i × user_i, 0.5, 2)`, `BasedOn` = old preset name if built-in non-Stock else "", ActivePreset = Custom, write into Suspension.Custom entries; (3) always `config.Remove("Suspension.User", key)` ×10 → fold is one-time by construction (next load binds defaults 1.0 → no-op). Tests cover no-double-fold, all-1.0 skip, Stock+user fold.
- `Save()`/`PushAllToRuntime()` per-category private helpers; steering keeps `RestoreBaseCurve()`.

## UI (SettingsPanel.cs, 7 tabs in 800×880)

- Tabs: Steering, Suspension, Aero, Brakes, Grip, Drivetrain, Assists — arrays sized 7, buttons at i/7..(i+1)/7, font 16. Footer reset: `Action[]` + `string[]` per tab (two-click arm pattern kept); each reset also `_tuner.ReapplyNow()` where vehicle fields are written.
- `SettingsPanel.Create(VehicleTuner tuner, Action requestClose)`; `_applier`→`_tuner`.
- Shared `PresetButtonLabel<T>` ("Custom (Race)" when BasedOn set). `AddSlider` gains optional readout formatter: value box 86→150 px, second Text line 12 px muted (factor top, absolute bottom). Formats: "×1.18", "38 000 N", "3 400 N·s/m", "30 cm".
- Suspension tab rewritten: sliders get/set via `SuspensionSettings.Shown.<Field>` / `BeginEdit()` + `ApplyLive()`, reference = `Reference().<Field>`, readout = `_tuner.MeanBaseline(kind, front) × factor`. BeginEdit never null for suspension → sliders work on Stock too (copies with BasedOn=""). SplitFrontRear kept.
- New tabs follow the same recipe (master → presets → description → sliders → status note). Drivetrain adds two 4-button diff-mode grids (Stock/Open/Locked/LSD). Assists: ABS + TCS sections with switches + threshold/cutoff/cut-multiplier sliders.
- Single `_refreshers` list runs all tabs (accepted; per-tab lists are the documented fallback if it ever matters).

## Verification

- **verify/ stubs**: add Brakes/brakeTorqueModifiers, EngineComponent.powerModifiers, TransmissionComponent (UpshiftRPM/DownshiftRPM setters), DifferentialComponent.Type, Powertrain.engine/transmission/differentials/wheels, WheelGroup.brakeCoefficient/handbrakeCoefficient, WheelUAPI grip props + IsGrounded + LongitudinalSlip, ModuleManager.AddAndOnboardNewComponent + Components, AerodynamicsModule/DownforcePoint.
- **Tests**: update steering-default expectation (Enabled==false); rewrite suspension applier tests for preset-factor semantics (BeginEdit on Stock, Reference, idempotence, restore); new migration tests (fold math, no-double-fold after Save+reload, all-1.0 skip, Street mapping); preset-book tests; per-system apply tests (brakes clamps, grip, drivetrain diff mode + 0-differential survival, aero onboard/reuse/disable, assists delegate register-once/call-with-fake-wheel/remove/re-register); config clamp tests for new keys.
- **run.sh fix**: version-agnostic SDK glob (`sdk/*/ | sort -V | tail -1`), keep NETStandard ref pack glob with NETCore.App.Ref fallback; runtimeconfig rollForward LatestMajor already handles SDK 10.
- In-game smoke test: each tab applies + restores; vehicle switch/spawn/despawn; config round-trip; aero onboarding log clean; assists react to induced slip; panel open/close behavior unchanged.

## Risks (documented mitigations)
TyreWear grip fight (detect + dim/note); GroundDetection overwrites only FrictionPreset/rolling resistance (grip props safe — verified); aero onboarding logs a benign "State definition not found" (`VC_LoadStateFromStateSettings`); power sliders trivialize game (accepted); RES2 engine sound reads revLimiterRPM at Start only (cosmetic — hint note); EVs ignore boost (hint note); game's own ABS/TCS modules multiply harmlessly with ours; steering default flip only affects fresh cfg files.

## Critical files
- `Settings/PresetBook.cs` (new), `Settings/Limits.cs`, `Settings/*Preset.cs`, `Settings/*Settings.cs`
- `Runtime/VehicleTuner.cs` + `.Systems.cs` + `.Assists.cs` (replace `SuspensionApplier.cs`)
- `Persistence/ModConfig.cs`, `Runtime/SettingsPanel.cs`, `Runtime/SettingsPanelManager.cs`, `Plugin.cs`, `PluginInfo.cs` (3.2.0)
- `verify/stubs/*.cs`, `verify/tests/Tests.cs`, `verify/run.sh`
- csproj: no reference changes (all new types in already-referenced NWH DLLs)

---

# 0.6.0-alpha (FEATURES.md implementation + audit of 0.5.0)

Implemented FEATURES.md on top of 0.5.0; see README "Changes in 0.6.0-alpha" for the full list,
the four spec recipes that were changed because they would crash or break the game (gear
re-shift via `ShiftInto`, class-blocking the name-routed input forks, blind `OnEnter` skipping,
per-wheel caster/toe without storage), and the audit fixes (dead-vehicle writes, latched-handbrake
ABS, TCS mid-shift, external config edits reverting panel edits, live-mode Submit re-fire).
New files: Settings/{AlignmentPreset,AlignmentSettings,GearboxPreset,GearboxSettings,UiSettings,PresetCodec}.cs,
Runtime/{VehicleTuner.Alignment,VehicleTuner.Gearbox,PanelLayout,GearGraph,TelemetryStrip}.cs.
