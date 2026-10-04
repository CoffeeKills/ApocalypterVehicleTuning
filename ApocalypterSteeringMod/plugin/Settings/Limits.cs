namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Single source of truth for every tunable range. Used by the config file
    /// (AcceptableValueRange) and by the in-game sliders, so a hand-edited
    /// config value can never exceed what the panel can show.
    /// Widening a range never invalidates a stored value, so 0.6.0's wider
    /// ranges need no migration.
    /// </summary>
    public static class Limits
    {
        public const float RateMin = 0.25f, RateMax = 3f;
        public const float SmoothMin = 0.5f, SmoothMax = 2f;
        public const float CurveScaleMin = 0.5f, CurveScaleMax = 2f;
        public const float SlipMin = 2f, SlipMax = 15f;
        public const float OppLockMin = 1f, OppLockMax = 3f;
        public const float ReturnScaleMin = 0.1f, ReturnScaleMax = 1f;
        public const float LinExpMin = 0.3f, LinExpMax = 3f;

        // Suspension fine-tune sliders (legacy v3.1 multiplier on top of the preset).
        public const float UserMin = 0.5f, UserMax = 2f;

        // Generic factor sliders (v3.2 categories). Still used by the legacy v3.1
        // fold and by Drivetrain's shift-duration factor.
        public const float FactorMin = 0.5f, FactorMax = 2f;

        // 0.6.0: suspension-only factor range (replaces FactorMin/Max for the
        // ten Suspension.Custom factors and their sliders).
        public const float SuspFactorMin = 0.25f, SuspFactorMax = 3f;

        public const float AeroScaleMin = 0f, AeroScaleMax = 2f;
        public const float AeroSpeedScaleMin = 0.5f, AeroSpeedScaleMax = 2f;
        public const float BrakeTorqueMin = 0.25f, BrakeTorqueMax = 3f;      // 0.6.0: was 0.5..2
        public const float ActuationMin = 0.5f, ActuationMax = 2f;           // 0.6.0: split from BrakeTorque (unchanged range)
        public const float BrakeAxleMin = 0f, BrakeAxleMax = 2f;
        public const float GripMin = 0.1f, GripMax = 3f;                     // 0.6.0: was 0.25..2
        public const float PowerMin = 0.25f, PowerMax = 3.5f;                // 0.6.0: was 0.5..2.5
        public const float LossMin = 0.25f, LossMax = 3f;                    // 0.6.0: engine braking (was FactorMin/Max)
        public const float BoostMin = 0.25f, BoostMax = 3f;                  // 0.6.0: turbo/boost (was FactorMin/Max)
        public const float RevLimitMin = 0.8f, RevLimitMax = 1.2f;
        // NOT widened: the shift-point lock-up guard (ShiftPoints) is reasoned for this range.
        public const float ShiftRpmMin = 0.8f, ShiftRpmMax = 1.2f;
        public const float FinalDriveMin = 0.5f, FinalDriveMax = 2f;         // 0.6.0: was 0.7..1.5
        // NOT widened: stiffness above max(1, stock) winds the diff up (0.2.0 fix 7).
        public const float DiffScaleMin = 0.5f, DiffScaleMax = 2f;
        public const float SlipThrMin = 0.02f, SlipThrMax = 0.5f;
        public const float CutoffSpeedMin = 0f, CutoffSpeedMax = 5f;
        public const float CutMultMin = 0f, CutMultMax = 1f;

        // 0.6.0 Alignment (offsets from each vehicle's stock geometry).
        // Camber beyond +-16 deg continues through a transform roll (the setter clamps).
        public const float AlignmentCamberMin = -30f, AlignmentCamberMax = 30f;  // degrees
        public const float AlignmentCasterMin = -10f, AlignmentCasterMax = 12f;   // degrees
        public const float AlignmentToeMin = -5f, AlignmentToeMax = 5f;           // degrees
        public const float AlignmentPosMin = -60f, AlignmentPosMax = 60f;         // centimetres

        // 0.6.0 Gearbox.
        public const int GearCountMin = 0, GearCountMax = 12;                     // 0 = each vehicle's own count
        public const float GearRatioMin = 0.5f, GearRatioMax = 1.5f;              // factor on each gear's own ratio
        public const float ClutchGripMin = 0.5f, ClutchGripMax = 2f;              // x slipTorque
        public const float ClutchRangeMin = 0.4f, ClutchRangeMax = 2f;            // x engagementRange
        public const float ClutchRpmMin = -500f, ClutchRpmMax = 500f;             // + engagementRPM

        // 0.6.0 Panel / telemetry.
        public const float PanelScaleMin = 0.5f, PanelScaleMax = 2f;
        public const float PanelWidthMin = 400f, PanelWidthMax = 800f;
        public const float PanelWidthDefault = 460f;
        public const float PanelAlphaMin = 0.4f, PanelAlphaMax = 1f;
        public const int LastTabMin = 0, LastTabMax = 9;
        public const float TelemetryScaleMin = 0.5f, TelemetryScaleMax = 2f;
    }
}
