using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Weight category (0.10.0): positive kg = real ballast at that axle (raises
    /// the rigidbody mass, shifts the centre of mass, scales the inertia tensor),
    /// negative kg = "balloon" lift applied per physics tick by an onboarded
    /// WeightLiftModule. The suspension springs are re-scaled with the mass so
    /// ballast does not bottom them out.
    /// </summary>
    public sealed partial class VehicleTuner
    {
        // ---------------------------------------------------------------- weight

        internal sealed class WeightData
        {
            public float StockMass;
            public Vector3 StockCom;      // local space
            public Vector3 StockInertia;  // diagonal only; inertiaTensorRotation never touched
            public Vector3 FrontPoint;    // local space (mean of that axle's wheel positions)
            public Vector3 RearPoint;
            public WeightLiftModule Lift; // null = could not be onboarded (no moduleManager)
            public float LastFrontKg;     // the axle split the last mass-property write used
            public float LastRearKg;
        }

        private void ApplyAllWeight()
        {
            WeightPreset p = WeightSettings.ActivePreset ?? WeightPreset.Stock;
            TargetPass(AppliedCat.Weight, r => WeightSettings.Book.ForVehicle(VehicleName(r.Vc)), ApplyWeight, RestoreWeight);
        }

        private void RestoreAllWeight()
        {
            RestorePass(AppliedCat.Weight, RestoreWeight);
        }

        /// <summary>
        /// Snapshot the rigidbody's stock mass properties and each axle's point,
        /// and onboard the lift module once (it stays onboarded forever; at
        /// 0 N it is inert). Axle point = mean of that axle's captured wheel
        /// positions in vehicle-local space (the frame centre of mass lives in);
        /// an axle with no wheels falls back to the stock COM. Called both at
        /// capture and on every toggle-on refresh: the game may have changed the
        /// mass since (cargo, fuel), and unlike aero there is no shipped module
        /// to protect — every WeightData is ours.
        /// </summary>
        private static WeightData CaptureWeight(VehicleController vc, VehicleRecord r)
        {
            Rigidbody rb = vc.vehicleRigidbody;
            if (rb == null)
            {
                return r.Weight;   // no rigidbody: nothing to tune (0.10.0 guard)
            }
            WeightData d = r.Weight ?? new WeightData();
            d.StockMass = rb.mass;
            d.StockCom = rb.centerOfMass;
            d.StockInertia = rb.inertiaTensor;

            Vector3 front = Vector3.zero, rear = Vector3.zero;
            int frontCount = 0, rearCount = 0;
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                if (wk.Key == null)
                {
                    continue;
                }
                Vector3 local = vc.transform.InverseTransformPoint(wk.Key.transform.position);
                if (wk.Value.IsFront) { front += local; frontCount++; }
                else { rear += local; rearCount++; }
            }
            d.FrontPoint = frontCount > 0 ? front / frontCount : d.StockCom;
            d.RearPoint = rearCount > 0 ? rear / rearCount : d.StockCom;

            if (d.Lift == null && vc.moduleManager != null)
            {
                WeightLiftModule m = new WeightLiftModule();
                vc.moduleManager.AddAndOnboardNewComponent(m);
                // Onboarding leaves the module uninitialised (LOD index -1);
                // VC_Enable initialises it, and 0 N targets keep it inert.
                m.VC_Enable(false);
                d.Lift = m;
            }
            return d;
        }

        private static void ApplyWeight(VehicleRecord r, WeightPreset p)
        {
            WeightData d = r.Weight;
            if (d == null)
            {
                return;   // weight capture failed for this vehicle (0.10.0 guard)
            }
            Rigidbody rb = r.Vc.vehicleRigidbody;
            if (rb == null)
            {
                return;
            }
            float ratio = WeightMath.MassRatio(d.StockMass, p.FrontKg, p.RearKg);
            // Mass-property writes re-run the PhysX mass matrix: skip them when
            // the ratio AND the axle split are unchanged (the fields already
            // carry those values from the last apply). Ratio alone is not enough:
            // front ballast and rear ballast share a ratio but different COMs.
            bool sameInput = Mathf.Abs(ratio - r.MassRatio) <= 1e-4f
                && Mathf.Abs(d.LastFrontKg - p.FrontKg) <= 1e-3f
                && Mathf.Abs(d.LastRearKg - p.RearKg) <= 1e-3f;
            if (!sameInput)
            {
                rb.mass = WeightMath.ComputeMass(d.StockMass, p.FrontKg, p.RearKg);
                rb.centerOfMass = WeightMath.ComputeCom(d.StockCom, d.StockMass, d.FrontPoint, d.RearPoint, p.FrontKg, p.RearKg);
                // Diagonal only: a stock off-diagonal tensor is approximated by its
                // diagonal, and inertiaTensorRotation is never touched.
                rb.inertiaTensor = d.StockInertia * ratio;
                d.LastFrontKg = p.FrontKg;
                d.LastRearKg = p.RearKg;
            }
            RescaleSpringsForMass(r, ratio);   // early-outs internally when unchanged

            float frontN, rearN;
            WeightMath.LiftFor(p.FrontKg, p.RearKg, out frontN, out rearN);
            WeightLiftModule m = d.Lift;
            if (m != null)
            {
                // LOD can switch the module off; 0 N targets keep it inert either way.
                if (!m.IsActive)
                {
                    m.VC_Enable(false);
                }
                m.FrontPoint = d.FrontPoint;
                m.RearPoint = d.RearPoint;
                m.FrontLiftN = frontN;
                m.RearLiftN = rearN;
            }
        }

        private static void RestoreWeight(VehicleRecord r)
        {
            WeightData d = r.Weight;
            if (d == null)
            {
                return;
            }
            Rigidbody rb = r.Vc.vehicleRigidbody;
            if (rb != null)
            {
                rb.mass = d.StockMass;
                rb.centerOfMass = d.StockCom;
                rb.inertiaTensor = d.StockInertia;
            }
            RescaleSpringsForMass(r, 1f);
            if (d.Lift != null)
            {
                d.Lift.FrontLiftN = 0f;
                d.Lift.RearLiftN = 0f;
            }
        }

        /// <summary>
        /// Single composition point for mass scaling of the springs. The
        /// suspension category writes spring force absolutely from its captured
        /// baseline, so when suspension is ON this delta-composes over what the
        /// suspension scan just wrote (which carries the old ratio); when
        /// suspension is OFF nothing else writes the springs, so it writes
        /// absolute = baseline x ratio (the scaling survives suspension off).
        /// Weight runs LAST in ApplyLiveCore so both orderings hold.
        /// </summary>
        private static void RescaleSpringsForMass(VehicleRecord r, float newRatio)
        {
            if (Mathf.Abs(newRatio - r.MassRatio) < 1e-4f)
            {
                return;
            }
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                if (SuspensionSettings.Enabled)
                {
                    u.SpringMaxForce *= newRatio / r.MassRatio;
                }
                else
                {
                    u.SpringMaxForce = wk.Value.SpringForce * newRatio;
                }
            }
            r.MassRatio = newRatio;
        }
    }
}
