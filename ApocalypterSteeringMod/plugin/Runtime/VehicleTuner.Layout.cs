using System.Collections.Generic;
using System.Text;
using ApocalypterSteeringMod.Settings;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Custom drivetrain layouts (0.6.2, part of the Drivetrain category): the gearbox drives a
    /// tree of mod-owned differentials that ends at the wheels the layout names.
    ///
    /// Why this is safe to do live (verified in gamecode/): NWH steps the powertrain every
    /// physics tick by recursing through each component's <c>_output</c>/<c>_outputB</c> object
    /// references (PowertrainComponent.ForwardStep/QueryAngularVelocity/QueryInertia,
    /// DifferentialComponent.cs:192-234), and the public Output/OutputB setters change those
    /// references immediately. The name hashes are only zero-checked while stepping and used to
    /// look components up once in VC_Initialize.
    ///
    /// Hazards handled here:
    ///  - A new DifferentialComponent has no split delegate until DifferentialType is assigned
    ///    (DifferentialComponent.cs:65) — ForwardStep would throw every tick. Assigned always.
    ///  - WheelComponent.ForwardStep sets wheelUAPI.AutoSimulate = false (:89): a wheel that stops
    ///    being driven is never simulated again (no suspension, no tyre forces). Released wheels get
    ///    AutoSimulate = true, MotorTorque = 0 and their own inertia back.
    ///  - Cycles / double-fed wheels: rejected by DrivetrainLayout's tree check.
    ///  - Saves: if the game serialises powertrain fields, a stored hash naming one of our nodes
    ///    would fail to resolve on load (null _output, non-zero hash → NullReferenceException every
    ///    tick). The stock components keep their captured hash values while a layout is applied;
    ///    only the object references move. Our nodes are never added to powertrain.differentials.
    ///  - The vehicle's own diffs are bypassed, not modified: the Drivetrain diff-mode code keeps
    ///    working on them, and OFF restores the exact references and hashes.
    /// </summary>
    public sealed partial class VehicleTuner
    {
        private const float AxleTolerance = 0.2f;       // NWH groups wheels into axles at 0.2 m (Powertrain.cs:76)
        private const float CentreTolerance = 0.01f;    // NWH's own left/right threshold (Powertrain.cs:155-160)
        private const float NodeInertia = 0.02f;        // NWH's DifferentialComponent default (VC_SetDefaults)

        internal sealed class LayoutData
        {
            // Stock wiring (captured with the drivetrain baseline).
            public TransmissionComponent Transmission;
            public PowertrainComponent TransOut;
            public int TransOutHash;
            public PowertrainComponent TransOutInput;
            public int TransOutInputHash;
            public WheelComponent[] Wheels;
            public PowertrainComponent[] WheelInput;
            public int[] WheelInputHash;
            public float[] WheelInertia;     // the wheel's own inertia (without reflected drivetrain inertia)
            public bool[] StockDriven;
            public int[] WheelAxle;          // 1-based from the front
            public char[] WheelSide;         // 'L', 'R', 'C'
            public int AxleCount;

            // The layout resolved for this vehicle (rebuilt only when the layout object changes).
            public DrivetrainLayout Resolved;
            public string ResolveError;      // null = resolved OK
            public DifferentialComponent[] Nodes;
            public PowertrainComponent[] OutA, OutB;
            public PowertrainComponent Root;
            public bool[] LayoutDriven;

            public bool Applied;
        }

        // ------------------------------------------------------------------ capture

        private static LayoutData CaptureLayout(VehicleController vc)
        {
            Powertrain pt = vc.powertrain;
            if (pt == null || pt.transmission == null || pt.wheels == null || pt.wheels.Count == 0)
            {
                return null;
            }
            var d = new LayoutData();
            TransmissionComponent t = pt.transmission;
            d.Transmission = t;
            d.TransOut = t.Output;
            d.TransOutHash = t.outputNameHash;
            if (d.TransOut != null)
            {
                d.TransOutInput = d.TransOut.Input;
                d.TransOutInputHash = d.TransOut.inputNameHash;
            }

            int n = pt.wheels.Count;
            d.Wheels = new WheelComponent[n];
            d.WheelInput = new PowertrainComponent[n];
            d.WheelInputHash = new int[n];
            d.WheelInertia = new float[n];
            d.StockDriven = new bool[n];
            d.WheelAxle = new int[n];
            d.WheelSide = new char[n];
            var z = new float[n];
            for (int i = 0; i < n; i++)
            {
                WheelComponent w = pt.wheels[i];
                d.Wheels[i] = w;
                if (w == null || w.wheelUAPI == null)
                {
                    continue;
                }
                d.WheelInput[i] = w.Input;
                d.WheelInputHash[i] = w.inputNameHash;
                Vector3 local = vc.transform.InverseTransformPoint(w.wheelUAPI.transform.position);
                z[i] = local.z;
                d.WheelSide[i] = local.x < -CentreTolerance ? 'L' : (local.x > CentreTolerance ? 'R' : 'C');
            }
            MarkDriven(d, d.TransOut, d.TransOutHash, d.StockDriven, 0);
            for (int i = 0; i < n; i++)
            {
                WheelComponent w = d.Wheels[i];
                if (w == null || w.wheelUAPI == null)
                {
                    continue;
                }
                // A driven wheel's WheelUAPI.Inertia carries the reflected drivetrain inertia
                // (WheelComponent.ForwardStep: outputInertia = own + inputInertia).
                d.WheelInertia[i] = d.StockDriven[i] && w.outputInertia > 0f
                    ? w.outputInertia - w.inputInertia
                    : w.wheelUAPI.Inertia;
            }

            // Axles: sort by z (front first), new axle when z jumps by more than NWH's 0.2 m.
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }
            System.Array.Sort(order, (a, b) => z[b].CompareTo(z[a]));
            int axle = 0;
            float prevZ = 0f;
            bool first = true;
            for (int k = 0; k < n; k++)
            {
                int i = order[k];
                if (d.Wheels[i] == null || d.Wheels[i].wheelUAPI == null)
                {
                    continue;
                }
                if (first || Mathf.Abs(z[i] - prevZ) > AxleTolerance)
                {
                    axle++;
                    first = false;
                }
                d.WheelAxle[i] = axle;
                prevZ = z[i];
            }
            d.AxleCount = axle;
            return d;
        }

        /// <summary>
        /// Mark the wheels a component tree drives, exactly as NWH would step them: a
        /// differential passes torque on only when both output hashes are set, any other
        /// component when its output hash is set. Depth-guarded against a corrupt stock tree.
        /// </summary>
        private static void MarkDriven(LayoutData d, PowertrainComponent c, int hashToC, bool[] driven, int depth)
        {
            if (c == null || hashToC == 0 || depth > 32)
            {
                return;
            }
            var wheel = c as WheelComponent;
            if (wheel != null)
            {
                int i = WheelIndex(d, wheel);
                if (i >= 0)
                {
                    driven[i] = true;
                }
                return;
            }
            var diff = c as DifferentialComponent;
            if (diff != null)
            {
                if (diff.outputNameHash != 0 && diff.outputBNameHash != 0)
                {
                    MarkDriven(d, diff.Output, diff.outputNameHash, driven, depth + 1);
                    MarkDriven(d, diff.OutputB, diff.outputBNameHash, driven, depth + 1);
                }
                return;
            }
            MarkDriven(d, c.Output, c.outputNameHash, driven, depth + 1);
        }

        private static int WheelIndex(LayoutData d, WheelComponent w)
        {
            for (int i = 0; i < d.Wheels.Length; i++)
            {
                if (d.Wheels[i] == w)
                {
                    return i;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------ resolve

        /// <summary>Find the wheel a layout token names on this vehicle; null + reason when it can't.</summary>
        internal static WheelComponent FindWheel(LayoutData d, DrivetrainLayout.Target t, out string error)
        {
            error = null;
            int axle = t.Axle == 0 ? d.AxleCount : t.Axle;
            if (axle < 1 || axle > d.AxleCount)
            {
                error = t.Text + ": this vehicle has " + d.AxleCount + (d.AxleCount == 1 ? " axle" : " axles");
                return null;
            }
            WheelComponent found = null;
            int count = 0;
            for (int i = 0; i < d.Wheels.Length; i++)
            {
                if (d.Wheels[i] != null && d.WheelAxle[i] == axle && d.WheelSide[i] == t.Side)
                {
                    found = d.Wheels[i];
                    count++;
                }
            }
            if (count == 0)
            {
                error = t.Text + ": axle " + axle + " has no " + SideWord(t.Side) + " wheel";
                return null;
            }
            if (count > 1)
            {
                error = t.Text + ": axle " + axle + " has " + count + " " + SideWord(t.Side) + " wheels (dual wheels can't be addressed)";
                return null;
            }
            return found;
        }

        private static string SideWord(char side)
        {
            return side == 'L' ? "left" : side == 'R' ? "right" : "centre";
        }

        /// <summary>
        /// Build this vehicle's node objects and targets for a layout. Allocates (once per layout
        /// change per vehicle); returns false with ResolveError set when the layout does not fit.
        /// </summary>
        internal static bool ResolveLayout(VehicleController vc, LayoutData d, DrivetrainLayout layout)
        {
            d.Resolved = layout;
            d.ResolveError = null;
            int n = layout.Nodes.Length;
            var nodes = new DifferentialComponent[n];
            for (int i = 0; i < n; i++)
            {
                DrivetrainLayout.Node spec = layout.Nodes[i];
                var node = new DifferentialComponent
                {
                    name = "AVT " + spec.Name,
                    inertia = NodeInertia,
                    vehicleController = vc,
                    biasAB = 1f - spec.Split,
                    stiffness = spec.Stiffness,
                    slipTorque = spec.SlipTorque,
                    powerRamp = spec.PowerRamp,
                    coastRamp = spec.CoastRamp
                };
                // Always assign: a new diff has no split delegate until the setter runs.
                node.DifferentialType = ToNwhType(spec.Type);
                nodes[i] = node;
            }

            var driven = new bool[d.Wheels.Length];
            var outA = new PowertrainComponent[n];
            var outB = new PowertrainComponent[n];
            string error;
            PowertrainComponent root = ResolveTarget(d, layout.Root, nodes, driven, out error);
            for (int i = 0; i < n && error == null; i++)
            {
                outA[i] = ResolveTarget(d, layout.Nodes[i].A, nodes, driven, out error);
                if (error == null)
                {
                    outB[i] = ResolveTarget(d, layout.Nodes[i].B, nodes, driven, out error);
                }
            }
            if (error != null)
            {
                d.ResolveError = error;
                d.Nodes = null;
                d.OutA = d.OutB = null;
                d.Root = null;
                d.LayoutDriven = null;
                return false;
            }
            d.Nodes = nodes;
            d.OutA = outA;
            d.OutB = outB;
            d.Root = root;
            d.LayoutDriven = driven;
            return true;
        }

        private static PowertrainComponent ResolveTarget(LayoutData d, DrivetrainLayout.Target t, DifferentialComponent[] nodes,
            bool[] driven, out string error)
        {
            error = null;
            if (t.Kind == DrivetrainLayout.TargetKind.Node)
            {
                return nodes[t.Node];
            }
            WheelComponent w = FindWheel(d, t, out error);
            if (w == null)
            {
                return null;
            }
            int i = WheelIndex(d, w);
            if (driven[i])
            {
                // Aliases that only collide on this vehicle (RL and A2L on a two-axle car).
                error = "wheel " + t.Text.ToUpperInvariant() + " is driven twice on this vehicle";
                return null;
            }
            driven[i] = true;
            return w;
        }

        // ------------------------------------------------------------------ apply / restore

        /// <summary>Called at the end of ApplyDrivetrain. Allocation-free unless the layout changed.</summary>
        private static void ApplyLayout(VehicleRecord r)
        {
            LayoutData d = r.Drivetrain != null ? r.Drivetrain.Layout : null;
            if (d == null)
            {
                return;
            }
            DrivetrainLayout layout = DrivetrainSettings.ActiveLayout;
            if (layout == null)
            {
                if (d.Applied)
                {
                    RestoreLayout(d);
                }
                return;
            }
            if (d.Resolved != layout)
            {
                bool ok = ResolveLayout(r.Vc, d, layout);
                LogLayoutResult(r.Vc, d, layout, ok);
            }
            if (d.ResolveError != null)
            {
                if (d.Applied)
                {
                    RestoreLayout(d);
                }
                return;
            }
            WireLayout(d);
            d.Applied = true;
        }

        /// <summary>
        /// Point the gearbox at the layout's root and each node at its outputs. Idempotent: every
        /// node's previous target is the same object (targets change only with a new layout, which
        /// creates new nodes), so a setter never clears another node's link.
        /// </summary>
        internal static void WireLayout(LayoutData d)
        {
            for (int i = 0; i < d.Nodes.Length; i++)
            {
                d.Nodes[i].Output = d.OutA[i];
                d.Nodes[i].OutputB = d.OutB[i];
            }
            d.Transmission.Output = d.Root;

            // Hash hygiene: the stock components keep their stock hashes (see the class summary).
            // Stepping only zero-checks them, and the stock transmission hash is non-zero.
            if (d.TransOutHash != 0)
            {
                d.Transmission.outputNameHash = d.TransOutHash;
            }
            if (d.TransOut != null && !(d.TransOut is WheelComponent))
            {
                d.TransOut.Input = d.TransOutInput;
                d.TransOut.inputNameHash = d.TransOutInputHash;
            }
            for (int i = 0; i < d.Wheels.Length; i++)
            {
                WheelComponent w = d.Wheels[i];
                if (w == null || w.wheelUAPI == null)
                {
                    continue;
                }
                if (!d.LayoutDriven[i])
                {
                    w.Input = d.WheelInput[i];
                    ReleaseWheel(d, i);
                }
                w.inputNameHash = d.WheelInputHash[i];
            }
        }

        /// <summary>Exact restore of the captured references and hashes.</summary>
        internal static void RestoreLayout(LayoutData d)
        {
            d.Applied = false;
            if (d.Transmission == null)
            {
                return;
            }
            d.Transmission.Output = d.TransOut;
            d.Transmission.outputNameHash = d.TransOutHash;
            if (d.TransOut != null)
            {
                d.TransOut.Input = d.TransOutInput;
                d.TransOut.inputNameHash = d.TransOutInputHash;
            }
            for (int i = 0; i < d.Wheels.Length; i++)
            {
                WheelComponent w = d.Wheels[i];
                if (w == null || w.wheelUAPI == null)
                {
                    continue;
                }
                w.Input = d.WheelInput[i];
                w.inputNameHash = d.WheelInputHash[i];
                if (!d.StockDriven[i])
                {
                    ReleaseWheel(d, i);
                }
            }
        }

        /// <summary>
        /// A wheel nothing drives any more must simulate itself again (WheelComponent.ForwardStep
        /// switched AutoSimulate off), with no leftover motor torque and its own inertia.
        /// Only touches wheels the powertrain had taken over.
        /// </summary>
        private static void ReleaseWheel(LayoutData d, int i)
        {
            var u = d.Wheels[i].wheelUAPI;
            if (u.AutoSimulate)
            {
                return;
            }
            u.MotorTorque = 0f;
            u.Inertia = d.WheelInertia[i];
            u.AutoSimulate = true;
        }

        // ------------------------------------------------------------------ read-only API

        /// <summary>A tracked vehicle's own wiring as layout text (what the log prints), or null.</summary>
        public string StockLayoutText(VehicleController vc)
        {
            VehicleRecord r;
            if (vc == null || !_records.TryGetValue(vc, out r) || r.Drivetrain == null || r.Drivetrain.Layout == null)
            {
                return null;
            }
            return DescribeStockLayout(r.Drivetrain.Layout);
        }

        // ------------------------------------------------------------------ torque split (0.7.0 panel readout)

        public const int MaxAxles = 8;

        /// <summary>
        /// Nominal share of the gearbox torque each axle receives (index 0 = front axle), walking the
        /// LIVE wiring from the gearbox (so a custom layout shows its own split): an Open diff splits
        /// by its bias (A gets 1 - biasAB, DifferentialComponent.cs OpenDiffTorqueSplit), a Locked/LSD
        /// diff nominally 50/50 (it moves torque with wheel speed), any other component passes it on.
        /// A diff with an unset output drives nothing (NWH steps it as a dead end). Allocation-free.
        /// Returns the number of driven axles.
        /// </summary>
        internal static int NominalAxleShares(LayoutData d, float[] shares)
        {
            for (int i = 0; i < shares.Length; i++)
            {
                shares[i] = 0f;
            }
            if (d == null || d.Transmission == null)
            {
                return 0;
            }
            Accumulate(d, d.Transmission.Output, d.Transmission.outputNameHash, 1f, shares, 0);
            int n = 0;
            for (int i = 0; i < shares.Length; i++)
            {
                if (shares[i] > 1e-4f)
                {
                    n++;
                }
            }
            return n;
        }

        private static void Accumulate(LayoutData d, PowertrainComponent c, int hashToC, float share, float[] shares, int depth)
        {
            if (c == null || hashToC == 0 || depth > 32 || share <= 0f)
            {
                return;
            }
            var wheel = c as WheelComponent;
            if (wheel != null)
            {
                int i = WheelIndex(d, wheel);
                int axle = i >= 0 ? d.WheelAxle[i] - 1 : -1;
                if (axle >= 0 && axle < shares.Length)
                {
                    shares[axle] += share;
                }
                return;
            }
            var diff = c as DifferentialComponent;
            if (diff != null)
            {
                if (diff.outputNameHash == 0 || diff.outputBNameHash == 0)
                {
                    return;
                }
                float a = diff.DifferentialType == DifferentialComponent.Type.Open ? 1f - diff.biasAB : 0.5f;
                Accumulate(d, diff.Output, diff.outputNameHash, share * a, shares, depth + 1);
                Accumulate(d, diff.OutputB, diff.outputBNameHash, share * (1f - a), shares, depth + 1);
                return;
            }
            Accumulate(d, c.Output, c.outputNameHash, share, shares, depth + 1);
        }

        private readonly float[] _shareScratch = new float[MaxAxles];

        /// <summary>The panel's drive summary for a vehicle: axle count, driven axles, nominal shares (copied into <paramref name="shares"/>).</summary>
        public bool TryGetDriveSplit(VehicleController vc, float[] shares, out int axleCount, out int drivenAxles)
        {
            axleCount = 0;
            drivenAxles = 0;
            VehicleRecord r;
            if (vc == null || !_records.TryGetValue(vc, out r) || r.Drivetrain == null || r.Drivetrain.Layout == null)
            {
                return false;
            }
            axleCount = r.Drivetrain.Layout.AxleCount;
            drivenAxles = NominalAxleShares(r.Drivetrain.Layout, _shareScratch);
            for (int i = 0; i < shares.Length && i < _shareScratch.Length; i++)
            {
                shares[i] = _shareScratch[i];
            }
            return true;
        }

        /// <summary>Does this vehicle have a centre (AWD/transfer) differential of its own?</summary>
        public bool HasCentreDiff(VehicleController vc)
        {
            VehicleRecord r;
            if (vc == null || !_records.TryGetValue(vc, out r) || vc.powertrain == null || vc.powertrain.differentials == null)
            {
                return false;
            }
            List<DifferentialComponent> diffs = vc.powertrain.differentials;
            for (int i = 0; i < diffs.Count; i++)
            {
                if (diffs[i] != null && ClassifyDiff(r, diffs[i], i) == DiffRole.Center)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Why the active layout is not applied to this vehicle (null = applied, or no layout active).</summary>
        public string LayoutProblem(VehicleController vc)
        {
            VehicleRecord r;
            if (vc == null || !_records.TryGetValue(vc, out r))
            {
                return "not tracked";
            }
            if (DrivetrainSettings.LayoutEnabled && DrivetrainSettings.Layout == null)
            {
                return DrivetrainSettings.LayoutError;
            }
            LayoutData d = r.Drivetrain != null ? r.Drivetrain.Layout : null;
            if (d == null)
            {
                return "no transmission or wheels";
            }
            return d.Resolved == DrivetrainSettings.ActiveLayout ? d.ResolveError : null;
        }

        // ------------------------------------------------------------------ logging

        // Per-vehicle log de-duplication (one line per vehicle per layout, survives re-captures).
        private static readonly Dictionary<string, DrivetrainLayout> LoggedLayoutResult = new Dictionary<string, DrivetrainLayout>();
        private static readonly HashSet<string> LoggedStockLayout = new HashSet<string>();
        private static string _loggedLayoutErrorText;

        private static void LogLayoutResult(VehicleController vc, LayoutData d, DrivetrainLayout layout, bool ok)
        {
            string name = VehicleName(vc);
            DrivetrainLayout last;
            if (Plugin.Log == null || (LoggedLayoutResult.TryGetValue(name, out last) && last == layout))
            {
                return;
            }
            LoggedLayoutResult[name] = layout;
            if (!ok)
            {
                Plugin.Log.LogWarning("Drivetrain layout not applied to '" + name + "': " + d.ResolveError
                    + ". The vehicle keeps its own drivetrain.");
                return;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < d.Wheels.Length; i++)
            {
                if (d.LayoutDriven[i])
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(WheelToken(d, i));
                }
            }
            Plugin.Log.LogInfo("Drivetrain layout applied to '" + name + "': " + layout.Nodes.Length
                + (layout.Nodes.Length == 1 ? " node" : " nodes") + ", driven wheels " + (sb.Length > 0 ? sb.ToString() : "none") + ".");
        }

        /// <summary>Logged once per text from ApplyAllDrivetrain while the layout is on but invalid.</summary>
        private static void LogLayoutParseError()
        {
            string text = DrivetrainSettings.LayoutText;
            if (Plugin.Log == null || !DrivetrainSettings.LayoutEnabled || DrivetrainSettings.Layout != null
                || string.Equals(_loggedLayoutErrorText, text, System.StringComparison.Ordinal))
            {
                return;
            }
            _loggedLayoutErrorText = text;
            Plugin.Log.LogWarning("Drivetrain layout ignored: " + DrivetrainSettings.LayoutError
                + ". Every vehicle keeps its own drivetrain. ([Drivetrain.Layout] Layout)");
        }

        /// <summary>Logged once per vehicle name at first capture: its wheels and its stock layout, ready to copy.</summary>
        private static void LogStockLayout(VehicleController vc, LayoutData d)
        {
            string name = VehicleName(vc);
            if (Plugin.Log == null || d == null || !LoggedStockLayout.Add(name))
            {
                return;
            }
            Plugin.Log.LogInfo("Drivetrain of '" + name + "': " + d.AxleCount + (d.AxleCount == 1 ? " axle" : " axles")
                + ", wheels " + WheelList(d) + ". Stock layout: " + DescribeStockLayout(d));
        }

        private static string WheelList(LayoutData d)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < d.Wheels.Length; i++)
            {
                if (d.Wheels[i] == null || d.Wheels[i].wheelUAPI == null)
                {
                    continue;
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(WheelToken(d, i));
            }
            return sb.ToString();
        }

        /// <summary>The layout token for a wheel: FL/FR/RL/RR on the first/last axle, A&lt;n&gt;L/R/C otherwise.</summary>
        internal static string WheelToken(LayoutData d, int i)
        {
            int axle = d.WheelAxle[i];
            char side = d.WheelSide[i];
            if (side != 'C' && d.AxleCount >= 2)
            {
                if (axle == 1) return side == 'L' ? "FL" : "FR";
                if (axle == d.AxleCount) return side == 'L' ? "RL" : "RR";
            }
            return side == 'C' ? "A" + axle : "A" + axle + side;
        }

        /// <summary>
        /// The vehicle's own wiring as layout text (the format [Drivetrain.Layout] Layout takes),
        /// so a layout can start from what the vehicle already has. Values outside the layout's
        /// ranges are clamped; an External diff (script-driven) has no layout equivalent.
        /// </summary>
        internal static string DescribeStockLayout(LayoutData d)
        {
            if (d.TransOut == null || d.TransOutHash == 0)
            {
                return "(gearbox drives nothing)";
            }
            var names = new Dictionary<PowertrainComponent, string>();
            var lines = new List<string>();
            string root = DescribeComponent(d, d.TransOut, names, lines, 0);
            if (root == null)
            {
                return "(not expressible as a layout)";
            }
            var sb = new StringBuilder("gearbox -> ").Append(root);
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append("; ").Append(lines[i]);
            }
            return sb.ToString();
        }

        private static string DescribeComponent(LayoutData d, PowertrainComponent c, Dictionary<PowertrainComponent, string> names,
            List<string> lines, int depth)
        {
            if (c == null || depth > 32)
            {
                return null;
            }
            var wheel = c as WheelComponent;
            if (wheel != null)
            {
                int i = WheelIndex(d, wheel);
                return i >= 0 ? WheelToken(d, i) : null;
            }
            var diff = c as DifferentialComponent;
            if (diff == null)
            {
                // A pass-through component: describe what it drives.
                return c.outputNameHash != 0 ? DescribeComponent(d, c.Output, names, lines, depth + 1) : null;
            }
            string existing;
            if (names.TryGetValue(diff, out existing))
            {
                return null;   // fed twice: not a tree
            }
            string name = NodeNameFor(diff.name, names.Count);
            names[diff] = name;
            if (diff.outputNameHash == 0 || diff.outputBNameHash == 0 || diff.DifferentialType == DifferentialComponent.Type.External)
            {
                return null;
            }
            int slot = lines.Count;
            lines.Add(null);
            string a = DescribeComponent(d, diff.Output, names, lines, depth + 1);
            string b = DescribeComponent(d, diff.OutputB, names, lines, depth + 1);
            if (a == null || b == null)
            {
                return null;
            }
            DiffMode mode = diff.DifferentialType == DifferentialComponent.Type.Locked ? DiffMode.Locked
                : diff.DifferentialType == DifferentialComponent.Type.LimitedSlip ? DiffMode.LimitedSlip : DiffMode.Open;
            lines[slot] = DrivetrainLayout.FormatNode(name, mode,
                Mathf.Clamp01(1f - diff.biasAB), Mathf.Clamp01(diff.stiffness),
                Mathf.Clamp(diff.slipTorque, 0f, DrivetrainLayout.SlipTorqueMax),
                Mathf.Clamp01(diff.powerRamp), Mathf.Clamp01(diff.coastRamp), a, b);
            return name;
        }

        /// <summary>A valid, unique node name from an NWH component name ("Front Differential" → "Front_Differential").</summary>
        private static string NodeNameFor(string raw, int index)
        {
            var sb = new StringBuilder();
            if (raw != null)
            {
                for (int i = 0; i < raw.Length && sb.Length < DrivetrainLayout.MaxNameLength - 3; i++)
                {
                    char c = raw[i];
                    sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
                }
            }
            string s = sb.ToString().Trim('_');
            DrivetrainLayout.Target t;
            if (s.Length == 0 || !char.IsLetter(s[0]) || DrivetrainLayout.TryParseWheel(s, out t)
                || string.Equals(s, "gearbox", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "transmission", System.StringComparison.OrdinalIgnoreCase))
            {
                s = "diff";
            }
            return s + "_" + (index + 1);
        }
    }
}
