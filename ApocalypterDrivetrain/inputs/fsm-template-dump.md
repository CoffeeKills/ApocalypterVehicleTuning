# §1 input: the game's shifting, decoded from the FSM data (2026-10-05)

The missing §1 input from CONTEXT.md. The static dump previously found NO gear writes because
the shift FSMs are **template-copies whose `FsmProperty` serialization carries a trailing
`setProperty` bool** (`FsmProperty.cs:50`) that the hand-rolled parser missed — every FSM with
a `SetProperty` action failed to parse (the 1295 failures). `tools/fsm_extract.py` is fixed
(fprop now reads `setProperty`); `tools/template_dump.py` re-parses them. All 8128 PlayMakerFSMs
now parse (1 truncated object excepted). `tools/template_dump.json` holds every FSM with full
action data.

## The answer to the §1 question

**The game shifts by `SetProperty` reflection on `VehicleController.input.ShiftInto`
(int: -1 = R, 0 = N, 1..5) and `input.ShiftUp` / `input.ShiftDown` (bool). It NEVER writes
`transmission.Gear` via FSMs** — no `CallMethod` touches shifting either. NWH's compiled
`VehicleInputHandler`/`TransmissionComponent` consume those input flags. Verified by scanning
every `SetProperty` action in all 8128 FSMs (correct action→param mapping — each typed param
list pools the values of its action type, in action order):

| target.propertyName | writes | writing FSMs |
|---|---|---|
| `input.ShiftInto` | 196 | `INPUT_GearChange`, `INPUT_HShifter` |
| `input.ShiftUp` / `input.ShiftDown` | 14 / 14 | `INPUT_ShiftUpDown` |
| `input.Throttle` | 70 | `Drive`, `INPUT_AxisInput`, `INPUT_CruiseControl`, `INPUT_NormalizedAxisInput` |
| `input.Brakes` | 42 | `Drive`, `INPUT_AxisInput`, `INPUT_NormalizedAxisInput` |
| `input.Steering` | 28 | `INPUT_AxisSteering`, `INPUT_MouseSteering` |
| `input.Handbrake` | 28 | `Handbrake` |
| `input.Clutch` | 28 | `INPUT_AxisInput`, `INPUT_NormalizedAxisInput` |
| `input.EngineStartStop` | 14 | `Start` |
| `input.swapInputInReverse` | 28 | `Bool_AutomaticGearbox` |
| `powertrain.transmission.transmissionType` | 28 | `CheckTag` |
| `powertrain.transmission.UpshiftRPM` / `DownshiftRPM` / `finalGearRatio` | 16 each | `CheckTag` |

### Suppression target for the mod's shift controller

Harmony-prefix PlayMaker's `SetProperty` action (Assembly-CSharp,
`HutongGames.PlayMaker.Actions.SetProperty`, source in `gamecode/SetProperty.cs`):
skip `OnEnter`/`OnUpdate` when the action's `targetProperty.targetObject.typeName ==
"NWH.VehiclePhysics2.VehicleController"` AND `propertyName ∈ {input.ShiftInto, input.ShiftUp,
input.ShiftDown}` while the Gearbox category is ON. The `GetButtonDown` READS
(`ShiftIntoR1`, `ShiftInto1..8`, `ShiftUp`, `ShiftDown` — InputController names, already in the
InputBlocker whitelist) stay untouched, so the mod's own controller can read the same requests
through the layer-A surface and write `transmission.Gear` itself. OFF must restore the game's
writes byte-for-byte (the hidden-runner applied-flags model).

## The per-vehicle shift FSM set (all 14 vehicles, on the `DriveTrigger/INPUT` GO)

`Bool_HShifter` and `Bool_AutomaticGearbox` (plain FSMs, in the old dump) enable/disable these
by name via `EnableFSM`:

- **`INPUT_GearChange`** (template-copy, 8 states) — one-shot shifting. `MAIN`: 7×
  `GetButtonDown` (`R`,`N`,`1`..`5`) + gear-change audio; each gear state: `GetParent`
  (fsmGameObject "DriveTrigger") → `GetParent` (fsmGameObject "Car") → `GetComponent`
  (fsmObject variable "VehicleController", typeName `NWH.VehiclePhysics2.VehicleController`)
  → `SetProperty` on `input.ShiftInto` with the int value of the state (R=-1, N=0, 1..5), then
  `FINISHED` → `MAIN`. The FsmProperty shape (verbatim from the data):
  ```json
  { "targetObject": { "useVariable": true, "name": "VehicleController", "tooltip": "",
      "showInInspector": false, "networkSync": false,
      "typeName": "NWH.VehiclePhysics2.VehicleController", "value": [0, 0] },
    "targetTypeName": "NWH.VehiclePhysics2.VehicleController",
    "propertyName": "input.ShiftInto",
    "int": { "useVariable": false, "name": "", "value": -1 },  // R; 0 = N; 1..5 per state
    "setProperty": true }
  ```
- **`INPUT_HShifter`** (template-copy, 7 states) — H-shifter style: `MAIN` writes an initial
  `SetProperty` `input.ShiftInto` then 6× `GetButtonDown` (`R`,`1`..`5`); each gear state
  repeats the parent-walk + `SetProperty` and holds with `GetButtonUp` → `FINISHED` → `MAIN`.
- **`INPUT_ShiftUpDown`** (template-copy, 3 states) — `shift`: 2× `GetButtonDown`
  (`ShiftUp`,`ShiftDown`); `UP`/`DOWN`: parent-walk + `SetProperty` `input.ShiftUp`=true /
  `input.ShiftDown`=true → `FINISHED` → `shift`.
- **`Drive`** (4 states `outCar`/`enter`/`inCar`/`exit`) — the driving FSM (the "Wrapper"
  equivalent; no FSM named Wrapper exists). `enter`/`exit` run 4× `EnableFSM` each (the shift
  + axis input FSMs); `exit` ends with `GetComponent` + 2× `SetProperty` (zeroing
  `input.Throttle`/`input.Brakes` on exit — this is why parked cars normally hold 0s) plus
  `DestroyComponent` and `SetVisibility`.
- Driving inputs come from **`INPUT_AxisInput`/`INPUT_NormalizedAxisInput`** (throttle/brakes/
  clutch), **`INPUT_AxisSteering`/`INPUT_MouseSteering`** (steering), **`Handbrake`**,
  **`Start`** (engine), **`INPUT_CruiseControl`** (throttle) — all template-copies now parsed.

The templates themselves (the `FsmTemplate` ScriptableObjects) still do not parse via the
[`m_Name`][category][Fsm] SO layout (0 hits) — the template-copy components carry the full
content, so this is cosmetic; `template_dump.json` is the working source. `CheckTag` (the
engine-prefab FSM) sets `transmissionType`/`UpshiftRPM`/`DownshiftRPM`/`finalGearRatio` from the
game's auto-gearbox settings.

## Consequence for FEATURES §1 design

The §1 spec's suppression guess ("skip when the target object is a VehicleController and the
property name is Gear/GearShift") is confirmed in shape but the names are `input.ShiftInto` /
`input.ShiftUp` / `input.ShiftDown` — patch those three, not `Gear`. Everything else in the §1
design stands (ShiftController reads the whitelisted buttons; CVT/External untouched;
Gearbox-OFF restore byte-for-byte).
