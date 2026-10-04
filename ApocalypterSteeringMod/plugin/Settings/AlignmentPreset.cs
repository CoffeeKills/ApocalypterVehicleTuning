namespace ApocalypterSteeringMod.Settings
{
    /// <summary>Wheel slot, as seen from the driver's seat.</summary>
    public enum WheelRole
    {
        FL = 0,
        FR = 1,
        RL = 2,
        RR = 3
    }

    /// <summary>
    /// Wheel geometry presets. Every value is an OFFSET in absolute units (degrees /
    /// centimetres) added to each vehicle's own captured geometry, so Stock (all 0) is
    /// exactly the shipped vehicle and one preset suits every vehicle.
    ///
    /// Sign conventions:
    ///  - Camber: NWH's WheelController convention, mirrored per side by NWH itself
    ///    (negative = top of the wheel leans in).
    ///  - Caster / toe: WheelGroup.CasterAngle / ToeAngle, per axle (NWH mirrors toe L/R).
    ///  - PosX: OUTWARD from the centreline (positive = wider track on both sides),
    ///    so presets stay symmetric. PosY: up. PosZ: forward. Centimetres.
    /// </summary>
    public sealed class AlignmentPreset : ITunablePreset
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public float CamberFL, CamberFR, CamberRL, CamberRR;   // deg
        public float CasterF, CasterR;                           // deg (per axle)
        public float ToeF, ToeR;                                 // deg (per axle)
        public float PosXFL, PosXFR, PosXRL, PosXRR;             // cm, outward
        public float PosYFL, PosYFR, PosYRL, PosYRR;             // cm, up
        public float PosZFL, PosZFR, PosZRL, PosZRR;             // cm, forward

        public string Label => Name;
        public string BasedOn { get; set; } = "";
        public bool CanEdit => true;

        public void CopyValuesFrom(ITunablePreset src)
        {
            var o = (AlignmentPreset)src;
            CamberFL = o.CamberFL; CamberFR = o.CamberFR; CamberRL = o.CamberRL; CamberRR = o.CamberRR;
            CasterF = o.CasterF; CasterR = o.CasterR;
            ToeF = o.ToeF; ToeR = o.ToeR;
            PosXFL = o.PosXFL; PosXFR = o.PosXFR; PosXRL = o.PosXRL; PosXRR = o.PosXRR;
            PosYFL = o.PosYFL; PosYFR = o.PosYFR; PosYRL = o.PosYRL; PosYRR = o.PosYRR;
            PosZFL = o.PosZFL; PosZFR = o.PosZFR; PosZRL = o.PosZRL; PosZRR = o.PosZRR;
        }

        // ------------------------------------------------------------ per-wheel access

        public float Camber(WheelRole r)
        {
            switch (r)
            {
                case WheelRole.FL: return CamberFL;
                case WheelRole.FR: return CamberFR;
                case WheelRole.RL: return CamberRL;
                default: return CamberRR;
            }
        }

        public void SetCamber(WheelRole r, float v)
        {
            switch (r)
            {
                case WheelRole.FL: CamberFL = v; break;
                case WheelRole.FR: CamberFR = v; break;
                case WheelRole.RL: CamberRL = v; break;
                default: CamberRR = v; break;
            }
        }

        /// <summary>axis 0 = X (outward), 1 = Y (up), 2 = Z (forward); centimetres.</summary>
        public float Pos(WheelRole r, int axis)
        {
            switch (axis)
            {
                case 0: return r == WheelRole.FL ? PosXFL : r == WheelRole.FR ? PosXFR : r == WheelRole.RL ? PosXRL : PosXRR;
                case 1: return r == WheelRole.FL ? PosYFL : r == WheelRole.FR ? PosYFR : r == WheelRole.RL ? PosYRL : PosYRR;
                default: return r == WheelRole.FL ? PosZFL : r == WheelRole.FR ? PosZFR : r == WheelRole.RL ? PosZRL : PosZRR;
            }
        }

        public void SetPos(WheelRole r, int axis, float v)
        {
            switch (axis)
            {
                case 0:
                    if (r == WheelRole.FL) PosXFL = v; else if (r == WheelRole.FR) PosXFR = v; else if (r == WheelRole.RL) PosXRL = v; else PosXRR = v;
                    break;
                case 1:
                    if (r == WheelRole.FL) PosYFL = v; else if (r == WheelRole.FR) PosYFR = v; else if (r == WheelRole.RL) PosYRL = v; else PosYRR = v;
                    break;
                default:
                    if (r == WheelRole.FL) PosZFL = v; else if (r == WheelRole.FR) PosZFR = v; else if (r == WheelRole.RL) PosZRL = v; else PosZRR = v;
                    break;
            }
        }

        public float Caster(bool front) { return front ? CasterF : CasterR; }
        public float Toe(bool front) { return front ? ToeF : ToeR; }

        /// <summary>True when any left/right pair differs (per-wheel values in use).</summary>
        public bool SidesDiffer()
        {
            if (CamberFL != CamberFR || CamberRL != CamberRR) return true;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Pos(WheelRole.FL, axis) != Pos(WheelRole.FR, axis)) return true;
                if (Pos(WheelRole.RL, axis) != Pos(WheelRole.RR, axis)) return true;
            }
            return false;
        }

        /// <summary>True when every offset is 0 (the shipped geometry).</summary>
        public bool IsIdentity()
        {
            if (CamberFL != 0f || CamberFR != 0f || CamberRL != 0f || CamberRR != 0f) return false;
            if (CasterF != 0f || CasterR != 0f || ToeF != 0f || ToeR != 0f) return false;
            for (int r = 0; r < 4; r++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Pos((WheelRole)r, axis) != 0f) return false;
                }
            }
            return true;
        }

        public static readonly AlignmentPreset[] Presets;
        public static readonly AlignmentPreset Stock;
        public static readonly AlignmentPreset Custom;
        public static readonly AlignmentPreset Defaults;

        static AlignmentPreset()
        {
            Stock = new AlignmentPreset
            {
                Name = "Stock",
                Description = "Each vehicle's own wheel geometry, exactly as shipped."
            };
            Custom = new AlignmentPreset { Name = "Custom", Description = "Your own geometry." };
            Defaults = new AlignmentPreset { Name = "Defaults" };

            Presets = new AlignmentPreset[]
            {
                Stock,
                new AlignmentPreset
                {
                    Name = "Street",
                    Description = "A touch of negative camber, slight toe-in up front and a little caster for stability.",
                    CamberFL = -1.0f, CamberFR = -1.0f, CamberRL = -0.5f, CamberRR = -0.5f,
                    ToeF = 0.05f, ToeR = -0.05f, CasterF = 1.0f
                },
                new AlignmentPreset
                {
                    Name = "Sport",
                    Description = "More camber and caster for corner grip and self-centering, slightly lowered.",
                    CamberFL = -2.0f, CamberFR = -2.0f, CamberRL = -1.2f, CamberRR = -1.2f,
                    ToeF = 0.10f, ToeR = 0.02f, CasterF = 3.0f,
                    PosYFL = -2f, PosYFR = -2f, PosYRL = -2f, PosYRR = -2f
                },
                new AlignmentPreset
                {
                    Name = "Race",
                    Description = "Aggressive camber and caster, low. Best on smooth tarmac; wears in on straights.",
                    CamberFL = -3.5f, CamberFR = -3.5f, CamberRL = -2.5f, CamberRR = -2.5f,
                    ToeF = 0.15f, ToeR = 0.05f, CasterF = 5.0f,
                    PosYFL = -3f, PosYFR = -3f, PosYRL = -3f, PosYRR = -3f
                },
                new AlignmentPreset
                {
                    Name = "Off-road",
                    Description = "Zero camber, toe-in, raised and with a wider track for stability on rubble.",
                    ToeF = 0.20f,
                    PosYFL = 5f, PosYFR = 5f, PosYRL = 5f, PosYRR = 5f,
                    PosXFL = 6f, PosXFR = 6f, PosXRL = 6f, PosXRR = 6f
                },
                new AlignmentPreset
                {
                    Name = "Stance",
                    Description = "Show car: extreme negative camber, slammed and tucked in. Looks, not grip.",
                    CamberFL = -8f, CamberFR = -8f, CamberRL = -8f, CamberRR = -8f,
                    PosYFL = -6f, PosYFR = -6f, PosYRL = -6f, PosYRR = -6f,
                    PosXFL = -4f, PosXFR = -4f, PosXRL = -4f, PosXRR = -4f
                },
                Custom
            };
        }
    }
}
