# CONTEXT.md — project state and how to continue (read this first in a fresh session)

## What this is

"Apocalypter Vehicle Tuning" — a BepInEx 5 mod for **Apocalypter** (SawyerK Games, Unity 2020.3.49, NWH Vehicle Physics 2 + WheelController 3D). An in-game tuning panel for every vehicle: steering (with editable curves), suspension, wheel alignment, aero, brakes, grip, drivetrain (incl. a config-driven custom drivetrain layout), assists (ABS/TCS), a telemetry strip, and a "coming soon" gearbox. The README.md is the full living document — its §2 "Load-bearing game facts" drives every design decision.

## Paths

- **Source tree**: `D:\projects\ApocalypterMods\ApocalypterSteeringMod\` (`plugin/` = the mod, `verify/` = self-contained harness, `docs/`, `gamecode/` = decompiled NWH excerpts, `FEATURES.md`, `PROMPT.md`, `CONTEXT.md` — this file, `release/README.txt`)
- **Game**: `D:\SteamLibrary\steamapps\common\Apocalypter\` (BepInEx at `BepInEx\`, plugin DLL lives at `BepInEx\plugins\ApocalypterSteeringMod.dll`)
- **Decompiled game source** (full, ~6600 files): `D:\projects\ApocalypterMods\ApocalypterSource\` (regenerable via `docs/decomSource.ps1`; NOT in git)
- **Git**: local repo at `D:\projects\ApocalypterMods`, remote https://github.com/CoffeeKills/ApocalypterVehicleTuning (main). Repo-local identity is "Claude Code <noreply@anthropic.com>".
- **Nexus**: "Vehicle Tuning Interface" (nexusmods.com/apocalypter/mods/19).

## Current state (2026-10-05, session end)

- **Shipped/installed: 0.7.6-alpha** (0.7.6 = the gearbox-at-load fix: the hook captured the prefab type Manual before CheckTag wrote Automatic; the guard + pass now adopt the game's delegate and type on every flip). (0.7.5 = the gearbox-at-load diagnostic round: hook logging + stuck-neutral detector + the load-path harness test; awaiting the user repro log). (0.7.4 = the per-tab Reset fix after the reorg + pins replaced by the telemetry readout list). (commit 3a6ab21). 0.7.0 = the 3rd-party §1–§5 round (ShiftController as `transmission.shiftDelegate`, ComingSoon gate gone, centre-diff + layout UI, Truck preset with SpreadRatios, telemetry pins, tighter UI, 8 audit fixes). 0.7.1 = the panel reorg (8 tabs by car area). 0.7.2 = telemetry pin-overlap fix. 0.7.3 = the 12-gear launch fix (ShiftDelegateGuard: NWH's ForwardStep re-assigns its own delegate when the game flips transmissionType; the game's skipping automatic then landed the 12-gear box in gear 8 — harness reproduces exactly; the guard re-hooks in the same tick). Harness: **614 tests (596 logic + 18 prefix)**, green.
- **Status: the mods are being left as-is.** The user will fix them up themselves and is mostly moving on: **Salvage Physics Overhaul (SPO)** — their own Bepu-v2 physics subsystem with a drop-in NWH shim — is the future (spec: `D:\projects\ApocalypterPhysics\SPEC.md`). New work targets SPO; this repo is maintenance/frozen mode.
- **Open items the user knows about** (not being actively fixed here; they are SPO design lessons):
  - The stuck-wheels session bug (engine revs, no drive, "even when everything turned off"; cleared by a game restart — session state, root cause not fully pinned; the delegate/type-flip class of bug, mostly guarded by 0.7.3).
  - "Custom gearbox didn't seem to work" (user observation while testing 12-gear setups — never reproduced/clarified).
- **In-game checks:** the user playtested 0.7.1 and reported no issues; README §10 items 38–46 and 29–37 remain individually unconfirmed.
- Release zip `ApocalypterVehicleTuning-0.7.3-alpha.zip` + audit zip at the repo root; `gamecode/` carries the PlayMaker sources; `docs/fsm-template-dump.md` documents the decoded FSM surface (SPO's census input).

### Inputs §1 needs (attached with this round's audit zip)
- **Attached**: the PlayMaker action sources (`SetProperty.cs`, `GetProperty.cs`, `CallMethod.cs`, `FsmProperty.cs`, `FsmObject.cs`, `FsmStateAction.cs`) in `gamecode/`, plus `docs/fsm-shift-inputs.md` and **`docs/fsm-template-dump.md`** — the full §1 input. The gap is closed: the hand-rolled parser missed the trailing `setProperty` bool in `FsmProperty` (template-copy serialization), so every FSM with a SetProperty action failed to parse; fixed in `tools/fsm_extract.py`, and `tools/template_dump.py` now parses all 8128 FSMs. **Decoded answer**: the game shifts by `SetProperty` reflection on `VehicleController.input.ShiftInto` (int, R=-1/N=0/1..5) and `input.ShiftUp`/`input.ShiftDown` — never `transmission.Gear`. The §1 suppression target is those three property writes on a VehicleController target; the shift-button reads (`ShiftIntoR1`, `ShiftInto1..8`, `ShiftUp`, `ShiftDown`) stay untouched. `tools/template_dump.json` holds every FSM's action data (local tooling, not in git).

## The established workflow

1. **Build**: `cd plugin && dotnet build -c Release` (0 warnings/errors expected; references game DLLs by absolute path).
2. **Test**: `bash verify/run.sh` in `verify/` (Git Bash; SDK 8+; compiles ALL plugin .cs against stubs + runs the suites — new files must be stub-covered).
3. **Install**: copy `plugin/bin/Release/netstandard2.0/ApocalypterSteeringMod.dll` (+ `icon.png` as `ApocalypterSteeringMod.png`) to `BepInEx\plugins\`; rebuild `ApocalypterSteeringMod.zip` (DLL+PNG at root — the Apocasetter updater contract). **The game locks the DLL while running** — install fails with "Permission denied" until the game is closed.
4. **Release zip**: `ApocalypterVehicleTuning-<ver>-alpha.zip` = DLL + PNG + `release/README.txt` at root (Nexus upload artifact).
5. **Audit zip**: stage README.md, PROMPT.md, FEATURES.md, CONTEXT.md, `plugin/` (sources only), `verify/` (incl. refs/), `gamecode/`, `docs/` → `ApocalypterVehicleTuning-Audit.zip` for the 3rd-party AI (no terminal). The AI reworks and returns a bundle.
6. **Merge a returned bundle**: diff against the tree, review the changelog against gamecode, then — **the user must explicitly authorize merging/running external-bundle code** (a permission gate; say "merge and run tests" is the phrase that works). Merge → harness → build → install → commit → refresh zips.

## Gotchas that have bitten us

- **BepInEx 5 version strings**: numeric-only ("0.6.2"); "-alpha" makes BepInEx skip the plugin.
- **Gearbox on automatics (0.6.x "stuck" bug):** in 0.6.x any gearbox change on an automatic left the car stuck. The likely mechanism, reproduced in the harness, is NWH's raw automatic hunting on wide ratio steps, with the clutch open on every shift. 0.7.0's `ShiftController` owns shifting whenever Gearbox changes it, and the `ComingSoon` gate is gone. An old cfg with `[Gearbox] Enabled = true` now applies.
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
