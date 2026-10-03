namespace ApocalypterSteeringMod.Settings
{
    /// <summary>Differential behavior per axle. Stock keeps the vehicle's own type.</summary>
    public enum DiffMode
    {
        Stock,
        Open,
        Locked,
        LimitedSlip
    }

    /// <summary>
    /// Drivetrain presets as multipliers on the vehicle's captured stock values.
    /// Axles without a differential keep their stock setup. Electric engines
    /// ignore the boost slider (no forced induction).
    /// </summary>
    public sealed class DrivetrainPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public float PowerScale = 1f;          // x engine.maxPower
        public float RevLimiterScale = 1f;     // x engine.revLimiterRPM
        public float LossScale = 1f;           // x engine.engineLossPercent (clamped 0..1)
        public float BoostScale = 1f;          // x forcedInduction.powerGainMultiplier (clamped 1..3)
        public float FinalDriveScale = 1f;     // x transmission.finalGearRatio
        public float UpshiftScale = 1f;        // x transmission.UpshiftRPM
        public float DownshiftScale = 1f;      // x transmission.DownshiftRPM
        public float ShiftDurationScale = 1f;  // x transmission.shiftDuration
        public DiffMode DiffFrontMode = DiffMode.Stock;
        public DiffMode DiffRearMode = DiffMode.Stock;
        public float DiffStiffnessScale = 1f;  // x differential.stiffness
        public float DiffBiasScale = 1f;       // x differential.biasAB (clamped 0.05..0.95)

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (DrivetrainPreset)src;
            PowerScale = o.PowerScale;
            RevLimiterScale = o.RevLimiterScale;
            LossScale = o.LossScale;
            BoostScale = o.BoostScale;
            FinalDriveScale = o.FinalDriveScale;
            UpshiftScale = o.UpshiftScale;
            DownshiftScale = o.DownshiftScale;
            ShiftDurationScale = o.ShiftDurationScale;
            DiffFrontMode = o.DiffFrontMode;
            DiffRearMode = o.DiffRearMode;
            DiffStiffnessScale = o.DiffStiffnessScale;
            DiffBiasScale = o.DiffBiasScale;
        }

        public static readonly DrivetrainPreset[] Presets;
        public static readonly DrivetrainPreset Stock;
        public static readonly DrivetrainPreset Custom;

        static DrivetrainPreset()
        {
            Stock = new DrivetrainPreset
            {
                Name = "Stock",
                Description = "The vehicle's own engine, gearing and differentials, exactly as shipped."
            };
            Custom = new DrivetrainPreset { Name = "Custom", Description = "Your own tuning." };

            Presets = new DrivetrainPreset[]
            {
                Stock,
                new DrivetrainPreset
                {
                    Name = "Street",
                    Description = "Stock power and gearing for everyday driving.",
                    // all factors 1, modes Stock
                },
                new DrivetrainPreset
                {
                    Name = "Off-road",
                    Description = "More low-end pull, shorter gearing, locked front and limited-slip rear.",
                    PowerScale = 1.1f, LossScale = 0.9f, FinalDriveScale = 1.2f,
                    DiffFrontMode = DiffMode.Locked, DiffRearMode = DiffMode.LimitedSlip,
                    DiffStiffnessScale = 1.2f
                },
                new DrivetrainPreset
                {
                    Name = "Sport",
                    Description = "More power, later upshifts, quicker shifts.",
                    PowerScale = 1.15f, FinalDriveScale = 1.05f,
                    UpshiftScale = 1.1f, DownshiftScale = 1.05f,
                    ShiftDurationScale = 0.8f, DiffStiffnessScale = 1.2f
                },
                new DrivetrainPreset
                {
                    Name = "Race",
                    Description = "High power and revs, locked axles, fast shifts.",
                    PowerScale = 1.3f, RevLimiterScale = 1.1f, FinalDriveScale = 0.95f,
                    UpshiftScale = 1.15f, DownshiftScale = 1.1f, ShiftDurationScale = 0.6f,
                    DiffFrontMode = DiffMode.Locked, DiffRearMode = DiffMode.Locked,
                    DiffStiffnessScale = 1.4f
                },
                new DrivetrainPreset
                {
                    Name = "Drift",
                    Description = "Strong power, long gearing, locked rear with an open front.",
                    PowerScale = 1.25f, FinalDriveScale = 0.85f, UpshiftScale = 1.1f,
                    DiffRearMode = DiffMode.Locked, DiffFrontMode = DiffMode.Open,
                    DiffStiffnessScale = 1.3f
                },
                Custom
            };
        }
    }
}
