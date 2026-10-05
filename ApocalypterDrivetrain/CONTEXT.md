# CONTEXT.md — ApocalypterDrivetrain — working state (gitignored, same convention as the steering mod)

## What this is

**New mod, first bundle round (2026-10-06)**: the vehicle drivetrain half of the
Salvage Physics Overhaul (SPEC.md in this folder). A BepInEx mod for Apocalypter that
replaces NWH's drivetrain from the inside — transmission, engine, clutch,
differentials, axles — and gives other mods a **config-driven drivetrain surface**
(JSON torque curves, ratios, layouts, multi-axle) with code hooks. "Wicked drivetrains"
without fighting NWH.

Sibling of `ApocalypterSteeringMod` and shares its conventions: hidden-runner survival
architecture, verify/ stub harness, audit-zip workflow, load-bearing game facts in
README.md §2.

## Established workflow (carried from the steering mod)

Three actors, one loop: the **3rd-party AI** builds in rounds from audit zips (no
terminal, has a .NET SDK — it runs `verify/run.sh` itself), the **user** ferries zips and
runs in-game A/B tests, the **orchestrator** (Claude) prepares bundles, reviews returns,
merges, stages installs, commits. The user pauses/steers the loop between rounds.

## State after round 1 (0.1.0-alpha, 2026-10-06)

Skeleton built: `plugin/` (model + A/B controller + 4 inert patch points), `verify/run.sh`
351/0 green. Nothing is applied in game. Next: the user's in-game checks (FEATURES.md), then
census entries for the 3 GamecodeOnly patch targets, then M1.

Orchestrator review passed (2026-10-06); harness made portable to SDK 9+ (netstandard-ref
cache/self-fetch in `verify/run.sh`); install staged for the user's in-game A/B.

## Inputs in this bundle

- `gamecode/` — decompiled NWH drivetrain sources (carried from the steering mod's
  `gamecode/`; the game's fork in `../ApocalypterSource/NWH.*` is the authority when they
  disagree).
- `inputs/census/` — the M0 census against the real game DLLs (2026-10-05): what the
  game's FSMs touch (the contract), NWH serialized field layouts, behavioural notes
  B1–B11, stub drift/gaps.
- `inputs/SPO.Vehicle/` — the pure vehicle-core interfaces the model targets.
- `inputs/fsm-template-dump.md` + `template_dump.json.gz` — the FSM write census.

## Game facts that matter for drivetrain work (details in README §2)

- Engine-swap FSMs call `VehicleController.SetGearArrayByValue(Single[])` — the game's
  NWH is developer-modified (B10); the real signature is in the census.
- NWH's floating origin shifts the whole world while driving (B11).
- The game's PlayMaker fork reports assembly version **1.6.0.0** at runtime and puts
  `PlayMakerFSM` in the **global namespace** (stock is `HutongGames.PlayMaker.`).
- The game's NWH has `NWH.VehiclePhysics2.ManagerVehicleComponent` (no `.Modules`
  segment) — the mod-harness stubs drift here (RECONCILIATION.md).
- NWH's shift delegate is reassigned on every `transmissionType` change; raw automatic
  shifts hunt on wide ratio steps; `gears[gearIndex]` is unguarded; the game's FSMs write
  shift *requests* (`input.ShiftInto`/`ShiftUp`/`ShiftDown`), never `transmission.Gear`.
- The powertrain steps by recursing `_output`/`_outputB` every tick; a wiring cycle
  crashes the game.

## Milestones (SPEC.md §3)

M1 transmission/shift logic → M2 drivetrain core + mod API → M3 tire/suspension → M4
assists/trailer/crush → M5 mod migration + docs. This round delivers the **0.1.0-alpha
skeleton** (FEATURES.md) — model + config + toggle + inert patch points, no behavior
replacement yet.
