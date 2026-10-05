// ApocalypterDrivetrain 0.1.0 — headless tests (run by verify/run.sh from the verify/ folder).
// Groups: identity + config surface, JSON reader, model happy paths, negative controls (one
// per rejection path, with an ErrorCode coverage check), warnings, wiring/cycles, runtime
// API (curves, shift hook, allocation-free), controller A/B (OFF = no touch), ModHost,
// patch-point evidence (stubs + gamecode + census), stub drift, README/example sync.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ApocalypterDrivetrain;
using ApocalypterDrivetrain.Model;
using ApocalypterDrivetrain.Patching;
using ApocalypterDrivetrain.Runtime;
using ApocalypterDrivetrain.Settings;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

public static class Tests
{
    private static int _fail, _pass;
    private static readonly HashSet<ErrorCode> ErrorsCovered = new HashSet<ErrorCode>();
    private static readonly HashSet<WarningCode> WarningsCovered = new HashSet<WarningCode>();

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + what); }
        else { _fail++; Console.WriteLine("  FAIL " + what); }
    }

    private static void Section(string name) { Console.WriteLine(); Console.WriteLine("== " + name); }

    /// <summary>Single quotes -> double quotes, so test JSON stays readable.</summary>
    private static string Q(string s) { return s.Replace('\'', '"'); }

    /// <summary>The built-in example with exactly one textual replacement (throws if the anchor is not unique).</summary>
    private static string Mutate(string find, string replace)
    {
        string src = BuiltInConfig.Json;
        int first = src.IndexOf(find, StringComparison.Ordinal);
        if (first < 0 || src.IndexOf(find, first + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("mutation anchor not unique: " + find);
        return src.Substring(0, first) + replace + src.Substring(first + find.Length);
    }

    private static string Dump(BuildResult r)
    {
        return string.Join(" | ", r.Diagnostics.Items.Select(d => d.ToString()).ToArray());
    }

    public static int Main()
    {
        try
        {
            Identity();
            ConfigSurface();
            JsonReader();
            BuiltInExample();
            HappyPaths();
            NegativeControls();
            Warnings();
            Wiring();
            ErrorPositions();
            RuntimeApi();
            Controller();
            Host();
            PatchPointEvidence();
            StubDrift();
            ReadmeSync();
            ModelIsolation();
        }
        catch (Exception e)
        {
            _fail++;
            Console.WriteLine("  FAIL unexpected exception: " + e);
        }
        Console.WriteLine();
        Console.WriteLine("tests: " + _pass + " passed, " + _fail + " failed");
        return _fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ identity

    private static void Identity()
    {
        Section("identity");
        var attr = typeof(Plugin).GetCustomAttributes(typeof(BepInPlugin), false).Cast<BepInPlugin>().Single();
        Check(attr.GUID == "dev.apocalypter.drivetrain", "GUID is locked to dev.apocalypter.drivetrain");
        Check(attr.Name == "ApocalypterDrivetrain", "plugin name");
        Check(attr.Version.ToString() == "0.1.0", "attribute version is numeric 0.1.0 (got " + attr.Version + ")");
        Check(ModInfo.Version.All(c => char.IsDigit(c) || c == '.'), "ModInfo.Version has no -alpha tag");
        Check(typeof(Plugin).IsSealed && typeof(Plugin).BaseType == typeof(BaseUnityPlugin), "Plugin is a BaseUnityPlugin");
    }

    // ------------------------------------------------------------------ config surface

    private static ConfigFile NewConfig()
    {
        string path = Path.Combine(Path.GetTempPath(), "adt-" + Guid.NewGuid().ToString("N") + ".cfg");
        return new ConfigFile(path, true);
    }

    private static void ConfigSurface()
    {
        Section("config surface (continuity snapshot)");
        ConfigFile cfg = NewConfig();
        var s = new DrivetrainSettings(cfg);
        var keys = cfg.Keys.Select(k => k.Section + "." + k.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        // Renaming or removing a key is a config-continuity break: it needs a migration, then update this list.
        Check(string.Join(",", keys) == "Drivetrain.ConfigPath,Drivetrain.Enabled,Drivetrain.ToggleKey",
            "config keys are exactly [Drivetrain] Enabled/ToggleKey/ConfigPath (got " + string.Join(",", keys) + ")");
        Check(s.Enabled.Value == false, "Enabled defaults to false");
        Check(s.ToggleKey.Value == "F9", "ToggleKey defaults to F9");
        Check(s.ConfigPath.Value == "", "ConfigPath defaults to empty (built-in example)");

        KeyCode k;
        Check(DrivetrainSettings.TryParseKey("F9", out k) && k == KeyCode.F9, "TryParseKey F9");
        Check(DrivetrainSettings.TryParseKey(" home ", out k) && k == KeyCode.Home, "TryParseKey is case-insensitive and trims");
        Check(!DrivetrainSettings.TryParseKey("F99", out k), "TryParseKey rejects an unknown name");
        Check(!DrivetrainSettings.TryParseKey("282", out k), "TryParseKey rejects a numeric string");
        Check(!DrivetrainSettings.TryParseKey("None", out k), "TryParseKey rejects None");
        Check(!DrivetrainSettings.TryParseKey("", out k), "TryParseKey rejects empty");
    }

    // ------------------------------------------------------------------ JSON reader

    private static JsonParseException ParseFail(string text)
    {
        try { Json.Parse(text); return null; }
        catch (JsonParseException e) { return e; }
    }

    private static void JsonReader()
    {
        Section("JSON reader");
        JsonValue v = Json.Parse(Q("{\n  'a': [1, 2.5e1, -0.5],\n  'b': {'c': true, 'd': null},\n  's': 'x\\u0041\\n'\n}"));
        Check(v.Kind == JsonKind.Object && v.Members.Count == 3, "parses an object");
        Check(v.Get("a").Items[1].Number == 25.0, "exponent numbers");
        Check(v.Get("b").Get("c").Bool && v.Get("b").Get("d").Kind == JsonKind.Null, "booleans and null");
        Check(v.Get("s").String == "xA\n", "string escapes");
        Check(v.Get("b").Line == 3 && v.Get("b").Column == 8, "value positions are line/column (got " + v.Get("b").Line + ":" + v.Get("b").Column + ")");

        var cases = new[]
        {
            new { T = "{'a': 1,}", Line = 1, Col = 9, What = "trailing comma in object" },
            new { T = "[1, 2,]", Line = 1, Col = 7, What = "trailing comma in array" },
            new { T = "{'a': 1}\n// c", Line = 2, Col = 1, What = "trailing comment" },
            new { T = "{\n'a': 'x\n'}", Line = 2, Col = 8, What = "newline inside a string" },
            new { T = "{'a': 01}", Line = 1, Col = 7, What = "leading zero" },
            new { T = "{'a': NaN}", Line = 1, Col = 7, What = "NaN literal" },
            new { T = "{'a': 1 'b': 2}", Line = 1, Col = 9, What = "missing comma" },
            new { T = "{'a': [1, 2}", Line = 1, Col = 12, What = "mismatched bracket" },
            new { T = "", Line = 1, Col = 1, What = "empty text" },
        };
        foreach (var c in cases)
        {
            JsonParseException e = ParseFail(Q(c.T));
            Check(e != null && e.Line == c.Line && e.Column == c.Col && !e.DuplicateKey,
                "rejects " + c.What + " at " + c.Line + ":" + c.Col + (e != null ? " (got " + e.Line + ":" + e.Column + " " + e.Reason + ")" : " (accepted!)"));
        }
        JsonParseException dup = ParseFail(Q("{\n 'a': 1,\n 'a': 2\n}"));
        Check(dup != null && dup.DuplicateKey && dup.Line == 3 && dup.Reason.Contains("first on line 2"), "duplicate key rejected with both lines");
        string deep = new string('[', 70) + new string(']', 70);
        Check(ParseFail(deep) != null, "nesting depth is bounded");
    }

    // ------------------------------------------------------------------ built-in example

    private static void BuiltInExample()
    {
        Section("built-in example");
        BuildResult r = DrivetrainBuilder.Build(BuiltInConfig.Json);
        Check(r.Ok && r.Diagnostics.Items.Count == 0, "builds with zero errors and zero warnings: " + Dump(r));
        if (!r.Ok) return;
        Drivetrain dt = r.Drivetrain;
        Check(dt.Name == "Example 4x4 pickup" && dt.Schema == 1, "name and schema");
        Check(dt.Engine.IdleRpm == 800 && dt.Engine.RedlineRpm == 5600 && dt.Engine.RevLimitRpm == 6000, "engine rpms");
        Check(Math.Abs(dt.Engine.TorqueCurve.Evaluate(2750f) - 305f) < 1e-3, "torque curve interpolates (2750 rpm -> 305 N·m)");
        Check(dt.Clutch.Launch != null && dt.Clutch.Launch.Rpm == 2500, "launch profile");
        Check(dt.Gearbox.ShiftLogic is AutomaticShiftLogicStub, "automatic resolves to the M1 stub");
        float[] nwh = dt.Gearbox.ToNwhGearArray();
        Check(nwh.SequenceEqual(new[] { -3.8f, 0f, 4.2f, 2.5f, 1.6f, 1.15f, 0.85f }), "NWH gear array layout [R..., 0, F...]");
        Check(dt.Differentials[0].Type == DiffType.TorqueSplit && dt.Differentials[0].Split == 0.4f, "centre torque split 0.4");
        Check(dt.Differentials[2].Type == DiffType.LimitedSlip && dt.Differentials[2].SlipTorque == 900f, "rear limited slip");
        Check(dt.DrivenWheelCount == 4 && dt.Wheels.Length == 4, "4 of 4 wheels driven");
        WheelDef fl = dt.Wheels.First(w => w.Name == "front.L");
        Check(fl.Position.X == -0.81f && fl.Position.Z == 1.45f && fl.Axle.Steered, "front.L placement (-track/2, 0, z), steered axle");
        Check(dt.Nodes[0].Kind == NodeKind.Gearbox && dt.Nodes[dt.Nodes[0].Outputs[0]].Name == "centre", "gearbox feeds centre");
        Check(dt.Nodes.All(n => n.Kind == NodeKind.Gearbox ? n.Input == -1 : n.Input >= 0), "every non-gearbox node has exactly one input");
    }

    // ------------------------------------------------------------------ happy paths

    private const string Engine = "'engine': {'idleRpm': 700, 'redlineRpm': 2400, 'revLimitRpm': 2600, 'revLimiterCutTime': 0.2, 'inertia': 1.8, "
        + "'torqueCurve': {'points': [[700, 900], [1400, 1700], [2600, 1200]]}}";
    private const string Clutch = "'clutch': {'capacity': 3000, 'engagementRpm': 800, 'engagementRange': 300, 'engagementCurve': [[0, 0], [1, 1]]}";

    private static string Truck(string gearbox, string diffs, string axles, string engine = Engine)
    {
        return Q("{'schema': 1, 'name': 'test', " + engine + ", " + Clutch + ", 'gearbox': {" + gearbox + "}, 'differentials': [" + diffs + "], 'axles': [" + axles + "]}");
    }

    private const string ManualBox = "'forward': [6, 4, 2.5, 1.6, 1], 'reverse': [-6, -9], 'finalDrive': 5, 'shiftLogic': 'manual', 'shiftTime': 0.4, 'output': ";

    private static void HappyPaths()
    {
        Section("model happy paths");
        string sixBySix = Truck(ManualBox + "'transfer'",
            "{'name': 'transfer', 'type': 'locked', 'stiffness': 0.8, 'outputs': ['front', 'bogie']},"
            + "{'name': 'front', 'type': 'open', 'outputs': ['front.L', 'front.R']},"
            + "{'name': 'bogie', 'type': 'locked', 'stiffness': 0.5, 'outputs': ['mid', 'rear']},"
            + "{'name': 'mid', 'type': 'open', 'outputs': ['mid.L', 'mid.R']},"
            + "{'name': 'rear', 'type': 'open', 'outputs': ['rear.L', 'rear.R']}",
            "{'name': 'front', 'z': 2.5, 'track': 2.0, 'steered': true}, {'name': 'mid', 'z': -0.6, 'track': 2.0}, {'name': 'rear', 'z': -1.95, 'track': 2.0}");
        BuildResult r = DrivetrainBuilder.Build(sixBySix);
        Check(r.Ok && r.Diagnostics.Items.Count == 0, "6x6 (transfer -> front + bogie -> mid + rear) builds clean: " + Dump(r));
        if (r.Ok)
        {
            Check(r.Drivetrain.DrivenWheelCount == 6 && r.Drivetrain.Axles.Length == 3, "6x6: 6/6 driven, 3 axles");
            Check(r.Drivetrain.Gearbox.ShiftLogic is ManualShiftLogic && !r.Drivetrain.Gearbox.HasShiftRpms, "manual logic, no shift rpms");
            Check(r.Drivetrain.Gearbox.ToNwhGearArray().SequenceEqual(new[] { -9f, -6f, 0f, 6f, 4f, 2.5f, 1.6f, 1f }), "two reverse gears: NWH order is [R2, R1, 0, ...]");
            Check(r.Drivetrain.Gearbox.RatioOf(-2) == -9f && r.Drivetrain.Gearbox.RatioOf(-1) == -6f && r.Drivetrain.Gearbox.RatioOf(0) == 0f, "RatioOf reverse/neutral");
        }

        string tag = Truck(ManualBox + "'rear'",
            "{'name': 'rear', 'type': 'limitedSlip', 'stiffness': 0.4, 'slipTorque': 2000, 'powerRamp': 1, 'coastRamp': 0, 'outputs': ['rear.L', 'rear.R']}",
            "{'name': 'front', 'z': 3.0, 'track': 2.0, 'steered': true}, {'name': 'rear', 'z': -1.0, 'track': 1.9}, {'name': 'tag', 'z': -2.4, 'track': 1.9, 'lift': true, 'steered': true}");
        r = DrivetrainBuilder.Build(tag);
        Check(r.Ok && r.Diagnostics.Items.Count == 0, "RWD + steered lift tag axle builds clean: " + Dump(r));
        if (r.Ok)
        {
            AxleDef t = r.Drivetrain.Axles[2];
            Check(t.Lift && t.Steered && !t.Driven, "tag axle: lift + steered, undriven");
            Check(r.Drivetrain.DrivenWheelCount == 2, "RWD: 2/6 driven");
        }

        r = DrivetrainBuilder.Build(Truck(ManualBox + "'front.L'", "", "{'name': 'front', 'z': 1, 'track': 1.5}, {'name': 'rear', 'z': -1, 'track': 1.5}"));
        Check(r.Ok && r.Drivetrain.DrivenWheelCount == 1 && r.Drivetrain.Differentials.Length == 0, "gearbox may drive one wheel directly (no differentials): " + Dump(r));

        string expr = "'engine': {'idleRpm': 800, 'redlineRpm': 5000, 'revLimitRpm': 5500, 'revLimiterCutTime': 0.1, 'inertia': 0.3, "
            + "'torqueCurve': {'expression': 'clamp(100 + 0.08*rpm - 0.00001*rpm^2, 0, 400)', 'samples': 48}}";
        r = DrivetrainBuilder.Build(Truck(ManualBox + "'front.L'", "", "{'name': 'front', 'z': 1, 'track': 1.5}", expr));
        Check(r.Ok, "expression torque curve builds: " + Dump(r));
        if (r.Ok)
        {
            PiecewiseLinear c = r.Drivetrain.Engine.TorqueCurve;
            Check(c.Count == 48 && c.MinX == 800f && c.MaxX == 5500f, "sampled over [idleRpm, revLimitRpm] with 48 points");
            Check(Math.Abs(c.Evaluate(4000f) - 260f) < 1.0f, "sampled curve matches the expression (4000 rpm ~ 260, got " + c.Evaluate(4000f) + ")");
            Check(r.Drivetrain.Engine.TorqueExpression != null, "expression source kept for the log");
        }

        Check(ShiftLogicRegistry.Register("test-hold", g => new ManualShiftLogic()), "a mod can register a named shift logic");
        Check(!ShiftLogicRegistry.Register("manual", g => null), "built-in names cannot be replaced");
        Check(!ShiftLogicRegistry.Register("test-hold", g => null), "registered names cannot be replaced");
        string custom = "'forward': [3, 1], 'reverse': [], 'finalDrive': 4, 'shiftLogic': 'test-hold', 'shiftTime': 0, 'output': 'front.L'";
        r = DrivetrainBuilder.Build(Truck(custom, "", "{'name': 'front', 'z': 1, 'track': 1.5}"));
        Check(r.Ok && r.Drivetrain.Gearbox.ShiftLogicName == "test-hold" && r.Drivetrain.Gearbox.Reverse.Length == 0, "config uses a registered custom shift logic (no reverse gear is allowed)");
        r = DrivetrainBuilder.Build(Truck(custom.Replace("'shiftTime'", "'upshiftRpm': 2000, 'downshiftRpm': 1200, 'shiftTime'"), "", "{'name': 'front', 'z': 1, 'track': 1.5}"));
        Check(r.Ok && r.Drivetrain.Gearbox.HasShiftRpms, "custom logic may carry shift rpms");
    }

    // ------------------------------------------------------------------ negative controls

    private struct Neg
    {
        public string Name, Json, Path;
        public ErrorCode Code;
    }

    private static Neg N(string name, ErrorCode code, string path, string json) { return new Neg { Name = name, Code = code, Path = path, Json = json }; }

    private static void NegativeControls()
    {
        Section("negative controls (every rejection path)");
        const string Box = "'forward': [3, 1], 'reverse': [-3], 'finalDrive': 4, 'shiftLogic': 'manual', 'shiftTime': 0.2, 'output': ";
        const string Two = "{'name': 'front', 'z': 1, 'track': 1.5}, {'name': 'rear', 'z': -1, 'track': 1.5}";
        var cases = new List<Neg>
        {
            // text / structure
            N("trailing comma", ErrorCode.JsonSyntax, "", Mutate("\"inertia\": 0.25,", "\"inertia\": 0.25,,")),
            N("duplicate key", ErrorCode.DuplicateKey, "", Mutate("\"inertia\": 0.25,", "\"inertia\": 0.25, \"inertia\": 0.3,")),
            N("not an object at top level", ErrorCode.WrongType, "", "[1, 2]"),
            N("missing engine.inertia", ErrorCode.MissingField, "engine.inertia", Mutate("\"inertia\": 0.25,", "")),
            N("missing engine section", ErrorCode.MissingField, "engine", Q("{'schema': 1, 'name': 'x', " + Clutch + ", 'gearbox': {" + Box + "'front.L'}, 'differentials': [], 'axles': [" + Two + "]}")),
            N("misspelt key", ErrorCode.UnknownField, "engine.intertia", Mutate("\"inertia\": 0.25,", "\"inertia\": 0.25, \"intertia\": 1,")),
            N("unknown top-level key", ErrorCode.UnknownField, "notes", Mutate("\"schema\": 1,", "\"schema\": 1, \"notes\": \"x\",")),
            N("unknown axle key", ErrorCode.UnknownField, "axles[1].height", Mutate("\"z\": -1.55,", "\"z\": -1.55, \"height\": 0.4,")),
            N("string where number", ErrorCode.WrongType, "engine.idleRpm", Mutate("\"idleRpm\": 800", "\"idleRpm\": \"800\"")),
            N("number where bool", ErrorCode.WrongType, "axles[0].steered", Mutate("\"steered\": true", "\"steered\": 1")),
            N("point is not a pair", ErrorCode.WrongType, "engine.torqueCurve.points[1]", Mutate("[2000, 290]", "[2000, 290, 1]")),
            N("diff output not a string", ErrorCode.WrongType, "differentials[1].outputs[0]", Mutate("\"front.L\", \"front.R\"", "1, \"front.R\"")),
            N("non-integer samples", ErrorCode.WrongType, "engine.torqueCurve.samples", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"200\", \"samples\": 2.5")),
            N("number beyond float", ErrorCode.NotFinite, "engine.inertia", Mutate("\"inertia\": 0.25", "\"inertia\": 1e300")),
            N("schema 2", ErrorCode.UnsupportedSchema, "schema", Mutate("\"schema\": 1", "\"schema\": 2")),
            N("empty name", ErrorCode.BadName, "name", Mutate("\"Example 4x4 pickup\"", "\" \"")),
            N("diff name with a dot", ErrorCode.BadName, "differentials[0].name", Mutate("\"name\": \"centre\"", "\"name\": \"cen.tre\"")),
            N("axle named gearbox", ErrorCode.BadName, "axles[1].name", Mutate("\"name\": \"rear\", \"z\"", "\"name\": \"gearbox\", \"z\"")),
            // value ranges (OutOfRange, one per field)
            N("schema negative", ErrorCode.OutOfRange, "schema", Mutate("\"schema\": 1", "\"schema\": -1")),
            N("idleRpm 0", ErrorCode.OutOfRange, "engine.idleRpm", Mutate("\"idleRpm\": 800", "\"idleRpm\": 0")),
            N("redlineRpm huge", ErrorCode.OutOfRange, "engine.redlineRpm", Mutate("\"redlineRpm\": 5600", "\"redlineRpm\": 50000")),
            N("revLimitRpm negative", ErrorCode.OutOfRange, "engine.revLimitRpm", Mutate("\"revLimitRpm\": 6000", "\"revLimitRpm\": -6000")),
            N("revLimiterCutTime 0", ErrorCode.OutOfRange, "engine.revLimiterCutTime", Mutate("\"revLimiterCutTime\": 0.12", "\"revLimiterCutTime\": 0")),
            N("inertia 0", ErrorCode.OutOfRange, "engine.inertia", Mutate("\"inertia\": 0.25", "\"inertia\": 0")),
            N("curve rpm 0", ErrorCode.OutOfRange, "engine.torqueCurve.points[0][0]", Mutate("[800, 210]", "[0, 210]")),
            N("samples 1", ErrorCode.OutOfRange, "engine.torqueCurve.samples", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"200\", \"samples\": 1")),
            N("capacity 0", ErrorCode.OutOfRange, "clutch.capacity", Mutate("\"capacity\": 650", "\"capacity\": 0")),
            N("engagementRpm 0", ErrorCode.OutOfRange, "clutch.engagementRpm", Mutate("\"engagementRpm\": 1100", "\"engagementRpm\": 0")),
            N("engagementRange negative", ErrorCode.OutOfRange, "clutch.engagementRange", Mutate("\"engagementRange\": 400", "\"engagementRange\": -1")),
            N("engagement y 1.5", ErrorCode.OutOfRange, "clutch.engagementCurve[1][1]", Mutate("[0.4, 0.15]", "[0.4, 1.5]")),
            N("launch rpm 0", ErrorCode.OutOfRange, "clutch.launch.rpm", Mutate("\"rpm\": 2500", "\"rpm\": 0")),
            N("finalDrive 0", ErrorCode.OutOfRange, "gearbox.finalDrive", Mutate("\"finalDrive\": 3.9", "\"finalDrive\": 0")),
            N("shiftTime 6", ErrorCode.OutOfRange, "gearbox.shiftTime", Mutate("\"shiftTime\": 0.25", "\"shiftTime\": 6")),
            N("upshiftRpm 0", ErrorCode.OutOfRange, "gearbox.upshiftRpm", Mutate("\"upshiftRpm\": 4800", "\"upshiftRpm\": 0")),
            N("ratio 5000", ErrorCode.OutOfRange, "gearbox.forward[0]", Mutate("[4.2, 2.5", "[5000, 2.5")),
            N("33 forward gears", ErrorCode.OutOfRange, "gearbox.forward", Mutate("[4.2, 2.5, 1.6, 1.15, 0.85]", "[" + string.Join(", ", Enumerable.Range(1, 33).Select(i => (40.0 / i).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).ToArray()) + "]")),
            N("9 reverse gears", ErrorCode.OutOfRange, "gearbox.reverse", Mutate("[-3.8]", "[-1, -2, -3, -4, -5, -6, -7, -8, -9]")),
            N("split 1", ErrorCode.OutOfRange, "differentials[0].split", Mutate("\"split\": 0.4", "\"split\": 1")),
            N("stiffness 0", ErrorCode.OutOfRange, "differentials[2].stiffness", Mutate("\"stiffness\": 0.6", "\"stiffness\": 0")),
            N("slipTorque 0", ErrorCode.OutOfRange, "differentials[2].slipTorque", Mutate("\"slipTorque\": 900", "\"slipTorque\": 0")),
            N("powerRamp 1.5", ErrorCode.OutOfRange, "differentials[2].powerRamp", Mutate("\"powerRamp\": 0.8", "\"powerRamp\": 1.5")),
            N("coastRamp -0.1", ErrorCode.OutOfRange, "differentials[2].coastRamp", Mutate("\"coastRamp\": 0.4", "\"coastRamp\": -0.1")),
            N("unknown diff type", ErrorCode.OutOfRange, "differentials[1].type", Mutate("\"type\": \"open\"", "\"type\": \"viscous\"")),
            N("axle z 60", ErrorCode.OutOfRange, "axles[0].z", Mutate("\"z\": 1.45", "\"z\": 60")),
            N("track 0", ErrorCode.OutOfRange, "axles[0].track", Mutate("\"track\": 1.62", "\"track\": 0")),
            N("17 axles", ErrorCode.OutOfRange, "axles", Truck(Box + "'a0.L'", "", string.Join(", ", Enumerable.Range(0, 17).Select(i => "{'name': 'a" + i + "', 'z': " + i + ", 'track': 1}").ToArray()))),
            // params that do not apply
            N("stiffness on open diff", ErrorCode.ParamNotApplicable, "differentials[1].stiffness", Mutate("\"type\": \"open\",", "\"type\": \"open\", \"stiffness\": 0.5,")),
            N("split on locked diff", ErrorCode.ParamNotApplicable, "differentials[0].split", Mutate("\"type\": \"torqueSplit\"", "\"type\": \"locked\", \"stiffness\": 0.5")),
            N("upshiftRpm on manual", ErrorCode.ParamNotApplicable, "gearbox.upshiftRpm", Mutate("\"shiftLogic\": \"automatic\"", "\"shiftLogic\": \"manual\"")),
            N("samples on a points curve", ErrorCode.ParamNotApplicable, "engine.torqueCurve.samples", Mutate("\"points\": [", "\"samples\": 8, \"points\": [")),
            // engine
            N("idle above redline", ErrorCode.IdleNotBelowRedline, "engine.idleRpm", Mutate("\"idleRpm\": 800", "\"idleRpm\": 5700")),
            N("redline above limiter", ErrorCode.RedlineAboveRevLimit, "engine.redlineRpm", Mutate("\"redlineRpm\": 5600", "\"redlineRpm\": 6100")),
            N("curve: neither source", ErrorCode.CurveSourceAmbiguous, "engine.torqueCurve", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "")),
            N("curve: both sources", ErrorCode.CurveSourceAmbiguous, "engine.torqueCurve", Mutate("\"points\": [", "\"expression\": \"200\", \"points\": [")),
            N("one torque point", ErrorCode.CurveTooFewPoints, "engine.torqueCurve.points", Mutate("[[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "[[800, 210]]")),
            N("rpm not increasing", ErrorCode.CurveNotIncreasing, "engine.torqueCurve.points[2][0]", Mutate("[3500, 320]", "[2000, 320]")),
            N("curve ends below limiter", ErrorCode.CurveDoesNotCoverRange, "engine.torqueCurve.points", Mutate(", [6000, 220]", "")),
            N("curve starts above idle", ErrorCode.CurveDoesNotCoverRange, "engine.torqueCurve.points", Mutate("[800, 210], ", "")),
            N("negative torque point", ErrorCode.CurveNegativeTorque, "engine.torqueCurve.points[4][1]", Mutate("[6000, 220]", "[6000, -5]")),
            N("negative torque expression", ErrorCode.CurveNegativeTorque, "engine.torqueCurve.expression", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"rpm - 3000\", \"samples\": 16")),
            N("expression syntax", ErrorCode.ExpressionSyntax, "engine.torqueCurve.expression", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"200 +\", \"samples\": 16")),
            N("expression unknown function", ErrorCode.ExpressionSyntax, "engine.torqueCurve.expression", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"sin(rpm)\", \"samples\": 16")),
            N("expression fractional power", ErrorCode.ExpressionSyntax, "engine.torqueCurve.expression", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"rpm^2.5\", \"samples\": 16")),
            N("expression non-finite", ErrorCode.ExpressionNotFinite, "engine.torqueCurve.expression", Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"1/(rpm-800)\", \"samples\": 16")),
            // clutch
            N("engagement below idle", ErrorCode.EngagementBelowIdle, "clutch.engagementRpm", Mutate("\"engagementRpm\": 1100", "\"engagementRpm\": 700")),
            N("engagement curve starts at 0.1", ErrorCode.EngagementCurveEndpoints, "clutch.engagementCurve", Mutate("[[0, 0], [0.4, 0.15], [1, 1]]", "[[0.1, 0], [1, 1]]")),
            N("engagement curve ends at 0.9", ErrorCode.EngagementCurveEndpoints, "clutch.engagementCurve", Mutate("[[0, 0], [0.4, 0.15], [1, 1]]", "[[0, 0], [0.9, 1]]")),
            N("engagement decreasing", ErrorCode.EngagementCurveDecreasing, "clutch.engagementCurve[2][1]", Mutate("[[0, 0], [0.4, 0.15], [1, 1]]", "[[0, 0], [0.4, 0.5], [0.6, 0.3], [1, 1]]")),
            N("engagement x not increasing", ErrorCode.CurveNotIncreasing, "clutch.engagementCurve[1][0]", Mutate("[[0, 0], [0.4, 0.15], [1, 1]]", "[[0, 0], [0, 0.15], [1, 1]]")),
            N("launch below engagement", ErrorCode.LaunchRpmOutOfRange, "clutch.launch.rpm", Mutate("\"rpm\": 2500", "\"rpm\": 900")),
            N("launch above redline", ErrorCode.LaunchRpmOutOfRange, "clutch.launch.rpm", Mutate("\"rpm\": 2500", "\"rpm\": 5800")),
            // gearbox
            N("no forward gears", ErrorCode.NoForwardGears, "gearbox.forward", Mutate("[4.2, 2.5, 1.6, 1.15, 0.85]", "[]")),
            N("negative forward ratio", ErrorCode.ForwardRatioNotPositive, "gearbox.forward[1]", Mutate("[4.2, 2.5", "[4.2, -2.5")),
            N("positive reverse ratio", ErrorCode.ReverseRatioNotNegative, "gearbox.reverse[0]", Mutate("[-3.8]", "[3.8]")),
            N("unknown shift logic", ErrorCode.ShiftLogicUnknown, "gearbox.shiftLogic", Mutate("\"shiftLogic\": \"automatic\"", "\"shiftLogic\": \"cvt\"")),
            N("automatic without downshiftRpm", ErrorCode.MissingField, "gearbox.downshiftRpm", Mutate("\"downshiftRpm\": 2000,", "")),
            N("custom with only upshiftRpm", ErrorCode.MissingField, "gearbox.downshiftRpm", Truck("'forward': [3, 1], 'reverse': [], 'finalDrive': 4, 'shiftLogic': 'test-hold', 'upshiftRpm': 2000, 'shiftTime': 0, 'output': 'front.L'", "", Two)),
            N("downshift above upshift", ErrorCode.ShiftRpmOrder, "gearbox.upshiftRpm", Mutate("\"downshiftRpm\": 2000", "\"downshiftRpm\": 5000")),
            N("upshift above limiter", ErrorCode.ShiftRpmOrder, "gearbox.upshiftRpm", Mutate("\"upshiftRpm\": 4800", "\"upshiftRpm\": 6500")),
            N("downshift below idle", ErrorCode.ShiftRpmOrder, "gearbox.upshiftRpm", Mutate("\"downshiftRpm\": 2000", "\"downshiftRpm\": 600")),
            // differentials
            N("diff with one output", ErrorCode.DiffOutputCount, "differentials[1].outputs", Mutate("[\"front.L\", \"front.R\"]", "[\"front.L\"]")),
            N("diff with three outputs", ErrorCode.DiffOutputCount, "differentials[1].outputs", Mutate("[\"front.L\", \"front.R\"]", "[\"front.L\", \"front.R\", \"rear.L\"]")),
            N("diff outputs identical", ErrorCode.DiffOutputDuplicate, "differentials[1].outputs", Mutate("[\"front.L\", \"front.R\"]", "[\"front.L\", \"front.L\"]")),
            N("missing diff stiffness", ErrorCode.MissingField, "differentials[2].stiffness", Mutate("\"stiffness\": 0.6, ", "")),
            // axles / wiring
            N("no axles", ErrorCode.NoAxles, "axles", Truck(Box + "'front.L'", "", "")),
            N("two axles at one z", ErrorCode.DuplicateAxlePosition, "axles[1].z", Mutate("\"z\": -1.55", "\"z\": 1.45")),
            N("duplicate axle name", ErrorCode.DuplicateName, "axles[1].name", Mutate("\"name\": \"rear\", \"z\"", "\"name\": \"front\", \"z\"")),
            N("duplicate diff name", ErrorCode.DuplicateName, "differentials[2].name", Mutate("\"name\": \"rear\", \"type\"", "\"name\": \"front\", \"type\"")),
            N("unknown wheel", ErrorCode.UnknownNode, "differentials[2].outputs[1]", Mutate("\"rear.L\", \"rear.R\"", "\"rear.L\", \"rear.X\"")),
            N("unknown gearbox output", ErrorCode.UnknownNode, "gearbox.output", Mutate("\"output\": \"centre\"", "\"output\": \"center\"")),
            N("wheel fed twice", ErrorCode.InputFedTwice, "differentials[2].outputs[0]", Mutate("[\"front.L\", \"front.R\"]", "[\"front.L\", \"rear.L\"]")),
            N("cycle back to centre", ErrorCode.WiringCycle, "differentials[2].outputs[1]", Mutate("\"rear.L\", \"rear.R\"", "\"rear.L\", \"centre\"")),
        };

        foreach (Neg c in cases)
        {
            BuildResult r = DrivetrainBuilder.Build(c.Json);
            bool hit = r.Diagnostics.Items.Any(d => d.IsError && d.Error == c.Code && d.Path == c.Path);
            Check(!r.Ok && hit, c.Name + " -> " + c.Code + " at '" + c.Path + "'" + (hit ? "" : " (got: " + Dump(r) + ")"));
            if (!r.Ok && hit) ErrorsCovered.Add(c.Code);
            Check(r.Drivetrain == null, c.Name + ": no model on error (never a silent default)");
        }

        var missing = Enum.GetValues(typeof(ErrorCode)).Cast<ErrorCode>().Where(e => !ErrorsCovered.Contains(e)).ToArray();
        Check(missing.Length == 0, "every ErrorCode has at least one passing negative control" + (missing.Length == 0 ? "" : " (uncovered: " + string.Join(", ", missing.Select(m => m.ToString()).ToArray()) + ")"));

        // Collect-all, not fail-fast: two independent errors are both reported.
        BuildResult two = DrivetrainBuilder.Build(Mutate("\"inertia\": 0.25", "\"inertia\": 0").Replace("\"capacity\": 650", "\"capacity\": 0"));
        Check(two.Diagnostics.ErrorCount == 2, "independent errors are all reported in one pass (" + Dump(two) + ")");
    }

    // ------------------------------------------------------------------ warnings

    private static void Warnings()
    {
        Section("warnings (flagged, never blocking)");
        BuildResult r = DrivetrainBuilder.Build(Mutate("[4.2, 2.5, 1.6, 1.15, 0.85]", "[4.2, 1.2, 1.0, 0.85]"));
        Check(r.Ok && r.Diagnostics.Has(WarningCode.ShiftWouldHunt), "wide 1->2 step: upshift lands at 1371 rpm < downshiftRpm 2000 -> ShiftWouldHunt: " + Dump(r));
        if (r.Diagnostics.Has(WarningCode.ShiftWouldHunt)) WarningsCovered.Add(WarningCode.ShiftWouldHunt);
        Check(r.Diagnostics.Items.Count(d => d.Warning == WarningCode.ShiftWouldHunt) == 1 && r.Diagnostics.Items.First().Path == "gearbox.forward[1]", "only the hunting step is flagged");

        r = DrivetrainBuilder.Build(Mutate("[4.2, 2.5, 1.6, 1.15, 0.85]", "[4.2, 2.5, 2.6, 1.15, 0.85]"));
        Check(r.Ok && r.Diagnostics.Has(WarningCode.ForwardRatiosNotDescending), "non-descending ratios -> ForwardRatiosNotDescending: " + Dump(r));
        if (r.Diagnostics.Has(WarningCode.ForwardRatiosNotDescending)) WarningsCovered.Add(WarningCode.ForwardRatiosNotDescending);

        string spare = Truck(ManualBox + "'rear'",
            "{'name': 'rear', 'type': 'open', 'outputs': ['rear.L', 'rear.R']}, {'name': 'spare', 'type': 'open', 'outputs': ['tag.L', 'tag.R']}",
            "{'name': 'front', 'z': 2, 'track': 2}, {'name': 'rear', 'z': -1, 'track': 2}, {'name': 'tag', 'z': -2.4, 'track': 2, 'lift': true}");
        r = DrivetrainBuilder.Build(spare);
        Check(r.Ok && r.Diagnostics.Has(WarningCode.DisconnectedNode), "differential unreachable from the gearbox -> DisconnectedNode: " + Dump(r));
        if (r.Ok)
        {
            Check(!r.Drivetrain.Axles[2].Driven && !r.Drivetrain.Nodes[r.Drivetrain.Differentials[1].Node].Reachable, "its wheels are not driven");
            WarningsCovered.Add(WarningCode.DisconnectedNode);
        }
        var missing = Enum.GetValues(typeof(WarningCode)).Cast<WarningCode>().Where(w => !WarningsCovered.Contains(w)).ToArray();
        Check(missing.Length == 0, "every WarningCode has a test");
    }

    // ------------------------------------------------------------------ wiring / cycles

    private static void Wiring()
    {
        Section("wiring: cycles are rejected before the game can recurse");
        const string Axles = "{'name': 'front', 'z': 2, 'track': 2}, {'name': 'rear', 'z': -1, 'track': 2}";

        BuildResult r = DrivetrainBuilder.Build(Truck(ManualBox + "'rear'",
            "{'name': 'rear', 'type': 'open', 'outputs': ['rear.L', 'gearbox']}", Axles));
        Diagnostic c = r.Diagnostics.Items.FirstOrDefault(d => d.IsError && d.Error == ErrorCode.WiringCycle);
        Check(!r.Ok && c.Message != null && c.Message.Contains("gearbox -> rear -> gearbox"), "feeding the gearbox back is a cycle, path named: " + Dump(r));

        r = DrivetrainBuilder.Build(Truck(ManualBox + "'rear'",
            "{'name': 'rear', 'type': 'open', 'outputs': ['rear.L', 'rear']}", Axles));
        c = r.Diagnostics.Items.FirstOrDefault(d => d.IsError && d.Error == ErrorCode.WiringCycle);
        Check(!r.Ok && c.Message != null && c.Message.Contains("rear -> rear"), "self-loop is a cycle: " + Dump(r));

        // A cycle among nodes the gearbox never reaches is still rejected (NWH would still recurse
        // if anything ever linked them).
        r = DrivetrainBuilder.Build(Truck(ManualBox + "'rear'",
            "{'name': 'rear', 'type': 'open', 'outputs': ['rear.L', 'rear.R']},"
            + "{'name': 'a', 'type': 'open', 'outputs': ['b', 'front.L']}, {'name': 'b', 'type': 'open', 'outputs': ['a', 'front.R']}", Axles));
        c = r.Diagnostics.Items.FirstOrDefault(d => d.IsError && d.Error == ErrorCode.WiringCycle);
        Check(!r.Ok && c.Message != null && c.Message.Contains("a -> b -> a"), "cycle in an unreachable subgraph is rejected: " + Dump(r));

        // Long chain cycle: three diffs.
        r = DrivetrainBuilder.Build(Truck(ManualBox + "'x'",
            "{'name': 'x', 'type': 'open', 'outputs': ['y', 'front.L']}, {'name': 'y', 'type': 'open', 'outputs': ['z', 'front.R']}, {'name': 'z', 'type': 'open', 'outputs': ['rear.L', 'x']}", Axles));
        c = r.Diagnostics.Items.FirstOrDefault(d => d.IsError && d.Error == ErrorCode.WiringCycle);
        Check(!r.Ok && c.Message != null && c.Message.Contains("x -> y -> z -> x"), "3-node cycle reported with its full path: " + Dump(r));

        // Wiring is skipped (not guessed) when the layout itself has errors.
        r = DrivetrainBuilder.Build(Mutate("\"track\": 1.62", "\"track\": 0").Replace("\"rear.L\", \"rear.R\"", "\"rear.L\", \"centre\""));
        Check(!r.Diagnostics.Has(ErrorCode.WiringCycle) && r.Diagnostics.Has(ErrorCode.OutOfRange), "wiring checks wait for a valid layout");
    }

    // ------------------------------------------------------------------ error positions

    private static int LineOf(string text, string anchor)
    {
        int idx = text.IndexOf(anchor, StringComparison.Ordinal);
        return text.Substring(0, idx).Count(ch => ch == '\n') + 1;
    }

    private static void ErrorPositions()
    {
        Section("config errors report line + field");
        string bad = Mutate("\"engagementRpm\": 1100", "\"engagementRpm\": 700");
        BuildResult r = DrivetrainBuilder.Build(bad);
        Diagnostic d = r.Diagnostics.Items.First(x => x.IsError);
        int want = LineOf(bad, "\"engagementRpm\": 700");
        Check(d.Line == want && d.Path == "clutch.engagementRpm", "semantic error at line " + want + " (got " + d.Line + ", " + d.Path + ")");
        string text = d.ToString();
        Check(text.Contains("line " + want) && text.Contains("clutch.engagementRpm") && text.Contains("EngagementBelowIdle"), "rendered: " + text);

        bad = Mutate("\"output\": \"centre\"", "\"output\": \"centre\",");
        r = DrivetrainBuilder.Build(bad);
        d = r.Diagnostics.Items.First();
        Check(d.Error == ErrorCode.JsonSyntax && d.Line == LineOf(bad, "\"output\"") + 1, "syntax error points at the line after the trailing comma (got " + d + ")");

        bad = Mutate("\"points\": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]", "\"expression\": \"200 + rpm * \", \"samples\": 16");
        r = DrivetrainBuilder.Build(bad);
        d = r.Diagnostics.Items.First();
        Check(d.Error == ErrorCode.ExpressionSyntax && d.Message.Contains("character 13"), "expression errors name the character position (" + d.Message + ")");
    }

    // ------------------------------------------------------------------ runtime API

    private static Func<long> AllocatedBytes()
    {
        MethodInfo m = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
        return m == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
    }

    private static void RuntimeApi()
    {
        Section("runtime API: curves, shift hook, allocation-free");
        var c = new PiecewiseLinear(new[] { 0f, 10f, 20f }, new[] { 0f, 100f, 50f });
        Check(c.Evaluate(5f) == 50f && c.Evaluate(15f) == 75f, "interpolates");
        Check(c.Evaluate(-5f) == 0f && c.Evaluate(99f) == 50f, "clamps outside the range");
        Check(c.Evaluate(float.NaN) == 0f, "NaN in -> 0 out (NaN guard)");
        Check(c.Evaluate(float.PositiveInfinity) == 50f, "+inf clamps");

        CurveExpression e = CurveExpression.Compile("2 + 3 * rpm ^ 2 - -1");
        Check(e.Evaluate(2) == 15, "precedence: 2 + 3*rpm^2 - -1 at 2 = 15");
        Check(CurveExpression.Compile("min(rpm, 10) + max(1, 2) + abs(-3) + clamp(rpm, 0, 1) + lerp(0, 10, 0.5)").Evaluate(4) == 4 + 2 + 3 + 1 + 5, "functions");
        Check(CurveExpression.Compile("(1 + 2) * 3").Evaluate(0) == 9, "parentheses");
        foreach (var bad in new[] { "rpm^9", "max(1)", "1 2", "foo", "(1", "", "rpm^-1" })
        {
            bool threw = false;
            try { CurveExpression.Compile(bad); } catch (ExpressionException) { threw = true; }
            Check(threw, "expression rejected: '" + bad + "'");
        }

        var manual = new ManualShiftLogic();
        var ctx = new ShiftContext { CurrentGear = 2, MinGear = -1, MaxGear = 5, RequestedGear = ShiftContext.NoRequest };
        Check(manual.SelectGear(ref ctx) == 2, "B1: -999 sentinel never shifts");
        ctx.ShiftUp = true; Check(manual.SelectGear(ref ctx) == 3, "B1: ShiftUp");
        ctx.ShiftUp = false; ctx.ShiftDown = true; Check(manual.SelectGear(ref ctx) == 1, "B1: ShiftDown");
        ctx.ShiftDown = false; ctx.RequestedGear = -1; Check(manual.SelectGear(ref ctx) == -1, "B1: ShiftInto R");
        ctx.RequestedGear = 0; Check(manual.SelectGear(ref ctx) == 0, "B1: ShiftInto N");
        ctx.RequestedGear = 9; Check(manual.SelectGear(ref ctx) == 2, "B1: out-of-range request (9 on a 5-speed) ignored");
        ctx.RequestedGear = ShiftContext.NoRequest; ctx.CurrentGear = 5; ctx.ShiftUp = true; Check(manual.SelectGear(ref ctx) == 5, "ShiftUp in top gear stays in top");

        // Property: whatever the input, the selected gear is valid (the "gear index always valid" seed for M1).
        var rng = new System.Random(1234);
        bool allValid = true;
        IShiftLogic[] logics = { manual, new AutomaticShiftLogicStub() };
        for (int i = 0; i < 20000; i++)
        {
            int min = -rng.Next(0, 4), max = rng.Next(1, 13);
            var p = new ShiftContext
            {
                MinGear = min, MaxGear = max, CurrentGear = rng.Next(min, max + 1),
                RequestedGear = rng.Next(4) == 0 ? ShiftContext.NoRequest : rng.Next(-20, 20),
                ShiftUp = rng.Next(3) == 0, ShiftDown = rng.Next(3) == 0,
            };
            int g = logics[i & 1].SelectGear(ref p);
            if (g < min || g > max) { allValid = false; break; }
        }
        Check(allValid, "property: 20k random shift contexts always select a gear in [min, max]");

        var gb = DrivetrainBuilder.Build(BuiltInConfig.Json).Drivetrain.Gearbox;
        Check(gb.RatioOf(99) == 0f && gb.RatioOf(-99) == 0f, "RatioOf out of range = neutral, never throws (vs NWH's unguarded gears[gearIndex])");

        Func<long> alloc = AllocatedBytes();
        Check(alloc != null, "allocation counter available");
        if (alloc != null)
        {
            PiecewiseLinear torque = DrivetrainBuilder.Build(BuiltInConfig.Json).Drivetrain.Engine.TorqueCurve;
            var ctx2 = new ShiftContext { CurrentGear = 1, MinGear = -1, MaxGear = 5, RequestedGear = 3 };
            float sink = 0; int gs = 0;
            for (int i = 0; i < 100; i++) { sink += torque.Evaluate(i * 60f); gs += manual.SelectGear(ref ctx2) + gb.ShiftLogic.SelectGear(ref ctx2); sink += gb.RatioOf(i % 7 - 1); }
            long before = alloc();
            for (int i = 0; i < 100000; i++) { sink += torque.Evaluate(i * 0.06f); gs += manual.SelectGear(ref ctx2) + gb.ShiftLogic.SelectGear(ref ctx2); sink += gb.RatioOf(i % 7 - 1); }
            long delta = alloc() - before;
            Check(delta == 0, "hot paths (Evaluate, SelectGear, RatioOf) allocate 0 bytes over 100k calls (got " + delta + ", sink " + (sink + gs > 0) + ")");
        }
    }

    // ------------------------------------------------------------------ controller

    private sealed class FakeLog : ILog
    {
        public readonly List<string> Lines = new List<string>();
        public void Info(string m) { Lines.Add("I " + m); }
        public void Warning(string m) { Lines.Add("W " + m); }
        public void Error(string m) { Lines.Add("E " + m); }
        public int Errors { get { return Lines.Count(l => l.StartsWith("E ")); } }
    }

    private sealed class FakeSource : IConfigTextSource
    {
        public string Text = BuiltInConfig.Json;
        public string Error;
        public bool TryRead(out string text, out string origin, out string error)
        {
            origin = "test";
            error = Error;
            text = Error == null ? Text : null;
            return Error == null;
        }
    }

    private sealed class FakeApplier : IPatchApplier
    {
        public readonly List<string> Applied = new List<string>();
        public int Removes;
        public bool IsCreated { get { return Applied.Count > 0; } }
        public void Apply(PatchPoint p, MethodInfo target) { Applied.Add(p.Id + "@" + target.DeclaringType.Name + "." + target.Name); }
        public void RemoveAll() { Removes++; }
    }

    private static void Controller()
    {
        Section("controller: A/B toggle, OFF = no touch");
        Check(PatchPoints.All.All(p => !p.Enabled), "every patch-point flag defaults to false");
        Check(PatchPoints.PassThroughPrefix(), "the prefix every point would install returns true (original runs)");

        var log = new FakeLog();
        var src = new FakeSource();
        var app = new FakeApplier();
        var c = new DrivetrainController(log, src, app, PatchPoints.All);
        Check(!c.IsOn && c.Status == DrivetrainController.OffStatus, "starts OFF");
        c.SetEnabled(false);
        Check(c.ConfigReads == 0 && c.Resolutions == 0 && app.Applied.Count == 0 && app.Removes == 0 && log.Lines.Count == 0,
            "OFF: no config read, no reflection on NWH, no apply, no remove, nothing logged");

        c.SetEnabled(true);
        Check(c.IsOn && c.Model != null && c.ConfigReads == 1, "ON loads and validates the config");
        Check(c.Status == "skeleton: NWH active, config loaded, 4 replacement points staged", "status line: " + c.Status);
        Check(c.StagedCount == 4 && c.AppliedCount == 0 && app.Applied.Count == 0 && app.Removes == 0, "ON: 4 staged, 0 applied (all flags false)");
        Check(log.Lines.Any(l => l.Contains("Example 4x4 pickup")), "model summary logged");
        c.SetEnabled(true);
        Check(c.ConfigReads == 1, "ON again is a no-op (no re-read)");
        c.Reload();
        Check(c.ConfigReads == 2 && c.IsOn && c.StagedCount == 4, "Reload re-reads while ON");
        c.SetEnabled(false);
        Check(!c.IsOn && c.Model == null && c.StagedCount == 0 && c.Status == DrivetrainController.OffStatus && app.Removes == 0,
            "OFF again: model dropped, nothing to remove");
        int reads = c.ConfigReads;
        c.Reload();
        Check(c.ConfigReads == reads, "Reload while OFF does nothing");

        // Config failures
        src.Text = Mutate("\"idleRpm\": 800", "\"idleRpm\": 0");
        c.SetEnabled(true);
        Check(c.IsOn && c.Model == null && c.StagedCount == 0 && c.Resolutions == 8, "invalid config: ON but nothing staged, no resolution attempted");
        Check(c.Status.StartsWith("skeleton: NWH active, config FAILED (1 error(s)") && c.Status.EndsWith("0 replacement points staged"), "status: " + c.Status);
        Check(log.Lines.Any(l => l.StartsWith("E config test: error OutOfRange") && l.Contains("engine.idleRpm")), "the error is logged with code and field");
        c.SetEnabled(false);
        src.Error = "ConfigPath file not found: x.json";
        c.SetEnabled(true);
        Check(c.Model == null && c.Status.Contains("config FAILED (ConfigPath file not found"), "unreadable config: FAILED, no fallback to the example");
        c.SetEnabled(false);
        src.Error = null;
        src.Text = BuiltInConfig.Json;

        // A flag set (M1/M2 will do this): only that point is applied; OFF removes.
        PatchPoints.Transmission.Enabled = true;
        try
        {
            c.SetEnabled(true);
            Check(app.Applied.Count == 1 && app.Applied[0] == "M1.transmission@TransmissionComponent.ForwardStep" && c.AppliedCount == 1,
                "flagged point applied exactly once, on the declared override (" + string.Join(",", app.Applied.ToArray()) + ")");
            c.SetEnabled(false);
            Check(app.Removes == 1, "OFF removes applied patches");
        }
        finally { PatchPoints.Transmission.Enabled = false; }

        // Unresolvable target: logged, not staged, never applied.
        var ghost = new PatchPoint
        {
            Id = "test.ghost", Milestone = "-", TargetType = typeof(NWH.VehiclePhysics2.Powertrain.TransmissionComponent),
            Method = "SimulateForwardStep", ParamTypes = Type.EmptyTypes, Evidence = Evidence.GamecodeOnly, Enabled = true,
        };
        var app2 = new FakeApplier();
        var log2 = new FakeLog();
        var c2 = new DrivetrainController(log2, new FakeSource(), app2, new[] { ghost });
        c2.SetEnabled(true);
        Check(c2.StagedCount == 0 && app2.Applied.Count == 0 && log2.Lines.Any(l => l.StartsWith("W patch point test.ghost")),
            "a point whose target is absent is reported and skipped, even if flagged");

        // The real applier is never constructed while OFF or while every flag is false.
        var real = new HarmonyPatchApplier();
        var c3 = new DrivetrainController(new FakeLog(), new FakeSource(), real, PatchPoints.All);
        c3.SetEnabled(false);
        Check(!real.IsCreated, "real Harmony applier: no instance while OFF (harmony id unused)");
        c3.SetEnabled(true);
        Check(!real.IsCreated && c3.StagedCount == 4, "real Harmony applier: no instance while ON with all flags false");
        c3.SetEnabled(false);
        Check(!real.IsCreated, "real Harmony applier: still no instance after OFF");
    }

    // ------------------------------------------------------------------ host

    private static void Host()
    {
        Section("ModHost: startup, hotkey, config reactions, runner");
        ConfigFile cfg = NewConfig();
        var log = new FakeLog();
        var app = new FakeApplier();
        string dir = Path.Combine(Path.GetTempPath(), "adt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ModHost.Init(cfg, log, dir, app);
        Check(!ModHost.Controller.IsOn && ModHost.Controller.ConfigReads == 0, "startup with defaults: OFF, config never read");
        Check(log.Lines.Count == 1 && log.Lines[0].Contains("0.1.0-alpha") && log.Lines[0].Contains("OFF, NWH untouched") && log.Lines[0].Contains("F9"),
            "exactly one startup status line: " + string.Join(" / ", log.Lines.ToArray()));
        Check(ModHost.ToggleKeyValid && ModHost.ToggleKey == KeyCode.F9, "hotkey parsed");

        ModHost.Toggle();
        Check(ModHost.Settings.Enabled.Value && ModHost.Controller.IsOn && ModHost.Controller.StagedCount == 4, "hotkey toggle -> Enabled=true -> controller ON");
        File.WriteAllText(Path.Combine(dir, "truck.json"), Mutate("\"Example 4x4 pickup\"", "\"From file\""));
        ModHost.Settings.ConfigPath.Value = "truck.json";
        Check(ModHost.Controller.ConfigReads == 2 && ModHost.Controller.Model != null && ModHost.Controller.Model.Name == "From file",
            "ConfigPath change while ON reloads; relative path resolves under the config dir");
        ModHost.Settings.ConfigPath.Value = "missing.json";
        Check(ModHost.Controller.Model == null && ModHost.Controller.Status.Contains("not found"), "missing ConfigPath file: FAILED, no fallback");
        ModHost.Toggle();
        Check(!ModHost.Settings.Enabled.Value && !ModHost.Controller.IsOn && app.Applied.Count == 0, "hotkey toggle -> OFF; nothing was ever applied");

        ModHost.Settings.ToggleKey.Value = "Banana";
        Check(!ModHost.ToggleKeyValid && log.Lines.Any(l => l.StartsWith("E") && l.Contains("Banana")), "bad ToggleKey: hotkey disabled and logged");
        ModHost.Tick();   // must not throw with the hotkey disabled
        ModHost.Settings.ToggleKey.Value = "F10";
        Check(ModHost.ToggleKeyValid && ModHost.ToggleKey == KeyCode.F10, "fixing ToggleKey re-enables the hotkey");

        // Enabled=true already in the file: ON at startup.
        ConfigFile cfg2 = NewConfig();
        cfg2.Bind(DrivetrainSettings.Section, "Enabled", false, "").Value = true;
        var log2 = new FakeLog();
        ModHost.Init(cfg2, log2, dir, new FakeApplier());
        Check(ModHost.Controller.IsOn && log2.Lines.Any(l => l.Contains("4 replacement points staged")), "Enabled=true in the config: ON at startup with status");

        int before = RunnerHost.Created;
        ModHost.OnSceneLoaded(default(UnityEngine.SceneManagement.Scene), UnityEngine.SceneManagement.LoadSceneMode.Single);
        Check(RunnerHost.Created == before + 1, "sceneLoaded recreates the hidden runner when it is gone (stub AddComponent = destroyed)");
        Check(RunnerHost.ObjectName.StartsWith("ApocalypterDrivetrain"), "runner object name");
        Check(typeof(Runner).BaseType == typeof(MonoBehaviour) && typeof(Runner).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic) != null, "runner is a MonoBehaviour with Update");
    }

    // ------------------------------------------------------------------ patch-point evidence

    private static JsonValue _census;
    private static JsonValue Census()
    {
        if (_census == null) _census = Json.Parse(File.ReadAllText("../inputs/census/census.json"));
        return _census;
    }

    private static string CensusTypeName(Type t) { return t.FullName; }

    /// <summary>The census entry for a method with exact parameter types, or null.</summary>
    private static JsonValue CensusMethod(Type type, string method, Type[] ps)
    {
        string want = string.Join(",", ps.Select(p => p.FullName).ToArray());
        foreach (JsonValue e in Census().Get("entries").Items)
        {
            if (e.Get("kind").String != "method" || e.Get("type").String != CensusTypeName(type) || e.Get("path").String != method) continue;
            JsonValue pt = e.Get("paramTypes");
            if (pt == null) continue;
            if (string.Join(",", pt.Items.Select(i => i.String).ToArray()) == want) return e;
        }
        return null;
    }

    private static bool CensusResolvedOn(JsonValue entry, Type declaring)
    {
        if (entry == null) return false;
        JsonValue res = entry.Get("resolution");
        if (!res.Get("resolved").Bool) return false;
        JsonValue hops = res.Get("hops");
        return hops.Items.Count == 1 && hops.Items[0].Get("declaringType").String == declaring.FullName;
    }

    private static void PatchPointEvidence()
    {
        Section("patch points: compile-checked + evidence (stubs, gamecode, census)");
        Check(PatchPoints.All.Length == 4 && PatchPoints.All.Select(p => p.Id).Distinct().Count() == 4, "4 distinct points (M1 transmission, M2 engine/clutch/differential)");
        Check(PatchPoints.PassThrough != null && PatchPoints.PassThrough.ReturnType == typeof(bool) && PatchPoints.PassThrough.GetParameters().Length == 0, "pass-through prefix is a parameterless bool method");
        foreach (PatchPoint p in PatchPoints.All)
        {
            MethodInfo m = p.Resolve();
            Check(m != null && m.DeclaringType == p.TargetType, p.Id + ": " + p.TargetName + " is declared on the type itself in the stubs (override, not base)");
            string file = Path.Combine("../gamecode", p.GamecodeFile);
            Check(File.Exists(file) && File.ReadAllText(file).Contains(p.GamecodeSignature), p.Id + ": gamecode/" + p.GamecodeFile + " declares '" + p.GamecodeSignature + "'");
            JsonValue entry = CensusMethod(p.TargetType, p.Method, p.ParamTypes);
            bool resolved = CensusResolvedOn(entry, p.TargetType);
            if (p.Evidence == Evidence.CensusResolved)
                Check(resolved, p.Id + ": census resolves it against the real DLL, declared on " + p.TargetType.Name);
            else
                Check(!resolved, p.Id + ": labelled GamecodeOnly and the census indeed has no resolved entry (relabel when the census adds it)");
        }
        Check(PatchPoints.All.Count(p => p.Evidence == Evidence.GamecodeOnly) == 3, "3 of 4 points still need census confirmation before M1/M2 enables them");

        // Negative controls for the evidence checks themselves.
        Check(!CensusResolvedOn(CensusMethod(typeof(NWH.VehiclePhysics2.Powertrain.TransmissionComponent), "SimulateForwardStep", Type.EmptyTypes), typeof(NWH.VehiclePhysics2.Powertrain.TransmissionComponent)),
            "negative control: SimulateForwardStep (steering stub helper) is NOT resolved by the census");
        Check(typeof(NWH.VehiclePhysics2.Powertrain.TransmissionComponent).GetMethod("SimulateForwardStep") == null, "negative control: and it is gone from the stubs");
        Check(!CensusResolvedOn(CensusMethod(typeof(NWH.VehiclePhysics2.Powertrain.DifferentialComponent), "ForwardStep", new[] { typeof(float), typeof(float) }), typeof(NWH.VehiclePhysics2.Powertrain.DifferentialComponent)),
            "negative control: a wrong parameter list does not match the census");
        Check(!File.ReadAllText("../gamecode/TransmissionComponent.cs").Contains("public float SimulateForwardStep("), "negative control: gamecode has no SimulateForwardStep");
    }

    // ------------------------------------------------------------------ stub drift

    private static void StubDrift()
    {
        Section("stubs: no member the real game lacks (census drift list)");
        Assembly stubs = typeof(UnityEngine.Object).Assembly;
        int checkedCount = 0, drift = 0;
        var still = new List<string>();
        foreach (JsonValue e in Census().Get("entries").Items)
        {
            JsonValue sources = e.Get("sources");
            if (sources.Get("stubs") == null || e.Get("resolution").Get("resolved").Bool) continue;
            checkedCount++;
            string typeName = e.Get("type").String, path = e.Get("path").String, kind = e.Get("kind").String;
            Type t = stubs.GetType(typeName);
            bool present;
            if (kind == "type") present = t != null;
            else if (t == null) present = false;
            else present = t.GetMember(path, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length > 0;
            if (present) { drift++; still.Add(kind + " " + typeName + "." + path); }
        }
        Check(checkedCount == 18, "census lists 18 stub-drift entries (got " + checkedCount + ")");
        Check(drift == 0, "none of them remain in the stubs" + (drift == 0 ? "" : ": " + string.Join(", ", still.ToArray())));
        Check(stubs.GetType("PlayMakerFSM") != null && stubs.GetType("HutongGames.PlayMaker.PlayMakerFSM") == null, "PlayMakerFSM is in the global namespace (README fact 9)");
        Check(stubs.GetType("NWH.VehiclePhysics2.ManagerVehicleComponent") != null, "ManagerVehicleComponent is in NWH.VehiclePhysics2 (README fact 9)");
    }

    // ------------------------------------------------------------------ README sync

    private static void ReadmeSync()
    {
        Section("README example == built-in config");
        string readme = File.ReadAllText("../README.md").Replace("\r\n", "\n");
        int sec = readme.IndexOf("## 4. Drivetrain config", StringComparison.Ordinal);
        int start = sec < 0 ? -1 : readme.IndexOf("```json\n", sec, StringComparison.Ordinal);
        int end = start < 0 ? -1 : readme.IndexOf("```", start + 8, StringComparison.Ordinal);
        Check(start > 0 && end > start, "README §4 has a ```json example block");
        if (start > 0 && end > start)
        {
            string block = readme.Substring(start + 8, end - start - 8);
            Check(block == BuiltInConfig.Json.Replace("\r\n", "\n"), "the README example is byte-identical to BuiltInConfig.Json");
        }
    }

    // ------------------------------------------------------------------ model isolation

    private static void ModelIsolation()
    {
        Section("model isolation (pure C#, NWH/Unity-free)");
        string path = Path.GetFullPath("out/Model.dll");
        Check(File.Exists(path), "run.sh compiled Model/ alone into out/Model.dll (no UnityEngine/NWH/BepInEx refs given)");
        if (!File.Exists(path)) return;
        AssemblyName[] refs = Assembly.LoadFile(path).GetReferencedAssemblies();
        Check(refs.All(r => r.Name == "netstandard"), "Model.dll references only netstandard (" + string.Join(", ", refs.Select(r => r.Name).ToArray()) + ")");
        Type[] modelTypes = typeof(Drivetrain).Assembly.GetTypes().Where(t => t.Namespace == "ApocalypterDrivetrain.Model").ToArray();
        bool clean = modelTypes.All(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .All(f => f.FieldType.Namespace == null || !(f.FieldType.Namespace.StartsWith("UnityEngine") || f.FieldType.Namespace.StartsWith("NWH"))));
        Check(clean && modelTypes.Length > 10, "no model field has a Unity or NWH type (" + modelTypes.Length + " model types)");
    }
}
