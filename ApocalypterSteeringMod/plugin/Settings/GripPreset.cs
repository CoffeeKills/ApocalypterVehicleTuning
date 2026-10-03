namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Tire grip presets as multipliers on each wheel's captured friction
    /// values (wheelUAPI grip/stiffness properties — surface-independent:
    /// ground detection replaces the friction PRESET, not these multipliers).
    /// </summary>
    public sealed class GripPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public float LongitudinalScale = 1f;  // x wheelUAPI.LongitudinalFrictionGrip
        public float LateralScale = 1f;       // x wheelUAPI.LateralFrictionGrip
        public float StiffnessScale = 1f;     // x both friction stiffness values

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (GripPreset)src;
            LongitudinalScale = o.LongitudinalScale;
            LateralScale = o.LateralScale;
            StiffnessScale = o.StiffnessScale;
        }

        public static readonly GripPreset[] Presets;
        public static readonly GripPreset Stock;
        public static readonly GripPreset Custom;

        static GripPreset()
        {
            Stock = new GripPreset
            {
                Name = "Stock",
                Description = "Each vehicle's own tires, exactly as shipped."
            };
            Custom = new GripPreset { Name = "Custom", Description = "Your own tuning." };

            Presets = new GripPreset[]
            {
                Stock,
                new GripPreset
                {
                    Name = "Sport",
                    Description = "A bit more side grip for confident cornering.",
                    LongitudinalScale = 1.05f, LateralScale = 1.1f, StiffnessScale = 1.0f
                },
                new GripPreset
                {
                    Name = "Race",
                    Description = "Sticky tires: strong grip in every direction.",
                    LongitudinalScale = 1.1f, LateralScale = 1.25f, StiffnessScale = 1.1f
                },
                new GripPreset
                {
                    Name = "Off-road",
                    Description = "Softer, easier-going tires for loose surfaces.",
                    LongitudinalScale = 0.8f, LateralScale = 0.85f, StiffnessScale = 0.8f
                },
                new GripPreset
                {
                    Name = "Drift",
                    Description = "Low side grip and easy breakaway: slides are easy to hold.",
                    LongitudinalScale = 0.95f, LateralScale = 0.55f, StiffnessScale = 0.8f
                },
                Custom
            };
        }
    }
}
