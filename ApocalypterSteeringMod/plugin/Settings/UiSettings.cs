using System;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>Corner the telemetry strip sits in.</summary>
    public enum TelemetryCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Panel and telemetry preferences (0.6.0). Pure UI state, never a vehicle change.
    /// Mirrored to [UI] / [Telemetry] by ModConfig.
    /// </summary>
    public static class UiSettings
    {
        public const float DefaultScale = 1f;
        public const float DefaultAlpha = 1f;

        /// <summary>Default false (0.6.0): the game keeps running while the panel is open.</summary>
        public static bool FreezeWhileOpen = false;
        public static float PanelScale = DefaultScale;
        public static float PanelWidth = Limits.PanelWidthDefault;
        public static float PanelAlpha = DefaultAlpha;
        public static int LastTab;

        /// <summary>Default true: passive, click-through UI (see README §8).</summary>
        public static bool TelemetryEnabled = true;
        public static float TelemetryScale = 1f;
        public static TelemetryCorner TelemetryPosition = TelemetryCorner.BottomLeft;

        public static void ResetPanel()
        {
            FreezeWhileOpen = false;
            PanelScale = DefaultScale;
            PanelWidth = Limits.PanelWidthDefault;
            PanelAlpha = DefaultAlpha;
        }

        /// <summary>Names only (case/space-tolerant); anything else is BottomLeft.</summary>
        public static TelemetryCorner ParseCorner(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return TelemetryCorner.BottomLeft;
            }
            string v = value.Trim();
            for (int i = 0; i < 4; i++)
            {
                TelemetryCorner c = (TelemetryCorner)i;
                if (string.Equals(c.ToString(), v, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
            return TelemetryCorner.BottomLeft;
        }

        /// <summary>A stored tab index, clamped into 0..9 (garbage = 0).</summary>
        public static int ClampTab(int tab)
        {
            return tab < Limits.LastTabMin || tab > Limits.LastTabMax ? 0 : tab;
        }
    }
}
