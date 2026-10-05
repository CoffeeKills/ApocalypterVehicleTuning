# ApocalypterDrivetrain (v0.1.0-alpha — skeleton)

BepInEx mod for Apocalypter: replaces NWH's drivetrain from the inside (SPEC.md) and
gives mods a config-driven drivetrain surface — engine, clutch, gearbox, differentials,
axles — so custom vehicles with "wicked drivetrains" stop being a fight with NWH's
assumptions. This first version is a **skeleton**: it loads, parses configs into the
drivetrain model, and prepares the A/B toggle and the M1/M2 patch points. It does not
replace NWH behavior yet.

## Changes in 0.1.0-alpha

First release. **No in-game behaviour change**: NWH drives every vehicle exactly as before,
whether the mod is OFF or ON.

**Built**
- **Bootstrap** (`plugin/Plugin.cs`, `Runtime/`): GUID `dev.apocalypter.drivetrain` (locked),
  version `0.1.0`. All state lives in a static `ModHost`; per-frame work (the hotkey) runs on
  a `HideAndDontSave` runner recreated on every `sceneLoaded` (fact 3). One status line at
  startup.
- **Drivetrain model** (`plugin/Model/`, pure C#, compiled on its own by `verify/run.sh` with no
  Unity/NWH/BepInEx references): engine (torque curve from points or an expression, inertia,
  idle/redline/limiter, limiter cut time), clutch (capacity, engagement rpm/range/curve, optional
  launch rpm), gearbox (any forward/reverse ratio set, final drive, named shift logic, shift
  rpms), differentials (open / locked / limitedSlip / torqueSplit, 2 outputs each), axles (any
  count, steered/lift flags, wheel placement as `SPO.Vehicle.Vec3`). One JSON in, one validated
  `Drivetrain` out. `GearboxDef.ToNwhGearArray()` produces NWH's `[R…, 0, F…]` layout (fact 6).
- **Validation**: 36 error codes, each with a negative control. Unknown keys, duplicate keys,
  wrong types, non-finite numbers and out-of-range values are errors with line, column and
  field path. Wiring: every name resolves, every node is fed at most once, **cycles are
  rejected** (including cycles the gearbox never reaches — fact 7). Warnings (never blocking):
  differential not reachable from the gearbox; forward ratios not descending; an automatic
  upshift that lands below `downshiftRpm` (fixed-RPM shifting would hunt — fact 8).
- **Shift-logic hook**: `IShiftLogic` + `ShiftLogicRegistry`. Built-ins `manual` (census B1
  request semantics) and `automatic` (**stub until M1**: honours driver requests, otherwise
  holds the gear). Other mods register names in code; an unregistered name is a load error.
- **A/B controller**: OFF = no config read, no reflection on NWH, no Harmony instance, no
  write. ON = read + validate the JSON, resolve the patch points, log
  `skeleton: NWH active, config loaded, 4 replacement points staged`.
- **Patch points** (`Patching/PatchPoints.cs`), declared and inert: `M1.transmission`
  (`TransmissionComponent.ForwardStep`), `M2.engine` (`EngineComponent.IntegrateDownwards`),
  `M2.clutch` (`ClutchComponent.ForwardStep`), `M2.differential`
  (`DifferentialComponent.ForwardStep`). Each runtime flag defaults to false and is not in
  the config. If one were set, its prefix just returns true, so the original still runs.

**Design decisions**
- No silent defaults: every field is required except `axles[].steered`/`lift` (default
  `false`) and `clutch.launch` (absent = no launch profile). A field that does not apply
  (`split` on a locked diff, `upshiftRpm` on `manual`) is an error, not ignored.
- Expressions are sampled into a piecewise-linear table at load. The tick path never
  interprets anything, and `Evaluate`/`SelectGear`/`RatioOf` allocate nothing (tested).
  Integer exponents only, and no transcendental functions (SPO.Vehicle's no-libm rule).
- The transmission point hooks the **step**, not the shift delegate, because NWH re-assigns
  the delegate on every `transmissionType` change (plan.md).
- **Evidence gap**: the census resolves only `DifferentialComponent.ForwardStep` against the
  real DLL. The other three targets come from `gamecode/` only and are labelled
  `GamecodeOnly`. Add them to the census before M1/M2 turns their flags on. At runtime an
  unresolvable target is logged and skipped, never applied.
- `SPO.Vehicle` is used for `Vec3` wheel placement only. `IVehicleBody`/`IWheelQuery` are not
  consumed until forces are applied (M2/M3); no placeholder code calls them.

**Config keys** (`BepInEx/config/dev.apocalypter.drivetrain.cfg`, pinned by a test)

| key | default | meaning |
|---|---|---|
| `[Drivetrain] Enabled` | `false` | A/B master switch (the hotkey flips it) |
| `[Drivetrain] ToggleKey` | `F9` | Unity `KeyCode` name; an invalid name disables the hotkey (logged) |
| `[Drivetrain] ConfigPath` | *(empty)* | JSON file; empty = built-in example; relative = under `BepInEx/config/`; missing file = error, no fallback |

**Harness** (`verify/`): the steering mod's `Tests.cs`, `tests/prefix/` and the v0.6.4 fixture
were removed, because they test steering-mod sources that are not in this bundle. Stubs: all 18
census "stub drift" items removed (including the steering transmission emulation). Moved:
`PlayMakerFSM` to the global namespace and `ManagerVehicleComponent` to `NWH.VehiclePhysics2`.
Added the real signatures the patch points target: `TransmissionComponent.ForwardStep`
(verbatim), `ClutchComponent.ForwardStep`, `EngineComponent.IntegrateDownwards`.

## 1. What the mod does (eventually)

M1 transmission/shift logic → M2 engine/clutch/differentials + the mod API → M3
tire/suspension → M4 assists/trailer/crush → M5 mod migration. Full scope: SPEC.md.

## 2. Load-bearing game facts (verified against decompiled source + game data; carried
from ApocalypterSteeringMod/README.md §2, re-verified for drivetrain relevance
2026-10-06)

1. **The game has no compiled game code.** `Assembly-CSharp` is 99% asset-store code
   (PlayMaker actions, InsaneSystems.InputManager, NWH, …). All game logic and UI are
   **PlayMaker FSMs serialized in scenes** inside `data.unity3d`.
2. **Vehicle input is written by FSMs** via PlayMaker `SetProperty` reflection every
   frame (`VehicleController.input.*`). **The mod must never write `vc.input.*`** — it is
   overwritten next frame. Assists act through `engine.powerModifiers` /
   `brakes.brakeTorqueModifiers` instead.
3. **The game destroys plugin-created GameObjects on scene load.** A PlayMaker scene-
   cleanup sweep disables/destroys unknown scene-root objects. Workaround (proven): host
   all runtime logic on a GameObject with `hideFlags = HideFlags.HideAndDontSave`,
   recreated on `SceneManager.sceneLoaded`. Harmony patches survive (they live in IL).
   **Preserve this architecture.**
4. **Game settings use Easy Save 3** (`SaveSettings.es3`). Reads must pass an explicit
   absolute path; **never call `ES3.Save`**.
5. **NWH live-settability** (verified): engine/transmission/differential fields public
   and read every tick; `transmission.UpshiftRPM/DownshiftRPM` settable properties;
   `DifferentialComponent.DifferentialType` setter re-assigns the split delegate live;
   `WheelGroup.brakeCoefficient/handbrakeCoefficient/antiRollBarForce` public fields.
   **Hazards**: `vc.DiffFrontType`/`DiffRearType` getters index `differentials[0/1]`
   unguarded; `GroundDetection` overwrites `FrictionPreset` + rolling resistance every
   ~0.1 s (it does NOT overwrite grip multipliers); a `TyreWear` component, if present,
   rewrites grip properties every frame.
6. **Gearbox**: `gears = [reverse…, 0, forward…]`, counts recomputed every `ForwardStep`;
   `CalculateTotalGearRatio` indexes `gears[gearIndex]` **unguarded**; `ShiftInto` refuses
   during the post-shift ban / an in-flight shift / full damage (instant does not bypass
   the ban) — so write `Gear` directly and defer shrinks while `isShifting`; CVT needs
   exactly 3 gears; `transmissionType` is live-safe.
7. **Powertrain wiring**: NWH steps the powertrain by recursing `_output`/`_outputB`
   object references every tick (`PowertrainComponent.cs:156-189`, `DifferentialComponent.cs:192-234`);
   the public `Output`/`OutputB` setters relink live (and clear the old target's
   `_input`); a new `DifferentialComponent` has no split delegate until `DifferentialType`
   is assigned; `WheelComponent.ForwardStep` sets `AutoSimulate = false` — an undriven
   wheel must have it back on to be simulated at all. **A cycle in the wiring recurses
   until the game crashes** — the mod's model rejects cycles at load.
8. **Shifting**: the game's FSMs never write `transmission.Gear`; they write shift
   *requests* by PlayMaker `SetProperty` on `input.ShiftInto` (R=-1/N=0/1..5),
   `input.ShiftUp`, `input.ShiftDown`. NWH applies them in
   `TransmissionComponent.ForwardStep` through `shiftDelegate(vc)` (Manual/Automatic/CVT),
   then `input.ResetShiftFlags()` (`:403-420`). A `transmissionType` change re-assigns
   NWH's own delegate on the next tick (`:405`), and the game's `CheckTag` FSM writes
   `transmissionType`/`UpshiftRPM`/`DownshiftRPM`/`finalGearRatio`. NWH's raw automatic
   shifts at fixed RPMs without checking the next gear's landing RPM (it hunts on wide
   ratio steps).
9. **The game's NWH is developer-modified** (census, 2026-10-05): engine-part FSMs
   (16 call sites) call `VehicleController.SetGearArrayByValue(Single[])` — not a stock
   NWH API — and the game has `NWH.VehiclePhysics2.ManagerVehicleComponent` (no
   `.Modules` segment). The game's PlayMaker fork reports assembly version **1.6.0.0**
   and puts `PlayMakerFSM` in the **global namespace** (stock:
   `HutongGames.PlayMaker.`).
10. **NWH's floating origin is active while driving**: the Drive FSM adds
    `NWH.Common.FloatingOrigin.FloatingOrigin` on vehicle enter and destroys it on exit,
    and sets `distanceThreshold`. While present, the whole world — including PhysX
    rigidbodies — jumps when the player gets far from the origin. Any mod state that
    must survive a shift has to be origin-relative.

## 3. Build and test

```
cd plugin && dotnet build -c Release -p:GameDir="<Apocalypter install dir>"   # real DLL (net472)
cd verify && bash run.sh                      # stubs + pure-model + plugin compile, then tests (.NET SDK 8+)
```

Install: copy the DLL to `BepInEx\plugins\`. Config appears on first run as
`BepInEx/config/dev.apocalypter.drivetrain.cfg`.

## 4. Drivetrain config (JSON)

Strict JSON: no comments, no trailing commas, and no duplicate or unknown keys. Units are SI
(rpm, N·m, kg·m², m, s). Errors are logged as
`error <Code> (line L, col C) <field.path>: <reason>`, all of them in one pass.

| field | type / range | notes |
|---|---|---|
| `schema` | integer, must be `1` | future versions migrate |
| `name` | non-empty string | |
| `engine.idleRpm` / `redlineRpm` / `revLimitRpm` | (0, 30000] | idle < redline ≤ revLimit |
| `engine.revLimiterCutTime` | (0, 2] s | fuel-cut duration when the limiter trips |
| `engine.inertia` | (0, 100] kg·m² | |
| `engine.torqueCurve.points` | `[[rpm, N·m], …]` | ≥ 2 points, rpm strictly increasing, torque ≥ 0, must span idleRpm…revLimitRpm (no extrapolation) |
| `engine.torqueCurve.expression` + `samples` | string + integer 2…512 | instead of `points`; sampled over idleRpm…revLimitRpm. Grammar: `+ - * /`, `^` with integer 0…8, `( )`, `rpm`, `min(a,b) max(a,b) clamp(x,lo,hi) abs(x) lerp(a,b,t)`. Must be finite and ≥ 0 everywhere |
| `clutch.capacity` | (0, 100000] N·m | torque transmissible when fully engaged |
| `clutch.engagementRpm` | (0, 30000] | ≥ engine.idleRpm |
| `clutch.engagementRange` | (0, 10000] rpm | |
| `clutch.engagementCurve` | `[[input, engagement], …]` in [0,1] | starts at input 0, ends at input 1, engagement never decreases |
| `clutch.launch.rpm` *(optional section)* | engagementRpm … redlineRpm | absent = no launch profile |
| `gearbox.forward` | 1…32 ratios > 0 | gear 1 first; non-descending order is a warning |
| `gearbox.reverse` | 0…8 ratios < 0 | R1 first (NWH sign convention) |
| `gearbox.finalDrive` | (0, 100] | |
| `gearbox.shiftTime` | [0, 5] s | |
| `gearbox.shiftLogic` | `manual`, `automatic`, or a name a mod registered | |
| `gearbox.upshiftRpm` / `downshiftRpm` | (0, 30000] | required for `automatic`, rejected for `manual`, both-or-neither for custom; idle < down < up ≤ revLimit |
| `gearbox.output` | node name | a differential or a wheel |
| `differentials[]` | `{name, type, …, outputs: [A, B]}` | exactly 2 distinct outputs (node names) |
| — `type: open` | no parameters | equal split |
| — `type: torqueSplit` | `split` (0, 1) | share of torque to output A (NWH `biasAB = 1 - split`) |
| — `type: locked` | `stiffness` (0, 1] | |
| — `type: limitedSlip` | `stiffness` (0,1], `slipTorque` (0,100000], `powerRamp` / `coastRamp` [0,1] | |
| `axles[]` | `{name, z, track, steered?, lift?}` | 1…16 axles; `z` [-50, 50] m (forward +), distinct per axle; `track` (0, 10] m; `steered`/`lift` default `false` |

**Names** (differentials and axles) are 1–32 characters from letters, digits, `_` and `-`. The
name `gearbox` is reserved. Each axle provides two wheel nodes, `<axle>.L` and `<axle>.R`, at
`(∓track/2, 0, z)` in body coordinates. **Wiring**: `gearbox.output` plus each differential's
`outputs` form the graph. Every name must exist, every node takes at most one input, and
cycles are rejected. A wheel is *driven* when the gearbox reaches it. A differential the
gearbox does not reach is a warning.

**Code hook**: `ShiftLogicRegistry.Register("my-logic", gearbox => new MyLogic())` before the
config loads; `IShiftLogic.SelectGear(ref ShiftContext)` must return a gear in
`[MinGear, MaxGear]`. Nothing calls it in 0.1.0 (M1 does).

The built-in example (used when `ConfigPath` is empty):

```json
{
  "schema": 1,
  "name": "Example 4x4 pickup",
  "engine": {
    "idleRpm": 800,
    "redlineRpm": 5600,
    "revLimitRpm": 6000,
    "revLimiterCutTime": 0.12,
    "inertia": 0.25,
    "torqueCurve": {
      "points": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]
    }
  },
  "clutch": {
    "capacity": 650,
    "engagementRpm": 1100,
    "engagementRange": 400,
    "engagementCurve": [[0, 0], [0.4, 0.15], [1, 1]],
    "launch": { "rpm": 2500 }
  },
  "gearbox": {
    "forward": [4.2, 2.5, 1.6, 1.15, 0.85],
    "reverse": [-3.8],
    "finalDrive": 3.9,
    "shiftLogic": "automatic",
    "upshiftRpm": 4800,
    "downshiftRpm": 2000,
    "shiftTime": 0.25,
    "output": "centre"
  },
  "differentials": [
    { "name": "centre", "type": "torqueSplit", "split": 0.4, "outputs": ["front", "rear"] },
    { "name": "front", "type": "open", "outputs": ["front.L", "front.R"] },
    { "name": "rear", "type": "limitedSlip", "stiffness": 0.6, "slipTorque": 900, "powerRamp": 0.8, "coastRamp": 0.4, "outputs": ["rear.L", "rear.R"] }
  ],
  "axles": [
    { "name": "front", "z": 1.45, "track": 1.62, "steered": true },
    { "name": "rear", "z": -1.55, "track": 1.64 }
  ]
}
```
