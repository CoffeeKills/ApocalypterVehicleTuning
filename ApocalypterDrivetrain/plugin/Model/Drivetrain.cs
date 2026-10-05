// The validated drivetrain object graph. Built only by DrivetrainBuilder; every instance has
// passed validation. Pure C#: no UnityEngine, no NWH (verify/run.sh compiles Model/ alone to
// prove it). Wheel placement uses SPO.Vehicle.Vec3 (body-local, metres).
using SPO.Vehicle;

namespace ApocalypterDrivetrain.Model
{
    public sealed class EngineDef
    {
        public float IdleRpm, RedlineRpm, RevLimitRpm;
        public float RevLimiterCutTime;   // s: fuel cut duration when the limiter trips
        public float Inertia;             // kg·m²
        public PiecewiseLinear TorqueCurve;   // rpm -> N·m, covers [IdleRpm, RevLimitRpm]
        public string TorqueExpression;       // source text when the curve came from an expression, else null
    }

    public sealed class LaunchDef
    {
        public float Rpm;   // engine speed the clutch holds for a launch
    }

    public sealed class ClutchDef
    {
        public float Capacity;          // N·m transmissible when fully engaged (NWH slipTorque)
        public float EngagementRpm;     // NWH engagementRPM
        public float EngagementRange;   // rpm over which automatic engagement completes
        public PiecewiseLinear EngagementCurve;   // clutch input 0..1 -> engagement 0..1
        public LaunchDef Launch;        // null = no launch profile (documented)
    }

    public sealed class GearboxDef
    {
        public float[] Forward;   // gear 1..n, all > 0
        public float[] Reverse;   // R1..Rn, all < 0 (may be empty)
        public float FinalDrive;
        public string ShiftLogicName;
        public IShiftLogic ShiftLogic;
        public bool HasShiftRpms;
        public float UpshiftRpm, DownshiftRpm;
        public float ShiftTime;   // s

        public int MinGear { get { return -Reverse.Length; } }
        public int MaxGear { get { return Forward.Length; } }

        /// <summary>Ratio of a gear (R = negative, 0 = neutral). Out of range returns 0 (neutral), never throws.</summary>
        public float RatioOf(int gear)
        {
            if (gear > 0 && gear <= Forward.Length) return Forward[gear - 1];
            if (gear < 0 && -gear <= Reverse.Length) return Reverse[-gear - 1];
            return 0f;
        }

        /// <summary>
        /// NWH's TransmissionComponent.gears layout (README fact 6, census B2):
        /// [R_n … R2, R1, 0, F1 … F_m], so that Gear = gearIndex - reverseGearCount.
        /// </summary>
        public float[] ToNwhGearArray()
        {
            int r = Reverse.Length;
            var a = new float[r + 1 + Forward.Length];
            for (int i = 0; i < r; i++) a[i] = Reverse[r - 1 - i];
            a[r] = 0f;
            for (int i = 0; i < Forward.Length; i++) a[r + 1 + i] = Forward[i];
            return a;
        }
    }

    public enum DiffType { Open, Locked, LimitedSlip, TorqueSplit }

    public sealed class DifferentialDef
    {
        public string Name;
        public DiffType Type;
        public float Split;        // TorqueSplit: share of torque to outputs[0] (NWH biasAB = 1 - Split)
        public float Stiffness;    // Locked, LimitedSlip
        public float SlipTorque;   // LimitedSlip, N·m
        public float PowerRamp, CoastRamp;   // LimitedSlip, 0..1
        public int Node;
    }

    public enum WheelSide { Left, Right }

    public sealed class WheelDef
    {
        public string Name;        // "<axle>.L" / "<axle>.R"
        public AxleDef Axle;
        public WheelSide Side;
        public Vec3 Position;      // body-local: X right, Y up (0 = axle height), Z forward
        public bool Driven;        // reachable from the gearbox through the wiring
        public int Node;
    }

    public sealed class AxleDef
    {
        public string Name;
        public int Index;          // order in the config
        public float Z, Track;
        public bool Steered, Lift;
        public WheelDef Left, Right;
        public bool Driven { get { return Left.Driven || Right.Driven; } }
    }

    public enum NodeKind { Gearbox, Differential, Wheel }

    public sealed class PowertrainNode
    {
        public NodeKind Kind;
        public string Name;
        public int Input = -1;          // feeding node, -1 = none (gearbox, or unfed)
        public int[] Outputs;           // gearbox: 1, differential: 2, wheel: 0
        public bool Reachable;          // reachable from the gearbox
    }

    public sealed class Drivetrain
    {
        public int Schema;
        public string Name;
        public EngineDef Engine;
        public ClutchDef Clutch;
        public GearboxDef Gearbox;
        public DifferentialDef[] Differentials;
        public AxleDef[] Axles;
        public WheelDef[] Wheels;
        /// <summary>Node 0 is the gearbox; then differentials, then wheels. Acyclic, each node fed at most once.</summary>
        public PowertrainNode[] Nodes;

        public int DrivenWheelCount
        {
            get { int n = 0; foreach (WheelDef w in Wheels) if (w.Driven) n++; return n; }
        }

        public string Summary()
        {
            return "'" + Name + "': " + Axles.Length + " axle(s), " + DrivenWheelCount + "/" + Wheels.Length
                + " wheels driven, " + Differentials.Length + " differential(s), " + Gearbox.Forward.Length
                + "F/" + Gearbox.Reverse.Length + "R gears, shift logic '" + Gearbox.ShiftLogicName + "'";
        }
    }
}
