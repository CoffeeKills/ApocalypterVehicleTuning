// The M1/M2 replacement points, declared and inert (FEATURES §4). A point is "staged" when
// its target method resolves in the running game; it is applied only if its runtime flag is
// set — and every flag is false in 0.1.0, so nothing is ever applied. Even if a flag were
// set, the prefix is a pass-through (returns true -> NWH's original runs).
//
// Evidence (verify/ checks both kinds):
//   CensusResolved - census.json resolves this exact signature against the real game DLL.
//   GamecodeOnly   - declared in gamecode/ (decompiled game fork) but the census never looked
//                    it up. Must be added to the census before M1/M2 turns the flag on.
// Not used as targets: TransmissionComponent.SimulateForwardStep / CompletePendingShift —
// those were steering-harness stub helpers; the census reports them absent from the game.
using System;
using System.Reflection;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocalypterDrivetrain.Patching
{
    public enum Evidence { CensusResolved, GamecodeOnly }

    public sealed class PatchPoint
    {
        public string Id;
        public string Milestone;
        public Type TargetType;
        public string Method;
        public Type[] ParamTypes;
        public Evidence Evidence;
        public string GamecodeFile;        // gamecode/<file>
        public string GamecodeSignature;   // exact declaration text in that file
        public string Why;

        /// <summary>Runtime gate. false in 0.1.0 for every point; not exposed in the config.</summary>
        public bool Enabled;

        public string TargetName { get { return TargetType.FullName + "." + Method + "(" + ParamList() + ")"; } }

        public string ParamList()
        {
            var names = new string[ParamTypes.Length];
            for (int i = 0; i < names.Length; i++) names[i] = ParamTypes[i].FullName;
            return string.Join(",", names);
        }

        /// <summary>Exactly the method declared on TargetType (an override, not the base).</summary>
        public MethodInfo Resolve()
        {
            return TargetType.GetMethod(Method,
                BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, ParamTypes, null);
        }
    }

    public static class PatchPoints
    {
        private static readonly Type[] StepParams = { typeof(float), typeof(float), typeof(float) };

        public static readonly PatchPoint Transmission = new PatchPoint
        {
            Id = "M1.transmission", Milestone = "M1",
            TargetType = typeof(TransmissionComponent), Method = "ForwardStep", ParamTypes = StepParams,
            Evidence = Evidence.GamecodeOnly, GamecodeFile = "TransmissionComponent.cs",
            GamecodeSignature = "public override float ForwardStep(float torque, float inertiaSum, float dt)",
            Why = "step level, not delegate level: NWH re-assigns shiftDelegate on every transmissionType change (README fact 8, census B3)",
        };

        public static readonly PatchPoint Engine = new PatchPoint
        {
            Id = "M2.engine", Milestone = "M2",
            TargetType = typeof(EngineComponent), Method = "IntegrateDownwards", ParamTypes = new[] { typeof(float) },
            Evidence = Evidence.GamecodeOnly, GamecodeFile = "EngineComponent.cs",
            GamecodeSignature = "public void IntegrateDownwards(float dt)",
            Why = "the engine's step: Powertrain.cs:45 calls it once per tick; EngineComponent has no ForwardStep override",
        };

        public static readonly PatchPoint Clutch = new PatchPoint
        {
            Id = "M2.clutch", Milestone = "M2",
            TargetType = typeof(ClutchComponent), Method = "ForwardStep", ParamTypes = StepParams,
            Evidence = Evidence.GamecodeOnly, GamecodeFile = "ClutchComponent.cs",
            GamecodeSignature = "public override float ForwardStep(float torque, float inertiaSum, float dt)",
            Why = "engagement curve + slip torque clamp live here",
        };

        public static readonly PatchPoint Differential = new PatchPoint
        {
            Id = "M2.differential", Milestone = "M2",
            TargetType = typeof(DifferentialComponent), Method = "ForwardStep", ParamTypes = StepParams,
            Evidence = Evidence.CensusResolved, GamecodeFile = "DifferentialComponent.cs",
            GamecodeSignature = "public override float ForwardStep(float torque, float inertiaSum, float dt)",
            Why = "torque split; NWH's split delegate is unset until DifferentialType is assigned (README fact 7)",
        };

        public static readonly PatchPoint[] All = { Transmission, Engine, Clutch, Differential };

        /// <summary>The prefix every point would install: returns true, so NWH's original always runs.</summary>
        public static bool PassThroughPrefix() { return true; }

        public static readonly MethodInfo PassThrough = typeof(PatchPoints).GetMethod("PassThroughPrefix", BindingFlags.Public | BindingFlags.Static);
    }
}
