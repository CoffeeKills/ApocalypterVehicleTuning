# §1 inputs: where the game's shifting lives (FSM-dump survey, 2026-10-04)

Survey of `tools/fsm_dump.json` (6834 parsed FSMs from `data.unity3d`) plus decompiled
source greps, answering the §1 question in CONTEXT.md: *does the game shift by
`SetProperty` on `Gear`, by `CallMethod ShiftInto`, or via `vc.input` shift flags?*

## What the static dump shows (all 6834 FSMs, all states)

- **No FSM writes `Gear` or `GearShift` via `SetProperty`** (propertyName scan found none)
  and **no FSM calls any method whose name contains Gear/Shift** (`functionCallParams` scan).
- **No FSM reads a Shift* button** — no `GetButton*`/`GetKey*`/`GetAxis*` action anywhere
  carries a `ShiftUp`/`ShiftDown`/`ShiftInto*` button-name string param.
- The vehicle prefab's `DriveTrigger/INPUT` GameObject carries exactly 6 FSMs on all 14
  vehicles: `INPUT_Headlight`, `INPUT_Handbrake`, `INPUT_Horn` (GetButtonDown/Up + events),
  `Bool_HShifter`, `Bool_MouseSteering`, `Bool_NormalizeInput` (BoolTest + 2× EnableFSM each).
  `Bool_HShifter` toggles exactly two FSMs — consistent with enabling the H-shifter template
  pair when the game setting is on.
- `DriveTrigger` FSMs are `Camera` (1st/3rd) and `ExitSpeed` — no driving logic.
- The only gear-adjacent FSMs: `HShifter` and `automaticGearbox` (1 each) — pure setting
  toggles with **zero actions** (they just mirror the game settings), and every engine
  prefab's `OnOff` FSM reads `RpmGear` (HUD rpm display).

## Where the shifting therefore is

1. **NWH's compiled input path** (not FSMs): every `ShiftInto*` reference in the decompiled
   source lives in `NWH.VehiclePhysics2.Input` — `InputSystemVehicleInputProvider.cs`,
   `InputManagerVehicleInputProvider.cs`, `MobileVehicleInputProvider.cs`,
   `VehicleInputActions.cs`, `VehicleInputHandler.cs`, `VehicleInputProviderBase.cs`,
   plus `TransmissionComponent.cs` (the `ShiftInto` API) and `TrailerModule.cs`.
   The game hooks NWH's input system with compiled code, not scene FSMs.
2. **Runtime FSM templates**: the mod's live discovery log (see
   `VehicleTuner.StockGearCountFromFsms` — it found per-vehicle `INPUT_*` FSMs carrying one
   `GetButtonDown("ShiftIntoN")` per stock gear) sees FSMs that are **not** among the
   parsed prefab FSMs. The dump's 1296 failed parses are mostly `FsmTemplate` objects, and
   `Assembly-CSharp/HutongGames.PlayMaker.Actions/RunFSM.cs` instantiates templates at
   runtime — so the `INPUT_ShiftInto1..8`/`INPUT_GearChange` FSMs are almost certainly
   `FsmTemplate`s the game runs via `RunFSM` on the vehicle (hence "Wrapper" naming from
   the live discovery log; no such name exists statically).

## Consequences for §1 (for the next round)

- Patching PlayMaker's `SetProperty` may be **insufficient or unnecessary**: the static
  data contains no SetProperty-on-Gear writes. The shift APPLICATION is compiled NWH code
  (`TransmissionComponent.Gear` / input providers) and/or the game's own shift code around
  them; the FSMs (templates) only produce shift REQUESTS from buttons.
- What §1 must suppress and what it must intercept should be decided from the **live**
  side, not the static dump: a discovery-log dump of one vehicle's FSM template instances
  (names, per-state actions, target object/property/method) is still the missing input —
  re-parse the `FsmTemplate` objects (the hand-rolled parser in `tools/fsm_extract.py`
  currently fails on their layout; extend it) or dump them from a running game.
- The compiled side is already auditable: the six input-provider files above and
  `TransmissionComponent.cs` are in `ApocalypterSource/` and `gamecode/` carries the
  excerpts the mod's patches touch.

## Attached with this bundle

`gamecode/` now also carries the PlayMaker action sources §1 asked for:
`SetProperty.cs`, `GetProperty.cs`, `CallMethod.cs` (Assembly-CSharp/HutongGames.PlayMaker.Actions)
and `FsmProperty.cs`, `FsmObject.cs`, `FsmStateAction.cs` (PlayMaker/HutongGames.PlayMaker).
`SetProperty` = targetProperty.SetValue() on OnEnter/OnUpdate; `FsmProperty` carries
`targetObject` (FsmObject), `targetTypeName`, `propertyName` and the typed value.
