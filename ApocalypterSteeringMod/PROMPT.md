# Task: audit and rework "Apocalypter Vehicle Tuning" (BepInEx mod for the Unity game Apocalypter)

Read **README.md first** — it contains the verified game-architecture facts that drive every unusual design decision in this codebase (the game destroys plugin-created GameObjects on scene load; vehicle input is written by PlayMaker FSMs every frame; the game's save data must only ever be read; the ABS/TCS slip sign conventions; the NWH live-settability hazards, including §2.14 on live drivetrain rewiring). Treat those facts as load-bearing: a rework must preserve them. The decompiled game code you need is in `gamecode/`, `docs/plan.md` records the design history, and **CONTEXT.md** carries the current project state, the established workflow and the standing gotchas — skim it once.

Two open user-reported issues travel with this bundle:
- `docs/crash-2026-10-04.md` — a native crash during save-load (all categories OFF at the time). Its two hardening items (pause the tuner's 2 s scan during the post-load spawn wave; exception-guard every capture/apply path per category) are still open.
- FEATURES.md §11 — the telemetry strip still does not display properly for the user. Verify its scale/anchoring/cells against a real screen and fix as part of §4.

## Your job

0. **Implement FEATURES.md.** It is the authoritative feature spec for 0.7.0-alpha and opens with a "Status after 0.6.2-alpha" section listing what is done and what remains — implement the remaining items (§1 the mod-owned gearbox subsystem is the centerpiece; §2 remainder, §3–§5, §10, §11). Where PROMPT.md and FEATURES.md conflict, FEATURES.md wins for features; PROMPT.md's hard constraints always win.
1. **Audit** `plugin/` against the game code and the README. Find real bugs — correctness, restore-completeness, config-migration edge cases, UI problems, allocation hazards — not style nits.
2. **Rework** what you find, and improve the design where you can justify it in the README. Follow the existing patterns: `PresetBook` preset semantics (copy-to-Custom with BasedOn), allocation-free hot paths, the `VehicleTuner` capture/apply/restore model (now with the custom drivetrain layout), the anchored uGUI kit. Do **not** change the hidden-runner survival architecture, the PlayMaker input-blocking recipe, the one-Graphic-per-GameObject rule, or the mouse-only panel without calling it out explicitly in your changelog. Do **not** remove `GearboxSettings.ComingSoon` until §1's shift controller is done.
3. **Test everything you change.** `verify/` is a self-contained harness: `bash verify/run.sh` compiles the plugin against stubs and runs the logic + steering-prefix tests (needs a .NET SDK 8+; `verify/refs/` is already populated). Extend `verify/stubs/` to mirror real signatures from `gamecode/` whenever you add new API usage. All tests must pass, and each new hazard fix needs a negative control (the established convention).
4. **Update the README**: add a "Changes in \<new version\>" section listing bugs fixed and design changes, any new config keys, and migration rules for existing config files. Version the mod below 1.0 (e.g. `0.7.0-alpha`; the BepInEx version string itself must stay numeric-only, e.g. `0.7.0`).

## Hard constraints

- Never call `ES3.Save` — the game's save data is read-only for the mod (explicit absolute paths only).
- Never write `vc.input.*` — the game's FSMs overwrite it every frame.
- Keep the steering Harmony prefix allocation-free (it runs every physics tick).
- BepInEx config is the only persistence. Keep config continuity: the plugin GUID must not change; old keys must migrate, not disappear (no existing key renamed, removed or default-changed).
- Local `tools/` and `wiki/` are not part of the repo — ignore them.

## Deliver

The reworked `plugin/`, `verify/`, and the updated `README.md` in the same bundle layout, with a short list of what you changed and why, plus the updated `FEATURES.md` with each feature marked done / harness-tested (a checklist) and its "Status" section refreshed. Reason through `run.sh` carefully — the target machine cannot run it for you.
