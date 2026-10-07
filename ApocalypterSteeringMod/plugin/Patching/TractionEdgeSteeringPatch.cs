using ApocalypterSteeringMod.Settings;
using HarmonyLib;
using NWH.VehiclePhysics2;
using UnityEngine;

namespace ApocalypterSteeringMod.Patching
{
    /// <summary>
    /// Replaces NWH's CalculateSteerAngles with the same pipeline the game uses
    /// (vehicle input linearity, speed-sensitive curve, smoothing, configured
    /// deg/s limit) plus a traction-edge clamp parameterized by the active
    /// SteeringPreset. Falls through to vanilla when disabled, for the Vanilla
    /// preset, and for raw-input / hold-position / reverse driving.
    /// </summary>
    [HarmonyPatch(typeof(Steering), "CalculateSteerAngles")]
    public static class TractionEdgeSteeringPatch
    {
        // Minimum forward speed (m/s) at which the traction model engages.
        private const float MIN_TRACTION_SPEED = 1.5f;

        // Sideslip angle (deg) beyond which steer input counts as opposite lock.
        private const float OPPOSITE_LOCK_SLIP_THRESHOLD = 5f;

        // Front-axle-to-CG distance approximation for the yaw slip lead (a/v factor).
        private const float FRONT_AXLE_WHEELBASE_FRACTION = 0.45f;

        // Cached accessors for Steering's private smoothing state. Resolved once,
        // instead of allocating Traverse objects every physics tick.
        private static readonly AccessTools.FieldRef<Steering, float> TargetAngleRef =
            AccessTools.FieldRefAccess<Steering, float>("_targetAngle");

        private static readonly AccessTools.FieldRef<Steering, float> SteerVelocityRef =
            AccessTools.FieldRefAccess<Steering, float>("_steerVelocity");

        [HarmonyPrefix]
        public static bool Prefix(Steering __instance)
        {
            if (!SteeringSettings.Enabled)
            {
                return true;
            }
            VehicleController vc = __instance.vehicleController;
            // 0.9.0: a vehicle with its own saved tune uses it even when the global is
            // Vanilla; everyone else falls back to the global active preset.
            SteeringPreset preset = SteeringSettings.Book.ForVehicle(vc != null && vc.gameObject != null ? vc.gameObject.name : "");
            if (preset == null || preset.IsVanilla)
            {
                return true;
            }
            Rigidbody rb = vc.vehicleRigidbody;
            float steeringInput = vc.input.Steering;

            if (rb == null || __instance.useRawInput)
            {
                return true;
            }

            Vector3 localVel = vc.transform.InverseTransformDirection(rb.velocity);
            float forwardVel = localVel.z;

            // Mirror vanilla's guard: with returnToCenter disabled on the vehicle, the
            // wheels hold position on release. This applies to every preset, including
            // hold curves (0.4.0 skipped it there, so a hold curve's return ramp
            // straightened a vehicle that was built never to self-center).
            if (!__instance.returnToCenter && steeringInput > -0.04f && steeringInput < 0.04f)
            {
                return true;
            }

            // True reverse stays vanilla for every preset (0.2.0 parked parking
            // maneuvers there); a gentle backward roll is treated as stopped.
            // 0.12.0: the old below-1.5-m/s vanilla fall-through for flat-1 return
            // curves is gone — the preset's full lock now applies while stationary
            // and at creep speeds too (the reported "stationary steering is default"
            // bug). Safe at rest: the traction clamp self-gates on
            // forwardVel >= MIN_TRACTION_SPEED, so nothing below it is affected.
            if (forwardVel < -MIN_TRACTION_SPEED)
            {
                return true;
            }

            float speedNorm = vc.Speed / 50f;
            // 0.8.0: the preset's MaxSteerAngle overrides the vehicle's own lock (0 = the
            // vehicle's own; high values for drift tunes). The whole pipeline — curve,
            // linearity, traction bounds, rate limiting — uses this cap.
            float maxSteer = preset.MaxSteerAngle > 0.1f ? preset.MaxSteerAngle : __instance.maximumSteerAngle;
            float smoothTime = __instance.speedSensitiveSmoothingCurve.Evaluate(speedNorm) * preset.SmoothingScale;

            // Lock-at-speed: the vehicle's own curve, or the preset's editable one.
            float curveValue = preset.UseVehicleCurve
                ? __instance.speedSensitiveSteeringCurve.Evaluate(speedNorm)
                : preset.LockCurve.Evaluate(speedNorm);

            float linearity = preset.LinearityOverride
                ? Mathf.Pow(Mathf.Abs(steeringInput), preset.LinearityExponent)
                : __instance.linearity.Evaluate(Mathf.Abs(steeringInput));

            float target = curveValue * maxSteer * linearity * (steeringInput < 0f ? -1f : 1f);

            // Never ask for more lock than the vehicle has (vehicle curves may exceed 1).
            target = Mathf.Clamp(target, -maxSteer, maxSteer);

            // Sideslip angle beta: velocity direction relative to the nose.
            // Positive = sliding right (rear stepped out to the left). The traction
            // model is only defined above MIN_TRACTION_SPEED, so it is skipped below
            // (the preset still steers there, without the clamp).
            float bodySlipDeg = 0f;
            if (forwardVel >= MIN_TRACTION_SPEED
                && (preset.TractionClampEnabled || preset.OppositeLockBoost > 1f))
            {
                bodySlipDeg = Mathf.Atan2(localVel.x, forwardVel) * Mathf.Rad2Deg;
            }

            if (preset.TractionClampEnabled && forwardVel >= MIN_TRACTION_SPEED)
            {
                // Yaw-rate lead: front-axle slip contribution of the chassis rotation,
                // lead = (a / v) * yawRate with 'a' approximated from the wheelbase.
                float frontAxleOffset = Mathf.Max(0.5f, vc.wheelbase) * FRONT_AXLE_WHEELBASE_FRACTION;
                float slipLeadDeg = frontAxleOffset / forwardVel * (rb.angularVelocity.y * Mathf.Rad2Deg);

                // Peak-grip limits. Steering beyond these scrubs the front tires:
                // opposite lock grows with the slide; steering into the slide is suppressed.
                // Bounds are clamped to the vehicle's lock BEFORE use. Without this, a slide
                // bigger than (slip window + max lock) makes low > high, and Mathf.Clamp then
                // returns a value beyond maximumSteerAngle.
                float lowLimit = Mathf.Clamp(bodySlipDeg + slipLeadDeg - preset.SlipAngleDeg, -maxSteer, maxSteer);
                float highLimit = Mathf.Clamp(bodySlipDeg + slipLeadDeg + preset.SlipAngleDeg, -maxSteer, maxSteer);
                target = Mathf.Clamp(target, lowLimit, highLimit);
            }

            // Vanilla smoothing, with the vehicle's configured rate limit
            // (boosted while catching a slide).
            float rateLimit = __instance.degreesPerSecondLimit * preset.RateMultiplier;
            if (SteeringSettings.MatchGameSteeringSpeed)
            {
                rateLimit *= SteeringSettings.GameSteeringSpeedFactor;
            }

            bool oppositeLock = (target > 0f && bodySlipDeg > OPPOSITE_LOCK_SLIP_THRESHOLD)
                             || (target < 0f && bodySlipDeg < -OPPOSITE_LOCK_SLIP_THRESHOLD);
            if (oppositeLock)
            {
                rateLimit *= preset.OppositeLockBoost;
            }

            float steerVelocity = SteerVelocityRef(__instance);
            float smoothedTarget = Mathf.SmoothDamp(TargetAngleRef(__instance), target, ref steerVelocity, smoothTime);
            SteerVelocityRef(__instance) = steerVelocity;
            TargetAngleRef(__instance) = smoothedTarget;

            // Unwinding toward center runs at ReturnCurve(speed) of the steer-in
            // rate. Flat 1 = symmetric (unchanged feel). A curve that starts at 0
            // makes the wheels hold their angle when stopped; as speed builds the
            // same line straightens them out. Winding on always uses the full rate,
            // and so does steering across center toward the other side: that is the
            // driver steering, not the wheel returning (with a hold curve the wheels
            // otherwise froze at rest whenever the opposite input was smaller than the
            // held angle, and counter-steer through center ran at the return rate).
            float current = __instance.angle;
            if (Mathf.Abs(smoothedTarget) < Mathf.Abs(current) && smoothedTarget * current >= 0f)
            {
                rateLimit *= preset.ReturnCurve.Evaluate(speedNorm);
            }

            __instance.angle = Mathf.MoveTowards(__instance.angle, smoothedTarget, rateLimit * vc.fixedDeltaTime);

            // Apply Ackermann geometry exactly as the game does.
            ApplyWheelAngles(__instance);

            return false;
        }

        // Verbatim copy of NWH's wheel-angle application loop (Steering.CalculateSteerAngles),
        // so steer coefficients (4WS), Ackermann and externally added angle (motorcycle
        // balancing) behave identically to vanilla.
        private static void ApplyWheelAngles(Steering steering)
        {
            foreach (var wheelGroup in steering.vehicleController.powertrain.wheelGroups)
            {
                float baseAngle = (steering.angle + steering.externallyAddedAngle) * wheelGroup.steerCoefficient;

                if (wheelGroup.Wheels.Count == 2 && steering.vehicleController.wheelbase > 0.001f && wheelGroup.addAckerman)
                {
                    float rad = baseAngle * 0.017453292f;
                    float sin = Mathf.Sin(rad);
                    float cos = Mathf.Cos(rad);
                    float rightAngle = Mathf.Atan(4f * wheelGroup.trackWidth * sin / (2f * steering.vehicleController.wheelbase * cos - wheelGroup.trackWidth * sin));
                    float leftAngle = Mathf.Atan(4f * wheelGroup.trackWidth * sin / (2f * steering.vehicleController.wheelbase * cos + wheelGroup.trackWidth * sin));

                    if (baseAngle < 0f)
                    {
                        wheelGroup.RightWheel.wheelUAPI.SteerAngle = rightAngle * 57.29578f;
                        wheelGroup.LeftWheel.wheelUAPI.SteerAngle = leftAngle * 57.29578f;
                    }
                    else
                    {
                        wheelGroup.LeftWheel.wheelUAPI.SteerAngle = leftAngle * 57.29578f;
                        wheelGroup.RightWheel.wheelUAPI.SteerAngle = rightAngle * 57.29578f;
                    }
                }
                else
                {
                    foreach (var wheelComponent in wheelGroup.Wheels)
                    {
                        wheelComponent.wheelUAPI.SteerAngle = baseAngle;
                    }
                }
            }
        }
    }
}
