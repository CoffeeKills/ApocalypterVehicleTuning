# CONTEXT.md — project state and how to continue (read this first in a fresh session)

## What this is

"Apocalypter Vehicle Tuning" — a BepInEx 5 mod for **Apocalypter** (SawyerK Games, Unity 2020.3.49, NWH Vehicle Physics 2 + WheelController 3D). An in-game tuning panel for every vehicle: steering (with editable curves), suspension, wheel alignment, aero, brakes, grip, drivetrain (incl. a config-driven custom drivetrain layout), assists (ABS/TCS), a telemetry strip, and a "coming soon" gearbox. The README.md is the full living document — its §2 "Load-bearing game facts" drives every design decision.

## Paths

- **Source tree**: `D:\projects\ApocalypterMods\ApocalypterSteeringMod\` (`plugin/` = the mod, `verify/` = self-contained harness, `docs/`, `gamecode/` = decompiled NWH excerpts, `FEATURES.md`, `PROMPT.md`, `CONTEXT.md` — this file, `release/README.txt`)
- **Game**: `D:\SteamLibrary\steamapps\common\Apocalypter\` (BepInEx at `BepInEx\`, plugin DLL lives at `BepInEx\plugins\ApocalypterSteeringMod.dll`)
- **Decompiled game source** (full, ~6600 files): `D:\projects\ApocalypterMods\ApocalypterSource\` (regenerable via `docs/decomSource.ps1`; NOT in git)
- **Git**: local repo at `D:\projects\ApocalypterMods`, remote https://github.com/CoffeeKills/ApocalypterVehicleTuning (main). Repo-local identity is "Claude Code <noreply@anthropic.com>".
- **Nexus**: "Vehicle Tuning Interface" (nexusmods.com/apocalypter/mods/19).

## Current state (2026-10-04)

- **Shipped/installed: 0.6.4-alpha** (PluginInfo "0.6.4"). Harness: **504 tests (486 logic + 18 prefix)**, green (run on .NET SDK 8.0.131). Built against the game DLLs (0 warnings), installed to `BepInEx\plugins\`, release zip `ApocalypterVehicleTuning-0.6.4-alpha.zip` + refreshed audit zip at the repo root.
- 0.6.3 closed FEATURES §10 (crash hardening) and §11 (telemetry pick: idling engines). 0.6.4 closed the user's follow-up: parked cars freeze their FSM-written input at exit values (handbrake/brakes) and stole the pick again — the pick is now liveness-gated (`UpdateInputLiveness`: input counts only while it changes, 2 s hold, `InputDeadZone` 0.05; first-sample grace). `[Telemetry] DebugPick` logs the pick for diagnostics. See README "Changes in 0.6.4-alpha".
- **Remaining for 0.7.0**: §1 mod-owned gearbox subsystem (shift-write suppression + `ShiftController`; `ComingSoon` gate stays until then), §2 remainder (layout panel UI), §3 Truck preset, §4 telemetry pins (+ the §4 remainder), §5 tighter UI.
- In-game checks pending: README §10 items 29–37 (0.6.2 items 29–33, 0.6.3 items 34–36, 0.6.4 item 37).

### Inputs §1 needs (attached with this round's audit zip)
- **Attached**: the PlayMaker action sources (`SetProperty.cs`, `GetProperty.cs`, `CallMethod.cs`, `FsmProperty.cs`, `FsmObject.cs`, `FsmStateAction.cs`) in `gamecode/`, plus `docs/fsm-shift-inputs.md` — a survey of all 6834 parsed FSMs: **no** FSM writes `Gear`/`GearShift` via SetProperty, no CallMethod touches shifting, no FSM reads a Shift* button. The shifting is compiled NWH input code + runtime FsmTemplates (the 1296 failed template parses; `RunFSM` instantiates them — likely where the live `INPUT_ShiftIntoN`/`Wrapper` FSMs come from).
- **Still needed**: the live FSM-template dump (one vehicle's `Wrapper` FSM and its `INPUT_GearChange`/`INPUT_ShiftUpDown` template instances, action list with target object/property/method) — either extend `tools/fsm_extract.py` to parse `FsmTemplate` objects, or capture a discovery-log dump from a running game.

## The established workflow

1. **Build**: `cd plugin && dotnet build -c Release` (0 warnings/errors expected; references game DLLs by absolute path).
2. **Test**: `bash verify/run.sh` in `verify/` (Git Bash; SDK 8+; compiles ALL plugin .cs against stubs + runs the suites — new files must be stub-covered).
3. **Install**: copy `plugin/bin/Release/netstandard2.0/ApocalypterSteeringMod.dll` (+ `icon.png` as `ApocalypterSteeringMod.png`) to `BepInEx\plugins\`; rebuild `ApocalypterSteeringMod.zip` (DLL+PNG at root — the Apocasetter updater contract). **The game locks the DLL while running** — install fails with "Permission denied" until the game is closed.
4. **Release zip**: `ApocalypterVehicleTuning-<ver>-alpha.zip` = DLL + PNG + `release/README.txt` at root (Nexus upload artifact).
5. **Audit zip**: stage README.md, PROMPT.md, FEATURES.md, CONTEXT.md, `plugin/` (sources only), `verify/` (incl. refs/), `gamecode/`, `docs/` → `ApocalypterVehicleTuning-Audit.zip` for the 3rd-party AI (no terminal). The AI reworks and returns a bundle.
6. **Merge a returned bundle**: diff against the tree, review the changelog against gamecode, then — **the user must explicitly authorize merging/running external-bundle code** (a permission gate; say "merge and run tests" is the phrase that works). Merge → harness → build → install → commit → refresh zips.

## Gotchas that have bitten us

- **BepInEx 5 version strings**: numeric-only ("0.6.2"); "-alpha" makes BepInEx skip the plugin.
- **The game's shift logic owns automatic transmissions** — any gearbox change on an automatic (mode/ratios/count/clutch) leaves the car stuck (engine revs, no drive). Hence the `ComingSoon` gate; only the 0.7.0 shift controller unlocks it. `GearCount = 12` in an old cfg is a landmine (set 0).
- **Config migrations are one-time, triggered by RAW-FILE key presence** (`ConfigFile.ContainsKey` only sees bound entries). Widening ranges never invalidates stored values.
- **The game never sets NWH's `isPlayerControllable`** → `Vehicle.ActiveVehicle` is empty; telemetry/driven-vehicle picks: live input → last-driven memory → running engine → fastest → first.
- **Driving input names** (whitelisted in InputBlocker, verified from the discovery log): `Steering`, `Throttle`, `Brakes`, `Handbrake`, `Clutch`, `Horn`, `Headlight`, `ShiftUp`, `ShiftDown`, `ShiftInto1..8`, `ShiftIntoR1`, `Cruise Control`, `Change Camera`, `TrailerAttachDetach`.
- **Panel**: docked right, live by default (`FreezeWhileOpen` toggle restores the old freeze), one Graphic per GameObject, mouse-only, strings must flow through Text.text (ApocaLanguage auto-translation) with `{0}` templates in UiStrings.
- **git hygiene**: `tools/` and `wiki/` are the user's local pipeline — gitignored, never committed (a 124 MB AssetRipper exe there broke a push). `gamecode/` is local-only (decompiled NWH excerpts), scrubbed from history; audit zips still carry it (private handoff).
- **`bash run.sh` on an external bundle triggers a permission classifier** — read/diff first, get the user's go-ahead, then merge.
- Commit messages end with `Co-Authored-By: Claude Code <noreply@anthropic.com>`.

## Housekeeping conventions

- README changelogs are per-version ("Changes in X-alpha") with a "Not changed" paragraph reaffirming the §2 facts.
- New config keys are additive only; string enums parse name-only with a Stock/default fallback.
- The harness's negative-control convention: each new hazard test should fail when its fix is reverted; document the counts in the README's verification section.
- `docs/strings.md` is the translator inventory (regenerate the grep sweep when strings change).
