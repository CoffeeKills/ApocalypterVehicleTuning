namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Stability assist presets. Implemented entirely through NWH's own public
    /// delegate hooks (brakes.brakeTorqueModifiers / engine.powerModifiers) —
    /// no vehicle fields are written, no modules are required. The game's own
    /// ABS/TCS modules, if a vehicle has any, run alongside (multipliers
    /// compose, which is harmless).
    /// </summary>
    public sealed class AssistsPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool AbsEnabled;
        public float AbsSlipThreshold = 0.1f;   // wheel slip fraction that triggers release
        public float AbsCutoffSpeed = 1f;       // m/s below which ABS does nothing
        public float AbsCutMultiplier = 0.01f;  // brake modifier returned while slipping
        public bool TcsEnabled;
        public float TcsSlipThreshold = 0.1f;
        public float TcsCutoffSpeed = 2f;
        public float TcsCutMultiplier = 0.01f;

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (AssistsPreset)src;
            AbsEnabled = o.AbsEnabled;
            AbsSlipThreshold = o.AbsSlipThreshold;
            AbsCutoffSpeed = o.AbsCutoffSpeed;
            AbsCutMultiplier = o.AbsCutMultiplier;
            TcsEnabled = o.TcsEnabled;
            TcsSlipThreshold = o.TcsSlipThreshold;
            TcsCutoffSpeed = o.TcsCutoffSpeed;
            TcsCutMultiplier = o.TcsCutMultiplier;
        }

        public static readonly AssistsPreset[] Presets;
        public static readonly AssistsPreset Off;
        public static readonly AssistsPreset Custom;

        static AssistsPreset()
        {
            Off = new AssistsPreset
            {
                Name = "Off",
                Description = "No assists. The game's own ABS/TCS, if a vehicle has them, still run."
            };
            Custom = new AssistsPreset { Name = "Custom", Description = "Your own tuning." };

            Presets = new AssistsPreset[]
            {
                Off,
                new AssistsPreset
                {
                    Name = "Standard",
                    Description = "Both assists on, with NWH's own default thresholds.",
                    AbsEnabled = true,
                    TcsEnabled = true
                },
                new AssistsPreset
                {
                    Name = "Sport",
                    Description = "Later, gentler intervention so the tires work harder.",
                    AbsEnabled = true, AbsSlipThreshold = 0.12f,
                    TcsEnabled = true, TcsSlipThreshold = 0.08f
                },
                new AssistsPreset
                {
                    Name = "Off-road",
                    Description = "Softer ABS for loose surfaces, traction control off.",
                    AbsEnabled = true, AbsSlipThreshold = 0.15f,
                    AbsCutoffSpeed = 0.5f, AbsCutMultiplier = 0.005f,
                    TcsEnabled = false
                },
                new AssistsPreset
                {
                    Name = "Race",
                    Description = "Late ABS only — no traction control.",
                    AbsEnabled = true, AbsSlipThreshold = 0.15f,
                    TcsEnabled = false
                },
                Custom
            };
        }
    }
}
