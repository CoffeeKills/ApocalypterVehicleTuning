# Task: build the "ApocalypterDrivetrain" mod — round 1, the full drivetrain skeleton

ApocalypterDrivetrain is a **new** BepInEx mod for Apocalypter. Read in this order:
**README.md** (the load-bearing game-architecture facts, carried from the verified
ApocalypterSteeringMod work — treat them as load-bearing), **CONTEXT.md** (working state +
workflow), **SPEC.md** (§3–§4 are the product), then **FEATURES.md** (the authoritative
spec for this round — the 0.1.0-alpha skeleton slice). Where FEATURES.md and this file
conflict, FEATURES.md wins for features; this file's hard constraints always win.

The inputs you need:
- `gamecode/` — decompiled NWH drivetrain sources (the game's fork; `ApocalypterSource/`
  in the mods repo is the authority when they disagree).
- `inputs/census/` — the M0 census against the **real game DLLs** (2026-10-05):
  `census.json` machine-readable, `CENSUS.md` readable, `serialized-layout.json` =
  Unity-serialized field layout of every NWH type (the inside-out contract), plus
  behavioural notes B1–B11 and the stub drift/gap report.
- `inputs/SPO.Vehicle/` — the pure vehicle-core interfaces (`IVehicleBody`/`IWheelQuery`)
  the drivetrain logic targets. The mod's model stays engine-agnostic.
- `inputs/fsm-template-dump.md` + `template_dump.json.gz` — the FSM write census (what the
  game's PlayMaker FSMs do to NWH, every `SetProperty` decoded).

## Your job

1. **Bootstrap the mod** (FEATURES.md §1): `plugin/` that loads in-game with **zero effect
   by default**, the hidden-runner survival architecture (README fact 3), GUID
   `dev.apocalypter.drivetrain` (locked from first release — config continuity starts now),
   numeric-only version `0.1.0`.
2. **Build the drivetrain model** (FEATURES.md §2): pure C#, NWH-free — engine (torque
   curve, inertia, rev limits), clutch (engagement), gearbox (ratio set + shift-logic
   hook), differentials (open/locked/LSD/split + wiring), axles/layout (multi-axle). JSON
   in → validated object graph out. **Every invalid input gets a specific error, never a
   silent default.** The wiring validator must reject cycles (README fact 14: a powertrain
   cycle crashes the game).
3. **Wire the in-game skeleton** (FEATURES.md §4): A/B toggle vs NWH (OFF = provably no
   touch), hotkey + status log, Harmony patch POINTS for M1 (transmission/shift) and M2
   (engine/clutch/diffs) prepared but **inert** — no behavior replacement this round.
4. **Test everything you change** (FEATURES.md §5): `verify/` harness — stubs compile +
   logic tests + negative controls (the established convention). Extend `verify/stubs/`
   to mirror real signatures from `gamecode/` whenever you touch new API. All tests green;
   each rejection path in §2 has a negative control.
5. **Deliver**: the same bundle layout, a "Changes in 0.1.0-alpha" section in README.md,
   FEATURES.md statuses refreshed (a checklist), and a short list of what you built and
   why. Reason through `verify/run.sh` carefully — the target machine cannot run it for
   you.

## Hard constraints

- **Never write `vc.input.*`** — the game's FSMs overwrite it every frame (README fact 2).
- **Never call `ES3.Save`** — the game's save data is read-only for mods (README fact 4).
- **Preserve the hidden-runner survival architecture** (README fact 3).
- **No changes to the game's FSMs or scenes** (SPEC §8).
- **Allocation-free hot paths.** Validation happens at config load, never per tick.
- **The census is the contract**: everything the game's FSMs touch must keep resolving
  after every future patch (inside-out keeps NWH's types and names alive for free).
- **The mod's logic targets SPO.Vehicle's interfaces** (`inputs/SPO.Vehicle/`) and
  stays physics-engine-agnostic — the in-game host is PhysX, nothing else.
- **Config continuity starts now**: the GUID never changes; future keys must migrate,
  never silently drop values.
- `verify/run.sh` must run on your machine (bash + .NET SDK 8+; `verify/refs/` is
  populated; the stubs compile without the game).
