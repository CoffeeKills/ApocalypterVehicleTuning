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
        SteeringPreset.Custom.SpeedCurveScale = 2f;   // asks for more than full lock
        SteeringSettings.Select(SteeringPreset.Custom);
        SteeringSettings.MatchGameSteeringSpeed = false;

        VehicleController vc = MakeCar(out FakeWheel[] wheels, 0f);
        var s = new Steering
        {
            vehicleController = vc,
            maximumSteerAngle = 30f,
            degreesPerSecondLimit = 100000f,   // let the angle reach its target in one tick
            linearity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)),
            speedSensitiveSteeringCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)),
            speedSensitiveSmoothingCurve = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(1f, 0.05f))
        };
        vc.Speed = 20f;

        try
        {
            // Full right input, curve scale 2 -> 60 deg requested on a 30 deg car.
            vc.input.Steering = 1f;
            vc.vehicleRigidbody.velocity = new Vector3(0f, 0f, 20f);
            vc.vehicleRigidbody.angularVelocity = new Vector3(0f, 0f, 0f);
            SteeringPreset.Custom.TractionClampEnabled = false;   // isolate the max-lock cap
            TractionEdgeSteeringPatch.Prefix(s);
            SteeringPreset.Custom.TractionClampEnabled = true;
            Check(s.angle <= 30.001f && s.angle > 29f, "curve scale x2 still capped at max lock (angle " + s.angle.ToString("0.0") + ")");

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
