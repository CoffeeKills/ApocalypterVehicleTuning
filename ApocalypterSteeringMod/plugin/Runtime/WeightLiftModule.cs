using NWH.VehiclePhysics2;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// NWH module the tuner onboards to apply "balloon" lift (0.10.0). Not a
    /// tuning component itself: the tuner writes newton targets and axle points
    /// into the public fields and the module applies the forces each physics
    /// tick, exactly like the shipped AerodynamicsModule does for downforce.
    /// Inert at 0 N; onboarded once and never removed (parked, per the aero
    /// precedent). Allocation-free per tick.
    /// </summary>
    public sealed class WeightLiftModule : VehicleComponent
    {
        public float FrontLiftN;
        public float RearLiftN;
        public Vector3 FrontPoint;
        public Vector3 RearPoint;

        public override void VC_FixedUpdate()
        {
            base.VC_FixedUpdate();
            if (vehicleController == null || vehicleController.vehicleRigidbody == null)
            {
                return;
            }
            Rigidbody rb = vehicleController.vehicleRigidbody;
            if (FrontLiftN > 1e-4f)
            {
                rb.AddForceAtPosition(Vector3.up * FrontLiftN, vehicleController.transform.TransformPoint(FrontPoint));
            }
            if (RearLiftN > 1e-4f)
            {
                rb.AddForceAtPosition(Vector3.up * RearLiftN, vehicleController.transform.TransformPoint(RearPoint));
            }
        }
    }
}
