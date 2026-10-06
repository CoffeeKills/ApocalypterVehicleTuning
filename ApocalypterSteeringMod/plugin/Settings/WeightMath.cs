using UnityEngine;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Pure math for the weight category (harness-tested, no Unity calls):
    /// ballast mass and weighted centre of mass, balloon lift in newtons
    /// (uncapped since 0.11.0), and the coarse/trim slider decomposition.
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
        /// Balloon lift in newtons per axle: |negative kg| x gravity. Uncapped
        /// (0.11.0): every negative kilogram lifts with exactly its own weight's
        /// worth of force. Positive kg produces no lift (ballast is handled by
        /// the mass-property writes).
        /// </summary>
        public static void LiftFor(float frontKg, float rearKg, out float frontN, out float rearN)
        {
            frontN = Mathf.Max(0f, -frontKg) * Gravity;
            rearN = Mathf.Max(0f, -rearKg) * Gravity;
        }

        /// <summary>Coarse part of a weight: v snapped to the nearest 100 kg (banker's rounding).</summary>
        public static float CoarseOf(float v)
        {
            return Mathf.Round(v / 100f) * 100f;
        }

        /// <summary>Trim part: what the coarse snap discarded; always in [-50, +50] kg.</summary>
        public static float TrimOf(float v)
        {
            return v - CoarseOf(v);
        }
    }
}
