using UnityEngine;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Pure math for the weight category (harness-tested, no Unity calls):
    /// ballast mass and weighted centre of mass, and balloon lift in newtons
    /// with a proportional total clamp so the car can't be blown away.
    /// </summary>
    public static class WeightMath
    {
        public const float Gravity = 9.81f;

        /// <summary>Stock mass plus the ballast added at each axle (negative = balloon, ignored here).</summary>
        public static float ComputeMass(float stockMass, float frontKg, float rearKg)
        {
            return stockMass + Mathf.Max(0f, frontKg) + Mathf.Max(0f, rearKg);
        }

        /// <summary>Total mass / stock mass, guarded against a missing/zero stock mass.</summary>
        public static float MassRatio(float stockMass, float frontKg, float rearKg)
        {
            if (stockMass <= 1e-3f) return 1f;
            return ComputeMass(stockMass, frontKg, rearKg) / stockMass;
        }

        /// <summary>
        /// Centre of mass (local space): the stock COM weighted by stock mass, the axle
        /// points weighted by the ballast there. Degenerate total falls back to the stock COM.
        /// </summary>
        public static Vector3 ComputeCom(Vector3 stockCom, float stockMass, Vector3 frontPoint, Vector3 rearPoint, float frontKg, float rearKg)
        {
            float fm = Mathf.Max(0f, frontKg);
            float rm = Mathf.Max(0f, rearKg);
            float total = stockMass + fm + rm;
            if (total <= 1e-3f) return stockCom;
            return (stockCom * stockMass + frontPoint * fm + rearPoint * rm) / total;
        }

        /// <summary>
        /// Balloon lift in newtons per axle: |negative kg| x gravity. Total lift is
        /// clamped to stockMass x gravity x capFactor, shared proportionally between
        /// the axles (both shrink by the same factor past the cap).
        /// </summary>
        public static void LiftFor(float frontKg, float rearKg, float stockMass, float capFactor, out float frontN, out float rearN)
        {
            float f = Mathf.Max(0f, -frontKg) * Gravity;
            float r = Mathf.Max(0f, -rearKg) * Gravity;
            float cap = Mathf.Max(0f, stockMass) * Gravity * capFactor;
            float total = f + r;
            if (total > cap && total > 1e-3f)
            {
                float k = cap / total;
                f *= k;
                r *= k;
            }
            frontN = f;
            rearN = r;
        }
    }
}
