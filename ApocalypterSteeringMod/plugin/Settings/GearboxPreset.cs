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
        // 0.7.0 shift controller knobs (the mod shifts tuned gearboxes itself, see ShiftController).
        public float ShiftUpFactor = 1f;                       // x the computed upshift RPM
        public float ShiftDownFactor = 1f;                     // x the computed downshift RPM
        public float KickdownScale = 1f;                       // x the full-throttle shift-point raise
        // 0.7.0: false = gears past the vehicle's own count continue its ratio progression (0.6.0);
        // true = all N gears are spread progressively over the vehicle's own 1st..top range.
        public bool SpreadRatios;

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
            ShiftUpFactor = o.ShiftUpFactor;
            ShiftDownFactor = o.ShiftDownFactor;
            KickdownScale = o.KickdownScale;
            SpreadRatios = o.SpreadRatios;
        }

        /// <summary>True when the preset touches no gear (count 0, every factor 1, no spread).</summary>
        public bool GearsIdentity()
        {
            if (GearCount != 0 || SpreadRatios)
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

        /// <summary>
        /// Does this preset need the mod's own shifting? Only when the gears change, the mode is
        /// forced or a shift knob moved: Stock and the clutch-only presets keep NWH's own shifting
        /// (variable shift points and all), so they are exactly as shipped apart from the clutch.
        /// </summary>
        public bool NeedsShiftController()
        {
            return !GearsIdentity() || TransmissionMode != GearboxMode.Stock
                || ShiftUpFactor != 1f || ShiftDownFactor != 1f || KickdownScale != 1f;
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
        public static readonly GearboxPreset Truck;

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

            // 0.7.0 (FEATURES §3): 12 gears, an earlier-shifting automatic and a stronger,
            // slower-engaging clutch. Deviation from the spec: the 12 gears are SPREAD over the
            // vehicle's own 1st..top range (progressive spacing) instead of continuing past its top
            // gear. On a typical 5-speed the continuation runs to ~0.1 by the 12th gear (a 40:1
            // spread; the 0.6.0 playtest's "ultra-tall continuation gears barely moved it"); the
            // spread gives ~6:1 with the per-gear factors below. The shift controller drives them.
            Truck = new GearboxPreset
            {
                Name = "Truck",
                Description = "12 close gears spread over the vehicle's own range, a low first gear, lazy early upshifts and a heavy-duty clutch. The mod shifts all 12 itself.",
                GearCount = 12,
                SpreadRatios = true,
                ClutchGripScale = 1.1f, ClutchRangeScale = 1.15f,
                ShiftUpFactor = 0.9f
            };
            Truck.GearScale[0] = 1.2f;
            for (int g = 1; g < 6; g++)
            {
                Truck.GearScale[g] = 1.1f;   // gears 2-6
            }
            Truck.GearScale[10] = 0.9f;      // gear 11
            Truck.GearScale[11] = 0.85f;     // gear 12

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
                Truck,
                Custom
            };
        }
    }
}
