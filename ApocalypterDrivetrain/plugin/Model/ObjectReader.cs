// Typed, position-aware access to one JSON object. Every key read is marked consumed;
// Finish() reports every key that was not read as UnknownField (a typo like "idelRpm" must
// never be ignored silently). Required fields that are absent are MissingField errors.
// Optional fields exist only where the README schema documents their default.
using System.Collections.Generic;
using System.Globalization;

namespace ApocalypterDrivetrain.Model
{
    public struct ValueRange
    {
        public double Min, Max;
        public bool MinInclusive, MaxInclusive;

        public static ValueRange Positive(double max) { return new ValueRange { Min = 0, Max = max, MinInclusive = false, MaxInclusive = true }; }
        public static ValueRange Closed(double min, double max) { return new ValueRange { Min = min, Max = max, MinInclusive = true, MaxInclusive = true }; }
        public static ValueRange Open(double min, double max) { return new ValueRange { Min = min, Max = max, MinInclusive = false, MaxInclusive = false }; }

        public bool Contains(double v)
        {
            bool lo = MinInclusive ? v >= Min : v > Min;
            bool hi = MaxInclusive ? v <= Max : v < Max;
            return lo && hi;
        }

        public override string ToString()
        {
            return (MinInclusive ? "[" : "(") + Fmt(Min) + ", " + Fmt(Max) + (MaxInclusive ? "]" : ")");
        }

        public static string Fmt(double v) { return v.ToString("0.######", CultureInfo.InvariantCulture); }
    }

    public sealed class ObjectReader
    {
        public readonly JsonValue Obj;
        public readonly string Path;
        private readonly Diagnostics _d;
        private readonly HashSet<string> _consumed = new HashSet<string>();

        public ObjectReader(JsonValue obj, string path, Diagnostics d)
        {
            Obj = obj; Path = path; _d = d;
        }

        public string PathOf(string key) { return string.IsNullOrEmpty(Path) ? key : Path + "." + key; }

        /// <summary>Raw member; reports MissingField when required and absent.</summary>
        public JsonValue Raw(string key, bool required)
        {
            _consumed.Add(key);
            JsonValue v = Obj.Get(key);
            if (v == null && required)
                _d.Error(ErrorCode.MissingField, PathOf(key), Obj, "required field is missing");
            return v;
        }

        public bool Has(string key) { return Obj.Get(key) != null; }

        public bool Number(string key, ValueRange range, out float value)
        {
            value = 0f;
            JsonValue v = Raw(key, true);
            return v != null && CheckNumber(v, PathOf(key), range, _d, out value);
        }

        /// <summary>Optional number: false + no error when absent.</summary>
        public bool OptionalNumber(string key, ValueRange range, out float value, out bool present)
        {
            value = 0f;
            JsonValue v = Raw(key, false);
            present = v != null;
            return v != null && CheckNumber(v, PathOf(key), range, _d, out value);
        }

        public static bool CheckNumber(JsonValue v, string path, ValueRange range, Diagnostics d, out float value)
        {
            value = 0f;
            if (v.Kind != JsonKind.Number)
            {
                d.Error(ErrorCode.WrongType, path, v, "expected a number, got " + v.KindName);
                return false;
            }
            double n = v.Number;
            if (double.IsNaN(n) || double.IsInfinity(n) || n > float.MaxValue || n < -float.MaxValue)
            {
                d.Error(ErrorCode.NotFinite, path, v, "number is not a finite single-precision value");
                return false;
            }
            if (!range.Contains(n))
            {
                d.Error(ErrorCode.OutOfRange, path, v, "must be in " + range + " (got " + ValueRange.Fmt(n) + ")");
                return false;
            }
            value = (float)n;
            return true;
        }

        public bool Integer(string key, int min, int max, out int value)
        {
            value = 0;
            JsonValue v = Raw(key, true);
            if (v == null) return false;
            if (v.Kind != JsonKind.Number || v.Number != System.Math.Floor(v.Number))
            {
                _d.Error(ErrorCode.WrongType, PathOf(key), v, "expected an integer, got " + (v.Kind == JsonKind.Number ? ValueRange.Fmt(v.Number) : v.KindName));
                return false;
            }
            if (v.Number < min || v.Number > max)
            {
                _d.Error(ErrorCode.OutOfRange, PathOf(key), v, "must be an integer in [" + min + ", " + max + "] (got " + ValueRange.Fmt(v.Number) + ")");
                return false;
            }
            value = (int)v.Number;
            return true;
        }

        /// <summary>Optional boolean with a documented default.</summary>
        public bool Bool(string key, bool documentedDefault, out bool value)
        {
            value = documentedDefault;
            JsonValue v = Raw(key, false);
            if (v == null) return true;
            if (v.Kind != JsonKind.Bool)
            {
                _d.Error(ErrorCode.WrongType, PathOf(key), v, "expected true or false, got " + v.KindName);
                return false;
            }
            value = v.Bool;
            return true;
        }

        public bool String(string key, out string value)
        {
            value = null;
            JsonValue v = Raw(key, true);
            if (v == null) return false;
            if (v.Kind != JsonKind.String)
            {
                _d.Error(ErrorCode.WrongType, PathOf(key), v, "expected a string, got " + v.KindName);
                return false;
            }
            value = v.String;
            return true;
        }

        public JsonValue Array(string key, bool required)
        {
            JsonValue v = Raw(key, required);
            if (v == null) return null;
            if (v.Kind != JsonKind.Array)
            {
                _d.Error(ErrorCode.WrongType, PathOf(key), v, "expected an array, got " + v.KindName);
                return null;
            }
            return v;
        }

        public ObjectReader Object(string key, bool required)
        {
            JsonValue v = Raw(key, required);
            if (v == null) return null;
            return Wrap(v, PathOf(key), _d);
        }

        public static ObjectReader Wrap(JsonValue v, string path, Diagnostics d)
        {
            if (v.Kind != JsonKind.Object)
            {
                d.Error(ErrorCode.WrongType, path, v, "expected an object, got " + v.KindName);
                return null;
            }
            return new ObjectReader(v, path, d);
        }

        /// <summary>Reports keys present in the object that no reader consumed.</summary>
        public void Finish()
        {
            foreach (KeyValuePair<string, JsonValue> m in Obj.Members)
            {
                if (!_consumed.Contains(m.Key))
                    _d.Error(ErrorCode.UnknownField, PathOf(m.Key), m.Value, "unknown field (check the spelling against the README schema)");
            }
        }

        /// <summary>A key that is valid in the schema but not for this variant (e.g. split on a locked diff).</summary>
        public void Reject(string key, ErrorCode code, string why)
        {
            JsonValue v = Obj.Get(key);
            if (v == null) return;
            _consumed.Add(key);
            _d.Error(code, PathOf(key), v, why);
        }
    }
}
