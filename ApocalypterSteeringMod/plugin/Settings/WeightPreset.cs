namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Weight presets: positive kg = real ballast added at that axle (raises the
    /// vehicle's mass and shifts its centre of mass), negative kg = a "balloon"
    /// the applier turns into upward lift at that axle. Both values in kg.
    /// </summary>
    public sealed class WeightPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public float FrontKg = 0f;   // + = ballast at the front axle, - = balloon lift
        public float RearKg = 0f;    // + = ballast at the rear axle, - = balloon lift

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (WeightPreset)src;
            FrontKg = o.FrontKg;
            RearKg = o.RearKg;
        }

        public static readonly WeightPreset[] Presets;
        public static readonly WeightPreset Stock;
        public static readonly WeightPreset Custom;

        static WeightPreset()
        {
            Stock = new WeightPreset
            {
                Name = "Stock",
                Description = "The vehicle's own weight, exactly as shipped."
            };
            Custom = new WeightPreset { Name = "Custom", Description = "Your own tuning." };

            Presets = new WeightPreset[]
            {
                Stock,
                new WeightPreset
                {
                    Name = "Front ballast",
                    Description = "Adds 400 kg over the front axle: more front grip, more front-end inertia.",
                    FrontKg = 400f, RearKg = 0f
                },
                new WeightPreset
                {
                    Name = "Rear ballast",
                    Description = "Adds 400 kg over the rear axle: more rear grip, more rear-end inertia.",
                    FrontKg = 0f, RearKg = 400f
                },
                new WeightPreset
                {
                    Name = "Full load",
                    Description = "Adds 300 kg over each axle: a heavy, planted vehicle.",
                    FrontKg = 300f, RearKg = 300f
                },
                new WeightPreset
                {
                    Name = "Lift",
                    Description = "Balloons at both axles: upward lift that unweights the wheels.",
                    FrontKg = -250f, RearKg = -250f
                },
                Custom
            };
        }
    }
}
