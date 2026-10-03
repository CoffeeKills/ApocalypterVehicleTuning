namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Brake presets as multipliers on the vehicle's captured stock values.
    /// Axle coefficients are clamped to the game's ranges at apply (0..1 brake,
    /// 0..2 handbrake). brakeOffThrottleIntensity is deliberately untouched.
    /// </summary>
    public sealed class BrakesPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public float TorqueScale = 1f;         // x vc.brakes.maxTorque
        public float FrontBrakeScale = 1f;     // x front WheelGroup.brakeCoefficient
        public float RearBrakeScale = 1f;      // x rear WheelGroup.brakeCoefficient
        public float HandbrakeScale = 1f;      // x WheelGroup.handbrakeCoefficient
        public float ActuationScale = 1f;      // x vc.brakes.actuationTime

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (BrakesPreset)src;
            TorqueScale = o.TorqueScale;
            FrontBrakeScale = o.FrontBrakeScale;
            RearBrakeScale = o.RearBrakeScale;
            HandbrakeScale = o.HandbrakeScale;
            ActuationScale = o.ActuationScale;
        }

        public static readonly BrakesPreset[] Presets;
        public static readonly BrakesPreset Stock;
        public static readonly BrakesPreset Custom;

        static BrakesPreset()
        {
            Stock = new BrakesPreset
            {
                Name = "Stock",
                Description = "The vehicle's own brakes, exactly as shipped."
            };
            Custom = new BrakesPreset { Name = "Custom", Description = "Your own tuning." };

            Presets = new BrakesPreset[]
            {
                Stock,
                new BrakesPreset
                {
                    Name = "Sport",
                    Description = "A bit more bite and a slightly quicker pedal.",
                    TorqueScale = 1.1f, FrontBrakeScale = 1.1f, RearBrakeScale = 1.05f,
                    HandbrakeScale = 1.0f, ActuationScale = 0.9f
                },
                new BrakesPreset
                {
                    Name = "Race",
                    Description = "Strong, quick brakes with a slight front bias and a softer handbrake.",
                    TorqueScale = 1.3f, FrontBrakeScale = 1.15f, RearBrakeScale = 1.1f,
                    HandbrakeScale = 0.8f, ActuationScale = 0.75f
                },
                new BrakesPreset
                {
                    Name = "Off-road",
                    Description = "Gentler pedal and a stronger handbrake for loose-surface control.",
                    TorqueScale = 1.05f, FrontBrakeScale = 0.9f, RearBrakeScale = 0.9f,
                    HandbrakeScale = 1.2f, ActuationScale = 1.1f
                },
                new BrakesPreset
                {
                    Name = "Drift",
                    Description = "Stock pedal feel with a much stronger handbrake for entry slides.",
                    TorqueScale = 1.0f, FrontBrakeScale = 0.95f, RearBrakeScale = 0.95f,
                    HandbrakeScale = 1.6f, ActuationScale = 1.0f
                },
                Custom
            };
        }
    }
}
