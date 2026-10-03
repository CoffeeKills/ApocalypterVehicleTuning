# Plan: ApocalypterSteeringMod v3 — In-Game Settings Panel, Steering Presets, Suspension Tuning

## Context

The user's BepInEx steering mod for "Apocalypter" (Unity 2020.3, NWH Vehicle Physics 2, BepInEx 5.4.23.5) currently patches `Steering.CalculateSteerAngles` with a traction-edge clamp whose tuning is hardcoded. The user wants: (1) all settings customizable in-game, (2) realistic suspension tuning, (3) steering presets (GTA-style keyboard, Truck-sim, Sim/Race, Drift, Vanilla, Custom). User decisions: **hotkey-only overlay panel** (not injected into the FSM-driven pause menu), **wheel support deferred** (design stays input-agnostic).

Key architecture facts (verified from decompiled source + game data):
- Game has no compiled menu code — pause menu is PlayMaker FSM + scene uGUI; nothing to patch. Panel = own overlay canvas.
- Game FSMs write `VehicleController.input.*` every frame via PlayMaker SetProperty reflection; NWH input providers unused. Mod must never write `vc.input.Steering` (overwritten next frame) — only post-process what it reads.
- Game's native `steeringspeed` setting lives in `SaveSettings.es3` (ES3, read-only import via `ES3.Load<T>(key, filePath, defaultValue)` — ES3.cs:352, verified read-only; game's `ES3SettingsMod` mutates shared static `ES3Settings.defaultSettings.path`, so always pass explicit absolute path, never `ES3.Save`).
- Suspension setters are abstract members of `WheelUAPI` (NWH.Common `WheelUAPI.cs:41-53`): `SpringMaxForce`, `SpringMaxLength`, `DamperBumpRate`, `DamperReboundRate` — live-settable per wheel, **no cast needed**. Per-axle ARB: `WheelGroup.antiRollBarForce` (public field). Digressive damper params: intentionally untouched.
- UI factory pattern: `CameraMovementRuntimeCreator.cs` (ScreenSpaceOverlay canvas, sliders/toggles/buttons, string-keyed dicts, cursor unlock). Fonts: TMP `"Fonts & Materials/LiberationSans SDF"` with legacy-Arial fallback. Legacy Input enabled (game's own InputManager uses it) → `Input.GetKeyDown(F7)` works. Avoid F11/F12/P/Return (taken).

## File structure (project `ApocalypterSteeringMod`, version → 3.0.0, assembly rename `SteeringFix` → `ApocalypterSteeringMod`; install note: delete old `BepInEx\plugins\SteeringFix.dll` to avoid double-patching)

```
Plugin.cs                              — slim entry: ModConfig.Load() → GameSettingsReader.Read() → PatchAll → persistent runtime GO (DontDestroyOnLoad) with SuspensionApplier + SettingsPanelManager
PluginInfo.cs                          — GUID (unchanged), name, version
Settings\SteeringPreset.cs             — data class + static factory (6 presets, static readonly AnimationCurves)
Settings\SteeringSettings.cs           — static runtime holder (Enabled, ActivePreset, MatchGameSteeringSpeed; setters mutating the Custom instance)
Settings\SuspensionPreset.cs           — data class + static factory (4 presets, absolute per-axle baselines)
Settings\SuspensionSettings.cs         — static runtime holder (Enabled, ActivePreset, 10 user multipliers)
Persistence\ModConfig.cs               — BepInEx ConfigFile binding, load/save, SettingChanged → static SettingsChanged event
Game\GameSettingsReader.cs             — read-only ES3 import of steeringspeed/smoothinput/normalizeinput (try/catch, absolute path)
Patching\TractionEdgeSteeringPatch.cs  — existing patch moved from Plugin.cs, parameterized (allocation-free)
Runtime\SuspensionApplier.cs           — MonoBehaviour: vehicle discovery, baseline cache, apply/restore
Runtime\SettingsPanelManager.cs        — F7 hotkey, panel lifecycle, cursor/EventSystem handling
Runtime\SettingsPanelBuilder.cs        — static uGUI factory (canvas, cycle buttons, sliders, toggles)
```

## Settings model

### Steering presets (steering patch knobs)
Fields: `IsVanilla`, `RateMultiplier` (× degreesPerSecondLimit), `CurveOverride`+`SpeedCurve` (evaluated at Speed/50, vanilla normalization), `SpeedCurveScale` (× vehicle curve when no override), `SmoothingScale` (× speedSensitiveSmoothingCurve), `TractionClampEnabled`, `SlipAngleDeg` (2–15, replaces 8.5 const), `OppositeLockBoost` (1.0–3.0), `LinearityOverride`+`LinearityExponent` (pow(|input|,exp)).

| Preset | Rate | Speed curve | Smooth | Clamp/Slip | OppLock | Linearity |
|---|---|---|---|---|---|---|
| GTA-style Keyboard | 1.6 | (0,1)(0.35,0.45)(1,0.15) | 0.8 | ON / 8.5° | 1.75 | pow 1.3 |
| Truck-sim | 0.7 | (0,1)(0.25,0.7)(1,0.25) | 1.4 | ON / 7.0° | 1.25 | pow 1.15 |
| Sim/Race | 1.0 | (0,1)(0.5,0.35)(1,0.22) | 1.0 | ON / 8.5° | 1.5 | vehicle curve |
| Drift | 1.4 | (0,1)(0.4,0.55)(1,0.3) | 0.6 | ON / 12.0° | 2.0 | pow 1.0 |
| Vanilla | — (IsVanilla=true → prefix returns true, stock NWH) | | | | | |
| **Custom (default)** | 1.0 | vehicle curve × 1.0 | 1.0 | ON / 8.5° | 1.75 | vehicle curve |

Custom defaults reproduce v2.0.0 exactly (no change for existing users). Steering master `Enabled = true` default.

`MatchGameSteeringSpeed` (default true): effective rate ×= `Clamp(gameSteeringspeed/50, 0.35, 2.5)` — respects the player's in-game steering-speed choice proportionally.

### Suspension presets (absolute per-axle baselines)
Fields per preset: Spring F/R (N/m), RideHeight F/R (m), Bump F/R, Rebound F/R (N·s/m), Arb F/R. Presets: **Comfort** 30k/27k, 0.34/0.33, 2.8k/2.5k, 4.2k/3.8k, 8k/6k; **Street (default)** 42k/38k, 0.30/0.29, 3.8k/3.4k, 5.7k/5.1k, 14k/10k; **Off-road** 26k/24k, 0.40/0.39, 2.2k/2.0k, 3.3k/3.0k, 5k/3.5k; **Race** 65k/58k, 0.26/0.25, 5.5k/4.9k, 8.2k/7.4k, 24k/18k.

User multipliers: 10 sliders (0.5–2.0, default 1.0), front/rear split for spring, ride height, bump, rebound, ARB.

**Apply formula: `effective = presetAbsolute[axle] × userSlider[axle]`** (presets set correct absolutes; sliders scale on top; vehicle originals cached only for restore-on-disable). Suspension master **default OFF** (opt-in, preserves stock feel on upgrade).

## Persistence
BepInEx ConfigFile (NOT ES3 — avoids corrupting the game's save file whose shared `ES3Settings.defaultSettings.path` the game mutates). Sections: `[Steering]`, `[Steering.Custom]`, `[Suspension]`, `[Suspension.User]`, `[UI]` (`ToggleKey="F7"`). ConfigEntries bound on load, pushed into runtime holders; `SettingChanged` → re-apply + static `SettingsChanged` event (panel + applier subscribe). Save on panel close / Plugin.Disable / Application.quitting.

Game settings import: `ES3.Load<T>(key, absolutePathToSaveSettings.es3, default)` in try/catch, at startup + panel "Reload" button. No `ES3.Save` anywhere in the mod.

## Steering patch rework
Keep patch permanently applied; prefix guard: `if (!SteeringSettings.Enabled) return true;` then `if (ActivePreset == null || IsVanilla) return true;` (a branch, not runtime patch/unpatch — fragile). Snapshot preset reference into a local once per call. Pipeline identical to current v2 code with parameterization: preset curve/scale → linearity → traction clamp (preset slip angle, only if enabled) → SmoothDamp (×SmoothingScale) → MoveTowards (`degreesPerSecondLimit × RateMultiplier × gameSteeringSpeedFactor × [OppositeLockBoost]`). Allocation-free: no LINQ/strings/closures/boxing in the prefix; static curves.

## Suspension applier
MonoBehaviour on the runtime GO. Every 2 s (Update, scaled) + on SettingsChanged + on panel open: `Object.FindObjectsOfType<VehicleController>()`; per vehicle cache originals (keyed by WheelUAPI instance: 4 values + `IsFront = InverseTransformPoint(wheelPos).z > 0`; per WheelGroup: original ARB). Prune Unity-null records. Apply idempotently to every known vehicle (self-heals external resets); when master OFF, restore originals once and skip. `OnDestroy` → RestoreAll. Digressive damper params untouched (comment explains why).

## Settings UI
F7 (config-editable, F8/F9 fallback) toggles panel; works while paused (Update, no timeScale changes). On open: cursor lock none + visible (restore prior on close); EventSystem find-or-create (`StandaloneInputModule`; remember+restore enabled state; never duplicate). Canvas: ScreenSpaceOverlay + CanvasScaler (1920×1080, match 0.5) + GraphicRaycaster; panel ~460×720 right-center, dark bg, ScrollRect content. Fonts: TMP LiberationSans SDF via Resources.Load (try/catch) → legacy Arial fallback.

Layout: title + hint → **STEERING**: master toggle, preset **cycle button** (`<  Custom  >` — no dropdown: TMP_Dropdown needs template assets that don't exist as loadable resources), Custom sliders (rate/smoothing/curve scale/traction toggle/slip/opp-lock/linearity toggle+exponent) or read-only summary for other presets, MatchGameSteeringSpeed toggle → **SUSPENSION**: master toggle, preset cycle button, 5 rows × (front slider, rear slider, value labels, per-row reset): spring/ride height/bump/rebound/ARB → **GAME SETTINGS**: read-only steeringspeed/smoothinput/normalizeinput + Reload button. Slider onValueChanged → write runtime holder immediately (live apply) + refresh label; suspension changes → `SuspensionApplier.ReapplyAll()`. Config hot-reload refreshes widgets when panel open.

## Build changes (csproj)
Add references (all from game's Managed, `Private=false`): `UnityEngine.UIModule.dll`, `UnityEngine.UI.dll`, `UnityEngine.InputLegacyModule.dll`, `UnityEngine.TextRenderingModule.dll`, `Unity.TextMeshPro.dll`, `Assembly-CSharp-firstpass.dll`. Not needed: Unity.InputSystem. Rename `<AssemblyName>` → `ApocalypterSteeringMod`.

## Verification
Without the game: `dotnet build -c Release` zero errors; grep prefix/applier paths for allocations (LINQ/ToString/closures); confirm no `ES3.Save` anywhere; confirm patch target unchanged.
In-game checklist (user): BepInEx log shows plugin + game-settings import; F7 opens/closes panel with cursor freed/restored; panel works while paused; cycle all 6 steering presets while driving — **Vanilla must feel exactly like unmodded game** (key regression test), Custom ≈ old v2.0.0; traction clamp + opposite lock catch behavior; suspension presets visibly change ride (Street stiff → Off-road soft/tall → Race stiff/low), sliders scale proportionally, master OFF restores stock bounce; switch vehicles → applied within 2 s, no errors on despawn; game's own steering-speed change + Reload shows new value and rate follows when MatchGameSteeringSpeed on; restart → settings persist in cfg; edit cfg while playing → hot-applies; LogOutput.log clean.

## Risks
1. Never write `vc.input.*` (FSM overwrites next frame) — input post-processing only.
2. ES3: explicit absolute paths only, read-only, try/catch (file may be mid-write).
3. Prefix must stay allocation-free (GC in FixedUpdate).
4. Vanilla = branch guard, not unpatch.
5. EventSystem find-or-create, restore state, never duplicate.
6. TMP load fallback to Arial so panel always renders.
7. F7 collision possible with scene-serialized FSMs (unverifiable) — config-editable fallback.
8. Panel open while driving: game input keeps driving — document "open while stopped/paused".
9. Suspension overrides per-vehicle tuning globally — opt-in default OFF.
10. Old DLL must be deleted (assembly renamed) or the prefix double-runs.
