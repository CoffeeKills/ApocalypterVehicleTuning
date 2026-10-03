namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Suspension presets expressed as MULTIPLIERS on each vehicle's own stock values.
    ///
    /// Why relative: NWH's spring "maxForce" is a force in newtons (not a rate), and the
    /// game sizes it from vehicle mass (stock auto-setup = (mass * g / 4) * 6 per wheel,
    /// damper rates = (mass * g / 4) * 0.15). A fixed absolute number can therefore only
    /// be right for one weight class: too stiff on a small car, bottomed-out on a truck.
    /// Scaling each vehicle's captured stock values works for every vehicle, and 1.0
    /// means "exactly as the game ships it".
    ///
    /// Anti-roll bar: stock 0 stays 0 (a car without an ARB is never given one).
    /// </summary>
    public sealed class SuspensionPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public float SpringFront = 1f, SpringRear = 1f;
        public float RideHeightFront = 1f, RideHeightRear = 1f;
        public float BumpFront = 1f, BumpRear = 1f;
        public float ReboundFront = 1f, ReboundRear = 1f;
        public float ArbFront = 1f, ArbRear = 1f;

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            CopyFrom((SuspensionPreset)src);
        }

        public void CopyFrom(SuspensionPreset src)
        {
            SpringFront = src.SpringFront;
            SpringRear = src.SpringRear;
            RideHeightFront = src.RideHeightFront;
            RideHeightRear = src.RideHeightRear;
            BumpFront = src.BumpFront;
            BumpRear = src.BumpRear;
            ReboundFront = src.ReboundFront;
            ReboundRear = src.ReboundRear;
            ArbFront = src.ArbFront;
            ArbRear = src.ArbRear;
        }

        public static readonly SuspensionPreset[] Presets;
        public static readonly SuspensionPreset Stock;
        public static readonly SuspensionPreset Custom;
        public static readonly SuspensionPreset Defaults;

        static SuspensionPreset()
        {
            Stock = new SuspensionPreset
            {
                Name = "Stock",
                Description = "Each vehicle's original suspension, exactly as the game ships it."
            };
            Custom = new SuspensionPreset { Name = "Custom", Description = "Your own tuning." };
            Defaults = new SuspensionPreset { Name = "Defaults" };

            Presets = new SuspensionPreset[]
            {
                Stock,
                new SuspensionPreset
                {
                    Name = "Comfort",
                    Description = "Softer springs and dampers, a little taller. Takes the edge off rough roads.",
                    SpringFront = 0.80f, SpringRear = 0.80f,
                    RideHeightFront = 1.06f, RideHeightRear = 1.06f,
                    BumpFront = 0.80f, BumpRear = 0.80f,
                    ReboundFront = 0.85f, ReboundRear = 0.85f,
                    ArbFront = 0.70f, ArbRear = 0.70f
                },
                new SuspensionPreset
                {
                    Name = "Sport",
                    Description = "Firmer and slightly lower. Sharper turn-in and less body roll.",
                    SpringFront = 1.18f, SpringRear = 1.15f,
                    RideHeightFront = 0.93f, RideHeightRear = 0.93f,
                    BumpFront = 1.15f, BumpRear = 1.15f,
                    ReboundFront = 1.20f, ReboundRear = 1.20f,
                    ArbFront = 1.30f, ArbRear = 1.20f
                },
                new SuspensionPreset
                {
                    Name = "Off-road",
                    Description = "Soft and tall for long travel over rubble. Loose anti-roll bars keep the wheels planted.",
                    SpringFront = 0.72f, SpringRear = 0.72f,
                    RideHeightFront = 1.25f, RideHeightRear = 1.25f,
                    BumpFront = 0.72f, BumpRear = 0.72f,
                    ReboundFront = 0.78f, ReboundRear = 0.78f,
                    ArbFront = 0.50f, ArbRear = 0.50f
                },
                new SuspensionPreset
                {
                    Name = "Race",
                    Description = "Stiff and low for flat cornering. Best on smooth roads; harsh over bumps.",
                    SpringFront = 1.40f, SpringRear = 1.32f,
                    RideHeightFront = 0.85f, RideHeightRear = 0.85f,
                    BumpFront = 1.30f, BumpRear = 1.25f,
                    ReboundFront = 1.40f, ReboundRear = 1.35f,
                    ArbFront = 1.60f, ArbRear = 1.40f
                },
                Custom
            };
        }
    }
}
