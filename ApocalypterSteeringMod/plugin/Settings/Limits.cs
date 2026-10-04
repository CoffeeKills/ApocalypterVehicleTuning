namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Single source of truth for every tunable range. Used by the config file
    /// (AcceptableValueRange) and by the in-game sliders, so a hand-edited
    /// config value can never exceed what the panel can show.
    /// Widening a range never invalidates a stored value, so no migration is
    /// ever needed for range changes.
    /// The few ranges that encode hard NWH limits are called out and stay.
    /// </summary>
    public static class Limits
    {
        // Steering (0.6.1 widened: the user wants room to play).
        public const float RateMin = 0.1f, RateMax = 5f;
        public const float SmoothMin = 0.1f, SmoothMax = 3f;
        public const float CurveScaleMin = 0.5f, CurveScaleMax = 2f;    // legacy v0.3.x key only
        public const float SlipMin = 0f, SlipMax = 25f;
        public const float OppLockMin = 1f, OppLockMax = 5f;
        public const float ReturnScaleMin = 0.1f, ReturnScaleMax = 1f;  // legacy v0.3.x key only
        public const float LinExpMin = 0.1f, LinExpMax = 5f;

        // Suspension fine-tune sliders (legacy v3.1 multiplier on top of the preset).
        public const float UserMin = 0.5f, UserMax = 2f;

        // Generic factor sliders (v3.2 categories). Still used by the legacy v3.1
        // fold and by Drivetrain's shift-duration factor.
        public const float FactorMin = 0.5f, FactorMax = 2f;

        // Suspension-only factor range (0.6.0; 0.6.1 widened).
        public const float SuspFactorMin = 0.1f, SuspFactorMax = 5f;

        public const float AeroScaleMin = 0f, AeroScaleMax = 4f;
        public const float AeroSpeedScaleMin = 0.1f, AeroSpeedScaleMax = 4f;
        public const float BrakeTorqueMin = 0.1f, BrakeTorqueMax = 5f;
        public const float ActuationMin = 0.1f, ActuationMax = 4f;
        public const float BrakeAxleMin = 0f, BrakeAxleMax = 4f;
        public const float GripMin = 0.05f, GripMax = 5f;
        public const float PowerMin = 0.1f, PowerMax = 5f;
        public const float LossMin = 0f, LossMax = 4f;                  // engine braking; 0 = none
        public const float BoostMin = 0f, BoostMax = 4f;                // turbo/boost; 0 = none
        public const float RevLimitMin = 0.8f, RevLimitMax = 1.2f;
        // NOT widened: the shift-point lock-up guard (ShiftPoints) is reasoned for this range.
        public const float ShiftRpmMin = 0.8f, ShiftRpmMax = 1.2f;
        public const float FinalDriveMin = 0.25f, FinalDriveMax = 3f;
        // Widened, but the apply caps stiffness at max(1, stock) (wind-up guard, 0.2.0 fix 7).
        public const float DiffScaleMin = 0.25f, DiffScaleMax = 3f;
        public const float SlipThrMin = 0.01f, SlipThrMax = 1f;
        public const float CutoffSpeedMin = 0f, CutoffSpeedMax = 20f;
        public const float CutMultMin = 0f, CutMultMax = 1f;

        // Alignment (offsets from each vehicle's stock geometry; 0.6.1 widened).
        // Camber beyond +-16 deg continues through a transform roll (the setter clamps).
        public const float AlignmentCamberMin = -45f, AlignmentCamberMax = 45f;  // degrees
        public const float AlignmentCasterMin = -25f, AlignmentCasterMax = 25f;  // degrees
        public const float AlignmentToeMin = -15f, AlignmentToeMax = 15f;        // degrees
        public const float AlignmentPosMin = -100f, AlignmentPosMax = 100f;      // centimetres

        // Gearbox. GearCount stays 0..12: the preset stores 12 per-gear keys.
        public const int GearCountMin = 0, GearCountMax = 12;                    // 0 = each vehicle's own count
        public const float GearRatioMin = 0.25f, GearRatioMax = 3f;              // factor on each gear's own ratio
        public const float ClutchGripMin = 0.1f, ClutchGripMax = 4f;             // x slipTorque
        public const float ClutchRangeMin = 0.1f, ClutchRangeMax = 4f;           // x engagementRange
        public const float ClutchRpmMin = -1500f, ClutchRpmMax = 1500f;          // + engagementRPM

        // Panel / telemetry (0.6.1 widened).
        public const float PanelScaleMin = 0.3f, PanelScaleMax = 3f;
        public const float PanelWidthMin = 300f, PanelWidthMax = 1000f;
        public const float PanelWidthDefault = 460f;
        public const float PanelAlphaMin = 0.15f, PanelAlphaMax = 1f;
        public const int LastTabMin = 0, LastTabMax = 9;
        public const float TelemetryScaleMin = 0.3f, TelemetryScaleMax = 3f;
    }
}
