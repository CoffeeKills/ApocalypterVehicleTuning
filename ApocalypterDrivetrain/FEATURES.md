# ApocalypterDrivetrain — features

Authoritative feature spec. Opens with the status of the last round and lists what
remains. This document defines round 1: **0.1.0-alpha — the full drivetrain skeleton**.
No behavior replacement yet; that starts in M1/M2 (SPEC.md §3).

## Status after 0.1.0-alpha (2026-10-06)

- [x] §1 Mod identity + bootstrap — GUID locked, numeric version, hidden runner, one startup line.
  *In-game load not yet observed* (user A/B pending).
- [x] §2 Drivetrain model — engine/clutch/gearbox/differentials/axles, 36 error codes, each
  with a negative control (enforced by a coverage test); cycles rejected incl. unreachable ones.
  The no-hunting rule exists as the **stub** `automatic` + a real load-time hunting *warning*.
- [x] §3 Config surface — `Enabled`/`ToggleKey`/`ConfigPath`, keys pinned by a test; README §4
  schema; example in README is byte-checked against the built-in.
- [x] §4 In-game skeleton — A/B controller, 4 inert patch points, hotkey + log status.
  **Open**: 3 of 4 patch targets are `GamecodeOnly` (no census entry) — add
  `TransmissionComponent.ForwardStep`, `ClutchComponent.ForwardStep`,
  `EngineComponent.IntegrateDownwards` to the census before M1/M2 enables them.
- [x] §5 Verification harness — `verify/run.sh`: 351 passed, 0 failed (.NET SDK 8.0.131,
  Linux). Stub drift: 18 → 0.

### In-game checks for the user (not testable headless)
- [ ] Log shows `ApocalypterDrivetrain 0.1.0-alpha (dev.apocalypter.drivetrain) loaded: OFF, NWH untouched; toggle with F9`
- [ ] F9 → `skeleton: NWH active, config loaded, 4 replacement points staged` (a lower N names
  the missing target in a warning line just above)
- [ ] Driving feels identical OFF vs ON (nothing is applied)
- [ ] After a scene change, F9 still toggles (runner recreated)

## §1 Mod identity + bootstrap

- BepInEx plugin "ApocalypterDrivetrain", GUID `dev.apocalypter.drivetrain` (locked from
  first release), attribute version `0.1.0` (numeric-only — BepInEx 5 skips `-alpha`
  tags).
- Hidden-runner survival architecture (README fact 3): all runtime logic on a
  `HideFlags.HideAndDontSave` GameObject, recreated on `SceneManager.sceneLoaded`.
  Harmony patches live in IL and survive scene loads.
- Loads with zero effect on the game: everything OFF by default, nothing patched while
  OFF, status logged once at startup.

## §2 Drivetrain model (pure C#, NWH-free)

One JSON config → one validated `Drivetrain` object graph. No UnityEngine in the model;
engine-agnostic (targets `SPO.Vehicle`'s `IVehicleBody`/`IWheelQuery` from
`inputs/SPO.Vehicle/`).

- **Engine**: torque curve (points or expression), inertia, idle/redline, rev-limit
  behavior.
- **Clutch**: engagement curve, slip behavior, launch profile.
- **Gearbox**: ratio set (reverse/neutral/forward, any count), shift-logic hook
  (delegate or named behavior). The no-hunting rule is a stub now — M1 fills it in.
- **Differentials**: open / locked / limited-slip / torque-split; per-axle and per-wheel
  wiring.
- **Axles/layout**: any axle count (multi-axle — the game has no 6x6 but configs exist),
  driven/steered/lift flags, wheel placement.
- **Wiring validation**: disconnected nodes flagged; **cycles rejected at load** (README
  fact 7: a powertrain cycle crashes the game — the model refuses before the game can).
- **Every invalid input gets a specific error, never a silent default**, and each
  rejection path has a negative-control test (§5).

## §3 Config surface

- BepInEx config: `[Drivetrain] Enabled` (default false), `ToggleKey`, `ConfigPath`
  (optional JSON file; a built-in example config ships with the mod and is documented in
  the README).
- JSON schema documented in README; parse errors report line/field.
- Config parse/validate happens at load and on toggle-ON, never per tick.

## §4 In-game skeleton (0.1.0)

- **A/B toggle**: OFF = the mod provably never touches NWH (no patch applied, no write).
  ON = loads + validates the JSON config and reports status: "skeleton: NWH active,
  config loaded, N replacement points staged".
- **Harmony patch POINTS prepared but inert** for M1/M2 targets:
  `TransmissionComponent` (M1 shift replacement), `EngineComponent` /
  `ClutchComponent` / `DifferentialComponent` (M2). Each point is compile-checked against
  the census signatures, gated by a runtime flag, all flags default false. No patch
  changes behavior this round.
- Hotkey toggle + BepInEx log status lines. No panel this round (panels come with the
  M4-era UI work).

## §5 Verification harness

- `verify/run.sh`: stubs compile + all logic tests. Extend `verify/stubs/` to mirror real
  signatures from `gamecode/` whenever new API is touched.
- Tests: model parse/validate/wire happy paths; **every rejection path in §2 has a
  negative control**; cycle rejection; config error reporting (line/field); OFF-means-
  no-touch as far as testable outside the game (patch flags default false, harmony id
  unused while OFF).
- All tests green before delivery.

## Later milestones (explicitly NOT this round)

- M1: transmission + shift logic replacement — no-hunting grid, launch-from-gear tests,
  gear-skip + delegate-reassignment regressions, "gear index always valid" property test,
  in-game A/B.
- M2: engine/clutch/differentials live relinking, custom layouts in-game, a mod-defined
  drivetrain driving with zero NWH patching.
- M3: tire/suspension. M4: assists, trailer stabilizer, crush feel. M5: mod migration,
  docs, release.
