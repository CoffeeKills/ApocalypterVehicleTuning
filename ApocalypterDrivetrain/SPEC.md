# SPEC — Salvage Physics Overhaul — vehicle subsystem (v0.7)

SPO is a **vehicle physics subsystem overhaul** for modded Apocalypter. Its purpose:
make it easy for mods to implement custom vehicles with custom ("wicked") drivetrains,
on a core that replaces NWH's physics from the inside.

## 1. Why

1. **For Apocalypter**: the game's driving logic (PlayMaker FSMs) binds to NWH's public
   API by reflection, and NWH's edges keep biting (silent shift-delegate reassignment,
   gear-skipping automatics, unguarded indexers, no clutch model). Every fix so far is a
   patch around those edges. Owning the physics removes the category of problem entirely.
2. **For mods**: a config-driven drivetrain surface — engine, clutch, gearbox,
   differentials, axles — where a mod defines a custom drivetrain (torque curves, ratios,
   layouts, multi-axle rigs) in data plus optional code hooks, and the game runs it.
   Custom vehicles stop being a fight with NWH's assumptions.

## 2. Architecture

- **Host abstraction.** `SPO.Vehicle` is pure C# (netstandard2.0), no physics-engine
  dependency, behind `IVehicleBody` / `IWheelQuery`. The in-game host is Unity/PhysX:
  the vehicle body stays a PhysX `Rigidbody`; SPO computes wheel/tire/powertrain forces
  and applies them, exactly as NWH does today.
- **Inside-out, not DLL swap.** Harmony patches replace NWH's physics from the inside;
  NWH's types stay, so every FSM-bound name stays valid for free; each replaced
  subsystem is live-toggleable against NWH for A/B.
- **The mod surface.** Drivetrains are defined in JSON config (torque curves, gear
  ratios and behavior, differential layout, axle wiring, multi-axle support) and
  extended with code hooks (custom engine/clutch/gearbox/differential components,
  layout events). The census's behavioural contract (B1–B11) defines what mods may
  rely on.

## 3. Milestones

| M | Deliverable | Acceptance |
|---|---|---|
| M0 | Census + baselines | census green vs real DLLs (contract 9/9 — **done 2026-10-05**); reconciliation: 2 rows explained-not-balanced, acceptance list open (RECONCILIATION.md); feel/performance baselines + horrid-states catalog (**baselines pending user**) |
| M1 | Transmission + shift logic (native) | No-hunting grid; launch-from-gear tests; gear-skip + delegate bugs as regression tests; "gear index always valid" property test; in-game A/B |
| M2 | Drivetrain core + mod API: engine, clutch, differentials (live relinking), custom layouts | Torque-curve match vs dump; clutch traces; layout rewiring; a mod-defined custom drivetrain drives in-game with zero NWH patching; maneuver proxies in band |
| M3 | Tire + suspension (substepped) | Skidpad/step-steer/braking in band; keyboard checklist; perf parity |
| M4 | Assists, trailer stabilizer, crush feel | Trailer wobble test; horrid-states property suite green |
| M5 | Mod migration, docs, release | Mods' harness green; feature-mapping table done |

## 4. The drivetrain surface (what "wicked drivetrains" means, concretely)

A mod can express, without touching NWH internals:

- **Engine**: arbitrary torque curve (data points or code), inertia, rev limits,
  multi-source setups (e.g. diesel + electric assist) via component hooks.
- **Clutch**: engagement model, launch behavior (the "launch from gear" trick must
  work by design, not by bug).
- **Gearbox**: any ratio set, any shift logic (auto/manual/custom), no-hunting
  shifting, per-gear launch targets.
- **Differentials**: open/locked/limited-slip/torque-split, per-axle and per-wheel
  wiring, live relinking (layouts changeable at runtime — the existing mods already do
  this to NWH).
- **Axles/layout**: multi-axle (the game has no 6x6 but configs exist), driven +
  steered combos, tag axles.
- Game integration stays FSM-compatible: B10 (engine-part swaps rewrite the gear array
  mid-session via `SetGearArrayByValue`) and B11 (NWH floating origin shifts the world
  while driving) remain vehicle-side facts to design for.

## 5. Harness & verification

- `verify/`: stub-compile + logic tests (the mod-repo pattern). Every milestone has
  numbers, tolerance bands vs the M0c baseline, and negative controls. Raw runner
  output pasted verbatim; oracles read-only for the builder.
- Allocation-free hot paths asserted; NaN guards at the API boundary; property tests
  for the horrid-states catalog (gear skip, delegate reassignment, `gears[gearIndex]`,
  stale input after exit, stuck wheels, NaN contagion, fall-through-world, trailer
  wobble, vehicle explosion).

## 6. Platform facts (runtime-measured 2026-10-05)

Unity 2020.3.49f1, Mono backend, BepInEx 5.4.23.5. The game's PlayMaker fork reports
assembly version **1.6.0.0** at runtime and puts `PlayMakerFSM` in the **global
namespace** (stock: `HutongGames.PlayMaker.`). NWH DLLs carry no version stamp; content
hashes captured by the census (CONTEXT.md). The game's NWH is developer-modified: it
has `VehicleController.SetGearArrayByValue(Single[])` (16 FSM call sites) and
`NWH.VehiclePhysics2.ManagerVehicleComponent` (no `.Modules` segment). Frameworks name:
"Physics Overhaul". License: MIT-0 (own code).

## 7. Decisions

- Repo: git, local only for now.
- Inside-out Harmony; mods stay on NWH until each feature is replaced (live A/B each).
- Reference vehicles for the feel baseline: **pending** (pick 3; light + heavy truck +
  truck+trailer is the working set).
- Sharing: excerpts only, never public.

## 8. Out of scope

- No changes to the game's FSMs or scenes.
- No Unity Editor tooling; runtime + JSON config only.
- No new tuning UI — the existing mods remain the tuning surface.
