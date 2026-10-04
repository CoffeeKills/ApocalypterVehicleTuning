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

- **Shipped/installed: 0.6.2-alpha** (PluginInfo "0.6.2" — numeric-only, BepInEx 5 skips "-alpha"). Harness: **467 tests (449 logic + 18 prefix)**, green. Release zip `ApocalypterVehicleTuning-0.6.2-alpha.zip`, audit zip `ApocalypterVehicleTuning-Audit.zip` at the repo root.
- **Next round (0.7.0-alpha) is with the 3rd-party AI**: the audit zip carries FEATURES.md (opens with a "Status after 0.6.2-alpha" section). Remaining: §1 mod-owned gearbox subsystem (shift-write suppression + ShiftController — unlocks the Gearbox tab, currently gated by `GearboxSettings.ComingSoon`), §2 remainder (panel UI for the drivetrain layout), §3 Truck 12-gear preset, §4 telemetry pins, §5 tighter UI, §10 crash hardening, §11 telemetry display fix.
- Open user-reported issues: `docs/crash-2026-10-04.md` (native crash during save-load spawn wave; hardening items pending) and the telemetry display complaint (FEATURES §11).

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
