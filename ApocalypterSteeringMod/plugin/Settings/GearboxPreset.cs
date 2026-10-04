namespace ApocalypterSteeringMod.Settings
{
    /// <summary>Gearbox shift mode. Stock keeps each vehicle's own transmission type.</summary>
    public enum GearboxMode
    {
        Stock,
        Manual,
        Automatic
    }

    /// <summary>
    /// One "clutch type": NWH2 has no clutch-type model, so a type is just a named set of
    /// the three clutch knobs (capacity, engagement speed, engagement point).
    /// </summary>
    public sealed class ClutchType
    {
        public string Name;
        public float Grip = 1f, Range = 1f, RpmOffset;

        public bool Matches(GearboxPreset p)
        {
            return Near(p.ClutchGripScale, Grip) && Near(p.ClutchRangeScale, Range) && Near(p.ClutchRpmOffset, RpmOffset);
        }

        private static bool Near(float a, float b)
        {
            float d = a - b;
            return d < 1e-3f && d > -1e-3f;
        }
    }

    /// <summary>
    /// Gearbox presets. Gear ratios are FACTORS on each vehicle's own ratios (so one
    /// config serves every vehicle); GearCount 0 keeps each vehicle's own count.
    /// Clutch values are factors (grip, range) and an RPM offset on the vehicle's own clutch.
    /// Deliberately shares no field with Drivetrain (no shiftDuration, shift RPMs or final
    /// drive here): the RefreshBaselines invariant needs disjoint field sets.
    /// </summary>
    public sealed class GearboxPreset : ITunablePreset
    {
        public const int MaxGears = 12;

        public string Name { get; set; }
        public string Description { get; set; }

        public int GearCount;                                  // 0 = vehicle's own count
        public readonly float[] GearScale = NewScales();       // [0] = 1st gear ... [11] = 12th
        public float ClutchGripScale = 1f;                     // x clutch.slipTorque
        public float ClutchRangeScale = 1f;                    // x clutch.engagementRange
        public float ClutchRpmOffset;                          // + clutch.engagementRPM
        public GearboxMode TransmissionMode = GearboxMode.Stock;

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        private static float[] NewScales()
        {
            var a = new float[MaxGears];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = 1f;
            }
            return a;
        }

        /// <summary>Factor for forward gear <paramref name="gear"/> (1-based); 1 outside 1..12.</summary>
        public float Scale(int gear)
        {
            return gear >= 1 && gear <= MaxGears ? GearScale[gear - 1] : 1f;
        }

        public void SetScale(int gear, float v)
        {
            if (gear >= 1 && gear <= MaxGears)
            {
                GearScale[gear - 1] = v;
            }
        }

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (GearboxPreset)src;
            GearCount = o.GearCount;
            for (int i = 0; i < MaxGears; i++)
            {
                GearScale[i] = o.GearScale[i];
            }
            ClutchGripScale = o.ClutchGripScale;
            ClutchRangeScale = o.ClutchRangeScale;
            ClutchRpmOffset = o.ClutchRpmOffset;
            TransmissionMode = o.TransmissionMode;
        }

        /// <summary>True when the preset touches no gear (count 0, every factor 1).</summary>
        public bool GearsIdentity()
        {
            if (GearCount != 0)
            {
                return false;
            }
            for (int i = 0; i < MaxGears; i++)
            {
                if (GearScale[i] != 1f)
                {
                    return false;
                }
            }
            return true;
        }

        public void ApplyClutchType(ClutchType t)
        {
            ClutchGripScale = t.Grip;
            ClutchRangeScale = t.Range;
            ClutchRpmOffset = t.RpmOffset;
        }

        public static readonly GearboxPreset[] Presets;
        public static readonly GearboxPreset Stock;
        public static readonly GearboxPreset Custom;
        public static readonly GearboxPreset Defaults;

        /// <summary>The "Clutch type" grid (Custom is not a table entry: it is any other combination).</summary>
        public static readonly ClutchType[] ClutchTypes =
        {
            new ClutchType { Name = "Stock" },
            new ClutchType { Name = "Street", Grip = 0.9f, Range = 1.2f },
            new ClutchType { Name = "Sport", Grip = 1.2f, Range = 0.85f },
            new ClutchType { Name = "Race", Grip = 1.8f, Range = 0.6f, RpmOffset = -200f }
        };

        static GearboxPreset()
        {
            Stock = new GearboxPreset
            {
                Name = "Stock",
                Description = "Each vehicle's own gears and clutch, exactly as shipped."
            };
            Custom = new GearboxPreset { Name = "Custom", Description = "Your own gearbox." };
            Defaults = new GearboxPreset { Name = "Defaults" };

            Presets = new GearboxPreset[]
            {
                Stock,
                new GearboxPreset
                {
                    Name = "Comfort",
                    Description = "Softer, slower-engaging clutch for smooth pull-aways (street single-plate).",
                    ClutchGripScale = 0.9f, ClutchRangeScale = 1.2f
                },
                new GearboxPreset
                {
                    Name = "Sport",
                    Description = "Firmer clutch that bites sooner and holds more torque.",
                    ClutchGripScale = 1.2f, ClutchRangeScale = 0.85f
                },
                new GearboxPreset
                {
                    Name = "Race",
                    Description = "Emulated twin-disc: high capacity, short engagement, bites low. Harsh launches.",
                    ClutchGripScale = 1.8f, ClutchRangeScale = 0.6f, ClutchRpmOffset = -200f
                },
                Custom
            };
        }
    }
}
