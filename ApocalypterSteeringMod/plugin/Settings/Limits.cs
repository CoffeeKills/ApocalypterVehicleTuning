namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Single source of truth for every tunable range. Used by the config file
    /// (AcceptableValueRange) and by the in-game sliders, so a hand-edited
    /// config value can never exceed what the panel can show.
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

        // Suspension fine-tune sliders (multiplier on top of the preset).
        public const float UserMin = 0.5f, UserMax = 2f;

        // Generic factor sliders (v3.2 categories).
        public const float FactorMin = 0.5f, FactorMax = 2f;
        public const float AeroScaleMin = 0f, AeroScaleMax = 2f;
        public const float AeroSpeedScaleMin = 0.5f, AeroSpeedScaleMax = 2f;
        public const float BrakeTorqueMin = 0.5f, BrakeTorqueMax = 2f;
        public const float BrakeAxleMin = 0f, BrakeAxleMax = 2f;
        public const float GripMin = 0.25f, GripMax = 2f;
        public const float PowerMin = 0.5f, PowerMax = 2.5f;
        public const float RevLimitMin = 0.8f, RevLimitMax = 1.2f;
        public const float ShiftRpmMin = 0.8f, ShiftRpmMax = 1.2f;
        public const float FinalDriveMin = 0.7f, FinalDriveMax = 1.5f;
        public const float DiffScaleMin = 0.5f, DiffScaleMax = 2f;
        public const float SlipThrMin = 0.02f, SlipThrMax = 0.5f;
        public const float CutoffSpeedMin = 0f, CutoffSpeedMax = 5f;
        public const float CutMultMin = 0f, CutMultMax = 1f;
    }
}
