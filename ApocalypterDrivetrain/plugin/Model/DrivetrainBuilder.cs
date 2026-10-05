// JSON text -> validated Drivetrain. Collects every error it can find in one pass (not
// fail-fast) so a config author sees all problems at once; returns a Drivetrain only when
// there are zero errors. Warnings never block. Load-time only.
using System;
using System.Collections.Generic;
using SPO.Vehicle;

namespace ApocalypterDrivetrain.Model
{
    public sealed class BuildResult
    {
        public Drivetrain Drivetrain;          // null when Diagnostics has errors
        public Diagnostics Diagnostics;
        public bool Ok { get { return Drivetrain != null; } }
    }

    public static class DrivetrainBuilder
    {
        public const int SchemaVersion = 1;
        public const float MaxRpm = 30000f;
        public const int MaxForwardGears = 32, MaxReverseGears = 8, MaxAxles = 16, MaxDifferentials = 32;
        public const int MinSamples = 2, MaxSamples = 512;
        private const string GearboxNode = "gearbox";

        public static BuildResult Build(string jsonText)
        {
            var d = new Diagnostics();
            var result = new BuildResult { Diagnostics = d };
            JsonValue root;
            try
            {
                root = Json.Parse(jsonText);
            }
            catch (JsonParseException e)
            {
                d.Items.Add(new Diagnostic
                {
                    IsError = true, Error = e.DuplicateKey ? ErrorCode.DuplicateKey : ErrorCode.JsonSyntax,
                    Line = e.Line, Column = e.Column, Path = "", Message = e.Reason,
                });
                return result;
            }
            ObjectReader top = ObjectReader.Wrap(root, "", d);
            if (top == null) return result;

            var dt = new Drivetrain();
            int schema;
            if (top.Integer("schema", 0, int.MaxValue, out schema))
            {
                if (schema != SchemaVersion)
                    d.Error(ErrorCode.UnsupportedSchema, "schema", top.Obj.Get("schema"),
                        "schema " + schema + " is not supported by this mod version (supports " + SchemaVersion + ")");
                dt.Schema = schema;
            }
            string name;
            if (top.String("name", out name))
            {
                if (name.Trim().Length == 0) d.Error(ErrorCode.BadName, "name", top.Obj.Get("name"), "name must not be empty");
                dt.Name = name;
            }

            ObjectReader engine = top.Object("engine", true);
            if (engine != null) dt.Engine = ReadEngine(engine, d);
            ObjectReader clutch = top.Object("clutch", true);
            if (clutch != null) dt.Clutch = ReadClutch(clutch, dt.Engine, d);
            ObjectReader gearbox = top.Object("gearbox", true);
            JsonValue gearboxOutput = null;
            if (gearbox != null) dt.Gearbox = ReadGearbox(gearbox, dt.Engine, d, out gearboxOutput);

            int errorsBeforeLayout = d.ErrorCount;
            List<JsonValue[]> diffOutputs;
            dt.Differentials = ReadDifferentials(top, d, out diffOutputs);
            dt.Axles = ReadAxles(top, d);
            bool layoutOk = d.ErrorCount == errorsBeforeLayout && dt.Differentials != null && dt.Axles != null
                && gearboxOutput != null;
            if (layoutOk) Wire(dt, gearboxOutput, diffOutputs, d);

            top.Finish();
            if (d.ErrorCount == 0) result.Drivetrain = dt;
            return result;
        }

        // ---------------------------------------------------------------- engine

        private static EngineDef ReadEngine(ObjectReader r, Diagnostics d)
        {
            var e = new EngineDef();
            bool idleOk = r.Number("idleRpm", ValueRange.Positive(MaxRpm), out e.IdleRpm);
            bool redOk = r.Number("redlineRpm", ValueRange.Positive(MaxRpm), out e.RedlineRpm);
            bool limOk = r.Number("revLimitRpm", ValueRange.Positive(MaxRpm), out e.RevLimitRpm);
            r.Number("revLimiterCutTime", ValueRange.Positive(2), out e.RevLimiterCutTime);
            r.Number("inertia", ValueRange.Positive(100), out e.Inertia);
            if (idleOk && redOk && e.IdleRpm >= e.RedlineRpm)
                d.Error(ErrorCode.IdleNotBelowRedline, r.PathOf("idleRpm"), r.Obj.Get("idleRpm"),
                    "idleRpm (" + ValueRange.Fmt(e.IdleRpm) + ") must be below redlineRpm (" + ValueRange.Fmt(e.RedlineRpm) + ")");
            if (redOk && limOk && e.RedlineRpm > e.RevLimitRpm)
                d.Error(ErrorCode.RedlineAboveRevLimit, r.PathOf("redlineRpm"), r.Obj.Get("redlineRpm"),
                    "redlineRpm (" + ValueRange.Fmt(e.RedlineRpm) + ") must not exceed revLimitRpm (" + ValueRange.Fmt(e.RevLimitRpm) + ")");
            bool rangeOk = idleOk && limOk && e.IdleRpm < e.RevLimitRpm;

            ObjectReader tc = r.Object("torqueCurve", true);
            if (tc != null)
            {
                bool hasPoints = tc.Has("points"), hasExpr = tc.Has("expression");
                if (hasPoints == hasExpr)
                {
                    d.Error(ErrorCode.CurveSourceAmbiguous, tc.Path, tc.Obj,
                        hasPoints ? "give either \"points\" or \"expression\", not both" : "needs \"points\" or \"expression\"");
                    tc.Raw("points", false); tc.Raw("expression", false); tc.Raw("samples", false);
                }
                else if (hasPoints)
                {
                    tc.Reject("samples", ErrorCode.ParamNotApplicable, "\"samples\" only applies to an \"expression\" curve");
                    float[] x, y;
                    if (ReadPoints(tc, "points", ValueRange.Positive(MaxRpm), ValueRange.Closed(-100000, 100000), d, out x, out y))
                    {
                        bool ok = true;
                        for (int i = 0; i < y.Length; i++)
                        {
                            if (y[i] < 0f)
                            {
                                d.Error(ErrorCode.CurveNegativeTorque, tc.PathOf("points") + "[" + i + "][1]", tc.Obj.Get("points").Items[i],
                                    "torque must be >= 0 (engine braking is not part of the torque curve)");
                                ok = false;
                            }
                        }
                        if (ok && rangeOk && (x[0] > e.IdleRpm || x[x.Length - 1] < e.RevLimitRpm))
                        {
                            d.Error(ErrorCode.CurveDoesNotCoverRange, tc.PathOf("points"), tc.Obj.Get("points"),
                                "curve spans " + ValueRange.Fmt(x[0]) + "-" + ValueRange.Fmt(x[x.Length - 1]) + " rpm but must cover idleRpm-revLimitRpm ("
                                + ValueRange.Fmt(e.IdleRpm) + "-" + ValueRange.Fmt(e.RevLimitRpm) + "); no extrapolation");
                            ok = false;
                        }
                        if (ok) e.TorqueCurve = new PiecewiseLinear(x, y);
                    }
                }
                else
                {
                    string src;
                    int samples;
                    bool srcOk = tc.String("expression", out src);
                    bool samplesOk = tc.Integer("samples", MinSamples, MaxSamples, out samples);
                    if (srcOk)
                    {
                        CurveExpression expr = null;
                        try { expr = CurveExpression.Compile(src); }
                        catch (ExpressionException ex)
                        {
                            d.Error(ErrorCode.ExpressionSyntax, tc.PathOf("expression"), tc.Obj.Get("expression"),
                                "at character " + ex.Position + ": " + ex.Message);
                        }
                        if (expr != null && samplesOk && rangeOk)
                        {
                            e.TorqueCurve = SampleExpression(expr, e.IdleRpm, e.RevLimitRpm, samples, tc, d);
                            if (e.TorqueCurve != null) e.TorqueExpression = src;
                        }
                    }
                }
                tc.Finish();
            }
            r.Finish();
            return e;
        }

        private static PiecewiseLinear SampleExpression(CurveExpression expr, float from, float to, int samples, ObjectReader tc, Diagnostics d)
        {
            var x = new float[samples];
            var y = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                double rpm = from + (to - (double)from) * i / (samples - 1);
                double t = expr.Evaluate(rpm);
                if (double.IsNaN(t) || double.IsInfinity(t) || Math.Abs(t) > float.MaxValue)
                {
                    d.Error(ErrorCode.ExpressionNotFinite, tc.PathOf("expression"), tc.Obj.Get("expression"),
                        "evaluates to a non-finite value at " + ValueRange.Fmt(rpm) + " rpm");
                    return null;
                }
                if (t < 0)
                {
                    d.Error(ErrorCode.CurveNegativeTorque, tc.PathOf("expression"), tc.Obj.Get("expression"),
                        "evaluates to negative torque (" + ValueRange.Fmt(t) + ") at " + ValueRange.Fmt(rpm) + " rpm; wrap it in max(..., 0) if that is intended");
                    return null;
                }
                x[i] = (float)rpm;
                y[i] = (float)t;
            }
            return new PiecewiseLinear(x, y);
        }

        /// <summary>[[x, y], ...] with >= 2 points and strictly increasing x.</summary>
        private static bool ReadPoints(ObjectReader r, string key, ValueRange xr, ValueRange yr, Diagnostics d, out float[] xs, out float[] ys)
        {
            xs = ys = null;
            JsonValue arr = r.Array(key, true);
            if (arr == null) return false;
            string path = r.PathOf(key);
            if (arr.Items.Count < 2)
            {
                d.Error(ErrorCode.CurveTooFewPoints, path, arr, "needs at least 2 points, got " + arr.Items.Count);
                return false;
            }
            var x = new float[arr.Items.Count];
            var y = new float[arr.Items.Count];
            bool ok = true;
            for (int i = 0; i < arr.Items.Count; i++)
            {
                JsonValue p = arr.Items[i];
                string pp = path + "[" + i + "]";
                if (p.Kind != JsonKind.Array || p.Items.Count != 2)
                {
                    d.Error(ErrorCode.WrongType, pp, p, "expected a [x, y] pair, got " + (p.Kind == JsonKind.Array ? "an array of " + p.Items.Count : p.KindName));
                    ok = false;
                    continue;
                }
                ok &= ObjectReader.CheckNumber(p.Items[0], pp + "[0]", xr, d, out x[i]);
                ok &= ObjectReader.CheckNumber(p.Items[1], pp + "[1]", yr, d, out y[i]);
            }
            if (!ok) return false;
            for (int i = 1; i < x.Length; i++)
            {
                if (x[i] <= x[i - 1])
                {
                    d.Error(ErrorCode.CurveNotIncreasing, path + "[" + i + "][0]", arr.Items[i],
                        "x must be strictly increasing (" + ValueRange.Fmt(x[i]) + " follows " + ValueRange.Fmt(x[i - 1]) + ")");
                    return false;
                }
            }
            xs = x; ys = y;
            return true;
        }

        // ---------------------------------------------------------------- clutch

        private static ClutchDef ReadClutch(ObjectReader r, EngineDef engine, Diagnostics d)
        {
            var c = new ClutchDef();
            r.Number("capacity", ValueRange.Positive(100000), out c.Capacity);
            bool engOk = r.Number("engagementRpm", ValueRange.Positive(MaxRpm), out c.EngagementRpm);
            r.Number("engagementRange", ValueRange.Positive(10000), out c.EngagementRange);
            bool idleKnown = engine != null && engine.IdleRpm > 0f;
            if (engOk && idleKnown && c.EngagementRpm < engine.IdleRpm)
                d.Error(ErrorCode.EngagementBelowIdle, r.PathOf("engagementRpm"), r.Obj.Get("engagementRpm"),
                    "engagementRpm (" + ValueRange.Fmt(c.EngagementRpm) + ") must not be below engine.idleRpm (" + ValueRange.Fmt(engine.IdleRpm) + ")");

            float[] x, y;
            if (ReadPoints(r, "engagementCurve", ValueRange.Closed(0, 1), ValueRange.Closed(0, 1), d, out x, out y))
            {
                bool ok = true;
                if (x[0] != 0f || x[x.Length - 1] != 1f)
                {
                    d.Error(ErrorCode.EngagementCurveEndpoints, r.PathOf("engagementCurve"), r.Obj.Get("engagementCurve"),
                        "must start at input 0 and end at input 1 (spans " + ValueRange.Fmt(x[0]) + "-" + ValueRange.Fmt(x[x.Length - 1]) + ")");
                    ok = false;
                }
                for (int i = 1; i < y.Length && ok; i++)
                {
                    if (y[i] < y[i - 1])
                    {
                        d.Error(ErrorCode.EngagementCurveDecreasing, r.PathOf("engagementCurve") + "[" + i + "][1]", r.Obj.Get("engagementCurve").Items[i],
                            "engagement must not decrease as clutch input rises");
                        ok = false;
                    }
                }
                if (ok) c.EngagementCurve = new PiecewiseLinear(x, y);
            }

            ObjectReader launch = r.Object("launch", false);
            if (launch != null)
            {
                c.Launch = new LaunchDef();
                if (launch.Number("rpm", ValueRange.Positive(MaxRpm), out c.Launch.Rpm) && engOk && engine != null && engine.RedlineRpm > 0f
                    && (c.Launch.Rpm < c.EngagementRpm || c.Launch.Rpm > engine.RedlineRpm))
                    d.Error(ErrorCode.LaunchRpmOutOfRange, launch.PathOf("rpm"), launch.Obj.Get("rpm"),
                        "launch rpm must be between clutch.engagementRpm (" + ValueRange.Fmt(c.EngagementRpm) + ") and engine.redlineRpm ("
                        + ValueRange.Fmt(engine.RedlineRpm) + ")");
                launch.Finish();
            }
            r.Finish();
            return c;
        }

        // ---------------------------------------------------------------- gearbox

        private static GearboxDef ReadGearbox(ObjectReader r, EngineDef engine, Diagnostics d, out JsonValue output)
        {
            var g = new GearboxDef();
            g.Forward = ReadRatios(r, "forward", MaxForwardGears, true, d);
            g.Reverse = ReadRatios(r, "reverse", MaxReverseGears, false, d);
            r.Number("finalDrive", ValueRange.Positive(100), out g.FinalDrive);
            r.Number("shiftTime", ValueRange.Closed(0, 5), out g.ShiftTime);

            string logic;
            bool logicOk = false;
            if (r.String("shiftLogic", out logic))
            {
                g.ShiftLogicName = logic;
                if (!ShiftLogicRegistry.IsRegistered(logic))
                    d.Error(ErrorCode.ShiftLogicUnknown, r.PathOf("shiftLogic"), r.Obj.Get("shiftLogic"),
                        "unknown shift logic '" + logic + "' (registered: " + ShiftLogicRegistry.RegisteredNames() + ")");
                else logicOk = true;
            }

            bool hasUp = r.Has("upshiftRpm"), hasDown = r.Has("downshiftRpm");
            if (logicOk && logic == ShiftLogicRegistry.Manual)
            {
                r.Reject("upshiftRpm", ErrorCode.ParamNotApplicable, "does not apply to shiftLogic 'manual'");
                r.Reject("downshiftRpm", ErrorCode.ParamNotApplicable, "does not apply to shiftLogic 'manual'");
            }
            else if (logicOk && (logic == ShiftLogicRegistry.Automatic || hasUp || hasDown))
            {
                // automatic: both required. A custom logic: optional, but both or neither.
                bool upOk = r.Number("upshiftRpm", ValueRange.Positive(MaxRpm), out g.UpshiftRpm);
                bool downOk = r.Number("downshiftRpm", ValueRange.Positive(MaxRpm), out g.DownshiftRpm);
                if (upOk && downOk)
                {
                    g.HasShiftRpms = true;
                    if (engine != null && engine.IdleRpm > 0f && engine.RevLimitRpm > 0f
                        && !(engine.IdleRpm < g.DownshiftRpm && g.DownshiftRpm < g.UpshiftRpm && g.UpshiftRpm <= engine.RevLimitRpm))
                        d.Error(ErrorCode.ShiftRpmOrder, r.PathOf("upshiftRpm"), r.Obj.Get("upshiftRpm"),
                            "needs idleRpm < downshiftRpm < upshiftRpm <= revLimitRpm (" + ValueRange.Fmt(engine.IdleRpm) + " < "
                            + ValueRange.Fmt(g.DownshiftRpm) + " < " + ValueRange.Fmt(g.UpshiftRpm) + " <= " + ValueRange.Fmt(engine.RevLimitRpm) + ")");
                }
            }
            else
            {
                r.Raw("upshiftRpm", false); r.Raw("downshiftRpm", false);   // unknown logic: already reported
            }

            output = null;
            string outName;
            if (r.String("output", out outName)) output = r.Obj.Get("output");

            if (g.Forward != null)
            {
                for (int i = 1; i < g.Forward.Length; i++)
                {
                    if (g.Forward[i] >= g.Forward[i - 1])
                    {
                        d.Warn(WarningCode.ForwardRatiosNotDescending, r.PathOf("forward") + "[" + i + "]", r.Obj.Get("forward").Items[i],
                            "gear " + (i + 1) + " (" + ValueRange.Fmt(g.Forward[i]) + ") is not shorter-geared than gear " + i
                            + " (" + ValueRange.Fmt(g.Forward[i - 1]) + "); automatic shift logic assumes descending ratios");
                        break;
                    }
                }
                if (g.HasShiftRpms)
                {
                    // README fact 8: a fixed-RPM automatic hunts when an upshift lands below the downshift point.
                    for (int i = 0; i + 1 < g.Forward.Length; i++)
                    {
                        float landing = g.UpshiftRpm * g.Forward[i + 1] / g.Forward[i];
                        if (landing < g.DownshiftRpm)
                            d.Warn(WarningCode.ShiftWouldHunt, r.PathOf("forward") + "[" + (i + 1) + "]", r.Obj.Get("forward").Items[i + 1],
                                "upshift " + (i + 1) + "->" + (i + 2) + " at " + ValueRange.Fmt(g.UpshiftRpm) + " rpm lands at " + ValueRange.Fmt(landing)
                                + " rpm, below downshiftRpm " + ValueRange.Fmt(g.DownshiftRpm) + ": fixed-RPM shifting would hunt (M1's no-hunting logic handles this)");
                    }
                }
            }
            if (logicOk && g.Forward != null && g.Reverse != null) g.ShiftLogic = ShiftLogicRegistry.Create(logic, g);
            r.Finish();
            return g;
        }

        private static float[] ReadRatios(ObjectReader r, string key, int max, bool forward, Diagnostics d)
        {
            JsonValue arr = r.Array(key, true);
            if (arr == null) return null;
            string path = r.PathOf(key);
            if (forward && arr.Items.Count == 0)
            {
                d.Error(ErrorCode.NoForwardGears, path, arr, "needs at least one forward gear");
                return null;
            }
            if (arr.Items.Count > max)
            {
                d.Error(ErrorCode.OutOfRange, path, arr, "at most " + max + " gears (got " + arr.Items.Count + ")");
                return null;
            }
            var a = new float[arr.Items.Count];
            bool ok = true;
            for (int i = 0; i < a.Length; i++)
            {
                string pp = path + "[" + i + "]";
                if (!ObjectReader.CheckNumber(arr.Items[i], pp, ValueRange.Closed(-1000, 1000), d, out a[i])) { ok = false; continue; }
                if (forward && a[i] <= 0f)
                {
                    d.Error(ErrorCode.ForwardRatioNotPositive, pp, arr.Items[i], "forward ratios must be > 0 (got " + ValueRange.Fmt(a[i]) + ")");
                    ok = false;
                }
                else if (!forward && a[i] >= 0f)
                {
                    d.Error(ErrorCode.ReverseRatioNotNegative, pp, arr.Items[i],
                        "reverse ratios must be < 0, NWH convention (got " + ValueRange.Fmt(a[i]) + "; write " + ValueRange.Fmt(-Math.Abs(a[i])) + ")");
                    ok = false;
                }
            }
            return ok ? a : null;
        }

        // ---------------------------------------------------------------- layout

        private static bool CheckName(string name, string path, JsonValue at, Diagnostics d)
        {
            if (name.Length == 0 || name.Length > 32)
            {
                d.Error(ErrorCode.BadName, path, at, "names must be 1-32 characters");
                return false;
            }
            foreach (char ch in name)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                {
                    d.Error(ErrorCode.BadName, path, at, "'" + name + "': names may use letters, digits, '_' and '-' only");
                    return false;
                }
            }
            if (name == GearboxNode)
            {
                d.Error(ErrorCode.BadName, path, at, "'gearbox' is reserved");
                return false;
            }
            return true;
        }

        private static DifferentialDef[] ReadDifferentials(ObjectReader top, Diagnostics d, out List<JsonValue[]> outputs)
        {
            outputs = new List<JsonValue[]>();
            JsonValue arr = top.Array("differentials", true);
            if (arr == null) return null;
            if (arr.Items.Count > MaxDifferentials)
            {
                d.Error(ErrorCode.OutOfRange, "differentials", arr, "at most " + MaxDifferentials + " differentials");
                return null;
            }
            var list = new DifferentialDef[arr.Items.Count];
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < list.Length; i++)
            {
                var diff = new DifferentialDef();
                list[i] = diff;
                JsonValue[] outs = null;
                outputs.Add(null);
                ObjectReader r = ObjectReader.Wrap(arr.Items[i], "differentials[" + i + "]", d);
                if (r == null) continue;
                if (r.String("name", out diff.Name) && CheckName(diff.Name, r.PathOf("name"), r.Obj.Get("name"), d))
                {
                    int first;
                    if (seen.TryGetValue(diff.Name, out first))
                        d.Error(ErrorCode.DuplicateName, r.PathOf("name"), r.Obj.Get("name"), "differential '" + diff.Name + "' is already defined at differentials[" + first + "]");
                    else seen[diff.Name] = i;
                }
                string type;
                if (r.String("type", out type))
                {
                    switch (type)
                    {
                        case "open":
                            diff.Type = DiffType.Open;
                            NotFor(r, "open", "split", "stiffness", "slipTorque", "powerRamp", "coastRamp");
                            break;
                        case "locked":
                            diff.Type = DiffType.Locked;
                            r.Number("stiffness", ValueRange.Positive(1), out diff.Stiffness);
                            NotFor(r, "locked", "split", "slipTorque", "powerRamp", "coastRamp");
                            break;
                        case "limitedSlip":
                            diff.Type = DiffType.LimitedSlip;
                            r.Number("stiffness", ValueRange.Positive(1), out diff.Stiffness);
                            r.Number("slipTorque", ValueRange.Positive(100000), out diff.SlipTorque);
                            r.Number("powerRamp", ValueRange.Closed(0, 1), out diff.PowerRamp);
                            r.Number("coastRamp", ValueRange.Closed(0, 1), out diff.CoastRamp);
                            NotFor(r, "limitedSlip", "split");
                            break;
                        case "torqueSplit":
                            diff.Type = DiffType.TorqueSplit;
                            r.Number("split", ValueRange.Open(0, 1), out diff.Split);
                            NotFor(r, "torqueSplit", "stiffness", "slipTorque", "powerRamp", "coastRamp");
                            break;
                        default:
                            d.Error(ErrorCode.OutOfRange, r.PathOf("type"), r.Obj.Get("type"),
                                "unknown differential type '" + type + "' (open, locked, limitedSlip, torqueSplit)");
                            foreach (string k in new[] { "split", "stiffness", "slipTorque", "powerRamp", "coastRamp" }) r.Raw(k, false);
                            break;
                    }
                }
                JsonValue o = r.Array("outputs", true);
                if (o != null)
                {
                    if (o.Items.Count != 2)
                        d.Error(ErrorCode.DiffOutputCount, r.PathOf("outputs"), o, "a differential has exactly 2 outputs (got " + o.Items.Count + ")");
                    else
                    {
                        bool ok = true;
                        for (int k = 0; k < 2; k++)
                        {
                            if (o.Items[k].Kind != JsonKind.String)
                            {
                                d.Error(ErrorCode.WrongType, r.PathOf("outputs") + "[" + k + "]", o.Items[k], "expected a node name, got " + o.Items[k].KindName);
                                ok = false;
                            }
                        }
                        if (ok && o.Items[0].String == o.Items[1].String)
                        {
                            d.Error(ErrorCode.DiffOutputDuplicate, r.PathOf("outputs"), o, "both outputs are '" + o.Items[0].String + "'");
                            ok = false;
                        }
                        if (ok) outs = new[] { o.Items[0], o.Items[1] };
                    }
                }
                outputs[i] = outs;
                r.Finish();
            }
            return list;
        }

        private static void NotFor(ObjectReader r, string type, params string[] keys)
        {
            foreach (string k in keys)
                r.Reject(k, ErrorCode.ParamNotApplicable, "'" + k + "' is not a parameter of a " + type + " differential");
        }

        private static AxleDef[] ReadAxles(ObjectReader top, Diagnostics d)
        {
            JsonValue arr = top.Array("axles", true);
            if (arr == null) return null;
            if (arr.Items.Count == 0)
            {
                d.Error(ErrorCode.NoAxles, "axles", arr, "needs at least one axle");
                return null;
            }
            if (arr.Items.Count > MaxAxles)
            {
                d.Error(ErrorCode.OutOfRange, "axles", arr, "at most " + MaxAxles + " axles");
                return null;
            }
            var list = new AxleDef[arr.Items.Count];
            var names = new Dictionary<string, int>();
            var zValid = new bool[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var a = new AxleDef { Index = i };
                list[i] = a;
                ObjectReader r = ObjectReader.Wrap(arr.Items[i], "axles[" + i + "]", d);
                if (r == null) continue;
                if (r.String("name", out a.Name) && CheckName(a.Name, r.PathOf("name"), r.Obj.Get("name"), d))
                {
                    int first;
                    if (names.TryGetValue(a.Name, out first))
                        d.Error(ErrorCode.DuplicateName, r.PathOf("name"), r.Obj.Get("name"), "axle '" + a.Name + "' is already defined at axles[" + first + "]");
                    else names[a.Name] = i;
                }
                bool zOk = r.Number("z", ValueRange.Closed(-50, 50), out a.Z);
                r.Number("track", ValueRange.Positive(10), out a.Track);
                r.Bool("steered", false, out a.Steered);
                r.Bool("lift", false, out a.Lift);
                zValid[i] = zOk;
                if (zOk)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (zValid[j] && list[j].Z == a.Z)
                        {
                            d.Error(ErrorCode.DuplicateAxlePosition, r.PathOf("z"), r.Obj.Get("z"),
                                "axle at z = " + ValueRange.Fmt(a.Z) + " coincides with axles[" + j + "]");
                            break;
                        }
                    }
                }
                a.Left = new WheelDef { Name = a.Name + ".L", Axle = a, Side = WheelSide.Left, Position = new Vec3(-a.Track * 0.5f, 0f, a.Z) };
                a.Right = new WheelDef { Name = a.Name + ".R", Axle = a, Side = WheelSide.Right, Position = new Vec3(a.Track * 0.5f, 0f, a.Z) };
                r.Finish();
            }
            return list;
        }

        // ---------------------------------------------------------------- wiring

        private static void Wire(Drivetrain dt, JsonValue gearboxOutput, List<JsonValue[]> diffOutputs, Diagnostics d)
        {
            var nodes = new List<PowertrainNode>();
            var byName = new Dictionary<string, int>();
            nodes.Add(new PowertrainNode { Kind = NodeKind.Gearbox, Name = GearboxNode, Outputs = new int[1] });
            byName[GearboxNode] = 0;
            foreach (DifferentialDef diff in dt.Differentials)
            {
                diff.Node = nodes.Count;
                byName[diff.Name] = nodes.Count;
                nodes.Add(new PowertrainNode { Kind = NodeKind.Differential, Name = diff.Name, Outputs = new int[2] });
            }
            var wheels = new List<WheelDef>();
            foreach (AxleDef a in dt.Axles)
            {
                foreach (WheelDef w in new[] { a.Left, a.Right })
                {
                    w.Node = nodes.Count;
                    byName[w.Name] = nodes.Count;
                    nodes.Add(new PowertrainNode { Kind = NodeKind.Wheel, Name = w.Name, Outputs = new int[0] });
                    wheels.Add(w);
                }
            }
            dt.Wheels = wheels.ToArray();
            dt.Nodes = nodes.ToArray();

            // Resolve every edge. Edge source positions are kept for error reporting.
            var edgeAt = new Dictionary<long, JsonValue>();
            var edgePath = new Dictionary<long, string>();
            bool resolved = Resolve(dt.Nodes, 0, 0, gearboxOutput, "gearbox.output", byName, edgeAt, edgePath, d);
            for (int i = 0; i < dt.Differentials.Length; i++)
            {
                JsonValue[] outs = diffOutputs[i];
                for (int k = 0; k < 2; k++)
                    resolved &= Resolve(dt.Nodes, dt.Differentials[i].Node, k, outs[k], "differentials[" + i + "].outputs[" + k + "]", byName, edgeAt, edgePath, d);
            }
            if (!resolved) return;

            // Cycles first (README fact 7: NWH recurses _output/_outputB until the game crashes).
            // Checked over ALL nodes, not just the ones reachable from the gearbox.
            var color = new int[dt.Nodes.Length];   // 0 white, 1 on stack, 2 done
            var stack = new List<int>();
            bool cyclic = false;
            for (int i = 0; i < dt.Nodes.Length && !cyclic; i++)
                if (color[i] == 0) cyclic = FindCycle(dt.Nodes, i, color, stack, edgeAt, edgePath, d);
            if (cyclic) return;

            // Each node fed at most once (NWH components have exactly one _input).
            for (int i = 0; i < dt.Nodes.Length; i++)
            {
                for (int k = 0; k < dt.Nodes[i].Outputs.Length; k++)
                {
                    int t = dt.Nodes[i].Outputs[k];
                    if (dt.Nodes[t].Input >= 0)
                    {
                        long key = Key(i, k);
                        d.Error(ErrorCode.InputFedTwice, edgePath[key], edgeAt[key],
                            "'" + dt.Nodes[t].Name + "' is already fed by '" + dt.Nodes[dt.Nodes[t].Input].Name + "'; every node takes exactly one input");
                    }
                    else dt.Nodes[t].Input = i;
                }
            }
            if (d.Has(ErrorCode.InputFedTwice)) return;

            // Reachability from the gearbox decides what is driven.
            var queue = new Queue<int>();
            queue.Enqueue(0);
            dt.Nodes[0].Reachable = true;
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                foreach (int t in dt.Nodes[n].Outputs)
                {
                    if (!dt.Nodes[t].Reachable) { dt.Nodes[t].Reachable = true; queue.Enqueue(t); }
                }
            }
            foreach (WheelDef w in dt.Wheels) w.Driven = dt.Nodes[w.Node].Reachable;
            for (int i = 0; i < dt.Differentials.Length; i++)
            {
                DifferentialDef diff = dt.Differentials[i];
                if (!dt.Nodes[diff.Node].Reachable)
                {
                    PowertrainNode n = dt.Nodes[diff.Node];
                    d.Warn(WarningCode.DisconnectedNode, "differentials[" + i + "]", null,
                        "'" + diff.Name + "' is not reachable from the gearbox: " + dt.Nodes[n.Outputs[0]].Name + " and "
                        + dt.Nodes[n.Outputs[1]].Name + " receive no torque from it");
                }
            }
        }

        private static long Key(int node, int output) { return ((long)node << 8) | (uint)output; }

        private static bool Resolve(PowertrainNode[] nodes, int from, int k, JsonValue nameAt, string path,
            Dictionary<string, int> byName, Dictionary<long, JsonValue> edgeAt, Dictionary<long, string> edgePath, Diagnostics d)
        {
            int target;
            if (!byName.TryGetValue(nameAt.String, out target))
            {
                var known = new List<string>(byName.Keys);
                known.Remove(GearboxNode);
                d.Error(ErrorCode.UnknownNode, path, nameAt, "no differential or wheel named '" + nameAt.String + "' (known: " + string.Join(", ", known.ToArray()) + ")");
                return false;
            }
            nodes[from].Outputs[k] = target;
            edgeAt[Key(from, k)] = nameAt;
            edgePath[Key(from, k)] = path;
            return true;
        }

        private static bool FindCycle(PowertrainNode[] nodes, int n, int[] color, List<int> stack,
            Dictionary<long, JsonValue> edgeAt, Dictionary<long, string> edgePath, Diagnostics d)
        {
            color[n] = 1;
            stack.Add(n);
            for (int k = 0; k < nodes[n].Outputs.Length; k++)
            {
                int t = nodes[n].Outputs[k];
                if (color[t] == 1)
                {
                    var names = new List<string>();
                    for (int i = stack.IndexOf(t); i < stack.Count; i++) names.Add(nodes[stack[i]].Name);
                    names.Add(nodes[t].Name);
                    long key = Key(n, k);
                    d.Error(ErrorCode.WiringCycle, edgePath[key], edgeAt[key],
                        "wiring cycle " + string.Join(" -> ", names.ToArray()) + " (NWH would recurse until the game crashes)");
                    return true;
                }
                if (color[t] == 0 && FindCycle(nodes, t, color, stack, edgeAt, edgePath, d)) return true;
            }
            stack.RemoveAt(stack.Count - 1);
            color[n] = 2;
            return false;
        }
    }
}
