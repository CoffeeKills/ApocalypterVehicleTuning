using System;
using System.Globalization;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Piecewise-linear curve over x in [0,1] with y clamped to [0,1], holding
    /// 2..8 points sorted by x. Evaluate is allocation-free (binary search) —
    /// it runs in the steering prefix every physics tick. Mutable; Clone() for
    /// copies (presets must never be mutated through a shared instance).
    /// Serialized as "x:y;x:y;..." with invariant culture and "R" round-trip
    /// formatting, which is the BepInEx config representation.
    /// </summary>
    public sealed class EditableCurve
    {
        public const int MinPoints = 2;
        public const int MaxPoints = 8;

        // Config bind defaults (kept in sync with the preset template curves;
        // a unit test asserts they parse to those curves).
        public const string DefaultLockCurveText = "0:1;0.5:0.35;1:0.22";
        public const string DefaultReturnCurveText = "0:1;1:1";

        private const float MinXGap = 1e-4f;

        private float[] _x;
        private float[] _y;

        public int Count { get { return _x.Length; } }

        public float X(int i) { return _x[i]; }
        public float Y(int i) { return _y[i]; }

        private EditableCurve(float[] x, float[] y)
        {
            _x = x;
            _y = y;
        }

        /// <summary>Authoring helper: even count of x,y pairs in order.</summary>
        public static EditableCurve FromPoints(params float[] xy)
        {
            if (xy == null || xy.Length < MinPoints * 2 || (xy.Length & 1) != 0)
            {
                throw new ArgumentException("EditableCurve.FromPoints needs at least 2 x,y pairs");
            }
            int n = xy.Length / 2;
            float[] x = new float[n];
            float[] y = new float[n];
            for (int i = 0; i < n; i++)
            {
                x[i] = Clamp01(xy[i * 2]);
                y[i] = Clamp01(xy[i * 2 + 1]);
            }
            return new EditableCurve(x, y);
        }

        public static EditableCurve Flat(float y)
        {
            return FromPoints(0f, y, 1f, y);
        }

        /// <summary>t is clamped to [0,1]; piecewise linear between points.</summary>
        public float Evaluate(float t)
        {
            t = Clamp01(t);
            if (_x.Length == 0)
            {
                return 0f;
            }
            if (t <= _x[0])
            {
                return _y[0];
            }
            int last = _x.Length - 1;
            if (t >= _x[last])
            {
                return _y[last];
            }
            // Binary search: first index with _x[i] > t.
            int lo = 0, hi = last;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_x[mid] <= t)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            float span = _x[lo] - _x[lo - 1];
            float u = span <= 1e-8f ? 0f : (t - _x[lo - 1]) / span;
            return _y[lo - 1] + (_y[lo] - _y[lo - 1]) * u;
        }

        public EditableCurve Clone()
        {
            return new EditableCurve((float[])_x.Clone(), (float[])_y.Clone());
        }

        public void CopyFrom(EditableCurve other)
        {
            _x = (float[])other._x.Clone();
            _y = (float[])other._y.Clone();
        }

        /// <summary>Sorted insert. Fails at MaxPoints or when x is already taken.</summary>
        public bool TryAddPoint(float x, float y)
        {
            if (_x.Length >= MaxPoints)
            {
                return false;
            }
            x = Clamp01(x);
            y = Clamp01(y);
            int at = IndexOfX(x);
            if (at >= 0)
            {
                return false;
            }
            int insert = ~at;
            float[] nx = new float[_x.Length + 1];
            float[] ny = new float[_y.Length + 1];
            Array.Copy(_x, 0, nx, 0, insert);
            Array.Copy(_y, 0, ny, 0, insert);
            nx[insert] = x;
            ny[insert] = y;
            Array.Copy(_x, insert, nx, insert + 1, _x.Length - insert);
            Array.Copy(_y, insert, ny, insert + 1, _y.Length - insert);
            _x = nx;
            _y = ny;
            return true;
        }

        /// <summary>
        /// Moves a point. y is clamped to [0,1]; x is clamped between the
        /// neighbours' x so point order can never break, keeping a gap of
        /// 2 x MinXGap to each neighbour. Without the gap a point dragged onto its
        /// neighbour's x formed a vertical step that TryParse deduplicates on the
        /// next load, so the curve the user saved was not the curve that came back.
        /// When the neighbours are already closer than that (a parsed file), x stays.
        /// </summary>
        public bool TryMovePoint(int index, float x, float y)
        {
            if (index < 0 || index >= _x.Length)
            {
                return false;
            }
            const float gap = 2f * MinXGap;
            float minX = index > 0 ? _x[index - 1] + gap : 0f;
            float maxX = index < _x.Length - 1 ? _x[index + 1] - gap : 1f;
            if (minX <= maxX)
            {
                _x[index] = Clamp(x, minX, maxX);
            }
            _y[index] = Clamp01(y);
            return true;
        }

        /// <summary>
        /// Same points within eps. Allocation-free replacement for comparing
        /// Serialize() strings (the panel compares on every refresh and every
        /// mesh rebuild while a point is dragged).
        /// </summary>
        public bool SameAs(EditableCurve other, float eps = 1e-5f)
        {
            if (other == null || other._x.Length != _x.Length)
            {
                return false;
            }
            for (int i = 0; i < _x.Length; i++)
            {
                if (Math.Abs(_x[i] - other._x[i]) > eps || Math.Abs(_y[i] - other._y[i]) > eps)
                {
                    return false;
                }
            }
            return true;
        }

        public bool TryRemovePoint(int index)
        {
            if (index < 0 || index >= _x.Length || _x.Length <= MinPoints)
            {
                return false;
            }
            float[] nx = new float[_x.Length - 1];
            float[] ny = new float[_y.Length - 1];
            Array.Copy(_x, 0, nx, 0, index);
            Array.Copy(_y, 0, ny, 0, index);
            Array.Copy(_x, index + 1, nx, index, _x.Length - index - 1);
            Array.Copy(_y, index + 1, ny, index, _y.Length - index - 1);
            _x = nx;
            _y = ny;
            return true;
        }

        /// <summary>Migration fold: scale every y by a factor, clamped to [0,1].</summary>
        public void ScaleY(float factor)
        {
            for (int i = 0; i < _y.Length; i++)
            {
                _y[i] = Clamp01(_y[i] * factor);
            }
        }

        public string Serialize()
        {
            string[] parts = new string[_x.Length];
            for (int i = 0; i < _x.Length; i++)
            {
                parts[i] = _x[i].ToString("R", CultureInfo.InvariantCulture)
                    + ":" + _y[i].ToString("R", CultureInfo.InvariantCulture);
            }
            return string.Join(";", parts);
        }

        /// <summary>
        /// Lenient parser: trims, accepts comma decimals, clamps x/y to [0,1],
        /// sorts and dedupes near-equal x. Fails (curve = null) on garbage or a
        /// point count outside [MinPoints, MaxPoints].
        /// </summary>
        public static bool TryParse(string s, out EditableCurve curve)
        {
            curve = null;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            string[] parts = s.Split(';');
            if (parts.Length < MinPoints || parts.Length > MaxPoints)
            {
                return false;
            }
            float[] x = new float[parts.Length];
            float[] y = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int sep = p.IndexOf(':');
                if (sep <= 0 || sep >= p.Length - 1)
                {
                    return false;
                }
                string xs = p.Substring(0, sep).Trim().Replace(',', '.');
                string ys = p.Substring(sep + 1).Trim().Replace(',', '.');
                if (!float.TryParse(xs, NumberStyles.Float, CultureInfo.InvariantCulture, out x[i])
                    || !float.TryParse(ys, NumberStyles.Float, CultureInfo.InvariantCulture, out y[i]))
                {
                    return false;
                }
                x[i] = Clamp01(x[i]);
                y[i] = Clamp01(y[i]);
            }
            // Sort by x and drop near-duplicates (stable counts).
            for (int i = 1; i < parts.Length; i++)
            {
                for (int j = i; j > 0 && x[j] < x[j - 1]; j--)
                {
                    float tx = x[j]; x[j] = x[j - 1]; x[j - 1] = tx;
                    float ty = y[j]; y[j] = y[j - 1]; y[j - 1] = ty;
                }
            }
            int n = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (n > 0 && x[i] - x[n - 1] < MinXGap)
                {
                    x[n - 1] = x[i];   // keep the last of a duplicate run
                    y[n - 1] = y[i];
                }
                else
                {
                    x[n] = x[i];
                    y[n] = y[i];
                    n++;
                }
            }
            if (n < MinPoints)
            {
                return false;
            }
            Array.Resize(ref x, n);
            Array.Resize(ref y, n);
            curve = new EditableCurve(x, y);
            return true;
        }

        /// <summary>Binary search for an existing x; ~index = insertion point.</summary>
        private int IndexOfX(float x)
        {
            int lo = 0, hi = _x.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                float d = _x[mid] - x;
                if (Math.Abs(d) < MinXGap)
                {
                    return mid;
                }
                if (d < 0f)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return ~lo;
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
