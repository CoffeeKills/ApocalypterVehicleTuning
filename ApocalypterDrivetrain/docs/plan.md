# Plan — ApocalypterDrivetrain (0.1.0-alpha skeleton)

Design history for the mod. This file records the reasoning behind the skeleton shape;
later rounds append their decisions here.

## Round 1 scope (2026-10-06)

The first slice builds everything the later milestones stand on, without changing
in-game behavior:

1. **The drivetrain model** is the product's core and is deliberately pure C#,
   NWH-free and Unity-free: it can be tested headless and stays portable. The
   steering mod's `DrivetrainLayout` (pure, NWH-free) is the precedent. JSON in,
   validated graph out, every error specific, cycles rejected at load (README fact 7).
2. **The in-game part is minimal**: hidden runner, A/B toggle default OFF, status
   lines, and the M1/M2 Harmony patch points declared but inert. This proves the
   survival architecture and the toggle discipline before any behavior is replaced.
3. **Config continuity starts at 0.1.0**: GUID locked, version numeric, all future
   keys migrate. The steering mod's migration history is the cautionary tale.

## The M1/M2 patch points (declared, not used)

- `TransmissionComponent` — M1 replaces the shift delegate (README fact 8: the game
  writes shift *requests* only; NWH re-assigns its own delegate on `transmissionType`
  change; raw automatics hunt on wide ratio steps). The patch point must survive the
  delegate being re-assigned every tick, so it hooks at the step level, not the
  delegate level.
- `EngineComponent` / `ClutchComponent` / `DifferentialComponent` — M2 replaces the
  forward-step internals with the config-driven model, live-relinkable per vehicle.

## Open questions for later rounds

- Where exactly the model's gearbox sits relative to the game's B10 gear-array
  rewrites (`SetGearArrayByValue` on engine swaps) — the model must own the array
  after M1 or the swap FSMs will fight it.
- Multi-axle: the game has no 6x6 vehicle, but configs exist — the model supports any
  axle count from day one so M2 never re-architects.

## Round 1 decisions (as built, 2026-10-06)

- **No silent defaults** is enforced structurally: `ObjectReader.Finish()` reports every
  unread key, `Reject()` reports keys that don't apply to a variant, and each `ErrorCode` must
  be hit by a negative control or `verify/` fails. Two codes were dropped during the build
  because they could never fire: `NoDrivenWheel` (once the wiring is acyclic and fed once,
  and every diff has exactly 2 outputs, the reachable graph is a tree ending in wheels), and
  the duplicate `*ParamNotApplicable` codes, merged into one.
- **Cycle check is not redundant with the fed-once check**: with cycle detection disabled,
  fed-once still catches most cycles (the cycle's entry node is fed twice) but NOT a cycle
  among nodes the gearbox never reaches — that slips through as two DisconnectedNode
  warnings. Sabotage-tested.
- **Patch-target evidence**: only `DifferentialComponent.ForwardStep` is census-resolved. The
  transmission/clutch overrides and `EngineComponent.IntegrateDownwards` are gamecode-only.
  The labels are tested both ways, so a census update forces a relabel.
- **Expressions sample at load** into the same piecewise-linear type as point curves. The
  tick path has one evaluator; it's allocation-free and NaN-guarded.
- **Steering-harness carry-over removed**: its tests can't compile without the steering
  sources, and its stub helpers were 13 of the 18 census drift items.

## Open for M1

- Census entries for the three GamecodeOnly targets (or re-run the census with them listed).
- `automatic` is a stub. M1 replaces it with the no-hunting logic, using the same landing-RPM
  arithmetic as `WarningCode.ShiftWouldHunt`.
- B10: `SetGearArrayByValue` rewrites `gears` on engine swaps. The model has
  `ToNwhGearArray()` but doesn't yet decide who owns the array.
