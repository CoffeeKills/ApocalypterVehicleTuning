# Apocalypter Vehicle Tuning (v0.7.4-alpha)

A BepInEx mod for **Apocalypter** (SawyerK Games, Unity 2020.3.49, BepInEx 5.4.23.5 + Harmony 2) that adds a full vehicle-tuning panel: steering, suspension, aero, brakes, tire grip, drivetrain, stability assists (ABS/TCS), wheel alignment and gearbox, all applied live to every vehicle in the game. Since 0.6.0 the panel docks to the right edge and you can keep driving while it is open.

The repo contains the complete mod source (`plugin/`) plus a self-contained test harness (`verify/` — Unity/NWH compile stubs and a 504-test suite, needs only a .NET SDK 8+; `verify/refs/` carries the BepInEx 5 core DLLs it needs). The `gamecode/` excerpts used for audits are kept out of the public repo.

## Changes in 0.2.0-alpha

Audit of 0.1.0-alpha against `gamecode/` and §2. Every fix below has a regression test in `verify/tests/Tests.cs` that **fails on 0.1.0-alpha** and passes now (negative control run: 23 of the new checks fail on the old plugin, the old plugin also crashes outright on a vehicle without a module manager). Suite: 131 tests (125 logic + 6 steering-prefix), all passing.

**Not changed** (all §2 facts preserved): the steering prefix (byte-identical), the hidden-runner survival architecture, the InputBlocker recipe, one Graphic per GameObject, the mouse-only panel, no `ES3.Save`, no `vc.input.*` writes, BepInEx config as the only persistence, the GUID.

### Bugs fixed

**Assists**
1. **ABS killed the handbrake.** `Brakes.VC_FixedUpdate` multiplies the brake-torque modifiers into the handbrake torque as well. A handbrake-locked rear wheel exceeded the ABS slip threshold, the delegate returned 0.01, and the handbrake dropped to 1% — no handbrake turns with assists on. NWH's own `ABSModule` has a `input.Handbrake < 0.1f` guard that the mod's copy lacked. Added (read-only access to `vc.input.Handbrake`).

**Drivetrain**
2. **Front/rear diff mapping by list index was wrong for RWD and AWD cars.** `differentials[0]` was always treated as the front axle, so the single (rear) diff of a RWD car got the *front* mode (Drift: rear "Locked" became "Open"), and the centre diff of an AWD car (index 2) got the rear mode. Each diff's axle is now resolved from what it drives: an axle diff's `OutputB` is a `WheelComponent` (front/rear taken from that wheel's captured flag), a centre diff's `OutputB` is another `DifferentialComponent`. The index convention (0 front, 1 rear, 2+ centre — what `vc.DiffFrontType` etc. use) remains only as a fallback for diffs whose outputs are not wired yet. Centre diffs keep their stock type.
3. **"Stock" diff mode did not restore.** Switching from Race (Locked) to Stock, or clicking "Stock" on a diff, while the category was enabled left the diffs Locked until the whole category was turned off. "Stock" now writes the captured type back.
4. **Allocation on every apply.** The `DifferentialType` setter re-assigns NWH's split delegate (a new delegate allocation). It was assigned on every `ApplyLive` (every 2 s and on every slider tick). It is now assigned only when the type actually changes, in both apply and restore.
5. **External diffs.** Assigning `Type.External` does not re-assign the split delegate (`AssignDifferentialDelegate` breaks out), so forcing and later restoring an External diff left the forced delegate behind. External diffs are never touched now.
6. **Diff bias made axle diffs one-sided.** `biasAB` is the A/B (left/right) torque split on an axle diff. Scaling it (×1.5 → 75/25) made the car pull to one side under power. The slider's own hint said "torque split between the axles", so it now applies to **centre diffs only** (relabelled "Diff bias (AWD)"). Axle diffs keep their stock bias.
7. **Diff stiffness above NWH's 0..1 range.** Stiffness × factor is now capped at `max(1, stock)`, because a locking diff above 1 winds up and oscillates (NWH tooltip).
8. **Gearbox lock-up from scaled shift points.** On transmissions without `variableShiftPoint`, NWH uses `UpshiftRPM` raw as the shift target (`TransmissionComponent.AutomaticShift`). Only the variable path clamps to `revLimiterRPM × 0.97`. An upshift at or above the scaled rev limiter is then reached only on limiter overshoot, or never, so the box sits bouncing on the limiter. The built-in Race preset does this on any vehicle whose stock upshift is ≥ ~96% of its limiter (test: 4500/4700 rpm → upshift 5175 against a 5170 limiter), and Custom factors can do it on any vehicle. Downshift ≥ upshift made the box hunt between gears, since the non-variable path has no clamp there either. The fixed-point upshift is now capped at 97% of the *scaled* limiter (the ceiling NWH applies to its own variable target), and the downshift at 90% of the upshift. Each cap relaxes to the vehicle's own stock ratio when the stock setup already exceeds it, so the Stock preset reproduces the shipped values exactly.

**Brakes**
9. **Axle and handbrake factors above 1 were dead.** NWH caps every wheel at `brakes.maxTorque`, and the mod clamped `brakeCoefficient` to 0..1, where the stock value is usually 1.0. So Front/Rear ×1.1–2.0 did nothing, the Race preset's front/rear factors were no-ops, and Drift's "much stronger handbrake" (×1.6) was capped back to ×1.0 on a full pull. Brakes are now *balance-preserving*: with `k = max(1, peak requested coefficient ÷ peak stock coefficient)` (for both pedal and handbrake), `maxTorque = stock × TorqueScale × k` and every coefficient is divided by `k`. Every brake path (pedal, handbrake, off-throttle, idle, disabled, reverse) goes through `AddBrakeTorque × coefficient`, so the per-axle torques are exactly `stock × Torque × axle factor`. Only the per-wheel cap moves with them. Stock factors give `k = 1`, i.e. the shipped values unchanged. Caveat: `AddBrakeTorqueAllWheels` also clamps the *sum* of simultaneous brake sources at `maxTorque`, so that sum ceiling rises by `k` too. **Behaviour change:** presets with axle/handbrake factors above 1 (Sport, Race, Off-road, Drift) now actually deliver them, so they brake somewhat harder than in 0.1.0.

**Aero**
10. **Stock was not stock.** On vehicles with a shipped `AerodynamicsModule`, every preset including Stock forced `simulateDrag = true` and switched `simulateDownforce` on whenever downforce points existed, even if the vehicle's designer had turned them off. The shipped switches are now kept, and scaling never turns a disabled effect on.
11. **Onboarded module left active on Stock.** Switching back to Stock (or a Custom with all factors 1) *while enabled* left the onboarded module running with default Cd 0.35 drag on a vehicle that never had aero. An identity preset now parks the module exactly like a restore does.
12. **Restore relied on "disabled" alone.** NWH can re-enable a disabled component (`UpdateLOD` with a state-settings `lodIndex`, or a parent `VC_Enable`). A restored onboarded module is now also made inert (`simulateDrag = simulateDownforce = false`, default coefficients), so re-enabling it is harmless.
13. **Onboarded module could stay uninitialised.** `AddAndOnboardNewComponent` loads `state.isEnabled` from the vehicle's state settings *without* initialising the module. The re-enable check now uses `IsActive` (enabled **and** initialised), so `VC_Enable` always initialises it. Without this, such a module would never run.
14. **Null guards.** A vehicle with no `moduleManager` threw a NullReferenceException in the apply pass every 2 s, which aborted every category after Aero for all vehicles. A null `downforcePoints` list on a module also threw. Both are guarded now.

**Lifecycle / restore completeness**
15. **A deactivated runner left vehicles tuned, and its replacement compounded.** Only `OnDestroy` restored. If the runner was deactivated rather than destroyed, `EnsureRunner` built a second runner whose tuner captured the *tuned* values as stock. Factors then compounded, and OFF "restored" to tuned values. Now `VehicleTuner.OnDisable` restores (and `OnEnable` re-applies on the next frame), and `EnsureRunner` destroys a stale inactive runner before creating the new one. *(Survival-architecture note: the hidden-runner + `sceneLoaded` recreation design is unchanged. This only hardens the "runner exists but inactive" branch of `EnsureRunner`.)*
16. **Restore clobbered categories that were never applied.** `RestoreAll` wrote captured values for all six categories even when a category was never switched on, overwriting anything the game had changed on those fields since capture. It now restores only applied categories.
17. **Injected menu buttons outlived their manager.** The clones live inside the *game's* canvases, so a replaced runner left a dead button behind and a second one appeared. `SettingsPanelManager.OnDestroy` now destroys its clones. A slot whose clone was destroyed by the game is re-injected (at most 5 attempts per canvas), where 0.1.0 left that menu without a button for the rest of the session.
18. **Input could stay blocked.** If the panel was closed and the manager was disabled in the same frame, before the deferred one-frame unblock ran, `InputBlocker` stayed active with `timeScale = 0`. `OnDisable` now completes the pending unblock.

**Config**
19. **Diff modes.** `Enum.TryParse` accepted any integer (`DiffFrontMode = 7` became an undefined `DiffMode`), and rejected `LSD`, the label the panel shows. The parser now accepts names only (case/space-tolerant) plus `LSD`; anything else falls back to Stock.
20. **Legacy `Suspension.Preset = Street`** was mapped at runtime, but the dead name stayed in the file until the panel was opened and closed. It is now rewritten to `Stock` during load.

**UI**
21. Clicking the already-active diff mode forked the preset into "Custom (…)" with nothing changed. It is a no-op now.
22. Turning "Separate front and rear" OFF on a built-in preset whose axles are already equal (Stock, Comfort, Off-road) forked it into Custom. It now forks only when the axles differ.
23. **Allocation:** the 4 Hz menu-button scan allocated a `Button[]` per canvas per scan. Per-slot `List<Button>` buffers are now refilled in place.
24. Preset texts that contradicted their values: Aero "Street" said "less drag, stock downforce" but is ×0.8 downforce / ×1.0 drag. Brakes "Race" said "rear bias" but is front 1.15 / rear 1.10. The descriptions were corrected (values unchanged). Brake and diff slider hints were updated for fixes 6 and 9.

### Config keys and migration

- **No new keys, no renamed keys, no removed keys** (GUID unchanged). A 0.1.0-alpha config file loads as is. Only some key *descriptions* changed (BepInEx rewrites the comment lines on save).
- Load-time normalisation: `Suspension.Preset = Street` → `Stock` (same runtime result as before). `Drivetrain.Custom.DiffFrontMode/DiffRearMode` values `LSD`/`lsd` → `LimitedSlip`, and numeric or unknown values → `Stock` (written back on the next save).
- The v3.1 → v3.2 `[Suspension.User]` fold (§5) is unchanged and still one-time.
- Saved Custom values keep their meaning. The behaviour changes users will notice are fixes 6, 8 and 9: `DiffBiasScale` now affects only centre diffs, extreme shift-point factors are capped, and brake axle/handbrake factors above 1 now take effect.

### Verification harness
- Stubs (mirroring `gamecode/` signatures): `PowertrainComponent` base for `WheelComponent`/`DifferentialComponent`, `DifferentialComponent.OutputB`, `VehicleInputHandler.Handbrake` (with the real 0..1 clamp), `VehicleComponent.IsActive` as enabled-and-initialised, `StateDefinition.initialized`, `AddAndOnboardNewComponent` calling `VC_LoadStateFromStateSettings`/`UpdateLOD`, and `Component.GetComponentsInChildren<T>(bool, List<T>)`. Test hooks: `DifferentialComponent.TypeAssignments` (counts setter calls) and `ManagerVehicleComponent.OnboardEnablesState` (simulates state settings that pre-enable a module).
- New test groups: brakes balance, drivetrain diffs + shift points, aero stock fidelity, ABS handbrake, lifecycle (Unity's `OnDisable`/`OnDestroy` invoked by reflection, as Unity calls them), config hardening.
- `run.sh` itself is unchanged.

### In-game checks to add to §10
- Assists Standard + Brakes Drift: the handbrake still locks the rear (fix 1), and the handbrake is clearly stronger than Stock (fix 9).
- RWD car, Drivetrain Drift: the rear diff is Locked (with F-key telemetry or by feel). Then switch to Stock while enabled: the diff opens again.
- Drivetrain Race on the highest-revving vehicle: the automatic box still upshifts.
- Aero: on a car without aero, Race → Stock while enabled gives no extra drag (top speed back to stock).

## Changes in 0.3.0-alpha

**Steering preset**
1. **"Truck-sim" is replaced by "Euro Truck".** The old preset was already the truck-ish option, but its tuning was too quick for a heavy-highway feel. Euro Truck: rate ×0.5 (was ×0.7), speed curve (0,1)(0.2,0.6)(0.5,0.3)(1,0.12) — at 80–90 km/h cruise the wheel cuts to roughly a third of full lock — smoothing ×1.7, slip clamp 6.5°, **no** opposite-lock boost (you counter-steer yourself; the wheel never snaps), linearity pow 1.35 (gentle around centre), and **center return ×0.25**: unwinding toward center runs at a quarter of the steer-in rate, so the wheel stays where you put it and eases back instead of springing to center (a new `Steering.Custom.CenterReturnScale` knob, range 0.1–1, default 1 = symmetric; other presets unchanged). A saved `Steering.Preset = Truck-sim` (and `Steering.Custom.BasedOn = Truck-sim`) migrates to Euro Truck on load, both at runtime (preset-book legacy map) and in the config file (rewritten on load, like the earlier Street→Stock migration). The Custom speed curve survives the migration.

**Apocasetter integration (zero code dependency)**
2. **The mod registers with the Apocasetter Mods menu.** A new `[General] Apocasetter = true` config entry (the opt-in convention Apocasetter reads via Chainloader) lists the mod with its square icon in the Mods window, where every config entry gets a described, ranged, live-editing control for free. No DLL reference to Apocasetter — it works exactly as before without it.
3. **Updater contract prepared.** `icon.png` (64×64, generated by `tools/make_icon.ps1`) ships beside the DLL as `ApocalypterSteeringMod.png`; the install zip now unpacks into `BepInEx\plugins` per the Apocasetter index contract (§6b). Index submission stays deferred until a public repo exists (local-only git for now).

**Localization groundwork**
4. **The panel is translatable via ApocaLanguage.** Every dynamic string moved into `Runtime/UiStrings.cs` as `{0}` templates (value formats, preset "Custom (…)" labels, status lines — singular/plural split), so ApocaLanguage's uGUI `Text` hooks can translate them; static labels already flowed through `Text.text` and need no change. Panel labels now render rich text so translated tags display correctly. `docs/strings.md` documents the full inventory and the translator rules (preset `Name` = config ID, never translated; `Label` = display). No dependency on ApocaLanguage — without it, everything renders in English as before.

**Menu button**
5. **The "Vehicle Tuning" button no longer covers Apocasetter's MODS button.** Both mods pinned their injected menu button at (-30,-30) top-right; ours drew on top and ate the MODS clicks. Ours now pins below: it stacks directly under a detected MODS-labeled button, or at a fixed clearance that a later Apocasetter injection still cannot overlap.

**Not changed** (all §2 facts preserved): the steering prefix (byte-identical), the hidden-runner survival architecture, the InputBlocker recipe, one Graphic per GameObject, the mouse-only panel, no `ES3.Save`, no `vc.input.*` writes, BepInEx config as the only persistence, the GUID.

## Changes in 0.4.0-alpha

**Steering settings rework: visual curve editors + speed-sensing center return**

1. **Two editable curve graphs replace the opaque steering sliders.** "Lock at speed" (how much steering you keep as speed rises; left edge = stopped, right edge = 180 km/h+) and "Return to center" (how fast the wheel straightens after you let go) are now visual graphs: **click to add a point, drag to move, double-click to remove** (2–8 points, piecewise-linear). The curves are drawn live and apply immediately, like the sliders. The old "Steering at speed" scale slider and "Center return" slider are gone — the curves replace them. "Use the vehicle's own curve" (lock) and "Use the vehicle's input curve" (linearity) toggles remain, and the grip section got clearer labels ("Front slip clamp", "Slip window").
2. **Speed-dependent center return — the Euro Truck preset now holds the wheels where you leave them when stopped and straightens them out as you drive** (its return curve ramps 0 at rest → 0.7 at top speed). A return curve that starts at 0 takes over low-speed steering (the mod runs its pipeline below 1.5 m/s, skipping the traction model), so "leave the wheels turned, press go" works: the truck drives off in that direction and eases straight. A flat return curve at 1 keeps vanilla's own low-speed behavior byte-identical — every non-Euro-Truck configuration feels exactly as in 0.3.0. True reverse always stays vanilla.
3. **New config keys, one-time migration.** `Steering.Custom` gains `UseVehicleCurve`, `LockCurve` (`"x:y;x:y"`, 2–8 points), `ReturnCurve`; `SpeedCurveScale` and `CenterReturnScale` are folded on load and removed: the old scale multiplies the BasedOn preset's lock curve (exact old behavior), the old center-return value becomes a flat return curve. Known caveat: a vehicle-curve user (BasedOn empty) with a scale ≠ 1 loses that scale — the old knob has no equivalent. Garbage hand-edited curve strings fall back to the BasedOn preset's curves.
4. **Presets keep their exact curves** (GTA, Euro Truck, Sim/Race, Drift — byte-identical keyframes, now editable). Forking a preset into Custom deep-copies its curves, so built-in presets can never be mutated by the editors.

**Not changed** (all §2 facts preserved): the steering prefix's math beyond the return/curve model (allocation-free; guards byte-identical for flat-1 return presets), the hidden-runner survival architecture, the InputBlocker recipe, one Graphic per GameObject, the mouse-only panel, no `ES3.Save`, no `vc.input.*` writes, BepInEx config as the only persistence, the GUID.

**Bug fixed before release (playtest)**: the curve-editor graph rect used a zero-height anchor band with a centred pivot, which inverts the rect in Unity — the graph spilled over the rows below it, rendering "oversized" and swallowing their clicks (sliders looked unchangeable). The graph now uses full-area anchors with insets (CurveEditor.cs); its raycast area is exactly the graph.

## Changes in 0.5.0-alpha

Audit of 0.4.0 against `gamecode/` and §2, with the playtest note on the curve editor checked first. Nine bugs fixed; nothing else reworked. Every fix has a regression test (`verify/`, section "0.5.0 regressions" plus 4 new prefix checks). **Negative control:** the new tests run against the untouched 0.4.0 plugin fail 14 logic checks (13 new + the one updated aero assertion) and 2 prefix checks; the other 16 new checks exercise new public API (`EditableCurve.SameAs`, the curve-editor layout constants and routing helpers) and cannot compile against 0.4.0. Suite: 238 tests (220 logic + 18 prefix), all passing.

**Not changed** (all §2 facts preserved): the hidden-runner survival architecture, the InputBlocker recipe, one Graphic per GameObject, the mouse-only panel, no `ES3.Save`, no `vc.input.*` writes, BepInEx config as the only persistence, the GUID, the steering prefix's target, allocation-free body and every guard for flat-return presets.

### Bugs fixed

**UI — curve editor (the playtest report)**
1. **The 0.4.0 one-line fix made the graph fill the whole row, so the rest of the row ended up underneath it.** Title, hint and readout were still anchored to the row's top and bottom halves, and the per-graph **Reset** button to its vertical centre: all under the opaque graph, which is a later sibling and a raycast target. The labels were invisible, Reset could not be clicked, and clicking where Reset should be added a curve point. Combined with the long wrapped hints, this matches the reported "oversized rows, values can't be changed". The row is now a header band (title + readout + Reset on one line, the hint wrapped below it) above the graph. The graph keeps the 0.4.0 full-area anchors with insets, now starting below the header (row 300 px, graph 190 px). The bands are public constants checked by the harness.
2. **The graph swallowed list scrolling.** uGUI sends a drag to the first `IDragHandler` up the hierarchy, which is the graph itself, so 0.4.0's "don't `Use()` the event" never reached the `ScrollRect`. Dragging over the graph's empty area did nothing. Drags that don't start on a handle are now forwarded to the parent `ScrollRect` (`OnBeginDrag/OnDrag/OnEndDrag`).
3. **Releasing a drag added a point.** uGUI still delivers `OnPointerClick` after a drag when the press and drag handler are the same object, with `eventData.dragging` still true. A scroll gesture over the graph therefore ended with a new point where the mouse was released. Clicks during or after a drag are ignored now.
4. **The graph was editable on a disabled tab.** `CanvasGroup.interactable` only gates `Selectable`s, never a custom Graphic's handlers. With Steering OFF (or Vanilla), clicking a graph forked the active preset into "Custom (…)" behind the dimmed UI. The editor now takes a `canEdit` predicate (`Steering ON && !Vanilla`). While it's false, drags scroll the list and clicks do nothing.

Routing decisions are pure static functions (`CurveEditor.RouteDrag/RouteClick`) so the harness can test them without a UI runtime. *(The mouse-only rule is unchanged: no navigation, no keyboard handling was added.)*

**Steering prefix (hold/return model from 0.4.0)**
5. **Counter-steering across center ran at the return rate.** "Unwinding" was `|target| < |angle|`, which is also true when the driver steers to the *other* side with less input than the current angle. With a hold curve (Euro Truck, 0 at rest), the wheels froze at rest until the opposite input exceeded the held angle. That is a long dead zone on a gamepad, and a hesitation while a keyboard ramps. At speed, counter-steer through center ran at 40–70%. The reduced rate now applies only when the target is on the same side as the wheel (or zero). Crossing center is driver input and uses the full rate. At return ×1 (every non-hold preset) the rate is unchanged.
6. **A hold curve ignored the vehicle's `returnToCenter = false`.** Vanilla's guard (deadzone input + `!returnToCenter` → keep the angle) ran only on the flat-return path, so on such a vehicle Euro Truck's return ramp straightened wheels the vehicle was built to hold. The guard now runs before the hold/flat split. On the flat path the order of the two guards is irrelevant (both return `true`), so flat-return behaviour is identical. README §7.1 ("prefix honors `returnToCenter`") holds again.

**Persistence**
7. **A dragged curve did not survive save/reload.** `TryMovePoint` clamped x *onto* a neighbour's x, a vertical step that `TryParse` deduplicates on load, so the reloaded curve lost a point and changed shape. A moved point now keeps a 2 × `MinXGap` (0.0002) gap to its neighbours. Neighbours already closer than that (a hand-edited file) keep x and move only y. This is the same rule that already makes adding a point onto an existing x fail.

**Aero (behaviour change)**
8. **Vehicles without aero got drag from "stock-drag" presets.** An onboarded module (the vehicle shipped none) used `default Cd × DragScale` whenever *any* factor differed from 1. Street ("less downforce, stock drag", drag ×1.0) gave every aero-less vehicle a full 0.35 Cd, Off-road ×1.05 gave 0.37, and a Custom drag *below* 1 added drag. The onboarded module can't simulate downforce (no point synthesis), so only drag matters. Its stock drag is none, so the factor now applies to the excess: `Cd = default × (DragScale − 1)`, onboarded and active only when that is positive, parked otherwise. This is continuous at ×1.0 and monotone. **Behaviour change:** on aero-less vehicles Race (×1.35) now adds 0.12 Cd instead of 0.47, and Sport 0.035 instead of 0.385. Vehicles that ship an `AerodynamicsModule` are unaffected.

**Restore completeness**
9. **Stale baselines.** Stock values were captured once, when the vehicle was first seen. If the game later changed a field while its category was OFF (the harness simulates FSM writes such as `maxPower`), switching the category ON scaled the stale value, and switching it OFF wrote the stale value back over the game's change. 0.2.0 fix 16 only covered categories that were never enabled. Now a category that goes from not-applied to applied re-reads its own fields on every tracked vehicle first (`VehicleTuner.RefreshBaselines`). This is safe because the fields then belong to the game, and categories never share a field. Nothing is re-read while a category is applied, so no compounding (tested). Onboarded aero modules keep their own inert baseline. Refreshing runs on a toggle, not per tick.

### Allocation
- The curve editor compared curves via two `Serialize()` strings on every panel refresh (1 Hz while open), on every refresher pass and on every mesh rebuild, i.e. every frame while dragging. `EditableCurve.SameAs` replaces it and allocates nothing. Hot paths (`Prefix`, apply/restore, ABS/TCS bodies) are unchanged and still allocation-free. The new `RefreshBaselines` allocates only for the drivetrain/aero re-capture, once per toggle.

### Config keys and migration
- **No new, renamed or removed keys** (GUID unchanged). A 0.4.0 config file loads as is, and so do 0.3.x/3.1 files (all earlier migrations unchanged and still covered).
- Curve strings written by 0.4.0 that already contain a near-coincident pair (gap < 0.0001) were already deduplicated on load by 0.4.0, so nothing changes for them. From now on the editor cannot create such pairs.

### Verification harness
- Stubs (real Unity 2020.3 signatures): `Component.GetComponentInParent<T>()`, and `ScrollRect` now implements `IBeginDragHandler/IDragHandler/IEndDragHandler` with the public virtual handlers.
- Logic tests: curve round-trip + `SameAs`, aero onboarding drag, baseline refresh (power/spring/brake torque), curve-editor header bands, drag/click routing; one 0.4.0 assertion updated to the fix-8 semantics (`onboarded module Cd = default × excess drag`).
- Prefix tests: opposite input at rest with a hold curve, same-side ease-off still holds, `returnToCenter = false` with a hold curve, flat-1 crossing unchanged.
- `run.sh` unchanged. Note that `run.sh` stops at the first failing stage (`set -e`), so the prefix suite only runs when the logic suite passes.

### Audited and left alone
Checked and not changed: brake normalisation (every NWH brake path goes through `AddBrakeTorque × coefficient`, verified against `Brakes.cs`/`WheelComponent.cs`), diff classification and the delegate-on-change rule, shift-point caps, ABS/TCS sign conventions and the handbrake guard, assists registration flags, every config migration (presence detection before Bind, one-time folds, Truck-sim rewrite, `_syncing` guard), InputBlocker/timeScale restore, the one-frame-late unblock, EventSystem find-or-create, menu-button injection. The curve readouts ("63 km/h · 40%", "N points") stay concatenated as documented in `docs/strings.md` §6.

## Changes in 0.6.0-alpha

Implements FEATURES.md (Alignment and Gearbox categories, live-driving panel with selective input blocking, docked/scalable/transparent panel, expert values and wider ranges, telemetry strip, preset copy/paste, small extras) on top of an audit of 0.5.0. Four of the spec's recipes would have crashed or broken the game as written; those are implemented differently and called out below under **Deviations from FEATURES.md**. Suite: **373 tests (355 logic + 18 prefix), all passing**. The steering-prefix suite and the prefix itself are unchanged.

### Playtest fixes (post-audit, before release)

1. **Steering stayed blocked in live mode.** The game's real steering action name is `Steering` (plus `ShiftInto1..8`/`ShiftIntoR1` for gears, `Change Camera`, `TrailerAttachDetach`), harvested from the blocker's discovery log and added to the driving whitelist. Throttle had worked; steering is now live too.
2. **Telemetry never showed.** `Vehicle.ActiveVehicle` stays empty in Apocalypter — the game never sets NWH's `isPlayerControllable`. The strip now reads the driven vehicle from the tuner's own records (most live FSM input, else fastest, else first tracked).
3. **Alignment "Height" (PosY) was inverted.** At a fixed resting spring length the body height is `ground + springLength − mountLocalY`, so the mount must move DOWN to raise the car. Positive PosY now = taller, as the presets intended (Off-road +5 raises, Stance −6 lowers).
4. **More freedom**: alignment camber ±30° (beyond the engine's ±16 setter clamp the excess continues as a transform roll), wheel position ±60 cm.
5. **Target selector (new)**: "Apply to" in the Panel tab — All vehicles / Last driven / Selected vehicle (picked from the tracked list by name). The tuner tracks per-vehicle applied state, so switching targets restores the old vehicle and tunes the new one exactly; OFF restores only what was applied. `[General] ApplyTarget` + `SelectedVehicle` config keys. Status lines now show the targeted count.
6. **Gearbox save self-heal**: a game save made while the gearbox was tuned bakes the extended gear list into the vehicle, which made the car undrivable (the game's shift logic fought the extra gears; the ultra-tall continuation gears barely moved it). On first sight of such a vehicle the tuner now repairs it: the stock gear count comes from the game's own `ShiftIntoN` input actions (with a geometric-continuation fallback), the tail is truncated, and the gear state is clamped back into range. The Gearbox tab notes that saving while tuned bakes changes into that save.

### Release prep (before the public release)

1. **Gearbox gated behind "coming soon".** The category is hidden behind a notice (`GearboxSettings.ComingSoon`) and applies nothing — the game's FSM shift logic owns automatic transmissions and any gearbox change there leaves the car stuck. The full logic stays implemented and harness-tested; it unlocks in 0.7.0 when the mod ships its own shift controller.
2. **Telemetry defaults OFF** (opt-in, like every category; `[Telemetry] Enabled = false`).
3. **Rebindable panel hotkey in the Settings tab** — click "Panel hotkey", press a key (Esc cancels; mouse buttons, digits and joystick keys are refused), applies immediately, persists to `[UI] ToggleKey` (default F7).
4. **"Panel" tab renamed to "Settings"** (panels are car parts).

### Bugs fixed (audit of 0.5.0)

1. **Writes into destroyed vehicles between scans.** Dead records were only purged by the 2 s scan, but `ApplyLive` runs on every slider tick. In that window a destroyed vehicle was still written to, and Aero could onboard a module into it. That throws inside NWH and aborts every later category in the same pass. `ApplyLive` and `RestoreAll` now purge dead records first (allocation-free).
2. **ABS cut a latched handbrake.** The 0.2.0 guard only checked `input.Handbrake` (as NWH's own `ABSModule` does). With `Brakes.HandbrakeType.Latching`, `handbrakeValue` stays applied after the input is released, so ABS cut the latched handbrake to 1%. The guard now also checks `brakes.handbrakeValue` (read-only).
3. **TCS cut power during gear shifts.** NWH's `TCSModule` stands down while `transmission.isShifting`, but the mod's delegate did not. With the clutch open, the slip is not engine-driven, and the cut left 1% power when the clutch re-engaged, so every upshift stumbled. The guard was added.
4. **An external config edit reverted unsaved panel edits.** A `SettingChanged` from a config manager (Apocasetter) pushed *all* entries to the runtime, overwriting every panel edit made since the last save (save happens on close). This was rare while the panel froze the game. With the live panel both UIs can be open at once. Now the runtime is mirrored into the entries first (no file write), the externally changed value is re-applied on top, and only then pushed (`ModConfig.OnEntryChanged`).
5. **Live-mode Submit re-fire** (new hazard, fixed before it shipped). A clicked panel button stays EventSystem-selected, and the input module fires it again on Submit (Enter/Space). With the game running, pressing Space as the handbrake would re-click e.g. Reset. The selection is cleared every frame while the panel is open (navigation is off, so selection has no other use).
6. **Narrow-row overlap** caught by the new layout test: in the stacked slider row the hint ran under the Reset button. Fixed (`PanelLayout.SliderRow`).

### Deviations from FEATURES.md (and why)

- **Gear-count re-shift: `Gear` setter, not `ShiftInto`.** The spec suggests `ShiftInto(min(gear, n), instant: true)`. In the real `TransmissionComponent` (`:436-453`), `ShiftInto` silently refuses during the post-shift ban (`instant` does not bypass it), while a shift is in flight, and at full damage. Any of those leaves `gearIndex` past the end of the shrunk list, and `CalculateTotalGearRatio` (`gears[gearIndex]`, unguarded) throws every physics tick. The tuner writes `transmission.Gear = n` directly (synchronous; `GearToIndex` only depends on the untouched reverse section). A shrink is **deferred while `isShifting`**: the in-flight coroutine sets `Gear = target` later (`:480`), and that target may be a gear the shrink removes. A restore during such a shift keeps placeholder copies of the stock top gear until the shift lands, then trims (`TrimPendingGearbox`). Harness-tested, including a stub `ShiftInto` that refuses exactly like NWH.
- **Input blocker layer B does not block the name-routed forks by class.** The spec keeps the class regex entries `GetButton`/`GetAxis`/`GetAxisKeyAxis` as "redundant-but-harmless". But those classes *are* the driving actions that call `InputController`, so skipping them by class kills throttle and steering. Layer B now routes `GetButton*`/`GetAxis`/`GetAxisKeyAxis` by their `buttonName`/`axisName` through layer A's whitelist. Every other matched class (`GetAxisOrig`, `MouseLook*`, `AnyKey`, `GetMouse*`, `GetKey*`, `Input*`, `Mouse*` …) stays blocked. This is still defence in depth: if Mono inlines a tiny `InputController` method into a caller and bypasses the layer-A patch, layer B already enforces the same whitelist.
- **`OnEnter` gating is conditional.** Blindly skipping a one-shot action's `OnEnter` skips its `Finish()`, so its FSM state never completes, even after the panel closes. Only actions that declare `everyFrame` are gated: `everyFrame = false` → skipped **and** `Finish()`ed (behaves as "no input"); `everyFrame = true` → skipped (its gated `OnUpdate` takes over). Actions without the flag run their `OnEnter` as in 0.5.0 (`InputBlocker.RouteOnEnter`).
- **Per-wheel caster/toe dropped.** The spec's field list and config keys have per-axle caster/toe only (`CasterF/R`, `ToeF/R`), so there is nowhere to store per-wheel values. Per-wheel mode covers camber and position. Caster/toe always go through `WheelGroup.CasterAngle/ToeAngle` (which re-apply themselves), so the "re-apply on rescan" hazard of direct euler writes does not arise.
- **Telemetry slip is shown ×90.** `WheelUAPI.LateralSlip` is degrees × 0.01111 × friction stiffness (`WheelController.cs:1010-1011`), not degrees. The strip shows `|slip| × 90` ("Slip 9.0°"), exact at stiffness 1 and approximate otherwise. The panel note says so.
- **Clutch engagement floor.** `engagementRPM = stock + offset`, but never below `min(stock, 1.1 × idleRPM)`. Below idle the clutch stays engaged while idling (NWH's own `VC_Validate` warning), so the car would drag in gear at a standstill. Race's −200 is unaffected on normal cars.
- **Alignment values are offsets** (degrees/cm added to each vehicle's own geometry), not absolute angles. Stock (all 0) must be "as shipped" on every vehicle, and one config serves all vehicles. The value box shows the offset and the second line shows "stock …" of the reference vehicle, as specified.
- **Freeze ON gates nothing on `OnEnter`.** Exact 0.5.0 behaviour: every matched class is skipped on its per-frame methods. Layer A keeps its whitelist, so a one-shot read of a non-driving name returns false/0 instead of the live key. That is only safer.
- **Nine books, not ten.** FEATURES.md says "all ten books" for the codec, but there are nine preset books (eight tuning categories + Steering). The Panel tab has none; Copy/Paste are disabled there.

### Features

- **Alignment** (`Settings/AlignmentPreset.cs`, `AlignmentSettings.cs`, `Runtime/VehicleTuner.Alignment.cs`, tab 8). Camber per wheel (`WheelUAPI.Camber`), caster/toe per axle (`WheelGroup.CasterAngle/ToeAngle`), position by moving the WheelController's own transform (`localPosition`; never the `Wheel` struct). Presets Stock/Street/Sport/Race/Off-road/Stance/Custom. PosX is **outward** (positive = wider track on both sides), so Off-road is +6 and Stance −4 (tucked). Hazards handled: a `CamberController` or a solid axle (`isSolid && 2 wheels && trackWidth ≠ 0`) owns camber, so those wheels are flagged and never written; closed `applyCasterAngle/applyToeAngle` gates are opened only while needed and restored; |x| ≥ 1 cm and the stock side are kept (crossing x = 0 flips NWH's side conventions); `wheelbase`/`trackWidth` go stale after moves (warned, not recomputed); euler Z preserved. Restore writes caster/toe, both gates, camber, position and euler back exactly. The reference vehicle (first tracked) drives the stock readouts; rows are disabled until one exists.
- **Gearbox** (`Settings/GearboxPreset.cs`, `GearboxSettings.cs`, `Runtime/VehicleTuner.Gearbox.cs`, `Runtime/GearGraph.cs`, tab 9). Per-gear ratio factors (0.5–1.5) on each vehicle's own ratios. Gear count 1–12 or 0 = the vehicle's own: added gears continue geometrically (`r[n] = r[n−1]² / r[n−2]`, one-gear boxes ×0.75, floor 0.05), truncation drops the top gears, and reverse + neutral are never altered. Clutch "types" are emulated, because NWH2 has no clutch-type model (the panel says so): Stock/Street/Sport/Race scale `slipTorque` (≥ 1) and `engagementRange` (≥ 1) and offset `engagementRPM`. Transmission mode is Stock/Manual/Automatic. CVT and External boxes keep their gears and mode and only get the clutch. A gear list outside NWH's documented layout is never touched. No field is shared with Drivetrain. The gear graph shows bars (current) vs outlines (stock); click selects a gear and highlights its slider, a drag on a bar edits it, other drags scroll the list. It is inert while Gearbox is OFF.
- **Live-driving panel + two-layer blocker** (`Runtime/InputBlocker.cs`). By default the game keeps running with the panel open: driving input (whitelist `Throttle, Brakes, Handbrake, Clutch, Horn, Headlight, ShiftUp, ShiftDown, Cruise Control`; axes `input`, `Steering Keyboard`) reaches the game, everything else is blocked, and `Time.timeScale` is never touched (opened from the pause menu, the game simply stays paused). Unknown names are blocked (fail-closed, and never reach `InputStorage`, which throws) and logged once each (max 64) so the in-game check can harvest missing driving names. `SetInputBlocked` / `SetFreeze` replace `Set`. Freeze ON restores the 0.5.0 behaviour exactly, including the modal dim and click-outside-close, and the one-frame-late unfreeze that restores a pause-menu 0. Live mode has no click-catcher: only the window raycasts. **Accepted risk:** opened from the pause menu, its buttons stay clickable beside the panel. All routing is pure static functions (`RouteControllerAction`, `RouteFsmAction`, `RouteOnEnter`, `RouteInstance`).
- **Docked panel, width, scale, transparency** (`Runtime/PanelLayout.cs`, Panel tab 10). The window is docked right at full height, `PanelWidth` 400–800 (default 460). The canvas runs `ConstantPixelSize` with `scaleFactor = Screen.height/1080 × PanelScale`, which renders exactly like 0.5.0 at ×1. Width is capped to the canvas on narrow screens. `PanelAlpha` uses a CanvasGroup. Every fixed-pixel size is now a pure function of the width and is re-applied live by `Relayout(width)`, with no rebuild and no blocker state change. Wide (≥ 600 px content) keeps 0.5.0's single-line row geometry; narrow stacks the row (title + Reset / hint / slider + value). The curve editors and gear graph shrink their readout and grow their hint band. Tabs are a two-row 5+5 strip.
- **Expert values + wider ranges.** Brake-torque N·m readout. Absolute readouts are blank (not "0 N") until a vehicle supplies a baseline. Widened: suspension factors 0.25–3 (`SuspFactorMin/Max`), power 0.25–3.5, engine braking and boost 0.25–3, final drive 0.5–2, grip 0.1–3, brake torque 0.25–3. Not widened: shift RPMs (lock-up guard), diff stiffness (wind-up), brake actuation (split into its own `ActuationMin/Max`, unchanged).
- **Telemetry strip** (`Runtime/TelemetryStrip.cs`): speed, RPM, gear (`GearName`), front slip of the active vehicle, at 4 Hz. It has its own canvas (sorting order 31000) on the hidden runner, no GraphicRaycaster, and every Graphic has `raycastTarget = false`. It hides while the panel is open, when disabled, and when there is no active vehicle. Its size and corner are in the Panel tab.
- **Preset copy/paste** (`Settings/PresetCodec.cs`): `AVT1|Category|Name|BasedOn=…|Key=Value|…`, keys sorted, invariant round-trip floats, curves as their own `x:y;x:y` text. Import always lands in Custom; built-ins are never overwritten. Unknown keys and unreadable values are skipped and counted, values are clamped to `Limits`, and missing keys keep Custom's values. A wrong tag, the wrong tab or malformed text is rejected with a reason. The clipboard (`GUIUtility.systemCopyBuffer`) is wrapped in try/catch.
- **Small extras:** digit hotkeys 1…0 switch tabs while open; footer "Turn everything off" (two-click arm, all nine categories off + reapply); the panel reopens on the last tab (`[UI] LastTab`).

### Config keys and migration

- **Added** (nothing renamed, removed or default-changed): `[Alignment] Enabled, Preset, PerWheel` · `[Alignment.Custom] BasedOn, CamberFL/FR/RL/RR, CasterFront/Rear, ToeFront/Rear, PosX|PosY|PosZ × FL/FR/RL/RR` · `[Gearbox] Enabled, Preset` · `[Gearbox.Custom] BasedOn, GearCount (int 0–12), Gear1Scale…Gear12Scale, ClutchGripScale, ClutchRangeScale, ClutchRpmOffset, TransmissionMode` · `[UI] FreezeWhileOpen, PanelScale, PanelWidth, PanelAlpha, LastTab (int 0–9)` · `[Telemetry] Enabled, Scale, Position`.
- **No migrations.** A 0.5.0 cfg loads as is; every old key and value is kept (harness test with a representative 0.5.0 file). The widened ranges never invalidate a stored value. New string enums (`TransmissionMode`, `Position`) are name-only parsed with a fallback (Stock / BottomLeft), like `ParseDiffMode`.
- **Deliberate behaviour-default changes:** the panel opens **live** (`FreezeWhileOpen = false`), **docked right at 460 px**, and the **telemetry strip is on** (`Telemetry.Enabled = true`, see §8). Set `[UI] FreezeWhileOpen = true` / `PanelWidth = 800` to get the 0.5.0 panel back.

### Allocation

Hot paths stay allocation-free: the steering prefix (unchanged), apply/restore of every category including Alignment and Gearbox, and the ABS/TCS bodies. `WriteGears` allocates only when the gear list grows past its capacity (a count change). Capture/refresh of Gearbox copies the gear list (once per toggle). The blocker's layer-B prefix reads an `FsmString` field through a cached `FieldInfo`, only while the panel is open. Telemetry formats strings at 4 Hz. Relayout runs on width/scale changes only.

### Not changed

All §2 facts are preserved. The hidden-runner survival architecture is unchanged; the telemetry strip lives on the same runner. The PlayMaker blocking recipe is unchanged in Freeze mode and extended (not replaced) in live mode, as described above. One Graphic per GameObject. Mouse-only panel (the digit hotkeys are tab shortcuts, not widget navigation). No `ES3.Save` and no `vc.input.*` writes (ABS reads `brakes.handbrakeValue`). BepInEx config is the only persistence; the GUID is unchanged. The steering prefix is unchanged byte for byte.

### Verification harness

- **Stubs** (real signatures from `gamecode/`): `WheelUAPI.Camber` (±16 clamp), `LateralSlip`, `SpringLength`; `WheelGroup` caster/toe setters, gates, `isSolid`, `ApplyGeometryValues()` and `Update()` verbatim; `PowertrainComponent` as the base of Engine/Clutch/Transmission with `OutputRPM`; `ClutchComponent`; `TransmissionComponent` with `gears`, `Gear`, `GearName`, a `ShiftInto` that refuses like NWH (ban/in-flight/damage), a test hook that keeps a shift in flight, and `SimulateForwardStep()`, which throws where `CalculateTotalGearRatio` would; `Brakes.handbrakeValue`; `Vehicle.ActiveVehicle`; `CamberController`; `InputController`/`InputStorage` (throwing lookups); `FsmString`, `FsmStateAction.OnEnter/Finish`, and fork actions `GetButton`, `GetAxis`, `GetAxisOrig`, `AnyKey`, `MouseLook`, `GetKeyDown`; Unity `Screen`, `GUIUtility.systemCopyBuffer`, `CanvasScaler.scaleFactor`, `Slider.wholeNumbers`, `KeyCode.Alpha*`, `Key.Digit*`, and Unity's fake null (`Object.TestDestroyed`). `WheelUAPI.Camber/LateralSlip` are `virtual` in the stub (abstract in the game) so the unchanged prefix suite's `FakeWheel` still compiles.
- **New logic tests (135):** audit regressions; alignment (roles, camber/caster/toe incl. L/R toe mirroring, Z preserved, outward PosX, x = 0 clamp, gates, solid axle + CamberController skip, rescan re-apply, OFF→ON baseline refresh, exact restore); gearbox (layout, continuation, add/truncate, ban-proof re-shift, deferred shrink under an in-flight shift, mid-shift restore + trim, clutch types and clamps, mode, CVT refusal, no Drivetrain field touched); blocker routing (whitelist, fork routing, OnEnter decisions, live instances, freeze save/restore incl. pause-menu 0); codec (byte-identical round trip for all nine books, header, garbage, clamping, unknown/missing keys, BasedOn resolution, status strings); config (defaults, Limits ↔ `AcceptableValueRange` for widened/unwidened/new keys, 0.5.0 file loads as is, out-of-range clamping, name-only parsing, round trip); layout at 400/460/800 (no overlaps across slider/option/master rows, tabs, curve editors, gear graph; wide = 0.5.0 geometry exactly; effective width/scale factor); telemetry formatting/sampling; two-click arm; all-off.
- **Negative control** (against the untouched 0.5.0 plugin, compiled against the new stubs): the 9 new checks that compile against 0.5.0 give 5 failures (the four audit bugs: destroyed-vehicle writes ×2, latched-handbrake ABS, TCS mid-shift, external edit reverting panel edits) and 4 passes (their positive controls). The other 126 new checks exercise 0.6.0 API and do not compile against 0.5.0. `run.sh` is unchanged.
- **gamecode/ gap:** `PowertrainComponent` (base of Engine/Clutch/Transmission/Wheel/Differential, declares `OutputRPM`) is referenced through `ClutchComponent.cs:10/95/108` but its file is not in the bundle. Please copy `PowertrainComponent.cs` from the decompiled tree into `gamecode/`. The stub mirrors only the members the mod uses.

## Changes in 0.7.4-alpha

- **Reset buttons fixed** (user report): the 0.7.1 tab reorg left the per-tab Reset actions in the old ten-tab order, so e.g. the Wheels tab reset the aero. The eight entries now match the new tabs — the merged tabs reset BOTH of their categories (Wheels = alignment + grip, Drivetrain = drivetrain + gearbox).
- **Telemetry pins replaced with a readout list** (user request: pins were useless). The strip now shows a chosen list of vehicle readouts instead of pinned slider values: **Speed, Engine RPM, Gear, Front slip, Rear slip, Lateral G, Longitudinal G, Steering angle, Throttle, Brakes** (up to 8, six per strip row, second row beyond). Pick them in Settings → "Strip contents"; `[Telemetry] Cells` config key (';'-separated names, unknown dropped). The `[Telemetry] Pins` key is retired (an old cfg line is ignored). The new sample fields (g-forces, per-axle slip, wheel angle, pedals) come from the existing pick + records — no new writes to the vehicle.
- Suite: **610 tests (592 logic + 18 prefix), all passing.**

## Changes in 0.7.3-alpha

- **12-gear launch fix** (user report: "almost impossible to accelerate, goes up in gears like launching from gear 8"). Root cause found in the real NWH source: `TransmissionComponent.ForwardStep` re-assigns its **own** shift delegate whenever `transmissionType` changes, so the game's auto-gearbox setting (its `CheckTag` FSM flips the type) silently kicked the mod's ShiftController off the tuned 12-gear box for up to 2 s — and NWH's own automatic with **gear-skipping** then landed the box in a tall gear (the harness reproduces exactly gear 8 from a launch at 3800 rpm). New `ShiftDelegateGuard`: a Harmony postfix on `AssignShiftDelegate` re-installs the controller in the same tick, so the window is zero. Harness: the stub now models NWH's skipping branch; the launch test runs with and without the guard (614 tests green).

## Changes in 0.7.2-alpha

- **Telemetry pins render fix** (user report: the pin row overlapped the main strip at any corner). Every strip cell is now anchored to the strip's own corner with a pure corner mapping (`CellAnchoredPosition`), so the fixed row hugs the screen corner and pin rows extend inward — they cannot overlap by construction. Harness adds a corner×pin-count sweep (611 tests green).

## Changes in 0.7.1-alpha

The panel is reorganized around the car, not the mod's internals (user request). Ten tabs become eight:

| Tab | Content |
|---|---|
| Steering | steering (unchanged) |
| Suspension | suspension (unchanged) |
| **Wheels** | wheel alignment **+ tire grip** (two sections, one tab) |
| **Drivetrain** | engine/diffs/custom layout **+ gearbox & shifting** (two sections, one tab) |
| Brakes | brakes (unchanged) |
| Assists | ABS/TCS (unchanged) |
| Aero | aero (unchanged) |
| Settings | panel + telemetry (unchanged) |

- Digit hotkeys: 1–8 (0 still jumps to the last tab). Copy/paste is disabled on the two merged tabs (it needs exactly one preset book; each section's presets work normally).
- **6x6 template removed** from the drivetrain layout buttons (the game has no 6x6 vehicles) and from the config description examples. The layout parser still accepts multi-axle text, so old configs with a 6x6 layout keep loading and applying.
- **One config behavior change:** a stored `[UI] LastTab` of 8 or 9 (the old Gearbox/Settings tabs) clamps to the last tab on load — the harness fixture proves the other 132 keys of a real 0.6.4 file still survive load + save byte-identically.

Suite: **610 tests (592 logic + 18 prefix), all passing.**

## Changes in 0.7.0-alpha

Implements FEATURES.md 0.7.0 (§1 the mod-owned gearbox subsystem; the §2 remainder; §3–§5) on top of an audit of 0.6.4. Re-checked §10 (crash hardening) and §11 (telemetry). Suite: **610 tests (592 logic + 18 prefix), all passing.** I ran it on .NET SDK 8.0.131 (Linux; `run.sh` is unchanged and still runs from Git Bash). The 0.6.4 baseline was 504 (486 + 18). The steering prefix and its 18-test suite are byte-identical. Every new fix has a negative control (table below).

Several spec recipes are implemented differently, because the decoded game data or the NWH source shows the spec's version would not work as intended. Each one is called out under **Deviations from FEATURES.md**.

### §1 Mod-owned gearbox: `ShiftController` (new: `Runtime/ShiftController.cs`)

**Where the game's shifting really lives** (`docs/fsm-template-dump.md` + `gamecode/TransmissionComponent.cs`):

- The game's FSMs never write `transmission.Gear`. They write *requests* through PlayMaker `SetProperty`: `input.ShiftInto` (R = −1, N = 0, 1..5), `input.ShiftUp` and `input.ShiftDown`.
- NWH's `TransmissionComponent.ForwardStep` then calls `shiftDelegate(vc)` every physics tick (`ManualShift` / `AutomaticShift` / `CVTShift`, `:403-420`), followed by `input.ResetShiftFlags()`. That delegate is the only code that *applies* a shift. NWH documents it as the extension point for custom shifting (`:93`).

New load-bearing fact §2.15.

**How the controller works.**

- While Gearbox is ON, each targeted vehicle's transmission gets the vehicle's own `ShiftController` delegate (allocated once per vehicle, like the ABS/TCS delegates). The delegate found there is captured.
- The game's FSM request writes still happen. The controller **reads** them (read-only, README §2.2) and NWH's own application is replaced.
- OFF puts the captured delegate instance back: one field write, so restore is byte-for-byte. The harness checks it with `ReferenceEquals`.
- `transmissionType` is **never written any more**. Writing it makes NWH re-assign its own delegate on the next tick (`:405`), so 0.7.0's mode setting only selects the controller's logic (Stock = follow the vehicle's own type, Manual, Automatic).

**Automatic logic.** It uses the per-gear shift points from the live ratio list and decides on NWH's no-slip `ReferenceShiftRPM`.

- **Upshift.** Starts at the vehicle's upshift RPM (Drivetrain-scaled, live) × `ShiftUpFactor` × kickdown, never below 1.25 × idle. It is then raised until the RPM the shift lands on clears the next gear's downshift floor (1.1 × idle) with a 90 % hysteresis margin. It is capped at 97 % of the rev limiter. If even the cap cannot land the next gear above that floor, the controller does not upshift at all.
- **Downshift.** The vehicle's downshift RPM × `ShiftDownFactor` × kickdown, capped at 90 % of the RPM an upshift into this gear lands on, and floored at 1.1 × idle. Upshift and downshift points can therefore never chase each other: the harness checks "lands above down, below up" over a grid of 525 ratio-step × factor × kickdown combinations.
- **Kickdown.** Above 80 % throttle both points rise by 15 % × `KickdownScale`, so the car holds gears longer and downshifts sooner.
- **Creep hold.** Below 2 m/s it holds 1st, dropping straight to 1st from a higher gear.
- **Spacing.** At least 0.6 s between the controller's own shifts. Manual-type boxes have no NWH post-shift ban.
- **Drive / neutral / reverse.** Drive→neutral follows NWH's rules, including the game's `RequireShiftInput` variant. Neutral and reverse stay with the vehicle's own NWH automatic delegate. A Manual-type car run in Automatic mode uses a pure mirror of NWH's "Auto" DNR rules instead.
- **Shifting itself** goes through `ShiftInto`, so it honours `shiftDuration`, the clutch's shift behaviour and the post-shift ban.

**Manual logic.**

- On a Manual-type vehicle (mode Stock or Manual), the vehicle's own `ManualShift` runs, which is exactly the game's behaviour, H-shifter hold included. Its `ShiftInto` bounds check already respects the tuned count.
- Manual mode on an Automatic-type vehicle uses the pure `ManualTarget` mapping instead: ShiftUp, ShiftDown or ShiftInto, clamped to `[−reverse, forward]`. ShiftUp reaches gears the game's number keys don't have.

**Robustness.**

- **The game changes the type.** If the game changes `transmissionType` while we own the box (its `CheckTag` FSM writes it), NWH re-assigns its delegate. The next 2 s pass detects the foreign delegate, takes it and the new type as the game's intent, and hooks again. OFF afterwards leaves the game's new type and NWH's delegate for it.
- **Stale controller.** A `ShiftController` delegate left behind by a runner that died without restoring is never captured as "stock": its own captured stock is used instead.
- **Faults.** A fault inside the controller is caught inside NWH's `ForwardStep`, logged once, and shifting falls back to the vehicle's own delegate. It never throws every tick.
- **Allocation.** The per-tick path is allocation-free (harness: 0 bytes over 500 ticks).

**When the controller hooks.** Only when the preset changes shifting: the gears differ, the mode is forced, or a shift knob is not 1 (`GearboxPreset.NeedsShiftController`). Stock and the clutch-only presets (Comfort / Sport / Race) keep NWH's own shifting, variable shift points included, so they are exactly as shipped apart from the clutch.

**Unlock.** `GearboxSettings.ComingSoon` is removed (code and tests), and so is the `AnyGearboxSkipped` skip and its panel note. Automatics are tunable. CVT and External boxes are still never hooked (clutch only, as before).

**On the 0.6.x "stuck automatic" root cause.** I could not prove it from the code alone. The harness does reproduce the most likely mechanism: on a wide ratio step, NWH's raw automatic shifts at a fixed RPM with no regard for the next gear. It hunts 1⇄2 on every tick (100 shifts in 100 ticks in the test), and every shift opens the clutch for `shiftDuration`, which matches "engine revs, wheels don't move". Under the controller the same box holds 1st until the 2nd-gear landing is drivable, then shifts once. README §10 item 38 is the decisive check.

**Gear display.** The game's HUD shows its own gear variable. A short lag is cosmetic and documented in the Gearbox status line. There is no HUD patching (it is a scene FSM, with no code to patch).

### §2 Drivetrain: centre diff, torque split, layout UI

- **Centre-diff mode** (Stock/Open/Locked/LSD) in the panel and in the config (`DiffCenterMode`). It applies to centre/transfer diffs, as classified by `ClassifyDiff` from their outputs. Axle diffs keep their own modes and External diffs are never touched.
- **Centre-diff bias slider:** now enabled only when the car you drive has a centre diff.
- **Per-axle torque-split readout.** Example: "Torque split of the car you drive (nominal): front 40% · rear 60%". It walks the **live** wiring from the gearbox, so a custom layout shows its own split. Open diffs split by bias (A gets 1 − biasAB); Locked and LSD diffs count as nominally 50/50. A one-axle vehicle shows "drives one axle. A custom layout below can make it AWD" (the spec's "disabled slider" note is obsolete since 0.6.2's live rewiring).
- **Layout UI (new "Drivetrain layout" section).**
  - A "Custom layout" switch.
  - Four template buttons: RWD, FWD, AWD (the default text), 4x4 locked. The active one highlights (no 6x6: the game has no 6x6 vehicles; the parser still accepts multi-axle text from older configs).
  - "Copy this vehicle's layout" puts the stock layout text of the car you drive on the clipboard. "Paste layout" parses before accepting and reports the parser's message if the text is invalid.
  - A status line: active / ignored with the reason / "needs Drivetrain on" / "this vehicle keeps its own drivetrain: why".
  - Text entry stays in the config or Apocasetter: the panel is mouse-only.

### §3 Truck gearbox preset

- **Values:** `Truck` has 12 gears; 1st +20 %, 2–6 +10 %, 7–10 stock, 11–12 −10 / −15 %; clutch × 1.1 capacity / × 1.15 range; `ShiftUpFactor` 0.9; mode Stock.
- **Grid:** the preset grid is now 6 buttons (3 + 3).
- **Spread (deviation):** Truck spreads its 12 gears over the vehicle's own 1st-to-top range (new `SpreadRatios`, see Deviations) instead of continuing the ratio progression.
- **Clutch row:** the clutch-type row shows "Custom" for Truck, because its clutch values match none of the four clutch types.

### §4 Telemetry pins

- **The button:** every category slider has a 26 × 26 `Pin` button left of its Reset.
- **Keys:** a pin key is `Category.ConfigKey`, e.g. `Steering.RateMultiplier` or `Suspension.SpringFront`. Those are the `[Category.Custom]` config key names, so the scheme is stable and needs no new table. `PresetCodec` owns the field list and gained a numeric getter.
- **What a pin shows:** the value of the preset that the category's tab shows, in the slider's own units. It is sampled on the strip's existing 4 Hz tick.
- **Strip layout:** the strip grows one 26 px row per two pins below the four fixed cells (440 px wide). It stays click-through, with one Graphic per GameObject (label and value are separate Text objects, so a translation pack matches the label).
- **Labels:** the slider's own title once the panel has been built, otherwise the key in words.
- **Limits and persistence:** at most 12 pins. Pinning switches the strip on. `[Telemetry] Pins` is `;`-separated; unknown keys, duplicates and anything past 12 are dropped at load, in the runtime and in the file.
- **Not pinnable:** the Settings tab's own sliders (panel size/width/alpha) — they are not tuning values.

### §5 Tighter UI ("bigger text, smaller boxes")

- **Constants:** every size is a `PanelLayout` constant or pure function.
- **Block sizes:** rows 58 → 50 (stacked 100 → 86), preset buttons 44 → 40, master row 76 → 66 (narrow 92 → 80, or 96 below 400 px of content), section titles 38 → 32, footer 124 → 110.
- **Fonts:** titles 17 → 19, hints 13 → 14, option hints 14 → 15, master 21/15 → 23/17, section titles 14 → 16, values 17 → 19, notes +1.
- **Font fitting (new):** `PanelLayout` has Arial / Arial Bold advance-width tables (`TextWidth`, `FitFont`). Slider titles and hints, switch-row titles, preset buttons and tab labels shrink to fit their band, but never below 11 px (tabs 10 px at the 300 px minimum). uGUI text here overflows rather than clipping, so a label wider than its band draws over the pin, Reset or slider. Two measured cases:
  - With the spec's flat 15 px tabs, "Suspension" (bold, 79 px at 14 px) would overflow its 82 px tab at the default 460 px window.
  - At the 300 px minimum width, 0.6.x drew it 68 px wide in a 50 px tab.
- **Tab rows:** under 360 px of window the tab strip uses 3 rows of 4. It keeps 2 rows of 5 above that.
- **Kept:** the 0.6.0 width-adaptive insets, the curve-editor and gear-graph header bands, and their layout assertions. Geometry is checked at 300 / 400 / 460 / 800 / 1000 px, with and without pins.

### Bugs fixed (audit of 0.6.4)

1. **Game-changed drivetrain and clutch values were overwritten and then mis-restored.**
   - **Evidence:** the decoded FSM data shows the game's `CheckTag` FSM writes `transmission.UpshiftRPM`, `DownshiftRPM`, `finalGearRatio` (and `transmissionType`). Apocalypter also swaps engines.
   - **Old behaviour:** with Drivetrain ON, the tuner re-applied stock × factor from the *first-sight* baseline every 2 s, so the game's change was undone within 2 s. OFF then restored the stale first-sight value, wiping the game's change for good. The clutch (Gearbox) had the same pattern.
   - **Fix:** the tuner now remembers what it last wrote (`Runtime/Drift.cs`). A live value that differs from that write was written by someone else and becomes the new stock: it is scaled from, and restored to. Allocation-free.
2. **"Reset panel settings" turned the telemetry strip ON at bottom-left** (the pre-release 0.6.0 defaults). The shipped defaults are OFF / ×1 / top-left. New `UiSettings.ResetTelemetry`.
3. **"Apply to: Selected vehicle" silently re-targeted another car.** Every panel refresh replaced a selection that was not currently tracked with the first tracked vehicle, even in "All" mode. Opening the panel before the selected car had spawned (after a save load) moved the tuning to another car and saved that choice. Now a non-empty selection is never replaced: the button shows "Name (not here)". Pure `TargetSettings.ResolveSelection`.
4. **A `PanelWidth` of 300–319 rendered 320 px wide.** `PanelLayout.MinWindowWidth` was 320 while the slider and config range go down to 300. It is now 300.
5. **Tab labels overflowed their tabs** at narrow widths (see §5).
6. **Telemetry note text was wrong.** It said the strip "hides while this panel is open"; it has stayed visible since 0.6.x by design. The class summary had the same error.
7. **"Last driven" targeting ran `FindDrivenVehicle` once per record per category.** That is a scan of every record which also advances input-liveness state, repeated on every slider tick: O(categories × vehicles²). It is now resolved once per pass (`BeginTargetPass`), so a pass can no longer see two different picks either. There is no harness negative control: it is performance only, and the targeting test covers behaviour.
8. **Digit tab hotkeys vs the game's shift keys** (the 0.6.3 risk note, README §10 item 36). Now that the mod's manual shifting answers `ShiftInto` requests, digits switch tabs only while the mouse is over the panel in live mode (Freeze mode: always). This changes a hotkey gate, not widget navigation: the panel is still mouse-only.

### Deviations from FEATURES.md (and why)

- **No Harmony patch on PlayMaker `SetProperty`.** The spec's suppression target (`Gear` / `GearShift`) never occurs in the game's FSMs (dump: zero writes). Suppressing the real writes (`input.Shift*`) and then re-reading the global shift buttons would lose the per-vehicle routing that the FSM write carries, and the H-shifter hold semantics. It would also put a prefix on every PlayMaker property write in the game, every frame. Replacing NWH's shift delegate gives the same result ("the request reaches the mod, the game's application does not") at the exact place NWH applies shifts, restores with one field write, and needs no new patch. The controller runs inside NWH's physics tick, not on the runner's 2 s tick.
- **Shift-point formula.** The spec's `upRpm[i] = clamp(stockUpshiftRpm × ratio[i+1]/ratio[i] × factor, …)` *lowers* the upshift point when the gap to the next gear is large (ratio < 1). That is backwards: a wide step needs a *later* upshift so the next gear lands above its downshift point. Otherwise you get exactly the hunting that stalls the car. The controller raises the point instead (see above). The negative control (the landing raise removed) fails 3 checks, including the grid's no-hunting property.
- **Kickdown raises the shift points.** The spec said lower the upshift point by ~15 % at >0.8 throttle, which would make full throttle shift *earlier*. Real kickdown holds gears longer and downshifts. Same 15 %, scaled by `KickdownScale`.
- **Reference RPM.** Decisions use NWH's `ReferenceShiftRPM` (no-slip, from wheel speed), as NWH's own automatic does, rather than engine `OutputRPM`. During a launch the engine sits at the clutch engagement RPM while the clutch slips, and wheelspin inflates it: either would trigger false upshifts.
- **`ShiftInto`, not `Gear =`**, for the controller's shifts. It honours `shiftDuration` and the clutch's shift curve. Instant gear writes are still used where they must be: the resize guard, `ClampGear`.
- **Truck ratios are spread, not continued (new `SpreadRatios`).** Continuing a typical 5-speed (3.274 … 0.817) to 12 gears runs down to 0.11 by the 12th, a 29:1 overall spread (harness control check). That is the 0.6.0 playtest's "ultra-tall continuation gears barely moved it". With `SpreadRatios` the gears fill the vehicle's own 1st-to-top range with progressive spacing, `r_k = r1 × (rTop / r1)^((k / (n − 1))^0.85)`. With the Truck factors that gives 5.7:1 over 12 strictly falling gears. The spacing is deliberately *not* geometric: a geometric list baked into a save would look like the 0.6.0 continuation to the save self-heal, which would wrongly truncate it (harness check). Custom presets keep the 0.6.0 continuation unless they switch spread on.
- **Mode no longer writes `transmissionType`** (see §1). The 0.6.0 test "mode Manual applied" changed accordingly.

### Crash report (`docs/crash-2026-10-04.md`) and §11 — reviewed

- **Crash report:** reviewed again. Both hardening items have been in place since 0.6.3 and are unchanged. 0.7.0 adds no capture work: the shift delegate is hooked in the apply path for already-tracked vehicles only, so nothing new runs during the post-load spawn wave. A controller fault is caught inside NWH's `ForwardStep`, so a half-initialised vehicle cannot turn into a per-tick exception.
- **§11 telemetry strip:** I could not check it against a real screen here (no game, no Unity runtime). Its geometry is now pure and harness-checked: `CellBand` / `StripHeight`, every cell inside the strip, none overlapping, with up to 12 pins. The in-game check is README §10 item 43.

### Config keys and migration

- **Added (6):**
  - `[Drivetrain.Custom] DiffCenterMode` (string, `Stock`)
  - `[Gearbox.Custom] SpreadRatios` (bool, false)
  - `[Gearbox.Custom] ShiftUpFactor` (0.5–1.5, 1)
  - `[Gearbox.Custom] ShiftDownFactor` (0.5–1.5, 1)
  - `[Gearbox.Custom] KickdownScale` (0.5–2, 1)
  - `[Telemetry] Pins` (string, empty)
- **Nothing renamed, removed or default-changed.** Harness: all 133 keys of a *real* 0.6.4 file (generated with the 0.6.4 build, `verify/tests/fixtures/v064.cfg`) survive load + save with identical values, and exactly these six are added.
- **No migration needed.** The new keys default to neutral values.
- **Behaviour changes for existing files:**
  - `[Gearbox] Enabled = true` in an existing cfg now applies (the gate is gone). Before 0.7.0 it was inert.
  - `TransmissionMode = Manual/Automatic` now selects the controller's logic instead of writing the vehicle's type.
  - The preset-list comment for `[Gearbox] Preset` mentions Truck. BepInEx only rewrites descriptions, not values.

### Allocation

These stay allocation-free:

- the steering prefix (unchanged)
- every apply/restore, now including Gearbox with the Truck spread and the hook (harness: 50 `ApplyLive` passes with seven categories plus a layout, 0 bytes)
- the shift controller's per-tick body (0 bytes over 500 ticks)
- the drift checks
- the ABS/TCS bodies

Allocations happen only at these points:

- the telemetry pin cells, at 4 Hz and on a pin change
- the panel readouts (the torque split uses a reused buffer)
- one `ShiftController` per vehicle, on its first hook

### Not changed

All §2 facts are preserved (plus the new §2.15).

- **Kept as they were:** the hidden-runner survival architecture, the PlayMaker input-blocking recipe, one Graphic per GameObject, the mouse-only panel (the digit gate narrows a hotkey; there is still no widget navigation), and BepInEx config as the only persistence, with the GUID unchanged.
- **Game data stays read-only:** no `ES3.Save`. The mod never writes `vc.input.*`; the shift controller only reads `ShiftInto/ShiftUp/ShiftDown/InputSwapped*`, and NWH resets them.
- **Unchanged code:** the steering prefix (byte-identical) and `run.sh`.

### Verification harness

- **Stubs** mirror the new API from `gamecode/`:
  - `TransmissionComponent`: `Shift` delegate, `shiftDelegate`, `AutomaticTransmissionDNRShiftType`, `dnrSpeedThreshold`, `ReferenceShiftRPM`.
  - `ForwardStep`'s order: re-assign the delegate on a type change → ratio → `shiftDelegate(vc)` → `ResetShiftFlags`.
  - NWH's `ManualShift` and a reduced `AutomaticShift` (Auto DNR plus the sequential fixed-RPM branch), so the hunting control reproduces.
  - `VehicleInputHandler.ShiftUp/ShiftDown/ShiftInto/InputSwappedThrottle/Brakes/ResetShiftFlags/swapInputInReverse/Clutch`.
  - `RectTransformUtility.RectangleContainsScreenPoint` and `Input.mousePosition`.
- **New checks (106):**
  - shift math: points, landing raise, ceiling, floors, the 525-case no-hunting grid, creep, DNR, manual mapping, modes
  - the controller: hook, restore by instance, the Truck 0→60→0 m/s drive through all 12 gears with zero reversals, manual requests, type-change re-hook, stale controller, fault, CVT, Stock/clutch-only presets keep NWH, allocation
  - the Truck preset and spread
  - drift (drivetrain and clutch)
  - centre diff and torque split
  - layout templates
  - pins: keys, load, cap, values, units, labels, strip geometry, config
  - layout at five widths, with pins
  - a real 0.6.4 cfg
  - the audit fixes
- **Updated checks (5):**
  - the 0.6.0 gearbox test's automatic-skip and mode checks (both now assert the 0.7.0 behaviour)
  - the codec key count
  - the 0.5.0 wide-row geometry (now the 0.7.0 geometry)
  - the allocation test (now includes Gearbox)
- **Negative controls** (each fix reverted on its own; failures):

  | Fix reverted | Failures |
  |---|---|
  | shift controller hook | 11 |
  | landing raise in the shift points | 3 |
  | drift adoption | 5 |
  | telemetry reset defaults | 1 |
  | selection kept while not spawned | 1 |
  | digit gate | 1 |
  | 300 px minimum width | 1 |
  | tab-font fitting | 4 |
  | Truck spread | 2 |
  | pins normalised in the file | 1 |

  Against the untouched 0.6.4 plugin, the new checks do not compile (they exercise 0.7.0 API), which is why the controls revert one fix at a time.

### In-game checks to add (§10 items 38–46)

See §10.

## Changes in 0.6.4-alpha

Scope: the user's in-game report that the telemetry strip still showed a parked car's zeros until they steered ("only when you turn does it show"). Root cause found in the pick: **a parked car's input is frozen at its exit values.** The game's FSM writes `vc.input.*` every frame only while a vehicle is driven (README §2.2); on exit the writes stop, so the parked car keeps whatever it froze with — handbrake held, brakes last pressed, last steering angle. 0.6.3 scored raw input, so that frozen value out-scored a hands-off player's zeros and stole the pick (and "Apply to: Last driven" with it). Steering raised the player's input above the frozen residual, which is why the strip "worked" only while turning. Suite: **504 tests (486 logic + 18 prefix), all passing**; negative control below.

**Not changed** (all §2 facts preserved): the pick order (live input → last-driven memory → running engine → fastest → first), the strip, the crash hardening, the input blocker, persistence. **Config: one key added** — `[Telemetry] DebugPick` (false). A 0.6.3 cfg loads as is.

### Liveness-gated input pick

1. **Only input that changed recently counts as driving.** Each record now tracks its input sum (`InputPrev`, `InputSeen`, `InputLastChange`). A sample marks the input live when it changed by more than `InputChangeEpsilon` = 0.02 (or is the first sample) and it is above `InputDeadZone` = 0.05; an unchanged value stays live for `InputHoldSeconds` = 2 s after its last change, then falls silent. A parked car's frozen handbrake/brakes/steering therefore never beats a hands-off player; fresh input (driving, or a car switch) still takes the pick immediately. The gate is the pure `UpdateInputLiveness` (harness-tested), allocation-free (three floats on the existing record).
2. **First-sample grace:** a vehicle captured with frozen input gets one 2 s grace window (it is sampled before its story is known); captures happen during the post-load quiet window when nobody is driving, so this self-corrects.
3. **Diagnostic:** `[Telemetry] DebugPick = true` logs once per second, per tracked vehicle: name, input sum, LIVE/stale, speed, RPM — and the pick. The in-game check for a wrong telemetry car reads this from `BepInEx\LogOutput.log` (allocates only while on).

### Verification harness
- New tests (14): frozen handbrake on a parked car (live throttle beats it, hands-off keeps the player's car, the strip shows the player's car, car switch still works, the hold window expires), the first-sample grace, and the pure liveness gate (first sample, hold window, stale, re-change, dead zone).
- **Negative control** (the liveness gate reverted alone): 5 failures — exactly the user's symptom (the parked car's frozen input steals the pick).

## Changes in 0.6.3-alpha

Scope (agreed with the user for this round): the two open user-reported issues — the telemetry strip (FEATURES §11) and the save-load crash hardening (FEATURES §10, `docs/crash-2026-10-04.md`) — plus an audit pass. FEATURES §1 (shift controller), §2 remainder, §3–§5 are **not** in this round; `GearboxSettings.ComingSoon` stays. Suite: **490 tests (472 logic + 18 prefix), all passing — run** on .NET SDK 8.0.131; every new fix has a negative control (counts below).

**Not changed** (all §2 facts preserved): hidden-runner survival architecture, the InputBlocker recipe, one Graphic per GameObject, mouse-only panel, no `ES3.Save`, no `vc.input.*` writes (still read-only), BepInEx config as the only persistence, the GUID, the steering prefix (byte-identical). **No config keys added, renamed or removed** — a 0.6.2 cfg loads as is.

### Telemetry strip (FEATURES §11) — root cause was the vehicle pick, not the strip

User symptom: after changing/enabling a setting the strip shows "0 km/h / N / zeros" until they steer, with no AI traffic around. The strip's scale (`Screen.height/1080 × scale`, ConstantPixelSize), corner anchoring (anchor = pivot = corner, margin inward) and 4 × 110 px cells were checked and are correct; the strip faithfully showed the vehicle `FindDrivenVehicle()` returned — the wrong one.

1. **The driven-vehicle pick counted an idling engine as input.** 0.6.0's "running engine whisper" (+0.0004) sat *inside* the input score, above the last-driven memory — contrary to the documented order (CONTEXT.md: live input → last-driven → running engine → fastest → first). With hands off the keys (clicking a panel setting) every idling car tied, the **first tracked** idler won, and the tie also **overwrote the memory**, so the wrong car stuck until the player gave input again. Now a running engine is a fallback below the memory and only real input updates it. **Bigger consequence, same fix:** "Apply to: Last driven" uses the same pick, so a panel edit with hands off applied the tuning to a parked car and *restored* the player's (harness-tested).

### Crash hardening (FEATURES §10, `docs/crash-2026-10-04.md`)

Report reviewed: the crash was native, during the game's own post-load spawn wave, with every category OFF; nothing implicates the mod. The two requested insurance items are in:

2. **Spawn-wave quiet window.** `Plugin.OnSceneLoaded` calls `VehicleTuner.NotifySceneLoaded()`: no scans for `SpawnQuietSeconds` = 5 s (static, so a recreated runner honours it). After the window, a scan whose vehicle count jumped by more than `SpawnJump` = 2 since the previous scan defers capture by one scan, at most `MaxSpawnDeferrals` = 3 in a row (a big save or a convoy mod can't starve capture). A runner's first scan never defers (no previous count). Apply/restore of already-tracked vehicles is unaffected. Pure gates `ShouldSkipScan` / `ShouldDeferCapture`. Side effect: after loading a save the telemetry strip and tuning appear ~5–7 s later than before.
3. **Per-category exception guards.** 0.6.2 had **no** exception handling in the tuner (the crash report's "the gearbox capture diagnostic already does this" was not accurate). Now:
   - *Capture*: each category (brakes, drivetrain + layout, aero, gearbox, assists, TyreWear check) is captured in its own try/catch; a failing one is left null and logged once. A capture with a failed category is dropped and retried on the next scans (a half-initialised vehicle usually completes); after `MaxCaptureAttempts` = 3 it is kept with the categories that worked. A throw in the wheel/axle pass itself always retries.
   - *Apply/restore*: `TargetPass`/`RestorePass` guard each record per category. 0.6.2 let one throwing vehicle escape `ApplyLive`, which aborted that category for every later vehicle **and every later category** in the same pass. The applied flag is set before the apply, so a half-applied category is still restored on OFF.
   - *Baseline refresh*: per record and category.
   - `ApplyAero`/`RestoreAero`/`ApplyAssists`/`RestoreAssists` dereferenced their capture data unconditionally — null-checked now (a failed capture leaves it null).
   - Faults log once per (category, vehicle, exception type), capped at 256 lines; the try/catch costs nothing on the hot path (the 0-byte allocation test still passes).

### Audited, not changed
Reviewed: the panel manager's per-frame path (hotkeys, rebind capture, one-frame-late unblock, selection clearing), telemetry layout math, gearbox capture null-guards, the 0.6.2 layout code. **Risk to check in-game (not changed):** the digit tab-hotkeys (1…0, live mode) could collide with the game's `ShiftInto1..8` if those are bound to number keys — a manual-gear driver would switch panel tabs while shifting. If so, the fix is to only take digits while the pointer is over the panel.

### Verification harness
- New tests (23): driven pick (hands-off keeps the player's car, telemetry shows it, Last-driven edits land on it, running beats dead with no history); fault guards (capture throw → healthy vehicle tracked, retry succeeds, give-up keeps partial record, failed category skipped; apply throw → no escape, other vehicle and later categories still applied, full restore); spawn gate (pure gates, quiet window, one-scan jump deferral, deferral cap). Test doubles `ThrowingModuleManager` (virtual `Components`, no stub change) and `ThrowingWheel`.
- **Negative controls** (each fix reverted alone): driven pick → 3 failures; pass guards → 3; aero capture guard → 2; quiet window → 2; jump deferral → 2.
- Stubs and `run.sh` unchanged.

## Changes in 0.6.2-alpha

Scope of this pass: (1) verify the curve-editor fix from the playtest note, (2) a **custom drivetrain layout** — define in the config where the gearbox, transfer cases and differentials send torque, down to individual wheels (user request; config only, no panel UI yet). FEATURES.md (the 0.7.0 spec: own shift controller, Truck preset, telemetry pins, UI resize) is **not** implemented here; its §2 question is answered below. Suite: **467 tests (449 logic + 18 prefix), all passing — actually run** on .NET SDK 8.0.131 for this pass, with negative controls for every new hazard test (each one fails when its fix is reverted).

**Not changed** (all §2 facts preserved): hidden-runner survival architecture, the InputBlocker recipe, one Graphic per GameObject, mouse-only panel, no `ES3.Save`, no `vc.input.*` writes, BepInEx config as the only persistence, the GUID, the steering prefix (byte-identical).

### Curve editor (playtest note) — verified, plus one bug fixed

Verified against the code: the graph uses full-area anchors (0,0)-(1,1) with insets (16, 10, 16, `GraphTop`), so its rect is exactly the graph area below the header band and can never invert or spill into neighbouring rows; title/readout/hint labels have `raycastTarget = false`, the row background is not a raycast target, and Reset sits in the header band above the graph. `Relayout` is wired for both curve editors and the gear graph. The 0.5.0 fixes (header band, scroll forwarding, no click-after-drag, inert while OFF/Vanilla) are intact.

1. **Grabbing a curve point often scrolled the list instead of moving the point.** `OnBeginDrag` picked the handle at `e.position`, but uGUI calls `OnBeginDrag` only after the pointer has moved past the EventSystem drag threshold (10 px by default). With a 14 px pick radius, a normal grab-and-pull is already outside the radius at that moment, so the drag fell through to the ScrollRect: the point looked unchangeable. The drag now picks at `e.pressPosition` (clicks still use the click position). `GearGraph` had the same pattern (a quick downward pull from a short bar left the bar's hit column and scrolled) and got the same fix. The pick is a pure static (`CurveEditor.PickHandleAt`) with harness checks; the press-vs-current choice itself needs the in-game check (§10 item 29).
   - Cosmetic, not changed: at the narrowest widths a long drag readout ("180 km/h · 100%") can overflow leftward into the title (horizontal overflow is on by design so text never truncates).

### Custom drivetrain layout (new)

**Is live rewiring safe?** Yes — verified in `gamecode/`, this answers FEATURES.md §2's "if a safe live axle-attach mechanism exists". NWH steps the powertrain every physics tick by recursing through each component's `_output`/`_outputB` object references (`PowertrainComponent.ForwardStep/QueryAngularVelocity/QueryInertia`, `DifferentialComponent.cs:192-234`), and the public `Output`/`OutputB` setters change them immediately. Name hashes are only zero-checked while stepping and resolved once in `VC_Initialize`. New fact §2.14.

**Config** (`[Drivetrain.Layout]`, needs `[Drivetrain] Enabled`, follows the panel's Apply-to target):
```
Enabled = true
Layout = gearbox -> transfer; transfer: Open split=0.4 -> front, rear; front: Open -> FL, FR; rear: LSD -> RL, RR
```
- `gearbox -> X` — what the gearbox drives: a node, or a single wheel.
- `name: Type [key=value ...] -> A, B` — a differential / transfer case with two outputs. Type `Open`, `Locked`, `LSD` (`LimitedSlip`). Keys: `split` (0–1, share of torque to output A — Open diffs, and LSD in reverse, as NWH implements them), `stiffness` (0–1, Locked/LSD), `slip` (LSD slip torque N·m, 0–5000), `power`/`coast` (LSD ramps, 0–1). Defaults are NWH's (0.5 / 0.5 / 400 / 1 / 0.5).
- Outputs: node names, or wheels `FL FR RL RR` (first/last axle) or `A<n>L`/`A<n>R`/`A<n>` (axle n from the front; no side = a centre wheel). Axles are grouped like NWH does it (0.2 m in z); side by the wheel's vehicle-local x (±0.01 m).
- Must be a tree: every node reachable from the gearbox, every node and wheel fed once. Wheels not named are undriven. A driveshaft is just an edge, so "where the driveshafts go" is the `->` structure.
- **Each vehicle's own layout is logged on first sight**, ready to copy and edit, e.g. `Drivetrain of 'Duke(Clone)': 2 axles, wheels FL FR RL RR. Stock layout: gearbox -> Center_Differential_1; ...`. Applying logs `Drivetrain layout applied to '…': 3 nodes, driven wheels FL FR RL RR.`; a layout that doesn't fit a vehicle (missing axle, dual wheels, `RL`/`A2L` naming the same wheel on a 2-axle car) logs why and that vehicle keeps its own drivetrain; an invalid text logs the parse error once and nothing changes.
- Examples: RWD `gearbox -> rear; rear: LSD -> RL, RR` · part-time 4x4 `gearbox -> transfer; transfer: Locked -> front, rear; front: Open -> FL, FR; rear: Open -> RL, RR`

**How it applies** (`Runtime/VehicleTuner.Layout.cs`, `Settings/DrivetrainLayout.cs`): the layout's nodes are mod-owned `DifferentialComponent`s ("AVT <name>"), one set per vehicle, built when the layout text changes. The gearbox's output is pointed at the root and each node at its outputs; the vehicle's own diffs are **bypassed, never edited** (so the existing diff-mode/bias/stiffness code keeps working on them, and has no audible effect while a layout is active). Re-applying is idempotent and allocation-free. OFF (layout, category, target switch, runner disable/destroy) restores the captured references and hashes exactly.

**Hazards handled** (each with a harness test that fails without the fix):
- A fresh `DifferentialComponent` has **no split delegate** until `DifferentialType` is assigned; Open is also the field default, so the usual assign-on-change rule would leave it null and `ForwardStep` would throw every tick. Nodes are always assigned.
- `WheelComponent.ForwardStep` switches the wheel's `AutoSimulate` off (`:89`). A wheel that stops being driven would **never be simulated again** (no suspension or tyre forces). Released wheels get `AutoSimulate = true`, `MotorTorque = 0` and their own inertia back (`outputInertia − inputInertia`, captured, so the reflected drivetrain inertia doesn't stay on a free-rolling wheel).
- **Cycles and double-fed wheels** (infinite recursion → game crash; a wheel stepped twice per tick) are rejected by the parser's tree check, and per-vehicle aliases are re-checked at resolve time.
- **Saves:** the stock components keep their captured name hashes while a layout is applied (only object references move), and the nodes are never added to `powertrain.differentials`. If the game serialises powertrain fields, a save made while a layout is active therefore resolves to the stock wiring on load instead of a hash naming a node that no longer exists (which would be a null `_output` behind a non-zero hash → NullReferenceException every tick). *Whether Apocalypter serialises these fields is unverified; this is defensive.*

**Limits (documented, not bugs):** no gear ratio on nodes (no low-range transfer case — NWH diffs have no ratio; the final drive is the gearbox's); dual-wheel axles can't be addressed per wheel; one global layout (the Apply-to selector picks vehicles; a per-vehicle-name layout would be the next step); the game's own diff-lock FSMs, if any vehicle has one, act on the bypassed diffs while a layout is active; no panel UI (the user asked for config only — Apocasetter's Mods window already edits the two keys live).

### Bug fixed (audit)

2. **The apply path was not allocation-free.** Six `ApplyAll*` passes built a capturing lambda (`r => ApplyX(r, p)`) on every `ApplyLive`, i.e. every 2 s scan and every slider tick (88 bytes per category per pass), contradicting §7.2. `TargetPass<T>` now takes the preset as an argument with static method groups (compiler-cached); the instance `ApplyGearbox` delegate is cached in a field. New harness check measures `GC.GetAllocatedBytesForCurrentThread` over 50 passes with six categories and a layout on: 0 bytes (4400 with one lambda put back).

### Config keys and migration
- **Added:** `[Drivetrain.Layout] Enabled` (bool, default false), `Layout` (string, default = the AWD example above). Nothing renamed, removed or default-changed; a 0.6.0 cfg loads as is. The layout text round-trips verbatim (never rewritten). "Reset all" on the Drivetrain tab turns the layout off and keeps the text.

### Verification harness
- Stubs now mirror the real wiring and stepping: `PowertrainComponent` (`name`, `inertia`, `Input`/`Output` setters, name hashes, `ForwardStep`/`QueryInertia`/`QueryAngularVelocity`), `DifferentialComponent` (`OutputB` setter verbatim incl. NWH's self-assignment quirk, the three split functions, `AssignDifferentialDelegate` — a fresh stub diff throws in `ForwardStep` exactly like the real one), `WheelComponent.ForwardStep`, `WheelUAPI` `MotorTorque/Inertia/AutoSimulate/Step/AngularVelocity/Mass/Radius/CounterTorque` (virtual, same reason as `Camber`), `PointerEventData.pressPosition`.
- New tests (69): layout parser (valid forms, 23 rejected inputs with their messages, wheel tokens), runtime (rigs wired like NWH's auto-setup; one step below the gearbox counts per-wheel `Step()` calls and torque: RWD, AWD 40/60 split, single wheel, 6x6 1/2-1/4-1/4, stock-text round trip, hash hygiene, idempotence, unfit/alias/invalid layouts, layout OFF / category OFF / OnDisable / target-switch restores, wheel release), config (fresh defaults, verbatim round trip, cyclic hand edit), curve-editor pick, apply-path allocation. `run.sh` unchanged.
- `gamecode/PowertrainComponent.cs` is now in the bundle (0.6.0 noted it was missing); the stub follows it.

### Reviewed, not changed
- `docs/crash-2026-10-04.md` (native crash during a save load, mod inert): read; its two hardening items are part of the 0.7.0 spec and not done in this pass. The layout adds one more read-only capture step at first sight of a vehicle and no writes while the category is off.

## 1. What the mod does

Nine tuning categories (plus a Panel tab for the panel itself), each with: a master ON/OFF switch (all default OFF — every category is opt-in), a row of presets, and labelled sliders with live values, "changed" highlight, per-slider Reset and plain-language hints. Moving any slider while a built-in preset is active copies that preset into a "Custom (Base)" slot (BasedOn tracked) so presets are never mutated. Everything applies live; settings persist in a BepInEx config file; panel opens via F7 or a "Vehicle Tuning" button cloned into the game's menus, docked right; by default the game keeps running (driving input live), `[UI] FreezeWhileOpen` restores the old modal pause. A click-through telemetry strip shows speed/RPM/gear/front slip while driving, plus any slider values pinned to it (0.7.0).

| Category | Model | Applied via |
|---|---|---|
| Steering | Harmony prefix on `Steering.CalculateSteerAngles`; traction-edge clamp on the vanilla pipeline | patch (allocation-free) |
| Suspension | per-wheel spring/ride-height/damper × factor, per-axle ARB × factor | WheelUAPI properties + WheelGroup field |
| Aero | drag/downforce × factor on NWH's real `AerodynamicsModule` (onboarded when absent) | module fields |
| Brakes | torque ×, per-axle brake / handbrake factors ×, actuation × — balance-preserving (maxTorque raised by k, coefficients ÷ k, see Changes) | Brakes + WheelGroup fields |
| Grip | per-wheel longitudinal/lateral grip + stiffness × | WheelUAPI properties |
| Drivetrain | power/revLimiter/loss/boost/finalDrive/shift-RPMs (guarded)/shiftDuration ×, diff modes (Stock/Open/Locked/LSD) per axle resolved from the wheels each diff drives, centre-diff bias ×, diff stiffness × | Powertrain fields |
| Assists | ABS + TCS via NWH's public delegate hooks — no modules, no vehicle fields | `brakes.brakeTorqueModifiers` / `engine.powerModifiers` |
| Alignment | camber per wheel, caster/toe per axle, wheel position per axle or per wheel — **offsets** (deg/cm) on each vehicle's own geometry | `WheelUAPI.Camber`, `WheelGroup.CasterAngle/ToeAngle` (+ gates), wheel `transform.localPosition` |
| Gearbox | per-gear ratio ×, gear count (0 = own; added gears continue the ratio progression, or 0.7.0 spread over the stock range), clutch type (emulated: capacity/engagement), shifting by the mod's own controller (mode, shift points, kickdown) | `transmission.gears` (+ `Gear` re-shift guard), `clutch` fields, `transmission.shiftDelegate` (0.7.0; `transmissionType` is never written) |

All factors are **multipliers on each vehicle's captured stock values** (effective = stock × factor). Stock baselines are captured when a vehicle is first seen, re-read whenever a category is switched on (while it was off the fields were the game's), and restored exactly when a category is switched off. The suspension tab additionally shows computed **absolute readouts** (mean stock baseline × factor, e.g. "×1.40 / 42 000 N"), Brakes shows the torque in N·m, Gearbox the absolute ratio of each gear and Alignment the reference vehicle's stock angle/position.

## 2. Load-bearing game facts (verified against decompiled source + game data)

These drove several unusual design decisions; treat them as load-bearing when reworking.

1. **The game has no compiled game code.** `Assembly-CSharp` is 99% asset-store code (PlayMaker actions, InsaneSystems.InputManager, NWH, …). All game logic and UI are **PlayMaker FSMs serialized in scenes** inside `data.unity3d`. There is no pause-menu class, no MenuManager.
2. **Vehicle input is written by FSMs** via PlayMaker `SetProperty` reflection every frame (`VehicleController.input.*`). NWH input providers unused. **The mod must never write `vc.input.*`** — it is overwritten next frame. Assists act through `engine.powerModifiers` / `brakes.brakeTorqueModifiers` / `AddBrakeTorque` instead.
3. **The game destroys plugin-created GameObjects on scene load.** A PlayMaker scene-cleanup sweep disables/destroys unknown scene-root objects. Workaround (proven): host all runtime logic on a GameObject with `hideFlags = HideFlags.HideAndDontSave`, recreated on `SceneManager.sceneLoaded`. Harmony patches survive (they live in IL). **Preserve this architecture.**
4. **Game input is legacy `UnityEngine.Input`** (its InputManager uses `Input.GetKeyDown`/axes); `Keyboard.current` from Unity.InputSystem also exists at runtime. The mod reads hotkeys through both.
5. **Game settings use Easy Save 3** (`SaveSettings.es3`). `ES3.Load<T>(key, filePath, defaultValue)` is read-only; the game's `ES3SettingsMod` mutates the shared static `ES3Settings.defaultSettings.path`, so the mod always passes an explicit absolute path and **never calls `ES3.Save`**. Key `steeringspeed` (float, default 50) scales the keyboard steering ramp and can scale the mod's steering rate ("Follow game's steering speed").
6. **uGUI rule that bit us**: Unity allows only **one `Graphic` component per GameObject** (an `Image` background and a `Text` label cannot share a GameObject). Panel labels live on child objects with `raycastTarget = false`.
7. **Menu-button recipe** (Apocasetter, proven): every enabled canvas showing game menu buttons (named Settings/Credits/Tutorial/Codex/Quit/Quit_To_Menu/Exit/Options) gets a clone with PlayMaker FSMs stripped, relabeled "Vehicle Tuning", `onClick` rewired, pinned top-right, shown/hidden with the canvas. Re-scan every 0.25 s; inactive canvases are invisible to `GameObject.Find`.
8. **Input capture while the panel is open** (Apocasetter's InputBlocker; 0.6.0 keeps it as layer B and adds the name whitelist of §2.11 so driving input stays live unless Freeze is ON): Harmony-prefix all PlayMaker `FsmStateAction` subclasses whose name matches `^(GetAxis|GetButton|GetKey|GetMouse|MouseLook|MousePick|AnyKey|GetTouch|GetAxisKeyAxis|Input|Mouse)` — on `OnUpdate`/`OnFixedUpdate`/`OnLateUpdate` — returning false while the panel is open, plus `Time.timeScale = 0` (saved/restored, including a pause-menu 0). Cursor re-freed every frame in LateUpdate. Widgets deliberately have `Navigation.Mode.None` (mouse-only panel — a user decision).
9. **NWH live-settability** (all verified): `WheelUAPI` abstract settable properties (`SpringMaxForce`, `SpringMaxLength`, `DamperBumpRate`, `DamperReboundRate`, `LongitudinalFrictionGrip/LateralFrictionGrip/LongitudinalFrictionStiffness/LateralFrictionStiffness`) need no cast; `WheelGroup.brakeCoefficient/handbrakeCoefficient/antiRollBarForce` are public fields; `Brakes.maxTorque/actuationTime` public; engine/transmission/differential fields public and read every tick; `transmission.UpshiftRPM/DownshiftRPM` are settable properties; `DifferentialComponent.DifferentialType` setter re-assigns the split delegate live. **Hazards**: `vc.DiffFrontType`/`DiffRearType` getters index `differentials[0/1]` unguarded (use `powertrain.differentials[i]` with a Count guard — the tuner does); `GroundDetection` overwrites `FrictionPreset` + rolling resistance every ~0.1 s (it does NOT overwrite the grip multipliers the mod uses); a `TyreWear` component, if present on a prefab, rewrites the grip properties every frame (the tuner flags it and the panel warns); `AerodynamicsModule` may be absent on prefabs — the tuner onboards one via `vc.moduleManager.AddAndOnboardNewComponent(new AerodynamicsModule())` + `VC_Enable(false)` and disables (not removes) it on restore.
10. **ABS/TCS hooks**: `Brakes.brakeTorqueModifiers` (List<BrakeTorqueModifier>) and `EngineComponent.powerModifiers` (List<PowerModifier>) are the exact hooks NWH's own modules use. Sign conventions (from `ABSModule.cs`/`TCSModule.cs`): ABS triggers on `+LongitudinalSlip × sign(LocalForwardVelocity) > threshold`; TCS on `−LongitudinalSlip × sign(LocalForwardVelocity) > threshold` (spinning wheels have negative slip). The mod registers one delegate per vehicle, flag-guarded against duplicates, removed on disable. Like `ABSModule`, the mod's ABS stands down while `vc.input.Handbrake >= 0.1` (read only) — `Brakes` multiplies the modifier into the handbrake torque too.

11. **Input routing (0.6.0)**: the game's driving FSM actions are HutongGames forks that read through `InsaneSystems.InputManager.InputController` by NAME (`GetButton → GetKeyActionIsActive(buttonName)`, `GetAxis → GetAnyAxisActionValue(axisName)`); `InputStorage.GetKeyByName/GetAxisByName` THROW on unknown names (InputStorage.cs:62-72); the renamed `GetAxisOrig` reads Unity's `Input.GetAxis` directly ("Mouse X/Y", scroll); `KeyAxisAction.GetValue` ramps with `Time.deltaTime` (KeyAxisAction.cs:36-47), so live driving needs timeScale untouched.
12. **Gearbox (0.6.0)**: `gears = [reverse…, 0, forward…]`, counts recomputed every `ForwardStep`, `CalculateTotalGearRatio` indexes `gears[gearIndex]` unguarded; `ShiftInto` refuses during the post-shift ban / an in-flight shift / full damage (instant does not bypass the ban) — so the mod writes `Gear` directly and defers shrinks while `isShifting`; CVT needs exactly 3 gears; `transmissionType` is live-safe.
13. **Geometry (0.6.0)**: `WheelGroup.CasterAngle/ToeAngle` setters call `ApplyGeometryValues` (euler X = −caster, Y = ∓toe by side of `localPosition.x`, Z kept; gated by `applyCasterAngle/applyToeAngle`); `CamberController` and solid axles (`WheelGroup.Update`) overwrite camber every tick; `vc.wheelbase`/`trackWidth` are computed at init only; NWH mirrors camber/toe by the sign of `localPosition.x`.
14. **Powertrain wiring (0.6.2)**: NWH steps the powertrain by recursing through `_output`/`_outputB` object references every tick (`PowertrainComponent.cs:156-189`, `DifferentialComponent.cs:192-234`); the public `Output`/`OutputB` setters relink live (and clear the old target's `_input`); name hashes are only zero-checked while stepping and resolved by name once in `VC_Initialize`. A new `DifferentialComponent` has no split delegate until `DifferentialType` is assigned. `WheelComponent.ForwardStep` sets `AutoSimulate = false` — an undriven wheel must have it back on to be simulated at all. A cycle in the wiring recurses until the game crashes.
15. **Shifting (0.7.0)**: the game's FSMs never write `transmission.Gear`; they write shift *requests* by PlayMaker `SetProperty` on `input.ShiftInto` (R=-1/N=0/1..5), `input.ShiftUp`, `input.ShiftDown` (`docs/fsm-template-dump.md`). NWH applies them in `TransmissionComponent.ForwardStep` through `shiftDelegate(vc)` (Manual/Automatic/CVT), then `input.ResetShiftFlags()` (`:403-420`). A `transmissionType` change re-assigns NWH's own delegate on the next tick (`:405`), and the game's `CheckTag` FSM writes `transmissionType`/`UpshiftRPM`/`DownshiftRPM`/`finalGearRatio`. NWH's raw automatic shifts at fixed RPMs without checking the next gear's landing RPM (it hunts on wide ratio steps). The mod's shift controller replaces the delegate and never writes the type.

## 3. Architecture (file-by-file)

```
Plugin.cs                       Slim entry: config load → ES3 read-only import → Harmony patches (steering + InputBlocker) → hidden runner GO; sceneLoaded → recreate runner.
PluginInfo.cs                   GUID "dev.apocalypter.tractionsteering" (unchanged for config continuity), name, version "0.7.0" (numeric-only: BepInEx 5 skips "-alpha" tags).
Settings/PresetBook.cs          Generic preset semantics shared by all 7 categories: ITunablePreset + PresetBook<T> (Identity/Custom/Defaults/NotFound, SetByName with legacy-name mapping, BeginEdit copy-to-Custom with BasedOn, Reference for per-slider Reset, ResetCustom).
Settings/EditableCurve.cs       Piecewise-linear curve over [0,1]², 2-8 points: allocation-free Evaluate (prefix hot path), add/move/remove, Clone, "x:y;x:y" (de)serialization with validation.
Settings/SteeringPreset.cs      6 presets (Vanilla/GTA/Euro Truck/Sim-Race/Drift/Custom) + Defaults; UseVehicleCurve + LockCurve + ReturnCurve; v2.0.0-behavior defaults; Custom mutated by UI; RestoreBaseCurve() re-attaches the BasedOn preset's curves (config-parse fallback).
Settings/SteeringSettings.cs    Book delegate; Enabled default FALSE (opt-in); MatchGameSteeringSpeed; GameSteeringSpeedFactor.
Settings/SuspensionPreset.cs    5 presets + Custom as multipliers on stock (Stock/Comfort/Sport/Off-road/Race); the preset factor IS the slider value.
Settings/SuspensionSettings.cs  Book delegate (legacy "Street"→"Stock"); SplitFrontRear; LinkRearToFront (BeginEdit first); factor accessors Spring(front) etc.
Settings/AeroPreset.cs          DownforceScale/DragScale/MaxDownforceSpeedScale; presets Stock/Street/Sport/Off-road/Race/Custom.
Settings/BrakesPreset.cs        TorqueScale/FrontBrakeScale/RearBrakeScale/HandbrakeScale/ActuationScale; presets Stock/Sport/Race/Off-road/Drift/Custom.
Settings/GripPreset.cs          LongitudinalScale/LateralScale/StiffnessScale; presets Stock/Sport/Race/Off-road/Drift/Custom.
Settings/DrivetrainPreset.cs    PowerScale/RevLimiterScale/LossScale/BoostScale/FinalDriveScale/UpshiftScale/DownshiftScale/ShiftDurationScale/DiffFrontMode/DiffRearMode (DiffMode enum)/DiffStiffnessScale/DiffBiasScale; presets Stock/Street/Off-road/Sport/Race/Drift/Custom.
Settings/AssistsPreset.cs       AbsEnabled/AbsSlipThreshold/AbsCutoffSpeed/AbsCutMultiplier + Tcs*; presets Off/Standard/Sport/Off-road/Race/Custom.
Settings/AlignmentPreset.cs     (0.6.0) WheelRole; Camber per wheel, Caster/Toe per axle, PosX(outward)/PosY/PosZ per wheel — offsets in deg/cm; presets Stock/Street/Sport/Race/Off-road/Stance/Custom.
Settings/AlignmentSettings.cs   (0.6.0) Book delegate; Enabled/PerWheel; axle helpers; LinkSides (fork-first).
Settings/GearboxPreset.cs       (0.6.0) GearCount, GearScale[12], clutch grip/range/RPM offset, GearboxMode; ClutchTypes table (Stock/Street/Sport/Race); presets Stock/Comfort/Sport/Race/Truck/Custom. 0.7.0: SpreadRatios, ShiftUpFactor/ShiftDownFactor/KickdownScale, NeedsShiftController.
Settings/TelemetryPins.cs       (0.7.0) Pin registry: "Category.ConfigKey" keys (PresetCodec's numeric fields), toggle/cap 12, load (drop unknown/dupes)/serialize, value of the shown preset, labels, units.
Settings/GearboxSettings.cs     (0.6.0) Book delegate; PerGearScale; ClutchTypeIndex.
Settings/DrivetrainLayout.cs    (0.6.2) Layout text parser + tree validator (pure, NWH-free).
Settings/UiSettings.cs          (0.6.0) Panel (freeze, scale, width, alpha, last tab) + telemetry preferences; name-only corner parse; tab clamp.
Settings/PresetCodec.cs         (0.6.0) Pure static preset export/import ("AVT1|…"), per-category key tables, clamping, Custom-only import.
Settings/Limits.cs              Single source of truth for every slider/config range.
Game/GameSettingsReader.cs      Read-only ES3 import of steeringspeed/smoothinput/normalizeinput; re-read on panel open.
Persistence/ModConfig.cs        BepInEx ConfigFile binding for all categories; AcceptableValueRange clamping; SettingChanged → runtime push + event; one write per save; v3.1→v3.2, v0.3.0 and v0.4.0 migrations (see §5); [General] Apocasetter opt-in key.
Patching/TractionEdgeSteeringPatch.cs  The steering prefix (allocation-free; target/guards unchanged since v3.0; Vanilla preset = early return true).
Runtime/VehicleTuner.cs         The multi-system tuner (replaces SuspensionApplier): 2 s unscaled scans (FindObjectsOfType), per-vehicle baseline capture (one pass, per-system null guards, mean-wheel-Z axle detection, TyreWear flag), per-category baseline refresh on OFF→ON (0.5.0), per-category apply/restore with applied-flags, OnDestroy → RestoreAll, MeanBaseline readout API, TrackedVehicles.
Runtime/VehicleTuner.Systems.cs Suspension/Grip/Brakes/Drivetrain/Aero apply+restore. Aero: find module in vc.moduleManager.Components; onboard when absent AND the preset asks for more drag than stock (an onboarded module adds Cd × (DragScale − 1)); re-enable after restore; no downforce-point synthesis (Stock = exactly as shipped).
Runtime/VehicleTuner.Assists.cs ABS/TCS delegate factory (allocated once per vehicle, reads live settings each tick) + registration/removal with flags.
Runtime/VehicleTuner.Alignment.cs (0.6.0) camber/caster/toe/position apply+restore, gate handling, camber-lock skip, x=0 clamp, baseline refresh.
Runtime/VehicleTuner.Layout.cs  (0.6.2) Custom drivetrain layout: stock-wiring capture (axles, sides, driven set, own wheel inertia), per-vehicle resolve, idempotent wiring with hash hygiene, wheel release, exact restore, stock-layout log text.
Runtime/VehicleTuner.Gearbox.cs (0.6.0) gear capture/layout check/continuation (0.7.0: or progressive spread), ratio+count write with the Gear re-shift guard and in-flight-shift deferral, clutch (drift-aware), mid-shift restore + trim; 0.7.0 HookShifter/UnhookShifter.
Runtime/ShiftController.cs      (0.7.0) The mod's own shifting: pure shift-point/DNR/manual math + a per-vehicle NWH shift delegate (reads the game's shift requests, ShiftInto, fault fallback, allocation-free).
Runtime/Drift.cs                (0.7.0) "Did the game change this field since our last write?" — adopt it as the new stock (drivetrain + clutch).
Runtime/PanelLayout.cs          (0.6.0) Every panel size as a pure function of the width (wide / narrow stacked rows), scale factor, effective width, digit→tab. 0.7.0: tighter constants, font constants, pin band, tab rows, Arial width tables + FitFont.
Runtime/GearGraph.cs            (0.6.0) Gear-ratio bar graph row (sibling of CurveEditor): bars vs stock outlines, click selects, bar drag edits, other drags scroll.
Runtime/TelemetryStrip.cs       (0.6.0) Click-through speed/RPM/gear/slip strip on the hidden runner, own canvas, 4 Hz. Shows FindDrivenVehicle(): live input → last driven → running engine → fastest → first (0.6.3). 0.7.0: pinned-value cells (pure CellBand/StripHeight).
Runtime/InputBlocker.cs        Two layers (0.6.0): InputController name whitelist (driving input stays live) + the PlayMaker class patch set (forks routed by name, OnEnter gated by everyFrame); SetInputBlocked / SetFreeze (see §2.8, §2.11).
Runtime/SettingsPanelManager.cs On the hidden runner: hotkey/Esc/digit polling (dual input), menu-button injection (§2.7), panel lifecycle (live vs freeze), per-frame cursor freeing + selection clearing, EventSystem find-or-create, auto-save on close.
Runtime/SettingsPanel.cs        The 8-tab docked panel (0.7.1: two merged tabs — Wheels = alignment + grip, Drivetrain = engine/diffs/gearbox/layout) (two-row tab strip, width-adaptive rows via Relayout, one Graphic per GO, mouse-only widgets, single refresher list, two-click per-tab reset-all and "Turn everything off", copy/paste preset footer, dim + click-outside close in Freeze mode only, absolute readouts).
Runtime/UiStrings.cs            Every dynamic panel string as a {0} template + value formatters, so ApocaLanguage can translate them (docs/strings.md).
Runtime/CurveEditor.cs          The visual curve editor row: header band (title, readout, Reset, hint) above a MaskableGraphic graph (grid + curve + mesh-drawn handles), click-add / drag-move / double-click-remove, non-handle drags forwarded to the list's ScrollRect, inert while its tab is OFF/Vanilla.
Runtime/UiKit.cs                Tiny uGUI widget kit (anchored layout, built-in Arial font with fallbacks, HitArea sliders, scroll view with auto-hide scrollbar).
icon.png                        Mod icon for the Apocasetter Mods window (generated by tools/make_icon.ps1; installs beside the DLL as ApocalypterSteeringMod.png).
tools/make_icon.ps1             Reproducible icon generator (System.Drawing); icon.png may be hand-replaced.
docs/strings.md                 String inventory + translator conventions (see §11).
gamecode/, PROMPT.md            Audit-bundle files, now kept in the tree (gamecode/ = decompiled NWH excerpts; PROMPT.md = the audit instructions for the rework pass).
```

## 4. Settings model (summary — full data in the preset classes)

- Steering knobs: `RateMultiplier` (× degreesPerSecondLimit) · `SmoothingScale` (× speedSensitiveSmoothingCurve) · `LinearityOverride`+`LinearityExponent` · `UseVehicleCurve` + `LockCurve` (editable lock-at-speed curve, y = fraction of max steer, evaluated at Speed/50) · `ReturnCurve` (editable return-to-center curve, y = fraction of the steer-in rate used while unwinding; flat 1 = symmetric, 0 at rest = holds the wheels) · `TractionClampEnabled` · `SlipAngleDeg` · `OppositeLockBoost`. Custom defaults reproduce the v2.0.0 feel.
- Steering physics: front-axle slip geometry `frontSlip ≈ bodySlip + (a/v)·yawRate − steerAngle`; clamp to ±SlipAngleDeg yields opposite-lock freedom, into-slide suppression and plow prevention. Clamp bounds are limited to `maximumSteerAngle` BEFORE clamping (fixes the v3.0 inverted-bounds bug).
- Suspension/other categories: `effective = capturedStock × presetFactor`; presets are authored factors; sliders edit them through Custom. Suspension readouts: mean stock baseline across tracked vehicles × factor.
- `MatchGameSteeringSpeed` (default true): effective steering rate ×= `Clamp(gameSteeringspeed/50, 0.35, 2.5)`.
- Alignment (0.6.0): effective = stock geometry + offset (deg for camber/caster/toe, cm for position; PosX outward). Gearbox (0.6.0): ratio_i = stock_i (continued past the vehicle's own count) × factor_i; clutch slipTorque/engagementRange × factor (≥ 1), engagementRPM + offset (≥ min(stock, 1.1 × idle)).

## 5. Config schema and migration

Sections: `Steering` (Enabled **false** by default, Preset, MatchGameSteeringSpeed) + `Steering.Custom.*` · `Suspension` (Enabled, Preset, SplitFrontRear) + `Suspension.Custom.*` (10 factor keys + BasedOn) · `Aero` / `Brakes` / `Grip` / `Drivetrain` / `Assists` (Enabled, Preset) + per-category `Custom.*` (incl. DiffFrontMode/DiffRearMode strings, Abs/Tcs keys) · `UI.ToggleKey`. 0.6.0 adds `Alignment` (Enabled, Preset, PerWheel) + `Alignment.Custom.*` (BasedOn + 20 offset keys), `Gearbox` (Enabled, Preset) + `Gearbox.Custom.*` (BasedOn, GearCount, Gear1..12Scale, clutch keys, TransmissionMode), `UI.FreezeWhileOpen/PanelScale/PanelWidth/PanelAlpha/LastTab`, `Telemetry.Enabled/Scale/Position`. All numeric entries carry AcceptableValueRanges from `Limits`. 0.6.0 needs no migration (additions + widened ranges only). 0.6.2 adds `Drivetrain.Layout` (Enabled, Layout) — additions only. 0.7.0 adds `Drivetrain.Custom.DiffCenterMode`, `Gearbox.Custom.SpreadRatios/ShiftUpFactor/ShiftDownFactor/KickdownScale`, `Telemetry.Pins` — additions only (harness: a real 0.6.4 file keeps all 133 keys and values).

**v3.1 → v3.2 migration (one-time, in `ModConfig.MigrateLegacySuspension`)**: (1) `Suspension.Preset = "Street"` maps to "Stock"; (2) if any legacy `[Suspension.User]` multiplier ≠ 1.0, fold `Custom_i = Clamp(presetFactor_i × user_i, 0.5, 2)` into the Suspension.Custom entries with BasedOn set, ActivePreset = Custom; (3) the 10 legacy keys are `config.Remove`d every load so the fold can never run twice. Covered by tests.

**v0.2.0 → v0.3.0 migration (one-time, in `ModConfig.MigrateLegacySteeringPreset`)**: `Steering.Preset = "Truck-sim"` and `Steering.Custom.BasedOn = "Truck-sim"` are rewritten to `"Euro Truck"` on load (the runtime preset book also maps the name, so either layer alone would load correctly; the file rewrite stops the dead name from lingering). The BasedOn rewrite matters: `RestoreBaseCurve()` needs a resolvable name or Custom silently loses its speed curve. Covered by tests.

**v0.3.0 → v0.4.0 migration (one-time, in `ModConfig.MigrateLegacySteeringCurves`)**: triggered by the presence of the legacy keys in the raw file text (checked before any Bind — a post-migration file never re-folds). `SpeedCurveScale` multiplies the BasedOn preset's lock curve (exact old behavior); `CenterReturnScale` becomes a flat return curve. Both keys are removed on every load. Vehicle-curve users (BasedOn empty) with a scale ≠ 1 lose the scale (documented). Covered by tests.

## 6. Build and test (no terminal needed on the target machine — but instructions for whoever runs it)

```
cd plugin && dotnet build -c Release          # real DLL, references game DLLs at D:\SteamLibrary\...\Apocalypter_Data\Managed (adjust paths in the csproj; 0.6.0 adds UnityEngine.IMGUIModule for the clipboard)
cd verify && bash run.sh                      # stubs compile + 610 tests (592 logic + 18 prefix); .NET SDK 8+; refs/ already populated (tests/fixtures/v064.cfg is read from verify/)
```
Install: copy the DLL to `BepInEx\plugins\` and `icon.png` next to it as `ApocalypterSteeringMod.png` (the Apocasetter Mods-window icon). Rebuild the install zip with both files at the zip root. **Never run it alongside an old `SteeringFix.dll`** (earlier assembly name) — both prefixes would double-process steering. The GUID is deliberately unchanged, so the existing config file migrates in place.

## 6b. Releasing / Apocasetter updater contract

The Apocasetter updater installs mods from its GitHub index (`DeonUrist/Apocasetter-Index`); its contract for a loose-DLL mod like this one:

- a public GitHub repo with a release whose tag equals the `[BepInPlugin]` version (`0.7.0`),
- a release `.zip` that unpacks into `BepInEx\plugins` — i.e. `ApocalypterSteeringMod.dll` + `ApocalypterSteeringMod.png` at the zip root (the layout of `BepInEx\plugins\ApocalypterSteeringMod.zip`),
- the `[General] Apocasetter = true` config entry (bound on load, written on first run),
- one-time submission via the index repo's "Submit a mod" issue template.

Git is currently local-only, so the index submission stays deferred until the user publishes a repo. Nexus releases ship the same DLL + icon.

The audit bundle (what the third-party rework pass receives) is separate from the install zip and is regenerated from the tree — everything it needs now lives here:

```
powershell Compress-Archive README.md,PROMPT.md,FEATURES.md,CONTEXT.md,plugin,verify,gamecode,docs ..\ApocalypterVehicleTuning-Audit.zip
```

## 7. Audit checklist (suggested focus)

1. **Settings respected**: prefix honors `useRawInput`, `returnToCenter`, vehicle curves, `degreesPerSecondLimit`, `maximumSteerAngle`, `externallyAddedAngle`; every category restores captured stock values on disable (aero disables onboarded modules; assists remove delegates).
2. **Allocation-free hot paths**: `Prefix`, tuner apply/restore, and the ABS/TCS delegate bodies must not allocate (no LINQ/ToString/closures/boxing in per-tick paths).
3. **No writes to the game's save data** (no `ES3.Save` anywhere) and **no writes to `vc.input.*`**.
4. **Survival**: hidden runner + sceneLoaded recreation; Harmony patch target unchanged; `_buildFailed` guard on panel build.
5. **Physics correctness**: traction-clamp sign conventions (§4), ABS/TCS slip sign conventions (§2.10), diff-mode index mapping (0=Stock 1=Open 2=Locked 3=LSD), brake/handbrake range clamps.
6. **Config hygiene**: migration one-time-ness, range clamping, `_syncing` guard, Save on close/disable.
7. **UI hygiene**: one Graphic per GO, labels raycastTarget=false, EventSystem never duplicated, cursor restored, timeScale restored (Freeze) / never touched (live), input unblocked one frame late, selection cleared every frame while open.
8. **0.6.0**: gear-list resize never leaves `gearIndex` out of range (also under an in-flight shift); alignment never crosses x = 0, never writes a locked camber, restores gates; blocker whitelist fail-closed; layout bands at 400/460/800.
9. **0.7.0**: the shift delegate is restored by instance on every OFF path (category, target switch, OnDisable/OnDestroy) and never captured from a stale controller; `transmissionType` never written; controller per-tick path allocation-free and exception-contained; drift adoption only on a mismatch with our own last write; pins never break one-Graphic-per-GO.

## 8. Known limitations / deliberate decisions

- Suspension/other tuning applies globally to all vehicles (opt-in per category; stock restored on disable). Factors preserve each vehicle's own character.
- 0.6.0: the panel no longer pauses the game by default (live mode; `FreezeWhileOpen` restores the pause). Widgets are mouse-only (user decision — no keyboard navigation); digits 1…0 only switch tabs.
- **Live-mode risk (accepted):** with no click-catcher, game UI beside the panel stays clickable — opened from the pause menu, its buttons can be clicked (uGUI onClick is not blocker-gated). Freeze ON keeps the 0.5.0 modal dim. Live mode never writes timeScale, so a panel opened from the pause menu keeps the game paused.
- **Whitelist is fail-closed:** a driving action whose name is not in `InputBlocker.DrivingKeys/DrivingAxes` does nothing while the panel is open; its name is logged once ("InputBlocker: blocked input action '…'"). Add such names to the whitelist after the in-game check.
- **Telemetry default ON** deviates from the opt-in convention on purpose: it is passive, click-through UI that changes no vehicle; one toggle in the Panel tab (or `[Telemetry] Enabled = false`) hides it.
- Alignment: moved wheels keep NWH's init-time wheelbase/track width (Ackermann, solid-axle camber); wheels with a camber controller or on a solid axle keep their camber; caster/toe are per axle only. Gearbox: CVT/External boxes and non-standard gear lists keep their gears; clutch types are emulated. 0.7.0 shift controller: the game's HUD gear readout may lag a shift (cosmetic); after the game changes a vehicle's `transmissionType` while tuned, NWH's own shifting runs for up to one 2 s pass until the controller re-hooks; Manual mode on an Automatic-type vehicle inherits NWH's 0.5 s post-shift ban (the type is not changed to avoid the ban, see §2.15); a save made while a spread/tuned box is applied bakes it (only the 0.6.0 continuation shape is auto-repaired — turn Gearbox off before saving). Telemetry pins show the tuning *setting* (the slider value), not a live vehicle measurement. If the runner is replaced exactly while a shift into an added gear is in flight, the placeholder gears left by the restore become the new runner's "stock" (harmless duplicates of the top gear; cleared by re-enabling and turning Gearbox off, or a restart).
- Vehicles without an aero module get extra drag only (0.5.0: default-module Cd × (DragScale − 1), so nothing at ×1.0 or below; no downforce-point synthesis); onboarded modules stay in `Components` (disabled and inert) after restore and are reused. A shipped module's own `simulateDrag`/`simulateDownforce` switches are never changed.
- The mod's TCS keeps a low-speed cutoff (`TcsCutoffSpeed`, default 2 m/s). NWH's `TCSModule` declares `lowerSpeedThreshold` but never reads it, so the stock module also cuts during a standing-start; the mod's launch behaviour therefore differs below the cutoff (set it to 0 for NWH-like launches). Left as is in 0.2.0 — it is a feel decision that needs in-game testing.
- Digressive damper valving params are deliberately untouched. `brakeOffThrottleIntensity` is deliberately untouched.
- The engine sound's max RPM (NWH2_RES2) is read once at Start — rev-limiter slider changes won't re-pitch existing sounds (cosmetic). Electric engines ignore the boost slider.
- Physical steering-wheel support is deferred; the steering patch is input-agnostic.
- The game's own ABS/TCS modules (if a prefab has them) run alongside the mod's delegates; modifier values multiply (harmless).
- Power sliders can trivialize the game (accepted — single-player tuning mod).
- **Apocasetter coexistence (intentional):** the mod keeps its own InputBlocker (it restores the exact saved timeScale, including a pause-menu 0, where Apocasetter's clamps ≤0 to 1) and its own "Vehicle Tuning" menu button (Apocasetter's `GameMenu.Tick` hardcodes the "MODS" label). Both install their blockers; each prefix is a per-frame bool check, so the overlap is harmless. The mod reads Apocasetter's `General.Apocasetter` convention but never references its DLL.
- The panel's translated strings come from ApocaLanguage's community packs; the mod ships no translations itself (English baseline, §11).

## 9. Provenance

- Decompilation: ILSpy over every DLL in `Apocalypter_Data\Managed` (`docs/decomSource.ps1`).
- Survival/menu-button/input-blocker recipes validated against existing Apocalypter mods: [Apocasetter](https://github.com/DeonUrist/Apocasetter) and [ApocalypterInspector](https://github.com/FiveDollaGobby/ApocalypterInspector). The mod implements the same techniques with its own code.
- NWH Vehicle Physics 2 / WheelController 3D excerpts in `gamecode/` are provided for audit reference only.

## 10. In-game test checklist (for the machine with the game)

1. Log shows `Apocalypter Vehicle Tuning 0.7.0 loaded.`, two `InputBlocker: patched …` counts (6 InputController methods + the PlayMaker action methods) and no errors.
2. F7 (or the "Vehicle Tuning" button in the pause menu) opens the panel docked right; cursor free; the game keeps running (0.6.0 live mode — see item 21; item 22 covers the Freeze option). Esc/F7/X/Done close it (click-outside only with Freeze ON).
3. Each of the 9 tuning tabs: master ON applies the preset, sliders live-edit (preset → "Custom (Base)"), Reset returns to the preset origin, OFF restores stock feel.
4. Steering tab: Vanilla must feel exactly unmodded; Custom defaults ≈ the old v2.0.0 feel; traction clamp + opposite lock behave as before.
5. Suspension: Race visibly stiffens/lowers, Off-road softens/raises; readouts show plausible absolutes; split front/rear works.
6. Aero: enable on a vehicle without a downforce setup → drag change only; disable → restored. Brakes: handbrake preset in Drift is noticeably stronger. Grip: Drift slides easily. Drivetrain: Race revs higher; diff lock on; restore exact.
7. Assists: Standard prevents lock-up under hard braking (feel + no flicker); TCS cuts wheelspin on launch.
8. Restart → settings persist; config file contains the new sections and no `[Suspension.User]`.
9. **0.3.0 — Euro Truck:** the steering tab shows "Euro Truck" (no "Truck"); values read ×0.50 rate, ×1.70 smoothing, 6.5°, ×1.00 counter-steer, ×0.25 center return. At highway speed the wheel is clearly slower and cuts less lock than the old Truck-sim; releasing the key does NOT snap the wheel back — it eases toward center at the reduced rate; no counter-steer snap when catching a slide.
10. **0.3.0 — migration:** with a cfg containing `Steering.Preset = Truck-sim` (or `BasedOn = Truck-sim`), after launch the panel shows Euro Truck active, the cfg now says `Preset = Euro Truck`, and a Custom's speed curve is preserved.
11. **0.3.0 — Apocasetter (install v2.0.6 first):** the Mods window lists "Apocalypter Vehicle Tuning" with the square icon; all seven sections' entries appear as live editors with ranges/descriptions; editing there updates the panel (SettingChanged is already wired); the cfg contains `[General] Apocasetter = true`. In the pause menu the "MODS" and "Vehicle Tuning" buttons sit stacked in the top-right corner without overlapping, and both click through.
12. **0.3.0 — ApocaLanguage (install v1.5.2 + a language pack):** panel labels translate on language switch; dynamic templates ("×1.40", "Custom (Race)") translate when the pack covers them; the "Vehicle Tuning" menu label translates.
13. **0.4.0 — curve editors:** both graphs draw the active preset's curves; dragging a point on a built-in preset forks it into "Custom (…)" and the graph recolors; click adds a point, double-click removes; dragging the scrollbar/list area still scrolls (no scroll stealing). Reset per graph returns the preset curve.
14. **0.4.0 — hold-then-straighten (Euro Truck):** with the wheels turned, stop and release the key — the wheels stay turned. Drive off: the truck follows the held angle, then straightens out as speed builds. At standstill, steering input still winds the wheels to lock at the normal rate. True reverse behaves like vanilla.
15. **0.4.0 — flat-1 regression:** on any non-Euro-Truck preset (or a Custom with a flat return line), low-speed and parking behavior matches 0.3.0 exactly (vanilla below 1.5 m/s).
16. **0.4.0 — migration:** with a 0.3.0 cfg containing `SpeedCurveScale`/`CenterReturnScale`, after launch the cfg has `UseVehicleCurve`, `LockCurve = …`, `ReturnCurve = …` and neither legacy key; the panel's graphs show the folded values; a reload does not change them again.
17. **0.5.0 — curve rows:** each graph row shows its title, hint and (when changed) the point count ABOVE the graph; the per-graph Reset button is visible top-right and works (after editing a curve it returns the preset curve). Dragging over the empty graph area scrolls the list; releasing that drag adds no point. With Steering OFF, or Vanilla selected, clicking/dragging a graph changes nothing (the preset stays selected, no "Custom (…)").
18. **0.5.0 — counter-steer at rest (Euro Truck):** stop with the wheels turned right, release, then tap/hold LEFT lightly (gamepad: a small stick deflection): the wheels move left immediately instead of staying frozen until the input exceeds the held angle. Releasing still holds.
19. **0.5.0 — aero on a vehicle without aero:** Aero ON + Street (or a Custom with drag ×1.0 or below) does not lower that vehicle's top speed; Race lowers it slightly (about a quarter of 0.4.0's Race drag).
20. **0.5.0 — save/reload a dragged curve:** drag a curve point hard against its neighbour, close the panel, restart: the curve has the same number of points and the same shape.

21. **0.6.0 — live driving:** open the panel while driving: throttle, brake, steering and handbrake keep working with the normal keyboard steering ramp; the mouse moves the cursor (no camera look); Esc closes and the pause menu does NOT open; the log shows no "InputBlocker: blocked input action" line for a driving control (if it does, report the name). Click a panel button, then drive and press Space/Enter: the button does not fire again.
22. **0.6.0 — freeze toggle:** Panel tab → Freeze ON: the panel is modal again (dim, click outside closes, game frozen); open it from the pause menu and close it: the game is still paused (timeScale 0 restored).
23. **0.6.0 — docking:** the panel sits on the right at full height with the game visible beside it; at widths 400/460/800 × sizes 0.5/1/2 every tab (both curve editors and the gear graph included) is fully visible and clickable, nothing overlaps; transparency slider works live.
24. **0.6.0 — alignment:** Race → front wheels visibly cambered (top in); Off-road → wider track (wheels further out) and raised; Stance → wheels tucked in and slammed; caster/toe change steering feel; moved wheels stay moved after 2 s; OFF restores exactly. A vehicle with a solid axle / camber controller shows the warning and keeps its camber. **Verify the sign directions** (camber negative = top in, PosX positive = outward, toe positive = toe-in).
25. **0.6.0 — gearbox:** on a 5-speed car set Gear count 6 → a 6th gear exists and is used, no exceptions in the log; cut to 4 while in 5th → drops to 4th at once, no exceptions; Race clutch launches harder and shifts crisper; Stock restores exactly; a CVT vehicle is untouched (status line says so).
26. **0.6.0 — telemetry:** strip shows live speed/RPM/gear/slip, clicks pass through it, it hides while the panel is open, corner/size follow the Panel tab.
27. **0.6.0 — copy/paste:** Suspension → Race → Copy preset → Reset all suspension → Paste preset: "Custom (Race)" with identical values; pasting it on the Aero tab is refused with a reason.
28. **0.6.0 — config:** a 0.5.0 cfg loads with all legacy keys/values intact and gains `[Alignment]`, `[Gearbox]`, `[Telemetry]` and the new `[UI]` keys; Apocasetter lists the new sections as live editors; editing there while the panel is open does not undo slider changes made in the panel.

29. **0.6.2 — curve point grab:** grab a curve point and pull it quickly (one fast flick): the point moves with the mouse; it must not scroll the list. Same on the gear graph: a quick downward pull from a short bar edits the bar. Dragging the empty graph area still scrolls.
30. **0.6.2 — stock layout log:** after loading a save, the log has one `Drivetrain of '…': N axles, wheels … Stock layout: gearbox -> …` line per vehicle. Copy one into `[Drivetrain.Layout] Layout`, set `Enabled = true` and Drivetrain ON: the vehicle drives exactly as before (same driven wheels).
31. **0.6.2 — RWD conversion:** on an AWD vehicle set `gearbox -> rear; rear: LSD -> RL, RR`: front wheels free-roll (no drive, but suspension and steering still work — if a front corner sinks or the front wheels stop turning, report it: that is the AutoSimulate hand-back), power oversteer is possible. Layout OFF: AWD again. No exceptions in the log.
32. **0.6.2 — AWD conversion:** on a RWD vehicle use the default example: the car pulls with all four wheels (try a slope or mud), with the open transfer case one spinning axle stalls progress; `Locked` transfer case: it does not. A 6x6 (if any) with the 6x6 example drives all three axles.
33. **0.6.2 — save while converted:** with a layout active, save, quit to menu, load: the vehicle loads driveable (stock wiring until the tuner re-applies a couple of seconds later), no NullReferenceException spam.

34. **0.6.3 — telemetry with hands off:** drive a car, stop, click a setting in the panel (hands off the keys): the strip keeps showing YOUR car (speed/RPM/gear), not zeros. With "Apply to: Last driven", that edit tunes your car, not a parked one.
35. **0.6.3 — save load:** load a save with several vehicles: the log shows no tuner activity for ~5 s, then the captures; no "failed and was skipped" warnings on a healthy save (if any appear, send the log — they name the category and vehicle).
36. **0.6.3 — digit keys vs gears:** with the panel open in live mode, shift with the number keys (if the game binds ShiftInto1..8 to them): note whether the panel tab also switches.
37. **0.6.4 — telemetry with a parked fleet:** park your car (handbrake on) next to other parked cars, hands off: the strip keeps YOUR car (speed/RPM/gear), not a parked car's zeros; driving straight shows values, not only while steering. If it ever picks the wrong car, set `[Telemetry] DebugPick = true`, wait ~2 s, reproduce, and send the `Telemetry pick:` lines from `BepInEx\LogOutput.log`.

38. **0.7.0 — the automatic that used to get stuck:** Gearbox ON, preset **Truck**, on an AUTOMATIC car (game setting automatic gearbox on). From a standstill, full throttle then cruise: the car drives off in 1st and shifts up by itself through the gears (watch the telemetry Gear cell — it should reach 10-12 on a long straight), with no rapid shift-flicker and no "engine revs, wheels don't turn". Lift off and brake to a stop: it shifts down and ends in N like stock. The log has no "Shift controller … failed" line.
39. **0.7.0 — manual mode:** same car, Shifting mode **Manual**: the game's gear keys (1..5, R, N, shift up/down) select gears; shift up continues past 5th to 12th; with the panel open and the mouse NOT over it, the number keys shift without switching panel tabs (item 36's risk); with the mouse over the panel they switch tabs.
40. **0.7.0 — exact restore:** Gearbox OFF while driving: the car shifts like stock again immediately (the game's own automatic or manual), the gear count is back to stock, nothing in the log. Toggle the game's automatic-gearbox setting while Gearbox is ON: within ~2 s the mod follows the new type (Stock mode).
41. **0.7.0 — Stock/clutch presets:** Gearbox ON with Stock, Comfort, Sport or Race: shifting feels exactly like stock (only the clutch changes); the Gearbox status line does not say "The mod shifts …".
42. **0.7.0 — drivetrain tab:** on an AWD car the centre-diff buttons work (Locked = no front/rear speed difference), the bias slider is enabled and the "Torque split" line shows front/rear %; on a RWD/FWD car it says "drives one axle" and the bias slider is dimmed. Pick the AWD layout template + "Custom layout" ON on a RWD car: the split line shows 40%/60% and the car pulls with all wheels. "Copy this vehicle's layout" then "Paste layout" round-trips; pasting garbage shows "Not a layout: …".
43. **0.7.0 — telemetry pins:** click Pin on 3 sliders (e.g. Steering speed, Stiffness, Gear 1): the strip turns on (if off) and grows one row per two pins with "title value" cells; values change as you move the sliders; the strip never takes a click; restart: the pins are still there. Check the strip at 1080p and 1440p/4K and both scales: cells readable, not overlapping. Settings tab → "Pinned values" OFF clears them.
44. **0.7.0 — tighter panel:** at widths 300 / 460 / 1000 every tab is visibly tighter than 0.6.4 with bigger text; no label runs under a Pin/Reset button (long titles are slightly smaller instead); at 300 px the tabs use three rows and every tab name fits.
45. **0.7.0 — game-changed values:** with Drivetrain ON (any preset), swap the car's engine (or change the game's automatic-gearbox setting): the new engine's power/shift points take effect (scaled), and Drivetrain OFF leaves the new engine's values, not the old ones.
46. **0.7.0 — config:** your 0.6.4 cfg loads unchanged and gains `DiffCenterMode`, `SpreadRatios`, `ShiftUpFactor`, `ShiftDownFactor`, `KickdownScale`, `Pins`. Note: if your cfg had `[Gearbox] Enabled = true`, the gearbox now applies (it was gated before).

## 11. Translation (ApocaLanguage)

The mod has no localization code of its own and no dependency on ApocaLanguage — every on-screen string flows through a Unity UI `Text.text` setter, which ApocaLanguage's Harmony hooks translate automatically (it remembers each component's English original, so switching language re-translates live). Dynamic strings are `{0}` templates in `Runtime/UiStrings.cs`; static labels stay literals in `SettingsPanel.cs`. The full inventory and the translator rules (preset `Name` = config ID never translated, `Label` = display; singular/plural separate templates; units inside templates; no angle brackets) are in `docs/strings.md`. To translate: install ApocaLanguage, load a language, and run its `CollectStrings` tool to capture every panel string into a `<lang>/*.json` pack.
