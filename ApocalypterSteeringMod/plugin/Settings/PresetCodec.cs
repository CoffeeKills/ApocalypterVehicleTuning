using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>The ten preset books the codec can export/import (the Panel tab has none).</summary>
    public enum PresetCategory
    {
        Steering,
        Suspension,
        Aero,
        Brakes,
        Grip,
        Drivetrain,
        Assists,
        Alignment,
        Gearbox,
        Weight
    }

    /// <summary>
    /// Preset export/import as one line of text (copy/paste between players):
    ///
    ///     AVT1|Category|PresetName|BasedOn=Name|Key=Value|Key=Value|...
    ///
    /// Fields after the header are sorted by key, numbers use invariant culture with
    /// round-trip formatting, so Serialize -> Parse -> Serialize is byte-identical.
    /// Steering curves keep their own "x:y;x:y" text. Import ALWAYS goes into the
    /// category's Custom slot (built-ins are never overwritten): BasedOn is kept when it
    /// names a built-in of that category, otherwise "". Unknown keys are skipped and
    /// counted, known keys are clamped to Limits, missing keys keep Custom's current value.
    /// Pure static and UI-free, so the harness tests it directly.
    /// </summary>
    public static class PresetCodec
    {
        public const string Tag = "AVT1";

        public enum Failure
        {
            None,
            Empty,
            WrongTag,
            WrongCategory,
            Malformed
        }

        public struct Result
        {
            public bool Ok;
            public Failure Why;
            public string Category;   // the category named in the text (for WrongCategory)
            public int Applied;       // keys applied
            public int Unknown;       // keys skipped
            public int Clamped;       // keys clamped into range
        }

        private enum Kind
        {
            Float,
            Int,
            Bool,
            Text
        }

        private sealed class Field
        {
            public string Key;
            public Kind Kind;
            public float Min, Max;
            public Func<ITunablePreset, string> Get;
            public Func<ITunablePreset, float> GetNum;   // 0.7.0: numeric fields only (telemetry pins)
            // Returns false when the value cannot be parsed; sets clamped when it was clamped.
            public Func<ITunablePreset, string, Field, bool> Set;
        }

        // --------------------------------------------------------------- field tables

        private static Field F<T>(string key, float min, float max, Func<T, float> get, Action<T, float> set) where T : class, ITunablePreset
        {
            return new Field
            {
                Key = key, Kind = Kind.Float, Min = min, Max = max,
                Get = p => FormatFloat(get((T)p)),
                GetNum = p => get((T)p),
                Set = (p, s, f) =>
                {
                    float v;
                    if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || float.IsNaN(v) || float.IsInfinity(v))
                    {
                        return false;
                    }
                    set((T)p, ClampCount(v, f.Min, f.Max));
                    return true;
                }
            };
        }

        private static Field I<T>(string key, int min, int max, Func<T, int> get, Action<T, int> set) where T : class, ITunablePreset
        {
            return new Field
            {
                Key = key, Kind = Kind.Int, Min = min, Max = max,
                Get = p => get((T)p).ToString(CultureInfo.InvariantCulture),
                GetNum = p => get((T)p),
                Set = (p, s, f) =>
                {
                    int v;
                    if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    {
                        return false;
                    }
                    set((T)p, (int)ClampCount(v, f.Min, f.Max));
                    return true;
                }
            };
        }

        private static Field B<T>(string key, Func<T, bool> get, Action<T, bool> set) where T : class, ITunablePreset
        {
            return new Field
            {
                Key = key, Kind = Kind.Bool,
                Get = p => get((T)p) ? "true" : "false",
                Set = (p, s, f) =>
                {
                    bool v;
                    if (!bool.TryParse(s, out v))
                    {
                        return false;
                    }
                    set((T)p, v);
                    return true;
                }
            };
        }

        private static Field Tx<P>(string key, Func<P, string> get, Func<P, string, bool> set) where P : class, ITunablePreset
        {
            return new Field
            {
                Key = key, Kind = Kind.Text,
                Get = p => get((P)p),
                Set = (p, s, f) => set((P)p, s)
            };
        }

        [ThreadStatic] private static bool _clampedFlag;

        private static float ClampCount(float v, float min, float max)
        {
            if (v < min) { _clampedFlag = true; return min; }
            if (v > max) { _clampedFlag = true; return max; }
            return v;
        }

        private static string FormatFloat(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static Dictionary<PresetCategory, Field[]> _tables;

        private static Field[] Table(PresetCategory c)
        {
            if (_tables == null)
            {
                _tables = BuildTables();
            }
            return _tables[c];
        }

        private static Dictionary<PresetCategory, Field[]> BuildTables()
        {
            var t = new Dictionary<PresetCategory, Field[]>();

            t[PresetCategory.Steering] = new[]
            {
                F<SteeringPreset>("RateMultiplier", Limits.RateMin, Limits.RateMax, p => p.RateMultiplier, (p, v) => p.RateMultiplier = v),
                F<SteeringPreset>("SmoothingScale", Limits.SmoothMin, Limits.SmoothMax, p => p.SmoothingScale, (p, v) => p.SmoothingScale = v),
                B<SteeringPreset>("UseVehicleCurve", p => p.UseVehicleCurve, (p, v) => p.UseVehicleCurve = v),
                Tx<SteeringPreset>("LockCurve", p => p.LockCurve != null ? p.LockCurve.Serialize() : EditableCurve.DefaultLockCurveText,
                    (p, s) => { EditableCurve c; if (!EditableCurve.TryParse(s, out c)) return false; p.LockCurve = c; return true; }),
                Tx<SteeringPreset>("ReturnCurve", p => p.ReturnCurve != null ? p.ReturnCurve.Serialize() : EditableCurve.DefaultReturnCurveText,
                    (p, s) => { EditableCurve c; if (!EditableCurve.TryParse(s, out c)) return false; p.ReturnCurve = c; return true; }),
                B<SteeringPreset>("TractionClampEnabled", p => p.TractionClampEnabled, (p, v) => p.TractionClampEnabled = v),
                F<SteeringPreset>("SlipAngleDeg", Limits.SlipMin, Limits.SlipMax, p => p.SlipAngleDeg, (p, v) => p.SlipAngleDeg = v),
                F<SteeringPreset>("OppositeLockBoost", Limits.OppLockMin, Limits.OppLockMax, p => p.OppositeLockBoost, (p, v) => p.OppositeLockBoost = v),
                F<SteeringPreset>("MaxSteerAngle", Limits.MaxSteerAngleMin, Limits.MaxSteerAngleMax, p => p.MaxSteerAngle, (p, v) => p.MaxSteerAngle = v),
                B<SteeringPreset>("LinearityOverride", p => p.LinearityOverride, (p, v) => p.LinearityOverride = v),
                F<SteeringPreset>("LinearityExponent", Limits.LinExpMin, Limits.LinExpMax, p => p.LinearityExponent, (p, v) => p.LinearityExponent = v)
            };

            float smin = Limits.SuspFactorMin, smax = Limits.SuspFactorMax;
            t[PresetCategory.Suspension] = new[]
            {
                F<SuspensionPreset>("SpringFront", smin, smax, p => p.SpringFront, (p, v) => p.SpringFront = v),
                F<SuspensionPreset>("SpringRear", smin, smax, p => p.SpringRear, (p, v) => p.SpringRear = v),
                F<SuspensionPreset>("RideHeightFront", smin, smax, p => p.RideHeightFront, (p, v) => p.RideHeightFront = v),
                F<SuspensionPreset>("RideHeightRear", smin, smax, p => p.RideHeightRear, (p, v) => p.RideHeightRear = v),
                F<SuspensionPreset>("BumpFront", smin, smax, p => p.BumpFront, (p, v) => p.BumpFront = v),
                F<SuspensionPreset>("BumpRear", smin, smax, p => p.BumpRear, (p, v) => p.BumpRear = v),
                F<SuspensionPreset>("ReboundFront", smin, smax, p => p.ReboundFront, (p, v) => p.ReboundFront = v),
                F<SuspensionPreset>("ReboundRear", smin, smax, p => p.ReboundRear, (p, v) => p.ReboundRear = v),
                F<SuspensionPreset>("ArbFront", smin, smax, p => p.ArbFront, (p, v) => p.ArbFront = v),
                F<SuspensionPreset>("ArbRear", smin, smax, p => p.ArbRear, (p, v) => p.ArbRear = v)
            };

            t[PresetCategory.Aero] = new[]
            {
                F<AeroPreset>("DownforceScale", Limits.AeroScaleMin, Limits.AeroScaleMax, p => p.DownforceScale, (p, v) => p.DownforceScale = v),
                F<AeroPreset>("DragScale", Limits.AeroScaleMin, Limits.AeroScaleMax, p => p.DragScale, (p, v) => p.DragScale = v),
                F<AeroPreset>("MaxDownforceSpeedScale", Limits.AeroSpeedScaleMin, Limits.AeroSpeedScaleMax, p => p.MaxDownforceSpeedScale, (p, v) => p.MaxDownforceSpeedScale = v)
            };

            t[PresetCategory.Brakes] = new[]
            {
                F<BrakesPreset>("TorqueScale", Limits.BrakeTorqueMin, Limits.BrakeTorqueMax, p => p.TorqueScale, (p, v) => p.TorqueScale = v),
                F<BrakesPreset>("FrontBrakeScale", Limits.BrakeAxleMin, Limits.BrakeAxleMax, p => p.FrontBrakeScale, (p, v) => p.FrontBrakeScale = v),
                F<BrakesPreset>("RearBrakeScale", Limits.BrakeAxleMin, Limits.BrakeAxleMax, p => p.RearBrakeScale, (p, v) => p.RearBrakeScale = v),
                F<BrakesPreset>("HandbrakeScale", Limits.BrakeAxleMin, Limits.BrakeAxleMax, p => p.HandbrakeScale, (p, v) => p.HandbrakeScale = v),
                F<BrakesPreset>("ActuationScale", Limits.ActuationMin, Limits.ActuationMax, p => p.ActuationScale, (p, v) => p.ActuationScale = v)
            };

            t[PresetCategory.Grip] = new[]
            {
                F<GripPreset>("LongitudinalScale", Limits.GripMin, Limits.GripMax, p => p.LongitudinalScale, (p, v) => p.LongitudinalScale = v),
                F<GripPreset>("LateralScale", Limits.GripMin, Limits.GripMax, p => p.LateralScale, (p, v) => p.LateralScale = v),
                F<GripPreset>("StiffnessScale", Limits.GripMin, Limits.GripMax, p => p.StiffnessScale, (p, v) => p.StiffnessScale = v)
            };

            t[PresetCategory.Drivetrain] = new[]
            {
                F<DrivetrainPreset>("PowerScale", Limits.PowerMin, Limits.PowerMax, p => p.PowerScale, (p, v) => p.PowerScale = v),
                F<DrivetrainPreset>("RevLimiterScale", Limits.RevLimitMin, Limits.RevLimitMax, p => p.RevLimiterScale, (p, v) => p.RevLimiterScale = v),
                F<DrivetrainPreset>("LossScale", Limits.LossMin, Limits.LossMax, p => p.LossScale, (p, v) => p.LossScale = v),
                F<DrivetrainPreset>("BoostScale", Limits.BoostMin, Limits.BoostMax, p => p.BoostScale, (p, v) => p.BoostScale = v),
                F<DrivetrainPreset>("FinalDriveScale", Limits.FinalDriveMin, Limits.FinalDriveMax, p => p.FinalDriveScale, (p, v) => p.FinalDriveScale = v),
                F<DrivetrainPreset>("UpshiftScale", Limits.ShiftRpmMin, Limits.ShiftRpmMax, p => p.UpshiftScale, (p, v) => p.UpshiftScale = v),
                F<DrivetrainPreset>("DownshiftScale", Limits.ShiftRpmMin, Limits.ShiftRpmMax, p => p.DownshiftScale, (p, v) => p.DownshiftScale = v),
                F<DrivetrainPreset>("ShiftDurationScale", Limits.FactorMin, Limits.FactorMax, p => p.ShiftDurationScale, (p, v) => p.ShiftDurationScale = v),
                F<DrivetrainPreset>("DiffStiffnessScale", Limits.DiffScaleMin, Limits.DiffScaleMax, p => p.DiffStiffnessScale, (p, v) => p.DiffStiffnessScale = v),
                F<DrivetrainPreset>("DiffBiasScale", Limits.DiffScaleMin, Limits.DiffScaleMax, p => p.DiffBiasScale, (p, v) => p.DiffBiasScale = v),
                Tx<DrivetrainPreset>("DiffFrontMode", p => p.DiffFrontMode.ToString(), (p, s) => { DiffMode m; if (!TryParseName(s, out m)) return false; p.DiffFrontMode = m; return true; }),
                Tx<DrivetrainPreset>("DiffCenterMode", p => p.DiffCenterMode.ToString(), (p, s) => { DiffMode m; if (!TryParseName(s, out m)) return false; p.DiffCenterMode = m; return true; }),
                Tx<DrivetrainPreset>("DiffRearMode", p => p.DiffRearMode.ToString(), (p, s) => { DiffMode m; if (!TryParseName(s, out m)) return false; p.DiffRearMode = m; return true; })
            };

            t[PresetCategory.Assists] = new[]
            {
                B<AssistsPreset>("AbsEnabled", p => p.AbsEnabled, (p, v) => p.AbsEnabled = v),
                F<AssistsPreset>("AbsSlipThreshold", Limits.SlipThrMin, Limits.SlipThrMax, p => p.AbsSlipThreshold, (p, v) => p.AbsSlipThreshold = v),
                F<AssistsPreset>("AbsCutoffSpeed", Limits.CutoffSpeedMin, Limits.CutoffSpeedMax, p => p.AbsCutoffSpeed, (p, v) => p.AbsCutoffSpeed = v),
                F<AssistsPreset>("AbsCutMultiplier", Limits.CutMultMin, Limits.CutMultMax, p => p.AbsCutMultiplier, (p, v) => p.AbsCutMultiplier = v),
                B<AssistsPreset>("TcsEnabled", p => p.TcsEnabled, (p, v) => p.TcsEnabled = v),
                F<AssistsPreset>("TcsSlipThreshold", Limits.SlipThrMin, Limits.SlipThrMax, p => p.TcsSlipThreshold, (p, v) => p.TcsSlipThreshold = v),
                F<AssistsPreset>("TcsCutoffSpeed", Limits.CutoffSpeedMin, Limits.CutoffSpeedMax, p => p.TcsCutoffSpeed, (p, v) => p.TcsCutoffSpeed = v),
                F<AssistsPreset>("TcsCutMultiplier", Limits.CutMultMin, Limits.CutMultMax, p => p.TcsCutMultiplier, (p, v) => p.TcsCutMultiplier = v)
            };

            var align = new List<Field>
            {
                F<AlignmentPreset>("CasterFront", Limits.AlignmentCasterMin, Limits.AlignmentCasterMax, p => p.CasterF, (p, v) => p.CasterF = v),
                F<AlignmentPreset>("CasterRear", Limits.AlignmentCasterMin, Limits.AlignmentCasterMax, p => p.CasterR, (p, v) => p.CasterR = v),
                F<AlignmentPreset>("ToeFront", Limits.AlignmentToeMin, Limits.AlignmentToeMax, p => p.ToeF, (p, v) => p.ToeF = v),
                F<AlignmentPreset>("ToeRear", Limits.AlignmentToeMin, Limits.AlignmentToeMax, p => p.ToeR, (p, v) => p.ToeR = v)
            };
            for (int r = 0; r < 4; r++)
            {
                WheelRole role = (WheelRole)r;
                align.Add(F<AlignmentPreset>("Camber" + role, Limits.AlignmentCamberMin, Limits.AlignmentCamberMax,
                    p => p.Camber(role), (p, v) => p.SetCamber(role, v)));
                for (int axis = 0; axis < 3; axis++)
                {
                    int a = axis;
                    align.Add(F<AlignmentPreset>(AlignmentPosKey(role, a), Limits.AlignmentPosMin, Limits.AlignmentPosMax,
                        p => p.Pos(role, a), (p, v) => p.SetPos(role, a, v)));
                }
            }
            t[PresetCategory.Alignment] = align.ToArray();

            var gear = new List<Field>
            {
                I<GearboxPreset>("GearCount", Limits.GearCountMin, Limits.GearCountMax, p => p.GearCount, (p, v) => p.GearCount = v),
                F<GearboxPreset>("ClutchGripScale", Limits.ClutchGripMin, Limits.ClutchGripMax, p => p.ClutchGripScale, (p, v) => p.ClutchGripScale = v),
                F<GearboxPreset>("ClutchRangeScale", Limits.ClutchRangeMin, Limits.ClutchRangeMax, p => p.ClutchRangeScale, (p, v) => p.ClutchRangeScale = v),
                F<GearboxPreset>("ClutchRpmOffset", Limits.ClutchRpmMin, Limits.ClutchRpmMax, p => p.ClutchRpmOffset, (p, v) => p.ClutchRpmOffset = v),
                B<GearboxPreset>("SpreadRatios", p => p.SpreadRatios, (p, v) => p.SpreadRatios = v),
                F<GearboxPreset>("ShiftUpFactor", Limits.ShiftFactorMin, Limits.ShiftFactorMax, p => p.ShiftUpFactor, (p, v) => p.ShiftUpFactor = v),
                F<GearboxPreset>("ShiftDownFactor", Limits.ShiftFactorMin, Limits.ShiftFactorMax, p => p.ShiftDownFactor, (p, v) => p.ShiftDownFactor = v),
                F<GearboxPreset>("KickdownScale", Limits.KickdownMin, Limits.KickdownMax, p => p.KickdownScale, (p, v) => p.KickdownScale = v),
                Tx<GearboxPreset>("TransmissionMode", p => p.TransmissionMode.ToString(), (p, s) => { GearboxMode m; if (!TryParseName(s, out m)) return false; p.TransmissionMode = m; return true; })
            };
            for (int g = 1; g <= GearboxPreset.MaxGears; g++)
            {
                int gi = g;
                gear.Add(F<GearboxPreset>(GearKey(gi), Limits.GearRatioMin, Limits.GearRatioMax, p => p.Scale(gi), (p, v) => p.SetScale(gi, v)));
            }
            t[PresetCategory.Gearbox] = gear.ToArray();

            t[PresetCategory.Weight] = new[]
            {
                F<WeightPreset>("FrontKg", Limits.WeightKgMin, Limits.WeightKgMax, p => p.FrontKg, (p, v) => p.FrontKg = v),
                F<WeightPreset>("RearKg", Limits.WeightKgMin, Limits.WeightKgMax, p => p.RearKg, (p, v) => p.RearKg = v)
            };

            // Sorted by key (ordinal) so the text is canonical.
            var keys = new List<PresetCategory>(t.Keys);
            foreach (PresetCategory c in keys)
            {
                Field[] arr = t[c];
                Array.Sort(arr, (a, b) => string.CompareOrdinal(a.Key, b.Key));
            }
            return t;
        }

        /// <summary>Config/codec key for an alignment position offset, e.g. PosXFL.</summary>
        public static string AlignmentPosKey(WheelRole role, int axis)
        {
            return "Pos" + (axis == 0 ? "X" : axis == 1 ? "Y" : "Z") + role;
        }

        /// <summary>Config/codec key for a gear factor, e.g. Gear3Scale.</summary>
        public static string GearKey(int gear)
        {
            return "Gear" + gear.ToString(CultureInfo.InvariantCulture) + "Scale";
        }

        /// <summary>Enum names only (case/space tolerant); numbers and unknown names fail.</summary>
        public static bool TryParseName<TEnum>(string value, out TEnum result) where TEnum : struct
        {
            result = default(TEnum);
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string v = value.Trim();
            if (v.Length == 0 || char.IsDigit(v[0]) || v[0] == '-' || v[0] == '+')
            {
                return false;
            }
            if (typeof(TEnum) == typeof(DiffMode) && string.Equals(v, "LSD", StringComparison.OrdinalIgnoreCase))
            {
                result = (TEnum)(object)DiffMode.LimitedSlip;
                return true;
            }
            return Enum.TryParse(v, true, out result) && Enum.IsDefined(typeof(TEnum), result);
        }

        /// <summary>True for a numeric (slider) key of this category — the telemetry-pinnable ones.</summary>
        public static bool IsNumericKey(PresetCategory c, string key)
        {
            Field f = Find(Table(c), key);
            return f != null && f.GetNum != null;
        }

        /// <summary>A numeric field of <paramref name="preset"/> (allocation-free after the tables exist).</summary>
        public static bool TryGetNumber(PresetCategory c, ITunablePreset preset, string key, out float value)
        {
            Field f = Find(Table(c), key);
            if (f == null || f.GetNum == null || preset == null)
            {
                value = 0f;
                return false;
            }
            value = f.GetNum(preset);
            return true;
        }

        /// <summary>The preset a category's sliders currently show (the active one; steering Vanilla shows the defaults).</summary>
        public static ITunablePreset ShownPreset(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.Steering:
                    SteeringPreset sp = SteeringSettings.ActivePreset ?? SteeringPreset.Custom;
                    return sp.IsVanilla ? SteeringPreset.Defaults : sp;
                case PresetCategory.Suspension: return SuspensionSettings.Shown;
                case PresetCategory.Aero: return AeroSettings.Shown;
                case PresetCategory.Brakes: return BrakesSettings.Shown;
                case PresetCategory.Grip: return GripSettings.Shown;
                case PresetCategory.Drivetrain: return DrivetrainSettings.Shown;
                case PresetCategory.Assists: return AssistsSettings.Shown;
                case PresetCategory.Alignment: return AlignmentSettings.Shown;
                case PresetCategory.Gearbox: return GearboxSettings.Shown;
                default: return WeightSettings.Shown;
            }
        }

        /// <summary>A fresh preset instance for a category (the per-vehicle import target).</summary>
        public static ITunablePreset NewPreset(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.Steering: return new SteeringPreset();
                case PresetCategory.Suspension: return new SuspensionPreset();
                case PresetCategory.Aero: return new AeroPreset();
                case PresetCategory.Brakes: return new BrakesPreset();
                case PresetCategory.Grip: return new GripPreset();
                case PresetCategory.Drivetrain: return new DrivetrainPreset();
                case PresetCategory.Assists: return new AssistsPreset();
                case PresetCategory.Alignment: return new AlignmentPreset();
                case PresetCategory.Gearbox: return new GearboxPreset();
                default: return new WeightPreset();
            }
        }

        /// <summary>Number of keys a category carries (for tests and the status line).</summary>
        public static int KeyCount(PresetCategory c)
        {
            return Table(c).Length;
        }

        // --------------------------------------------------------------- serialize

        public static string Serialize(PresetCategory category, ITunablePreset preset)
        {
            return SerializeLine(category, preset.Name, ExportBasedOn(category, preset), preset);
        }

        /// <summary>
        /// Per-vehicle blob variant (0.9.0): a vehicle copy is not a selectable preset,
        /// so it has no name of its own and exports the BasedOn SaveVehicle stored on it.
        /// The "Custom" name is cosmetic; ParseInto ignores it.
        /// </summary>
        public static string SerializeVehicle(PresetCategory category, ITunablePreset copy)
        {
            return SerializeLine(category, "Custom", copy.BasedOn ?? "", copy);
        }

        private static string SerializeLine(PresetCategory category, string name, string basedOn, ITunablePreset preset)
        {
            var sb = new StringBuilder(256);
            sb.Append(Tag).Append('|').Append(category.ToString()).Append('|').Append(name);
            sb.Append("|BasedOn=").Append(basedOn);
            Field[] fields = Table(category);
            for (int i = 0; i < fields.Length; i++)
            {
                sb.Append('|').Append(fields[i].Key).Append('=').Append(fields[i].Get(preset));
            }
            return sb.ToString();
        }

        /// <summary>Custom carries its own BasedOn; a built-in exports its own name, so import forks Custom(Name).</summary>
        private static string ExportBasedOn(PresetCategory category, ITunablePreset preset)
        {
            Book b = BookFor(category);
            if (preset == b.Custom)
            {
                return preset.BasedOn ?? "";
            }
            return preset.Name;
        }

        /// <summary>True when <paramref name="name"/> names a built-in preset of the category — a valid BasedOn origin.</summary>
        public static bool ResolveBuiltIn(PresetCategory category, string name)
        {
            return BookFor(category).ResolveBuiltIn(name);
        }

        // --------------------------------------------------------------- parse / import

        /// <summary>
        /// Parse <paramref name="text"/> for <paramref name="expected"/> and, on success,
        /// write it into that category's Custom slot and make Custom active.
        /// </summary>
        public static Result Import(PresetCategory expected, string text)
        {
            Book b = BookFor(expected);
            ITunablePreset temp = b.NewTemp();
            temp.CopyValuesFrom(b.Custom);
            string basedOn;
            Result r = ParseInto(expected, text, temp, out basedOn);
            if (!r.Ok)
            {
                return r;
            }
            b.Custom.CopyValuesFrom(temp);
            b.Custom.BasedOn = b.ResolveBuiltIn(basedOn) ? basedOn : "";
            b.ActivateCustom();
            return r;
        }

        /// <summary>Parse only (no side effects on the books beyond <paramref name="target"/>).</summary>
        public static Result ParseInto(PresetCategory expected, string text, ITunablePreset target, out string basedOn)
        {
            basedOn = "";
            var r = new Result();
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                r.Why = Failure.Empty;
                return r;
            }
            string[] parts = text.Trim().Split('|');
            if (parts.Length < 3 || !string.Equals(parts[0].Trim(), Tag, StringComparison.Ordinal))
            {
                r.Why = WrongOrMalformed(parts);
                return r;
            }
            string cat = parts[1].Trim();
            r.Category = cat;
            if (!string.Equals(cat, expected.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                r.Why = Failure.WrongCategory;
                return r;
            }

            Field[] fields = Table(expected);
            for (int i = 3; i < parts.Length; i++)
            {
                string part = parts[i];
                int eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    if (part.Trim().Length == 0)
                    {
                        continue;   // tolerate a trailing '|'
                    }
                    r.Why = Failure.Malformed;
                    return r;
                }
                string key = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();
                if (string.Equals(key, "BasedOn", StringComparison.Ordinal))
                {
                    basedOn = value;
                    continue;
                }
                Field f = Find(fields, key);
                if (f == null)
                {
                    r.Unknown++;
                    continue;
                }
                _clampedFlag = false;
                if (!f.Set(target, value, f))
                {
                    r.Unknown++;   // unparsable value: skipped like an unknown key, Custom keeps its value
                    continue;
                }
                if (_clampedFlag)
                {
                    r.Clamped++;
                }
                r.Applied++;
            }
            r.Ok = true;
            return r;
        }

        private static Failure WrongOrMalformed(string[] parts)
        {
            return parts.Length >= 1 && parts[0].Trim().StartsWith("AVT", StringComparison.Ordinal) ? Failure.WrongTag
                : parts.Length >= 1 && parts[0].Trim().Length > 0 ? Failure.WrongTag : Failure.Malformed;
        }

        private static Field Find(Field[] fields, string key)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.Equals(fields[i].Key, key, StringComparison.Ordinal))
                {
                    return fields[i];
                }
            }
            return null;
        }

        // --------------------------------------------------------------- book adapters

        private sealed class Book
        {
            public ITunablePreset Custom;
            public Func<ITunablePreset> NewTemp;
            public Func<string, bool> ResolveBuiltIn;
            public Action ActivateCustom;
        }

        private static Book BookFor(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.Steering:
                    return new Book
                    {
                        Custom = SteeringPreset.Custom,
                        NewTemp = () => new SteeringPreset { Name = "Import" },
                        ResolveBuiltIn = n => SteeringSettings.Book.FindBuiltIn(n) != null,
                        ActivateCustom = () => SteeringSettings.Book.Active = SteeringPreset.Custom
                    };
                case PresetCategory.Suspension:
                    return Make(SuspensionSettings.Book, () => new SuspensionPreset { Name = "Import" });
                case PresetCategory.Aero:
                    return Make(AeroSettings.Book, () => new AeroPreset { Name = "Import" });
                case PresetCategory.Brakes:
                    return Make(BrakesSettings.Book, () => new BrakesPreset { Name = "Import" });
                case PresetCategory.Grip:
                    return Make(GripSettings.Book, () => new GripPreset { Name = "Import" });
                case PresetCategory.Drivetrain:
                    return Make(DrivetrainSettings.Book, () => new DrivetrainPreset { Name = "Import" });
                case PresetCategory.Assists:
                    return Make(AssistsSettings.Book, () => new AssistsPreset { Name = "Import" });
                case PresetCategory.Alignment:
                    return Make(AlignmentSettings.Book, () => new AlignmentPreset { Name = "Import" });
                case PresetCategory.Gearbox:
                    return Make(GearboxSettings.Book, () => new GearboxPreset { Name = "Import" });
                default:
                    return Make(WeightSettings.Book, () => new WeightPreset { Name = "Import" });
            }
        }

        private static Book Make<P>(PresetBook<P> book, Func<P> temp) where P : class, ITunablePreset
        {
            return new Book
            {
                Custom = book.Custom,
                NewTemp = () => temp(),
                ResolveBuiltIn = n => book.FindBuiltIn(n) != null,
                ActivateCustom = () => book.Active = book.Custom
            };
        }
    }
}
