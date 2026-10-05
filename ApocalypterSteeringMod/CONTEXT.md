# CONTEXT.md — project state and how to continue (read this first in a fresh session)

## What this is

"Apocalypter Vehicle Tuning" — a BepInEx 5 mod for **Apocalypter** (SawyerK Games, Unity 2020.3.49, NWH Vehicle Physics 2 + WheelController 3D). An in-game tuning panel for every vehicle: steering (with editable curves), suspension, wheel alignment, aero, brakes, grip, drivetrain (incl. a config-driven custom drivetrain layout), assists (ABS/TCS), a telemetry strip, and the mod-owned gearbox + shift controller (0.7.0). The README.md is the full living document — its §2 "Load-bearing game facts" drives every design decision.

## Paths

- **Source tree**: `D:\projects\ApocalypterMods\ApocalypterSteeringMod\` (`plugin/` = the mod, `verify/` = self-contained harness, `docs/`, `gamecode/` = decompiled NWH excerpts, `FEATURES.md`, `PROMPT.md`, `CONTEXT.md` — this file, `release/README.txt`)
- **Game**: `D:\SteamLibrary\steamapps\common\Apocalypter\` (BepInEx at `BepInEx\`, plugin DLL lives at `BepInEx\plugins\ApocalypterSteeringMod.dll`)
- **Decompiled game source** (full, ~6600 files): `D:\projects\ApocalypterMods\ApocalypterSource\` (regenerable via `docs/decomSource.ps1`; NOT in git)
- **Git**: local repo at `D:\projects\ApocalypterMods`, remote https://github.com/CoffeeKills/ApocalypterVehicleTuning (main). Repo-local identity is "Claude Code <noreply@anthropic.com>".
- **Nexus**: "Vehicle Tuning Interface" (nexusmods.com/apocalypter/mods/19).

## Current state (2026-10-06)

- **Shipped/installed: 0.9.0-alpha** (commit 6486127, pushed; game install updated; release + audit zips refreshed at the repo root).
  - 0.9.0 = **per-vehicle tunes**: `[PerVehicle] Tunes` config blob (`VehicleName|Category|code`, code = a PresetCodec AVT1 line); `PresetBook<T>` per-vehicle store (`SaveVehicle`/`ForVehicle`/`RemoveVehicle`/`ClearVehicles`/…), every apply path picks `Book.ForVehicle`; BasedOn preserved through `PresetCodec.SerializeVehicle`/`ResolveBuiltIn`; clear-and-reimport on load (idempotent), saved from MirrorRuntimeToEntries, `_perVehicleTunes` wired for live external (Apocasetter) edits. **`[General] ResetOnSaveSwitch` default flipped false → true** (existing cfg files keep their stored value; only unbound keys pick up the new default).
  - 0.8.0 = max steering angle (`[Steering.Custom] MaxSteerAngle`, 0 = the vehicle's own lock, ≤70°; the prefix cap feeds the whole pipeline) + save tracking (`Game/SaveTracker.cs` newest-`SaveGameN.es3` pick, pure + harness-tested; `LastSave`/`LastSaveStamp`; ResetOnSaveSwitch default false there).
  - 0.7.7 = quiet release logging (`[Gearbox] DebugHooks` off by default; stuck-neutral warning stays). 0.7.6 = the gearbox-at-load fix (the hook captured the prefab type before CheckTag wrote the real one; the guard + pass now adopt the game's delegate AND type on every flip — confirmed working in-game). 0.7.5 = the gearbox-at-load diagnostic round. 0.7.4 = the per-tab Reset fix + pins replaced by the telemetry readout list. 0.7.3 = the 12-gear launch fix (ShiftDelegateGuard). 0.7.2 = telemetry pin-overlap fix. 0.7.1 = the panel reorg (8 tabs by car area). 0.7.0 = the 3rd-party §1–§5 round (ShiftController, gearbox, centre-diff + layout UI, Truck preset, tighter UI, 8 audit fixes).
  - Harness: **646 tests (627 logic + 19 prefix)**, green.
- **Status: the mods are being left as-is.** The user will fix them up themselves and is mostly moving on: **Salvage Physics Overhaul (SPO)** — their own Bepu-v2 physics subsystem with a drop-in NWH shim — is the future (spec: `D:\projects\ApocalypterPhysics\SPEC.md`). New work targets SPO; this repo is maintenance/frozen mode.
- **0.9.0 flagged gaps** (documented in README §8, not being fixed here):
  - No panel UI for per-vehicle save/remove (`SettingsPanel.cs` unmodified in 0.9.0 — the blob is config-level / Apocasetter-editable).
  - `ShiftController.cs:287` still uses the global `GearboxSettings.ActivePreset` for shift decisions while gearbox apply picks per-vehicle — needs a decision.
- **Open items the user knows about** (not being actively fixed here; they are SPO design lessons):
  - The stuck-wheels session bug (engine revs, no drive, "even when everything turned off"; cleared by a game restart — session state, root cause not fully pinned; the delegate/type-flip class of bug, mostly guarded by 0.7.3).
  - "Custom gearbox didn't seem to work" (user observation while testing 12-gear setups — never reproduced/clarified).
- **In-game checks:** README §10 items 29–37 (0.6.x/0.7.0) and 47–50 (0.8.0/0.9.0) remain individually unconfirmed.
- `gamecode/` carries the PlayMaker sources; `docs/fsm-template-dump.md` documents the decoded FSM surface (SPO's census input).

## The established workflow

1. **Build**: `cd plugin && dotnet build -c Release` (0 warnings/errors expected; references game DLLs by absolute path).
2. **Test**: `bash verify/run.sh` in `verify/` (Git Bash; SDK 8+; compiles ALL plugin .cs against stubs + runs the suites — new files must be stub-covered).
3. **Install**: copy `plugin/bin/Release/netstandard2.0/ApocalypterSteeringMod.dll` (+ `icon.png` as `ApocalypterSteeringMod.png`) to `BepInEx\plugins\`; rebuild `ApocalypterSteeringMod.zip` (DLL+PNG at root — the Apocasetter updater contract). **The game locks the DLL while running** — install fails with "Permission denied" until the game is closed.
4. **Release zip**: `ApocalypterVehicleTuning-<ver>-alpha.zip` = DLL + PNG + `release/README.txt` at root (Nexus upload artifact).
5. **Audit zip**: stage README.md, PROMPT.md, FEATURES.md, CONTEXT.md, `plugin/` (sources only), `verify/` (incl. refs/), `gamecode/`, `docs/` → `ApocalypterVehicleTuning-Audit.zip` for the 3rd-party AI (no terminal). The AI reworks and returns a bundle.
6. **Merge a returned bundle**: diff against the tree, review the changelog against gamecode, then — **the user must explicitly authorize merging/running external-bundle code** (a permission gate; say "merge and run tests" is the phrase that works). Merge → harness → build → install → commit → refresh zips.

## Gotchas that have bitten us

- **BepInEx 5 version strings**: numeric-only ("0.6.2"); "-alpha" makes BepInEx skip the plugin.
- **BepInEx 5 multi-line config values**: real `\n` in a value is escaped to a literal two-char `\n` on save and un-escaped on load (round-trip safe); hand-written multi-line values truncate to the first line. `[PerVehicle] Tunes` is documented as panel-managed because of this.
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
- **Standing instruction (user): "do the readme always and keep context files up to date."** Every release updates, in one pass: the README changelog + title/suite counts/§3 source map/§10 checklist, `release/README.txt`, CONTEXT.md state, FEATURES.md status, `docs/strings.md` sweep count, and the memory files — then rebuilds the release + audit zips (they embed those docs).
- New config keys are additive only; string enums parse name-only with a Stock/default fallback. (0.9.0's ResetOnSaveSwitch default flip is the documented exception: existing cfg files keep their stored value.)
- The harness's negative-control convention: each new hazard test should fail when its fix is reverted; document the counts in the README's verification section.
- `docs/strings.md` is the translator inventory (regenerate the grep sweep when strings change).
