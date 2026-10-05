// The gearbox's shift-logic hook (FEATURES §2): a named behaviour resolved at config load.
// Built-ins: "manual", "automatic". Other mods register their own names in code before the
// config loads; an unregistered name is a load error, never a fallback.
// Nothing calls SelectGear this round — M1's TransmissionComponent patch is the caller.
using System;
using System.Collections.Generic;

namespace ApocalypterDrivetrain.Model
{
    /// <summary>Per-tick input to a shift logic. A struct passed by ref: no allocation per tick.</summary>
    public struct ShiftContext
    {
        public const int NoRequest = -999;   // NWH's ShiftInto sentinel (census B1)

        public int CurrentGear;
        public int MinGear, MaxGear;          // -reverse count .. forward count
        public int RequestedGear;             // input.ShiftInto; > -100 is a request (B1)
        public bool ShiftUp, ShiftDown;
        public float EngineRpm;
        public float Throttle;
    }

    public interface IShiftLogic
    {
        /// <summary>The gear that should be engaged. Must return a gear in [MinGear, MaxGear].</summary>
        int SelectGear(ref ShiftContext ctx);
    }

    /// <summary>Driver-requested shifting only (census B1 semantics).</summary>
    public sealed class ManualShiftLogic : IShiftLogic
    {
        public int SelectGear(ref ShiftContext ctx)
        {
            int g = ctx.CurrentGear;
            if (ctx.ShiftUp) g++;
            else if (ctx.ShiftDown) g--;
            else if (ctx.RequestedGear > -100)
            {
                // An out-of-range explicit request (e.g. 9 on a 5-speed) is ignored, not clamped.
                if (ctx.RequestedGear >= ctx.MinGear && ctx.RequestedGear <= ctx.MaxGear) g = ctx.RequestedGear;
            }
            if (g < ctx.MinGear) g = ctx.MinGear;
            if (g > ctx.MaxGear) g = ctx.MaxGear;
            return g;
        }
    }

    /// <summary>
    /// STUB until M1. Honors driver requests like manual and otherwise holds the gear. M1 adds
    /// the no-hunting automatic (landing-RPM check per shift). The load-time hunting warning
    /// (WarningCode.ShiftWouldHunt) is already real.
    /// </summary>
    public sealed class AutomaticShiftLogicStub : IShiftLogic
    {
        private readonly ManualShiftLogic _manual = new ManualShiftLogic();
        public int SelectGear(ref ShiftContext ctx) { return _manual.SelectGear(ref ctx); }
    }

    public static class ShiftLogicRegistry
    {
        public const string Manual = "manual";
        public const string Automatic = "automatic";

        private static readonly Dictionary<string, Func<GearboxDef, IShiftLogic>> Factories =
            new Dictionary<string, Func<GearboxDef, IShiftLogic>>
            {
                { Manual, g => new ManualShiftLogic() },
                { Automatic, g => new AutomaticShiftLogicStub() },
            };

        /// <summary>
        /// Registers a named shift logic for configs to use ("shiftLogic": "name"). Returns false
        /// (and changes nothing) if the name is empty or already registered — names are never replaced.
        /// </summary>
        public static bool Register(string name, Func<GearboxDef, IShiftLogic> factory)
        {
            if (string.IsNullOrEmpty(name) || factory == null || Factories.ContainsKey(name)) return false;
            Factories.Add(name, factory);
            return true;
        }

        public static bool IsRegistered(string name) { return name != null && Factories.ContainsKey(name); }

        public static string RegisteredNames()
        {
            var names = new List<string>(Factories.Keys);
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names.ToArray());
        }

        internal static IShiftLogic Create(string name, GearboxDef gearbox) { return Factories[name](gearbox); }
    }
}
