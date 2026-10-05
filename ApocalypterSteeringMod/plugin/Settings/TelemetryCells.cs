using System;
using System.Collections.Generic;
using System.Text;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>One telemetry strip readout.</summary>
    public enum TelemetryCell
    {
        Speed,      // km/h
        Rpm,        // engine rpm
        Gear,       // gear name
        SlipFront,  // mean front-axle slip, degrees (x90 convention)
        SlipRear,   // mean rear-axle slip, degrees
        LatG,       // lateral acceleration, g (yaw rate x speed)
        LongG,      // longitudinal acceleration, g (speed delta per tick)
        Steering,   // mean front wheel steer angle, degrees
        Throttle,   // input throttle, percent
        Brakes      // input brakes, percent
    }

    /// <summary>
    /// The telemetry strip's chosen readouts (0.7.4; replaces the 0.7.0 pin feature, which the
    /// user found useless — the strip now shows a chosen list of vehicle readouts instead of
    /// pinned slider values). Persisted as [Telemetry] Cells, ';'-separated names; unknown
    /// names and duplicates are dropped, over-cap entries too. Pure and UI-free (harness-tested).
    /// </summary>
    public static class TelemetryCells
    {
        public const int MaxCells = 8;
        public const char Separator = ';';
        public const string DefaultText = "Speed;Rpm;Gear;SlipFront";

        public static readonly TelemetryCell[] All = (TelemetryCell[])Enum.GetValues(typeof(TelemetryCell));

        private static readonly List<TelemetryCell> _selected = new List<TelemetryCell>();

        /// <summary>The selected cells, in selection order (read-only view for the strip).</summary>
        public static IList<TelemetryCell> Selected
        {
            get { return _selected.AsReadOnly(); }
        }

        public static int Count
        {
            get { return _selected.Count; }
        }

        /// <summary>Bumped on every change, so the strip rebuilds its cells only when needed.</summary>
        public static int Version { get; private set; }

        public static bool IsOn(TelemetryCell cell)
        {
            return _selected.Contains(cell);
        }

        /// <summary>Turn a cell on/off. False when the cell is not in All (never) or turning on would exceed MaxCells.</summary>
        public static bool Set(TelemetryCell cell, bool on)
        {
            if (on)
            {
                if (_selected.Contains(cell))
                {
                    return true;
                }
                if (_selected.Count >= MaxCells)
                {
                    return false;
                }
                _selected.Add(cell);
                Version++;
                return true;
            }
            if (_selected.Remove(cell))
            {
                Version++;
            }
            return true;
        }

        public static string Label(TelemetryCell cell)
        {
            switch (cell)
            {
                case TelemetryCell.Speed: return "Speed";
                case TelemetryCell.Rpm: return "Engine RPM";
                case TelemetryCell.Gear: return "Gear";
                case TelemetryCell.SlipFront: return "Front slip";
                case TelemetryCell.SlipRear: return "Rear slip";
                case TelemetryCell.LatG: return "Lateral G";
                case TelemetryCell.LongG: return "Longitudinal G";
                case TelemetryCell.Steering: return "Steering angle";
                case TelemetryCell.Throttle: return "Throttle";
                default: return "Brakes";
            }
        }

        public static string Description(TelemetryCell cell)
        {
            switch (cell)
            {
                case TelemetryCell.Speed: return "Road speed";
                case TelemetryCell.Rpm: return "Engine revolutions per minute";
                case TelemetryCell.Gear: return "Current gear (NWH gear name)";
                case TelemetryCell.SlipFront: return "Mean front-axle tyre slip, approximate degrees";
                case TelemetryCell.SlipRear: return "Mean rear-axle tyre slip, approximate degrees";
                case TelemetryCell.LatG: return "Cornering force in g (yaw rate x speed)";
                case TelemetryCell.LongG: return "Acceleration / braking in g";
                case TelemetryCell.Steering: return "Mean front wheel angle in degrees";
                case TelemetryCell.Throttle: return "Throttle pedal input, percent";
                default: return "Brake pedal input, percent";
            }
        }

        /// <summary>Name-only parse (case-insensitive); unknown names fall back to the given cell.</summary>
        public static TelemetryCell Parse(string s, TelemetryCell fallback)
        {
            if (string.IsNullOrEmpty(s))
            {
                return fallback;
            }
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].ToString(), s.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }
            return fallback;
        }

        public static string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _selected.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(Separator);
                }
                sb.Append(_selected[i].ToString());
            }
            return sb.ToString();
        }

        /// <summary>Replace the selection from config text. Returns how many entries were dropped.</summary>
        public static int Load(string text)
        {
            _selected.Clear();
            Version++;
            int dropped = 0;
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            string[] parts = text.Split(Separator);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0)
                {
                    continue;
                }
                TelemetryCell c = Parse(p, TelemetryCell.Speed);
                if (!string.Equals(c.ToString(), p, StringComparison.OrdinalIgnoreCase)
                    || _selected.Contains(c) || _selected.Count >= MaxCells)
                {
                    dropped++;
                    continue;
                }
                _selected.Add(c);
            }
            return dropped;
        }
    }
}
