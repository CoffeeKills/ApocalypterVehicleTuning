namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Aerodynamics presets as multipliers on the vehicle's captured NWH
    /// AerodynamicsModule values. Vehicles without a module (or without downforce
    /// points) get drag tuning only — the applier never synthesizes downforce.
    /// </summary>
    public sealed class AeroPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public float DownforceScale = 1f;          // x each DownforcePoint.maxForce
        public float DragScale = 1f;               // x frontalCd (clamped 0..1) and sideCd (clamped 0..2)
        public float MaxDownforceSpeedScale = 1f;  // x maxDownforceSpeed

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (AeroPreset)src;
            DownforceScale = o.DownforceScale;
            DragScale = o.DragScale;
            MaxDownforceSpeedScale = o.MaxDownforceSpeedScale;
        }

        public static readonly AeroPreset[] Presets;
        public static readonly AeroPreset Stock;
        public static readonly AeroPreset Custom;

        static AeroPreset()
        {
            Stock = new AeroPreset
            {
                Name = "Stock",
                Description = "The vehicle's own aerodynamics, exactly as shipped."
            };
            Custom = new AeroPreset { Name = "Custom", Description = "Your own tuning." };

            Presets = new AeroPreset[]
            {
                Stock,
                new AeroPreset
                {
                    Name = "Street",
                    Description = "Road setup: less downforce, stock drag.",
                    DownforceScale = 0.8f, DragScale = 1.0f, MaxDownforceSpeedScale = 1.0f
                },
                new AeroPreset
                {
                    Name = "Sport",
                    Description = "More downforce and a little drag for stable high-speed corners.",
                    DownforceScale = 1.2f, DragScale = 1.1f, MaxDownforceSpeedScale = 1.1f
                },
                new AeroPreset
                {
                    Name = "Off-road",
                    Description = "Low downforce, drag close to stock, effective earlier.",
                    DownforceScale = 0.6f, DragScale = 1.05f, MaxDownforceSpeedScale = 0.85f
                },
                new AeroPreset
                {
                    Name = "Race",
                    Description = "Big downforce, more drag, working up to higher speeds.",
                    DownforceScale = 1.6f, DragScale = 1.35f, MaxDownforceSpeedScale = 1.25f
                },
                Custom
            };
        }
    }
}
