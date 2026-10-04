using System;
using ApocalypterSteeringMod.Patching;
using ApocalypterSteeringMod.Settings;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using NWH.VehiclePhysics2.Powertrain.Wheel;
using UnityEngine;
public sealed class FakeWheel : WheelUAPI
{
    public override float SteerAngle { get; set; }
    public override float SpringMaxLength { get; set; }
    public override float SpringMaxForce { get; set; }
    public override float DamperBumpRate { get; set; }
    public override float DamperReboundRate { get; set; }
    public override float LongitudinalFrictionGrip { get; set; }
    public override float LateralFrictionGrip { get; set; }
    public override float LongitudinalFrictionStiffness { get; set; }
    public override float LateralFrictionStiffness { get; set; }
    public override bool IsGrounded { get { return true; } }
    public override float LongitudinalSlip { get { return 0f; } }
}

public static class PrefixTests
{
    private static int _fail, _pass;

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + what); }
        else { _fail++; Console.WriteLine("  FAIL " + what); }
    }

    private static bool Near(float a, float b, float eps = 1e-3f) { return Math.Abs(a - b) <= eps; }

    public static int Main()
    {
        Console.WriteLine("Steering prefix: lock is never exceeded");
        TestPrefix();
        Console.WriteLine(_pass + " passed, " + _fail + " failed");
        return _fail == 0 ? 0 : 1;
    }

    private static VehicleController MakeCar(out FakeWheel[] wheels, float pivotZ)
    {
        var vc = new VehicleController();
        vc.vehicleRigidbody = new Rigidbody();
        vc.wheelbase = 2.6f;
        vc.fixedDeltaTime = 0.02f;
        wheels = new FakeWheel[4];
        // Two axles; the vehicle pivot sits at pivotZ (e.g. on the rear axle, which used to break front/rear detection).
        float[] z = { 1.3f, 1.3f, -1.3f, -1.3f };
        for (int g = 0; g < 2; g++)
        {
            var group = new WheelGroup { antiRollBarForce = g == 0 ? 5000f : 0f, addAckerman = false, steerCoefficient = g == 0 ? 1f : 0f };
            for (int i = 0; i < 2; i++)
            {
                var w = new FakeWheel { SpringMaxForce = 30000f, SpringMaxLength = 0.3f, DamperBumpRate = 3000f, DamperReboundRate = 3500f };
                w.transform.position = new Vector3(0f, 0f, z[g * 2 + i] - pivotZ);
                wheels[g * 2 + i] = w;
                group.Wheels.Add(new WheelComponent { wheelUAPI = w });
            }
            vc.powertrain.wheelGroups.Add(group);
        }
        return vc;
    }

    private static void TestPrefix()
    {
        SteeringSettings.Enabled = true;
        SteeringPreset.Custom.ResetToDefaults();
        SteeringPreset.Custom.UseVehicleCurve = true;
        SteeringSettings.Select(SteeringPreset.Custom);
        SteeringSettings.MatchGameSteeringSpeed = false;

        VehicleController vc = MakeCar(out FakeWheel[] wheels, 0f);
        var s = new Steering
        {
            vehicleController = vc,
            maximumSteerAngle = 30f,
            degreesPerSecondLimit = 100000f,   // let the angle reach its target in one tick
            linearity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)),
            speedSensitiveSteeringCurve = new AnimationCurve(new Keyframe(0f, 2f), new Keyframe(1f, 2f)),   // exceeds 1 -> the max-lock cap is load-bearing
            speedSensitiveSmoothingCurve = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(1f, 0.05f))
        };
        vc.Speed = 20f;

        try
        {
            // Full right input, vehicle curve 2 -> 60 deg requested on a 30 deg car.
            vc.input.Steering = 1f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 20f);
            vc.vehicleRigidbody.angularVelocity = new Vector3(0f, 0f, 0f);
            SteeringPreset.Custom.TractionClampEnabled = false;   // isolate the max-lock cap
            TractionEdgeSteeringPatch.Prefix(s);
            SteeringPreset.Custom.TractionClampEnabled = true;
            Check(s.angle <= 30.001f && s.angle > 29f, "vehicle curve above 1 still capped at max lock (angle " + s.angle.ToString("0.0") + ")");

            // Huge slide: velocity points 60 deg to the right. Old code: lo = 60-8.5 = 51.5 > hi = 30 -> 51.5 deg.
            s.angle = 0f;
            vc.input.Steering = 1f;
            vc.vehicleRigidbody.velocity = new Vector3(34.6f, 0f, 20f);
            TractionEdgeSteeringPatch.Prefix(s);
            Check(Math.Abs(s.angle) <= 30.001f, "large slide stays within +/-30 (angle " + s.angle.ToString("0.0") + ")");
            Check(Math.Abs(wheels[0].SteerAngle) <= 30.001f, "wheel angle within lock");

            // Partial input during the same slide: inverted clamp bounds used to return 51.5 deg here.
            s.angle = 0f;
            vc.input.Steering = 0.3f;
            vc.vehicleRigidbody.velocity = new Vector3(34.6f, 0f, 20f);
            TractionEdgeSteeringPatch.Prefix(s);
            Check(Math.Abs(s.angle) <= 30.001f, "30% input in a large slide stays within +/-30 (angle " + s.angle.ToString("0.0") + ")");

            // Same in the other direction.
            s.angle = 0f;
            vc.input.Steering = -1f;
            vc.vehicleRigidbody.velocity = new Vector3(-34.6f, 0f, 20f);
            TractionEdgeSteeringPatch.Prefix(s);
            Check(Math.Abs(s.angle) <= 30.001f, "large left slide stays within +/-30 (angle " + s.angle.ToString("0.0") + ")");

            // Center return curve: unwinding toward center is scaled, winding on is not.
            SteeringPreset.Custom.ReturnCurve = EditableCurve.Flat(0.25f);
            s.degreesPerSecondLimit = 100f;                 // 2 deg per tick at full rate
            s.angle = 10f;
            vc.input.Steering = 0f;                         // release: target -> 0
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 20f);
            TractionEdgeSteeringPatch.Prefix(s);
            Check(s.angle > 9.49f && s.angle < 10f, "release unwinds at the flat-0.25 rate (angle " + s.angle.ToString("0.00") + ")");
            s.angle = 0f;
            vc.input.Steering = 1f;
            TractionEdgeSteeringPatch.Prefix(s);
            Check(s.angle >= 1.9f, "winding on still uses the full rate (angle " + s.angle.ToString("0.00") + ")");
            SteeringPreset.Custom.ReturnCurve = EditableCurve.Flat(1f);
            s.degreesPerSecondLimit = 100000f;

            // Unwind rate follows the return curve vs speed: ramp (0,0)(0.5,0.5)(1,1).
            SteeringPreset.Custom.ReturnCurve = EditableCurve.FromPoints(0f, 0f, 0.5f, 0.5f, 1f, 1f);
            s.degreesPerSecondLimit = 100f;
            vc.Speed = 12.5f;                               // norm 0.25 -> curve 0.25 -> 0.5 deg/tick
            s.angle = 10f;
            vc.input.Steering = 0f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 12.5f);
            TractionEdgeSteeringPatch.Prefix(s);
            Check(s.angle > 9.49f && s.angle < 9.51f, "unwind at norm 0.25 uses the curve's half rate (angle " + s.angle.ToString("0.00") + ")");
            vc.Speed = 25f;                                 // norm 0.5 -> curve 0.5 -> 1 deg/tick
            s.angle = 10f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 25f);
            TractionEdgeSteeringPatch.Prefix(s);
            Check(s.angle > 8.99f && s.angle < 9.01f, "unwind at norm 0.5 uses the curve's rate (angle " + s.angle.ToString("0.00") + ")");

            // Hold at rest: ramp curve + no speed -> the prefix runs and the angle holds.
            s.angle = 10f;
            vc.input.Steering = 0f;
            vc.Speed = 0f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 0f);
            Check(!TractionEdgeSteeringPatch.Prefix(s) && s.angle > 9.999f,
                "hold-at-rest: prefix runs and the wheels hold their angle (angle " + s.angle.ToString("0.00") + ")");

            // Low-speed steering still works with a hold curve (no clamp, full wind rate).
            vc.input.Steering = 1f;
            vc.Speed = 0.5f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 0.5f);
            Check(!TractionEdgeSteeringPatch.Prefix(s) && s.angle > 10f,
                "low-speed input steers with a hold curve (angle " + s.angle.ToString("0.00") + ")");

            // 0.5.0: counter-steering across center at rest is the driver steering, not
            // the wheel returning. 0.4.0 applied the hold rate (0) and froze the wheels
            // at +10 for any left input smaller than the held angle.
            s.angle = 10f;
            vc.input.Steering = -0.1f;                      // linear, vehicle curve 2 x 30 -> target -6 (|6| < |10|)
            vc.Speed = 0f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 0f);
            Check(!TractionEdgeSteeringPatch.Prefix(s) && s.angle < 9.5f,
                "hold curve: small opposite input still steers across center at rest (angle " + s.angle.ToString("0.00") + ")");
            s.angle = 10f;
            vc.input.Steering = 0.1f;                       // same side, smaller -> still a (held) return
            TractionEdgeSteeringPatch.Prefix(s);
            Check(s.angle > 9.999f, "hold curve: easing off on the same side still holds at rest (angle " + s.angle.ToString("0.00") + ")");

            // 0.5.0: a vehicle built with returnToCenter = false keeps vanilla's hold on
            // release, also under a hold curve (0.4.0 ran the return ramp instead).
            s.returnToCenter = false;
            s.angle = 10f;
            vc.input.Steering = 0f;
            vc.Speed = 25f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 25f);
            Check(TractionEdgeSteeringPatch.Prefix(s) && Near(s.angle, 10f),
                "returnToCenter=false + hold curve: release falls through to vanilla's hold");
            s.returnToCenter = true;
            vc.Speed = 0.5f;

            // True reverse still falls through to vanilla even with a hold curve.
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, -5f);
            Check(TractionEdgeSteeringPatch.Prefix(s), "true reverse falls through with a hold curve");
            SteeringPreset.Custom.ReturnCurve = EditableCurve.Flat(1f);

            // Flat-1 return keeps the old low-speed guard: below 1.5 m/s -> vanilla.
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 0.5f);
            Check(TractionEdgeSteeringPatch.Prefix(s), "flat-1 return keeps the vanilla low-speed fall-through");
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 20f);
            vc.Speed = 20f;
            s.degreesPerSecondLimit = 100000f;

            // Flat-1 (every non-hold preset): counter-steer across center keeps the full
            // rate, exactly as before (the 0.5.0 crossing rule changes nothing at rate x1).
            s.degreesPerSecondLimit = 100f;
            s.angle = 10f;
            vc.input.Steering = -0.1f;
            TractionEdgeSteeringPatch.Prefix(s);
            Check(Near(s.angle, 8f), "flat-1 return: crossing toward center runs at the full rate (angle " + s.angle.ToString("0.00") + ")");
            s.degreesPerSecondLimit = 100000f;

            // Vanilla preset falls through.
            SteeringSettings.Select(SteeringPreset.Vanilla);
            Check(TractionEdgeSteeringPatch.Prefix(s), "Vanilla preset -> vanilla code runs");
            SteeringSettings.Select(SteeringPreset.Custom);
        }
        catch (Exception ex)
        {
            Check(false, "prefix threw: " + ex);
        }
        SteeringPreset.Custom.ResetToDefaults();
    }

}
