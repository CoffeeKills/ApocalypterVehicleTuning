// Compile-only stubs of the game types the mod touches. Member names, types and
// accessibility copied from the decompiled game code in gamecode/.
#pragma warning disable CS0169, CS0414, CS0649, CS1591
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
    }
}

namespace NWH.VehiclePhysics2.Input
{
    public class VehicleInputHandler : NWH.VehiclePhysics2.VehicleComponent
    {
        public float Steering { get; set; }
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
    // PowertrainComponent itself is not in gamecode/; the mod never names it. Only the
    // inheritance (WheelComponent / DifferentialComponent : PowertrainComponent, see
    // WheelComponent.cs:10, DifferentialComponent.cs:8) and OutputB's type matter.
    public class PowertrainComponent : NWH.VehiclePhysics2.VehicleComponent { }

    public class WheelComponent : PowertrainComponent
    {
        public NWH.Common.Vehicles.WheelUAPI wheelUAPI;
    }

    public class Powertrain
    {
        public List<NWH.VehiclePhysics2.Powertrain.Wheel.WheelGroup> wheelGroups = new List<NWH.VehiclePhysics2.Powertrain.Wheel.WheelGroup>();
        public List<WheelComponent> wheels = new List<WheelComponent>();
        public EngineComponent engine = new EngineComponent();
        public TransmissionComponent transmission = new TransmissionComponent();
        public List<DifferentialComponent> differentials = new List<DifferentialComponent>();
    }

    public class EngineComponent
    {
        public delegate float PowerModifier();

        public float maxPower = 120f;
        public float revLimiterRPM = 4700f;
        public float engineLossPercent = 0.25f;
        public List<PowerModifier> powerModifiers = new List<PowerModifier>();
        public ForcedInduction forcedInduction = new ForcedInduction();

        public class ForcedInduction
        {
            public float powerGainMultiplier = 1.4f;
        }
    }

    public class TransmissionComponent
    {
        public float finalGearRatio = 6f;
        public float shiftDuration = 0.2f;
        private float _upshiftRPM = 2800f;
        private float _downshiftRPM = 1400f;

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
    public class WheelGroup
    {
        public bool addAckerman = true;
        public float antiRollBarForce;
        public float trackWidth;
        public float steerCoefficient;
        public float brakeCoefficient = 1f;
        public float handbrakeCoefficient = 1f;
        private List<WheelComponent> wheels = new List<WheelComponent>();
        public WheelComponent LeftWheel { get { return null; } }
        public WheelComponent RightWheel { get { return null; } }
        public List<WheelComponent> Wheels { get { return wheels; } }
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
}

namespace HutongGames.PlayMaker
{
    public abstract class FsmStateAction
    {
        public virtual void OnUpdate() { }
        public virtual void OnFixedUpdate() { }
        public virtual void OnLateUpdate() { }
    }
}

public class PlayMakerFSM : MonoBehaviour { }

public class PlayMakerArrayListProxy : MonoBehaviour { }

public static class ES3
{
    public static T Load<T>(string key, string filePath, T defaultValue) { return defaultValue; }
}
