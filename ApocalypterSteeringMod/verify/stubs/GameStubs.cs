// Compile-only stubs of the game types the mod touches. Member names, types and
// accessibility copied from the decompiled game code in gamecode/. Where a body
// matters to a test it mirrors the real body (cited by file:line).
#pragma warning disable CS0169, CS0414, CS0649, CS1591
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NWH.Common.Vehicles
{
    public abstract class Vehicle : MonoBehaviour
    {
        public static List<Vehicle> ActiveVehicles = new List<Vehicle>();
        public Rigidbody vehicleRigidbody;
        public float Speed { get; set; }
        public float LocalForwardVelocity { get; set; }

        // Vehicle.cs:47
        public static Vehicle ActiveVehicle
        {
            get
            {
                int count = ActiveVehicles.Count;
                if (count == 0)
                {
                    return null;
                }
                return ActiveVehicles[count - 1];
            }
        }
    }

    public abstract class WheelUAPI : MonoBehaviour
    {
        public abstract float SteerAngle { get; set; }
        public abstract float SpringMaxLength { get; set; }
        public abstract float SpringMaxForce { get; set; }
        public abstract float DamperBumpRate { get; set; }
        public abstract float DamperReboundRate { get; set; }
        public abstract float LongitudinalFrictionGrip { get; set; }
        public abstract float LateralFrictionGrip { get; set; }
        public abstract float LongitudinalFrictionStiffness { get; set; }
        public abstract float LateralFrictionStiffness { get; set; }
        public abstract bool IsGrounded { get; }
        public abstract float LongitudinalSlip { get; }

        // WheelUAPI.cs:39 / :93 / :47 declare these ABSTRACT. They are virtual here only so
        // the 0.5.0 steering-prefix suite's FakeWheel (which must not change) still compiles;
        // the mod reads/writes them through the base type exactly as against the real API.
        // Camber mirrors WheelController's setter clamp to +-16 deg (WheelController.cs:292-312).
        private float _camber;
        public virtual float Camber
        {
            get { return _camber; }
            set { _camber = value < -16f ? -16f : (value > 16f ? 16f : value); }
        }
        private float _lateralSlip;
        public virtual float LateralSlip { get { return _lateralSlip; } }
        public void SetLateralSlip(float v) { _lateralSlip = v; }   // test hook
        public virtual float SpringLength { get { return 0f; } }
    }
}

namespace NWH.VehiclePhysics2.Input
{
    public class VehicleInputHandler : NWH.VehiclePhysics2.VehicleComponent
    {
        public float Steering { get; set; }
        public float Throttle { get; set; }     // VehicleInputHandler.cs:74 (0..1 clamp)
        public float Brakes { get; set; }       // VehicleInputHandler.cs:88 (0..1 clamp)
        private float _handbrake;
        public float Handbrake
        {
            get { return _handbrake; }
            set { _handbrake = value < 0f ? 0f : (value > 1f ? 1f : value); }   // VehicleInputHandler.cs:158
        }
    }
}

namespace NWH.VehiclePhysics2.Powertrain
{
    // PowertrainComponent itself is not in gamecode/; ClutchComponent.cs:10 / :95 / :108
    // show the base type and its OutputRPM member. WheelComponent / DifferentialComponent /
    // Engine / Clutch / Transmission all derive from it (WheelComponent.cs:10,
    // DifferentialComponent.cs:8, ClutchComponent.cs:10, TransmissionComponent.cs:14).
    public class PowertrainComponent : NWH.VehiclePhysics2.VehicleComponent
    {
        public float OutputRPM { get; set; }
        public float InputRPM { get; set; }
        protected float _damage;
    }

    public class WheelComponent : PowertrainComponent
    {
        public NWH.Common.Vehicles.WheelUAPI wheelUAPI;
    }

    public class Powertrain
    {
        public ClutchComponent clutch = new ClutchComponent();   // Powertrain.cs:13
        public List<NWH.VehiclePhysics2.Powertrain.Wheel.WheelGroup> wheelGroups = new List<NWH.VehiclePhysics2.Powertrain.Wheel.WheelGroup>();
        public List<WheelComponent> wheels = new List<WheelComponent>();
        public EngineComponent engine = new EngineComponent();
        public TransmissionComponent transmission = new TransmissionComponent();
        public List<DifferentialComponent> differentials = new List<DifferentialComponent>();
    }

    public class EngineComponent : PowertrainComponent
    {
        public delegate float PowerModifier();

        public float maxPower = 120f;
        public float revLimiterRPM = 4700f;
        public float idleRPM = 900f;   // EngineComponent.cs:120
        public float engineLossPercent = 0.25f;
        public List<PowerModifier> powerModifiers = new List<PowerModifier>();
        public ForcedInduction forcedInduction = new ForcedInduction();

        public class ForcedInduction
        {
            public float powerGainMultiplier = 1.4f;
        }
    }

    // ClutchComponent.cs
    public class ClutchComponent : PowertrainComponent
    {
        public enum ClutchControlType { Automatic, UserInput, Manual }
        public float engagementRPM = 1200f;
        public float throttleEngagementOffsetRPM = 400f;
        public float clutchInput;
        public AnimationCurve engagementCurve = new AnimationCurve();
        public ClutchControlType controlType;
        public float engagementRange = 400f;
        public float slipTorque = 500f;
        public float creepTorque;
        public float creepSpeedLimit = 1f;
        private float _clutchEngagement;
        public float Engagement { get { return _clutchEngagement; } }
    }

    public class TransmissionComponent : PowertrainComponent
    {
        // TransmissionComponent.cs:25
        public enum TransmissionShiftType { Manual, Automatic, AutomaticSequential_Obsolete, CVT, External }

        public float finalGearRatio = 6f;
        public float shiftDuration = 0.2f;
        public List<float> gears = new List<float> { -2.216f, 0f, 3.274f, 2.093f, 1.439f, 1.084f, 0.817f };
        public int forwardGearCount;
        public int reverseGearCount;
        public float postShiftBan = 0.5f;
        public float variableShiftIntensity = 0.3f;
        public bool isPostShiftBanActive;
        public bool isShifting;
        public float shiftProgress;
        public TransmissionShiftType transmissionType = TransmissionShiftType.Automatic;
        public int gearIndex;
        private float _upshiftRPM = 2800f;
        private float _downshiftRPM = 1400f;
        private readonly Dictionary<int, string> _gearNameCache = new Dictionary<int, string>();

        public TransmissionComponent()
        {
            UpdateGearCounts();
            Gear = 0;   // VC_Initialize, :221
        }

        public float UpshiftRPM
        {
            get { return _upshiftRPM; }
            set { _upshiftRPM = value < 0f ? 0f : value; }
        }

        public float DownshiftRPM
        {
            get { return _downshiftRPM; }
            set { _downshiftRPM = value < 0f ? 0f : value; }
        }

        // :184 — GearToIndex/IndexToGear use reverseGearCount (:698-706).
        public int Gear
        {
            get { return gearIndex - reverseGearCount; }
            set { gearIndex = value + reverseGearCount; }
        }

        // :196
        public string GearName
        {
            get
            {
                int gear = Gear;
                string value;
                if (_gearNameCache.TryGetValue(gear, out value))
                {
                    return value;
                }
                value = gear != 0 ? (gear <= 0 ? ("R" + -gear) : gear.ToString()) : "N";
                _gearNameCache[gear] = value;
                return value;
            }
        }

        // :382 (private in the game; the stub calls it from SimulateForwardStep)
        private void UpdateGearCounts()
        {
            forwardGearCount = 0;
            reverseGearCount = 0;
            for (int i = 0; i < gears.Count; i++)
            {
                if (gears[i] > 0f) forwardGearCount++;
                else if (gears[i] < 0f) reverseGearCount++;
            }
        }

        // :436 — refuses while shifting, during the post-shift ban (unless to/from N) and
        // at full damage; instant does NOT bypass the ban. The coroutine is collapsed to its
        // synchronous effect (Gear = target) or, with ShiftHook set, deferred to the test.
        public static bool DeferShifts;   // test hook: leave the shift "in flight"
        public int PendingTarget = int.MinValue;
        public void ShiftInto(int targetGear, bool instant = false)
        {
            int gear = Gear;
            bool flag = targetGear == 0 || gear == 0;
            if (targetGear == gear || targetGear < -100 || _damage == 1f)
            {
                return;
            }
            int num = targetGear + reverseGearCount;
            if (num >= 0 && num < gears.Count && !isShifting && (flag || !isPostShiftBanActive))
            {
                if (DeferShifts)
                {
                    isShifting = true;
                    PendingTarget = targetGear;
                    return;
                }
                Gear = targetGear;
            }
        }

        /// <summary>Test hook: the shift coroutine reaching its "Gear = targetGear" line.</summary>
        public void CompletePendingShift()
        {
            if (PendingTarget != int.MinValue)
            {
                Gear = PendingTarget;
                PendingTarget = int.MinValue;
            }
            isShifting = false;
        }

        /// <summary>
        /// Test hook mirroring ForwardStep's start (:410 UpdateGearCounts, :416
        /// CalculateTotalGearRatio = gears[gearIndex] UNGUARDED, :311-322). Throws
        /// ArgumentOutOfRangeException exactly where the game would.
        /// </summary>
        public float SimulateForwardStep()
        {
            UpdateGearCounts();
            return gears[gearIndex] * finalGearRatio;
        }
    }

    public class DifferentialComponent : PowertrainComponent
    {
        public enum Type { Open, Locked, LimitedSlip, External }

        private Type _differentialType = Type.Open;
        // Test hook: counts setter calls (the real setter allocates a new split delegate).
        public int TypeAssignments;
        public Type DifferentialType { get { return _differentialType; } set { _differentialType = value; TypeAssignments++; } }
        public float biasAB = 0.5f;
        public float stiffness = 0.5f;
        public PowertrainComponent OutputB { get; set; }   // DifferentialComponent.cs:78
    }
}

namespace NWH.VehiclePhysics2.Powertrain.Wheel
{
    // WheelGroup.cs
    public class WheelGroup
    {
        public bool addAckerman = true;
        public float antiRollBarForce;
        public float brakeCoefficient = 1f;
        public float handbrakeCoefficient = 1f;
        public bool isSolid;
        public float trackWidth;
        public float steerCoefficient;
        public bool applyCasterAngle = true;
        private float _casterAngle;
        public bool applyToeAngle = true;
        private float _toeAngle;
        private List<WheelComponent> wheels = new List<WheelComponent>();
        private float _camber;

        // :67-91 — setters re-apply the geometry immediately, no runtime clamp.
        public float ToeAngle
        {
            get { return _toeAngle; }
            set { _toeAngle = value; ApplyGeometryValues(); }
        }

        public float CasterAngle
        {
            get { return _casterAngle; }
            set { _casterAngle = value; ApplyGeometryValues(); }
        }

        public WheelComponent LeftWheel { get { return wheels.Count != 0 ? wheels[0] : null; } }
        public WheelComponent RightWheel { get { return wheels.Count > 1 ? wheels[1] : null; } }
        public List<WheelComponent> Wheels { get { return wheels; } }

        // :151-168 — the solid-axle camber overwrite (ARB part omitted).
        public void Update()
        {
            int count = wheels.Count;
            if (isSolid && count == 2 && trackWidth != 0f)
            {
                WheelComponent a = wheels[0];
                WheelComponent b = wheels[1];
                float y = b.wheelUAPI.SpringLength - a.wheelUAPI.SpringLength;
                _camber = Mathf.Atan2(y, trackWidth) * 57.29578f;
                a.wheelUAPI.Camber = 0f - _camber;
                b.wheelUAPI.Camber = _camber;
            }
        }

        // :188-205 verbatim.
        public void ApplyGeometryValues()
        {
            foreach (WheelComponent wheel in Wheels)
            {
                if (applyCasterAngle || applyToeAngle)
                {
                    Vector3 e = wheel.wheelUAPI.transform.localEulerAngles;
                    if (wheel.wheelUAPI.transform.localPosition.x >= 0f)
                    {
                        wheel.wheelUAPI.transform.localEulerAngles = new Vector3(applyCasterAngle ? (0f - _casterAngle) : e.x, applyToeAngle ? (0f - _toeAngle) : e.y, e.z);
                    }
                    else
                    {
                        wheel.wheelUAPI.transform.localEulerAngles = new Vector3(applyCasterAngle ? (0f - _casterAngle) : e.x, applyToeAngle ? _toeAngle : e.y, e.z);
                    }
                }
            }
        }
    }
}

namespace NWH.VehiclePhysics2
{
    public class StateDefinition
    {
        public bool isEnabled;
        public bool initialized;
        public int lodIndex = -1;
    }

    public abstract class VehicleComponent
    {
        public VehicleController vehicleController;
        public StateDefinition state = new StateDefinition();
        // VehicleComponent.cs:18 — enabled AND initialised.
        public bool IsActive { get { return state.isEnabled && state.initialized; } }

        // Simplified VehicleComponent.cs:54/73 (the calledByParent bookkeeping is omitted).
        public virtual bool VC_Enable(bool calledByParent) { state.initialized = true; state.isEnabled = true; return true; }
        public virtual bool VC_Disable(bool calledByParent)
        {
            if (!state.initialized || !state.isEnabled) return false;
            state.isEnabled = false;
            return true;
        }
        public virtual void VC_LoadStateFromStateSettings() { }
        public virtual void UpdateLOD() { }
    }

    public class VehicleController : NWH.Common.Vehicles.Vehicle
    {
        public NWH.VehiclePhysics2.Input.VehicleInputHandler input = new NWH.VehiclePhysics2.Input.VehicleInputHandler();
        public NWH.VehiclePhysics2.Powertrain.Powertrain powertrain = new NWH.VehiclePhysics2.Powertrain.Powertrain();
        public Steering steering = new Steering();
        public Brakes brakes = new Brakes();
        public NWH.VehiclePhysics2.Modules.ModuleManager moduleManager = new NWH.VehiclePhysics2.Modules.ModuleManager();
        public float wheelbase = -1f;
        public float fixedDeltaTime = 0.02f;
    }

    public class Brakes
    {
        public delegate float BrakeTorqueModifier();

        public float maxTorque = 7000f;
        public float actuationTime = 0.1f;
        public float handbrakeValue;   // Brakes.cs:50 (latched value with HandbrakeType.Latching)
        public List<BrakeTorqueModifier> brakeTorqueModifiers = new List<BrakeTorqueModifier>();
    }

    public class Steering : VehicleComponent
    {
        public float degreesPerSecondLimit = 180f;
        public bool useRawInput;
        public AnimationCurve linearity;
        public float maximumSteerAngle = 25f;
        public bool returnToCenter = true;
        public AnimationCurve speedSensitiveSteeringCurve;
        public AnimationCurve speedSensitiveSmoothingCurve;
        private float _steerVelocity;
        private float _targetAngle;
        public float angle;
        public float externallyAddedAngle;
        public virtual void CalculateSteerAngles() { }
    }
}

namespace NWH.VehiclePhysics2.Modules
{
    public class ManagerVehicleComponent : NWH.VehiclePhysics2.VehicleComponent
    {
        public virtual List<NWH.VehiclePhysics2.VehicleComponent> Components { get; } = new List<NWH.VehiclePhysics2.VehicleComponent>();

        // ManagerVehicleComponent.cs:30. Test hook: OnboardEnablesState simulates a vehicle
        // whose state settings define the module as enabled (isEnabled = true loaded from
        // the definition file, but NOT initialised).
        public static bool OnboardEnablesState;

        public void AddAndOnboardNewComponent(NWH.VehiclePhysics2.VehicleComponent component)
        {
            Components.Add(component);
            component.vehicleController = vehicleController;
            component.VC_LoadStateFromStateSettings();
            if (OnboardEnablesState) component.state.isEnabled = true;
            component.UpdateLOD();
        }
    }

    public class ModuleManager : ManagerVehicleComponent { }
}

namespace NWH.VehiclePhysics2.Modules.Aerodynamics
{
    public class DownforcePoint
    {
        public float maxForce;
        public Vector3 position;
    }

    public class AerodynamicsModule : NWH.VehiclePhysics2.VehicleComponent
    {
        public Vector3 dimensions = new Vector3(2f, 1.5f, 4.5f);
        public float frontalCd = 0.35f;
        public float sideCd = 1.05f;
        public bool simulateDrag = true;
        public bool simulateDownforce;
        public float maxDownforceSpeed = 80f;
        public List<DownforcePoint> downforcePoints = new List<DownforcePoint>();
    }
}

namespace NWH.WheelController3D
{
    public class TyreWear : MonoBehaviour { }

    // CamberController.cs — overwrites WheelController.Camber every FixedUpdate.
    public class CamberController : MonoBehaviour
    {
        public AnimationCurve camberCurve;
    }
}

namespace HutongGames.PlayMaker
{
    public abstract class FsmStateAction
    {
        public bool Finished { get; private set; }   // test visibility of Finish()
        public virtual void OnEnter() { }
        public virtual void OnUpdate() { }
        public virtual void OnFixedUpdate() { }
        public virtual void OnLateUpdate() { }
        public void Finish() { Finished = true; }
    }

    public class FsmString
    {
        public string Value { get; set; }
        public FsmString() { }
        public FsmString(string v) { Value = v; }
    }

    public class FsmBool
    {
        public bool Value { get; set; }
    }

    public class FsmFloat
    {
        public float Value { get; set; }
    }
}

// The game's HutongGames forks (Assembly-CSharp). Only the shape the blocker needs:
// the class names and their public FsmString name fields (buttonName / axisName).
namespace HutongGames.PlayMaker.Actions
{
    using HutongGames.PlayMaker;

    public class GetButton : FsmStateAction
    {
        public FsmString buttonName = new FsmString();
        public FsmBool storeResult = new FsmBool();
        public bool everyFrame;
        public override void OnEnter() { storeResult.Value = InsaneSystems.InputManager.InputController.GetKeyActionIsActive(buttonName.Value); if (!everyFrame) Finish(); }
        public override void OnUpdate() { storeResult.Value = InsaneSystems.InputManager.InputController.GetKeyActionIsActive(buttonName.Value); }
    }

    public class GetButtonDown : FsmStateAction
    {
        public FsmString buttonName = new FsmString();
    }

    public class GetAxis : FsmStateAction
    {
        public FsmString axisName = new FsmString();
        public FsmFloat store = new FsmFloat();
        public bool everyFrame;
        public override void OnUpdate() { store.Value = InsaneSystems.InputManager.InputController.GetAnyAxisActionValue(axisName.Value); }
    }

    public class GetAxisKeyAxis : FsmStateAction
    {
        public FsmString axisName = new FsmString();
    }

    // The renamed original: Unity Input.GetAxis("Mouse X"/"Mouse Y"/scroll) directly.
    public class GetAxisOrig : FsmStateAction
    {
        public FsmString axisName = new FsmString();
        public bool everyFrame;
    }

    public class AnyKey : FsmStateAction { }

    public class MouseLook : FsmStateAction
    {
        public bool everyFrame = true;
    }

    public class GetKeyDown : FsmStateAction { }
}

namespace InsaneSystems.InputManager
{
    // InputController.cs (static facade over InputStorage).
    public static class InputController
    {
        public static bool GetKeyActionIsActive(string actionName) { return GetKeyAction(actionName).IsActive(); }
        public static bool GetKeyActionIsDown(string actionName) { return GetKeyAction(actionName).IsDown(); }
        public static bool GetKeyActionIsUp(string actionName) { return GetKeyAction(actionName).IsUp(); }
        public static float GetAxisActionValue(string actionName) { return GetAxisAction(actionName).GetValue(); }
        public static float GetKeyAxisActionValue(string actionName) { return GetKeyAxisAction(actionName).GetValue(); }
        public static float GetAnyAxisActionValue(string actionName)
        {
            float v = GetAxisActionValue(actionName);
            if (v != 0f) return v;
            return GetKeyAxisActionValue(actionName);
        }
        public static AxisAction GetAxisAction(string actionName) { return InputStorage.Singleton.GetAxisByName(actionName); }
        public static KeyAxisAction GetKeyAxisAction(string actionName) { return InputStorage.Singleton.GetKeyAxisByName(actionName); }
        public static KeyAction GetKeyAction(string actionName) { return InputStorage.Singleton.GetKeyByName(actionName); }
    }

    public abstract class InputAction
    {
        public string Name;
        public virtual bool IsActive() { return false; }
    }

    public class KeyAction : InputAction
    {
        public bool IsDown() { return false; }
        public bool IsUp() { return false; }
    }

    public class AxisAction : InputAction { public float GetValue() { return 0f; } }
    public class KeyAxisAction : InputAction { public float GetValue() { return 0f; } }

    // InputStorage.cs:62-72 — unknown names THROW (NullReferenceException).
    public class InputStorage
    {
        public static readonly InputStorage Singleton = new InputStorage();
        public readonly List<KeyAction> keys = new List<KeyAction>();
        public readonly List<AxisAction> axis = new List<AxisAction>();
        public readonly List<KeyAxisAction> keyAxes = new List<KeyAxisAction>();

        public KeyAction GetKeyByName(string name)
        {
            for (int i = 0; i < keys.Count; i++) if (keys[i].Name == name) return keys[i];
            throw new NullReferenceException("No key " + name + " found!");
        }

        public AxisAction GetAxisByName(string name)
        {
            for (int i = 0; i < axis.Count; i++) if (axis[i].Name == name) return axis[i];
            throw new NullReferenceException("No axis " + name + " found!");
        }

        public KeyAxisAction GetKeyAxisByName(string name)
        {
            for (int i = 0; i < keyAxes.Count; i++) if (keyAxes[i].Name == name) return keyAxes[i];
            throw new NullReferenceException("No key axis " + name + " found!");
        }
    }
}

public class PlayMakerFSM : MonoBehaviour { }

public class PlayMakerArrayListProxy : MonoBehaviour { }

public static class ES3
{
    public static T Load<T>(string key, string filePath, T defaultValue) { return defaultValue; }
}
