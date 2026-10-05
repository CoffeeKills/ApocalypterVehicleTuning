# RECONCILIATION.md — nothing silently dropped (generated)

Each source counts what it SHOULD have seen by an independent method (raw text / metadata tables) and what it parsed. A row that does not balance is listed with ✗ and must be explained before the census is accepted.

## Source: fsm

| check | raw | parsed | | method |
|---|---|---|---|---|
| actions SetProperty | 3792 | 3792 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.SetProperty" |
| actions GetProperty | 294 | 294 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.GetProperty" |
| actions CallMethod | 107 | 107 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.CallMethod" |
| actions GetComponent | 1426 | 1426 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.GetComponent" |
| actions AddComponent | 587 | 587 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.AddComponent" |
| actions DestroyComponent | 907 | 907 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.DestroyComponent" |
| actions HasComponent | 20 | 20 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.HasComponent" |
| actions ActivateComponent2 | 32 | 32 | ✓ | raw substring count of "HutongGames.PlayMaker.Actions.ActivateComponent2" |
| FsmProperty params (targetTypeName fields) | 4086 | 4086 | ✓ | raw substring count of "targetTypeName" |
| FSM links (templateName fields, one per parsed PlayMakerFSM) | 8128 | 8128 | ✓ | raw substring count of "templateName" |
| FsmProperty params referenced by a SetProperty/GetProperty action vs listed | 4086 | 4086 | ✓ | list lengths vs paramDataPos references |
| target actions with >= 1 entry (SetProperty/GetProperty/CallMethod/GetComponent/AddComponent/...) | 7165 | 7165 | ✓ | every target action must yield an entry or an explicit dynamic entry |

Stats: actions.ActivateComponent2=32, actions.ActivateGameObject=1629, actions.ActivateGameObjects=107, actions.AddComponent=587, actions.AddForce=90, actions.AddToFsmInt=68, actions.AnimateFloat=17, actions.AnimatorPlay=254, actions.ApplicationQuit=1, actions.ArrayAdd=30, actions.ArrayGetNext=27, actions.ArrayGetRandom=149, actions.ArrayListAdd=392, actions.ArrayListClear=1, actions.ArrayListFindGameObjectsByTag=2, actions.ArrayListGet=191, actions.ArrayListGetNext=18, actions.ArrayListGetRandom=46, actions.ArrayListGetRandomCurvedWeighted=12, actions.ArrayResize=3, actions.AudioListenerSetPause=4, actions.AudioListenerSetVolume=5, actions.AudioPlay=357, actions.AudioStop=440, actions.BoolAllTrue=291, actions.BoolTest=867, actions.CallMethod=107, actions.ColliderSetIsTrigger=104, actions.CollisionEvent=2, actions.CollisionEventPlus=28, actions.ConvertFloatToInt=262, actions.ConvertFloatToString=223, actions.ConvertIntToFloat=11, actions.ConvertIntToString=8, actions.CreateObject=531, actions.CurveFloat=6, actions.DeactivateSelf=7, actions.DestroyComponent=907, actions.DestroyObject=101, actions.DestroySelf=172, actions.dropped=0, actions.EaseFloat=2, actions.EnableCollider=364, actions.EnableFSM=3574, actions.EnableGUI=2, actions.ES3PlayMaker.CacheFile=2, actions.ES3PlayMaker.FileExists=3, actions.ES3PlayMaker.Load=511, actions.ES3PlayMaker.LoadAll=170, actions.ES3PlayMaker.LoadMultiple=11, actions.ES3PlayMaker.Save=500, actions.ES3PlayMaker.SaveAll=170, actions.ES3PlayMaker.SaveMultiple=11, actions.ES3PlayMaker.StoreCachedFile=2, actions.Explosion=9, actions.FindChild=66, actions.FindClosest=25, actions.FindGameObject=121, actions.FloatAdd=1149, actions.FloatChanged=71, actions.FloatClamp=530, actions.FloatCompare=1529, actions.FloatDivide=1, actions.FloatInterpolate=4, actions.FloatMultiply=364, actions.FloatOperator=273, actions.FloatRemap=68, actions.FloatSignTest=124, actions.FsmBoolTest=15, actions.FsmFloatAdd=535, actions.FsmHasVariable=3, actions.GameObjectCompareTag=454, actions.GameObjectHasChildren=1216, actions.GameObjectIsChildOf=676, actions.GameObjectIsNull=5, actions.GetAxis=99, actions.GetAxisKeyAxis=14, actions.GetAxisOrig=19, actions.GetButton=29, actions.GetButtonDown=1589, actions.GetButtonUp=107, actions.GetChild=3476, actions.GetChildCountAdvanced=313, actions.GetCollisionInfo=28, actions.GetComponent=1426, actions.GetDistance=121, actions.GetEventFloatData=11, actions.GetFPS=1, actions.GetFsmArray=9, actions.GetFsmArrayItem=13, actions.GetFsmBool=486, actions.GetFsmColor=1, actions.GetFsmFloat=1778, actions.GetFsmGameObject=573, actions.GetFsmInt=1629, actions.GetFsmObject=159, actions.GetFsmString=525, actions.GetFsmTexture=10, actions.GetFsmVector3=46, actions.GetGameObjectSibling=139, actions.GetGameObjectSpeed=50, actions.GetKeyDown=11, actions.GetKeyUp=3, actions.GetLayer=27, actions.GetMass=280, actions.GetMaterial=62, actions.GetMouseButtonDown=52, actions.GetMouseButtonUp=2, actions.GetMouseX=14, actions.GetName=880, actions.GetNextChild=146, actions.GetOwner=1632, actions.GetParent=2185, actions.GetPosition=1925, actions.GetProperty=294, actions.GetRandomChild=43, actions.GetRotation=455, actions.GetSpeed=118, actions.GetSystemDateTime=12, actions.GetTag=82, actions.GetVector3XYZ=124, actions.GetVelocity=23, actions.HasComponent=20, actions.HasRigidBody=280, actions.IntAdd=438, actions.IntChanged=3, actions.IntClamp=20, actions.IntCompare=1518, actions.IntOperator=204, actions.IsActive=6, actions.iTweenMoveTo=43, actions.iTweenPunchRotation=1, actions.iTweenRotateBy=1, actions.iTweenRotateTo=154, actions.iTweenStop=44, actions.LoadLevel=5, actions.LoadLevelNum=1, actions.LookAt=249, actions.Micosmo.SensorToolkit.PlayMaker.SensorGetDetectionRayHit=152, actions.Micosmo.SensorToolkit.PlayMaker.SensorGetDetections=210, actions.Micosmo.SensorToolkit.PlayMaker.SensorGetLineOfSightResult=19, actions.MouseLook=19, actions.MousePick=39, actions.MousePickEvent=239, actions.NextFrameEvent=1894, actions.particleSystemClear=52, actions.ParticleSystemEmit=11, actions.ParticleSystemPlay=152, actions.particleSystemSpeed=16, actions.ParticleSystemStop=149, actions.PerlinNoise=61, actions.PlayRandomSound=22, actions.PlaySound=1070, actions.RandomFloat=677, actions.RandomInt=260, actions.RandomWait=274, actions.Raycast=793, actions.RigidBodyMoveRotation=2, actions.Rotate=423, actions.ScaleTime=9, actions.SelectRandomFloat=51, actions.SelectRandomInt=205, actions.SendEvent=4483, actions.SetAngularVelocity=16, actions.SetAudioClip=375, actions.SetAudioLoop=146, actions.SetAudioPitch=110, actions.SetAudioVolume=20, actions.SetBoolValue=194, actions.SetCameraFOV=6, actions.SetFloatValue=495, actions.SetFsmArray=1, actions.SetFsmBool=318, actions.SetFsmFloat=1189, actions.SetFsmGameObject=62, actions.SetFsmInt=1123, actions.SetFsmObject=34, actions.SetFsmString=52, actions.SetGameObject=663, actions.SetIntValue=501, actions.SetIsKinematic=460, actions.SetLayer=1028, actions.SetLensDistortion=2, actions.SetLightColor=1, actions.SetLightCookie=1, actions.SetLightIntensity=87, actions.SetLightRange=1, actions.SetMass=280, actions.SetMaterial=176, actions.SetMesh=166, actions.SetMouseCursor=5, actions.SetName=389, actions.SetParent=967, actions.SetPosition=807, actions.SetProperty=3792, actions.SetRandomRotation=208, actions.SetRotation=722, actions.SetScale=17, actions.SetSphereColliderRadius=154, actions.SetStringValue=31, actions.SetTag=609, actions.setTextmeshProText=18, actions.setTextmeshProTextColor=44, actions.SetVector3XYZ=30, actions.SetVelocity=199, actions.SetVisibility=274, actions.SmoothLookAt=102, actions.SphereCast2=52, actions.Steam_GetAchievementInfo=11, actions.Steam_IsInitialized=11, actions.Steam_SetAchievement=11, actions.StringAppend=411, actions.StringAppend2=56, actions.StringCompare=513, actions.target=7165, actions.TransformDirection=158, actions.TranslatePosition=2, actions.TriggerEvent=1504, actions.TriggerEventByLayer=103, actions.TweenCamera=2, actions.TweenFloat=11, actions.UiGraphicSetColor=35, actions.UiInputFieldGetTextAsInt=1, actions.UiRawImageSetTexture=151, actions.UiSetIsInteractable=41, actions.UiSliderSetValue=22, actions.UiTextSetText=4528, actions.UiToggleGetIsOn=27, actions.UiToggleSetIsOn=54, actions.UseGravity=51, actions.Vector3AddXYZ=574, actions.Vector3Compare=51, actions.Vector3Interpolate=2, actions.Vector3Lerp=36, actions.Vector3Multiply=34, actions.Wait=2402, entries=172, fsms.failedUpstream=1, fsms.parsed=8128, params.fsmProperty.listed=4086, params.fsmProperty.referenced=4086, templates.parsed=0

Notes (1):

- FSM NOT PARSED by the upstream parser (listed, not silently dropped): level1:13456 size=6756 error='unpack_from requires a buffer of at least 6760 bytes for unpacking 4 bytes at of'

## Source: il

| check | raw | parsed | | method |
|---|---|---|---|---|
| watched TypeRef rows reached by the walk | 67 | 64 | ✗ | module TypeRef table (watched namespaces) vs refs seen in signatures/IL |
| watched MemberRef rows reached by the walk | 276 | 276 | ✓ | module MemberRef table (watched declaring types) vs refs seen in IL operands |

Stats: assemblies.scanned=105, entries=369, skipped.nwhInternal=4

Notes (3):

- TypeRef in the metadata table but not reached (Assembly-CSharp): NWH.VehiclePhysics2.Sound.SoundComponents.EngineRunningComponent
- TypeRef in the metadata table but not reached (Assembly-CSharp): UnityEngine.PhysicMaterial
- TypeRef in the metadata table but not reached (UnityEngine.UI): UnityEngine.Collider

## Source: mods

| check | raw | parsed | | method |
|---|---|---|---|---|
| watched TypeRef rows reached by the walk | 30 | 28 | ✗ | module TypeRef table (watched namespaces) vs refs seen in signatures/IL |
| watched MemberRef rows reached by the walk | 157 | 157 | ✓ | module MemberRef table (watched declaring types) vs refs seen in IL operands |

Stats: assemblies.scanned=1, entries=152

Notes (2):

- TypeRef in the metadata table but not reached (ApocalypterSteeringMod): NWH.WheelController3D.CamberController
- TypeRef in the metadata table but not reached (ApocalypterSteeringMod): NWH.VehiclePhysics2.Modules.ModuleManager

## Source: stubs

| check | raw | parsed | | method |
|---|---|---|---|---|
| public stub declarations (types + members) vs entries | 326 | 326 | ✓ | declarations counted while walking vs distinct entries (differs only if two declarations share a key, e.g. overloads with equal signatures) |

Stats: entries=326

## Cross-checks

### Stub drift (stub members not found in the real DLLs) — 18

The mod harness stubs claim a member the game does not have (a test-only helper, or a stale copy). Each must be explained or removed from the stubs.

- `member\|HutongGames.PlayMaker.PlayMakerFSM\|Fsm` (stubs×2) — type not found: HutongGames.PlayMaker.PlayMakerFSM
- `member\|NWH.Common.Vehicles.WheelUAPI\|StepCount` (stubs×2) — member not found: NWH.Common.Vehicles.WheelUAPI.StepCount (hop 1 of 1 in 'StepCount')
- `member\|NWH.VehiclePhysics2.Modules.ManagerVehicleComponent\|Components` (stubs×1) — type not found: NWH.VehiclePhysics2.Modules.ManagerVehicleComponent
- `member\|NWH.VehiclePhysics2.Modules.ManagerVehicleComponent\|OnboardEnablesState` (stubs×2) — type not found: NWH.VehiclePhysics2.Modules.ManagerVehicleComponent
- `member\|NWH.VehiclePhysics2.Powertrain.DifferentialComponent\|TypeAssignments` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.DifferentialComponent.TypeAssignments (hop 1 of 1 in 'TypeAssignments')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|AfterDelegateReassign` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.AfterDelegateReassign (hop 1 of 1 in 'AfterDelegateReassign')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|DeferShifts` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.DeferShifts (hop 1 of 1 in 'DeferShifts')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|HasNwhDelegate` (stubs×1) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.HasNwhDelegate (hop 1 of 1 in 'HasNwhDelegate')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|NwhAutoShifts` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.NwhAutoShifts (hop 1 of 1 in 'NwhAutoShifts')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|PendingTarget` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.PendingTarget (hop 1 of 1 in 'PendingTarget')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|TestReferenceRpm` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.TestReferenceRpm (hop 1 of 1 in 'TestReferenceRpm')
- `member\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|TestRpmPerMps` (stubs×2) — member not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent.TestRpmPerMps (hop 1 of 1 in 'TestRpmPerMps')
- `method\|NWH.Common.Vehicles.WheelUAPI\|SetLateralSlip(System.Single)` (stubs×1) — method not found: NWH.Common.Vehicles.WheelUAPI::SetLateralSlip(System.Single)
- `method\|NWH.VehiclePhysics2.Modules.ManagerVehicleComponent\|AddAndOnboardNewComponent(NWH.VehiclePhysics2.VehicleComponent)` (stubs×1) — type not found: NWH.VehiclePhysics2.Modules.ManagerVehicleComponent
- `method\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|CompletePendingShift()` (stubs×1) — method not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent::CompletePendingShift()
- `method\|NWH.VehiclePhysics2.Powertrain.TransmissionComponent\|SimulateForwardStep()` (stubs×1) — method not found: NWH.VehiclePhysics2.Powertrain.TransmissionComponent::SimulateForwardStep()
- `type\|HutongGames.PlayMaker.PlayMakerFSM\|` (stubs×1) — type not found: HutongGames.PlayMaker.PlayMakerFSM
- `type\|NWH.VehiclePhysics2.Modules.ManagerVehicleComponent\|` (stubs×1) — type not found: NWH.VehiclePhysics2.Modules.ManagerVehicleComponent

### Stub gaps (NWH members the mod DLLs use that the stubs do not declare) — 2

The mod harness cannot test these. Empty unless a mod DLL was scanned.

- `member\|NWH.VehiclePhysics2.ManagerVehicleComponent\|Components` (mods×3)
- `method\|NWH.VehiclePhysics2.ManagerVehicleComponent\|AddAndOnboardNewComponent(NWH.VehiclePhysics2.VehicleComponent)` (mods×1)

### FSM references that do not resolve statically — 0

PlayMaker resolves these at runtime too; if they do not resolve here they likely fail in game (or the parse is wrong).


### Runtime-only references (ACCEPTANCE: must be zero or explained) — 0

Seen by the runtime census but by no static source — a dynamic name, or a hole in a static source.


### References the game itself failed to resolve at runtime — 0

PlayMaker asked for these and got nothing back.


## Acceptance checklist (SPEC §5 M0a)

- [ ] every reconciliation row balances (2 unbalanced)
- [ ] runtime census adds zero unexpected entries (runtime log not supplied yet)
- [ ] contract tests: positive control green against the real NWH DLLs, negative controls (a) and (b) red — contract tests: 9 passed, 0 failed
