namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// "Did the game change this field behind our back?" (0.7.0 audit fix). The tuner writes
    /// stock x factor and remembers what it wrote. When the live value no longer matches that,
    /// someone else wrote the field since — e.g. the game's CheckTag FSM setting
    /// transmission.UpshiftRPM / DownshiftRPM / finalGearRatio (docs/fsm-template-dump.md writer
    /// table) or an engine swap re-sizing engine/clutch values — and that value is the game's new
    /// stock. 0.6.x kept scaling (and, on OFF, restoring) the stale first-sight baseline, so a
    /// tuned category silently undid the game's change every 2 s and restored the old value when
    /// switched off. Pure and allocation-free.
    /// </summary>
    internal static class Drift
    {
        /// <summary>Relative tolerance: a float read back after a write compares equal.</summary>
        public const float Tolerance = 1e-4f;

        public static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            float m = b < 0f ? -b : b;
            return d <= Tolerance * (m > 1f ? m : 1f);
        }

        /// <summary>The baseline to use now: the live value when it moved away from what we last wrote.</summary>
        public static float Adopt(float stock, float lastWritten, float live)
        {
            return Same(live, lastWritten) ? stock : live;
        }
    }
}
