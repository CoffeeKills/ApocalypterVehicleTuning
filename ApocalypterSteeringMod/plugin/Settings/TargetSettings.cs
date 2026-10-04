using System;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>What the tuning applies to (0.6.1 targeting feature).</summary>
    public enum TargetMode
    {
        All,          // every tracked vehicle (the pre-targeting behaviour)
        LastDriven,   // the vehicle with the most live FSM input (same pick as telemetry)
        Selected      // one tracked vehicle, by GameObject name
    }

    /// <summary>
    /// Runtime target selection, pushed from the config and read by the tuner on
    /// every apply pass. Selecting by NAME survives scene loads and respawns
    /// (a respawned vehicle has the same prefab name); a name with no tracked
    /// vehicle simply applies to nothing until one spawns.
    /// </summary>
    public static class TargetSettings
    {
        public static TargetMode Mode = TargetMode.All;
        public static string SelectedName = "";

        public static string ModeName(TargetMode m)
        {
            switch (m)
            {
                case TargetMode.LastDriven: return "Last driven";
                case TargetMode.Selected: return "Selected vehicle";
                default: return "All vehicles";
            }
        }

        /// <summary>Name-only parse (case/space tolerant), anything else falls back to All.</summary>
        public static TargetMode Parse(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return TargetMode.All;
            }
            string t = s.Trim().ToLowerInvariant();
            if (t == "all" || t == "all vehicles")
            {
                return TargetMode.All;
            }
            if (t == "lastdriven" || t == "last driven")
            {
                return TargetMode.LastDriven;
            }
            if (t == "selected" || t == "selected vehicle")
            {
                return TargetMode.Selected;
            }
            return TargetMode.All;
        }
    }
}
