// Piecewise-linear curve: the runtime representation of every curve in the model (torque,
// clutch engagement). Expressions are sampled into one of these at load, so the tick path
// never interprets anything. Evaluate is allocation-free and NaN-guarded.
using System;

namespace ApocalypterDrivetrain.Model
{
    public sealed class PiecewiseLinear
    {
        private readonly float[] _x;
        private readonly float[] _y;

        /// <summary>Arrays are copied. Caller (the builder) has validated: >= 2 points, x strictly increasing, all finite.</summary>
        public PiecewiseLinear(float[] x, float[] y)
        {
            if (x == null || y == null || x.Length != y.Length || x.Length < 2)
                throw new ArgumentException("PiecewiseLinear needs >= 2 matching points");
            _x = (float[])x.Clone();
            _y = (float[])y.Clone();
        }

        public int Count { get { return _x.Length; } }
        public float MinX { get { return _x[0]; } }
        public float MaxX { get { return _x[_x.Length - 1]; } }
        public float XAt(int i) { return _x[i]; }
        public float YAt(int i) { return _y[i]; }

        /// <summary>
        /// Linear interpolation; clamps to the end values outside [MinX, MaxX]. NaN input returns
        /// 0 (the API-boundary NaN guard: a NaN must never enter the powertrain).
        /// </summary>
        public float Evaluate(float x)
        {
            if (float.IsNaN(x)) return 0f;
            int n = _x.Length;
            if (x <= _x[0]) return _y[0];
            if (x >= _x[n - 1]) return _y[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_x[mid] <= x) lo = mid; else hi = mid;
            }
            float t = (x - _x[lo]) / (_x[hi] - _x[lo]);
            return _y[lo] + (_y[hi] - _y[lo]) * t;
        }
    }
}
