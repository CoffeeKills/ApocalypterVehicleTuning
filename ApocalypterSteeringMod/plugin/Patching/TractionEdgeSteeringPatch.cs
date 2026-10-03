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
    /// preset, and for raw-input / hold-position / stationary / reverse driving.
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
            SteeringPreset preset = SteeringSettings.ActivePreset;
            if (preset == null || preset.IsVanilla)
            {
                return true;
            }

            VehicleController vc = __instance.vehicleController;
            Rigidbody rb = vc.vehicleRigidbody;
            float steeringInput = vc.input.Steering;

            // Mirror vanilla's guards so configured behavior is never overridden:
            // raw-input vehicles steer directly, and with returnToCenter disabled
            // the wheels hold their position when input is released.
            if (rb == null
                || __instance.useRawInput
                || (!__instance.returnToCenter && steeringInput > -0.04f && steeringInput < 0.04f))
            {
                return true;
            }

            // Stationary and reverse: leave parking maneuvers to vanilla.
            Vector3 localVel = vc.transform.InverseTransformDirection(rb.velocity);
            float forwardVel = localVel.z;
            if (forwardVel < MIN_TRACTION_SPEED)
            {
                return true;
            }

            float speedNorm = vc.Speed / 50f;
            float maxSteer = __instance.maximumSteerAngle;
            float smoothTime = __instance.speedSensitiveSmoothingCurve.Evaluate(speedNorm) * preset.SmoothingScale;

            // Speed curve: the preset's own, or the vehicle's. SpeedCurveScale applies to both
            // so the "Steering at speed" slider behaves the same on every preset.
            float curveValue = (preset.CurveOverride && preset.SpeedCurve != null
                ? preset.SpeedCurve.Evaluate(speedNorm)
                : __instance.speedSensitiveSteeringCurve.Evaluate(speedNorm)) * preset.SpeedCurveScale;

            float linearity = preset.LinearityOverride
                ? Mathf.Pow(Mathf.Abs(steeringInput), preset.LinearityExponent)
                : __instance.linearity.Evaluate(Mathf.Abs(steeringInput));

            float target = curveValue * maxSteer * linearity * (steeringInput < 0f ? -1f : 1f);

            // Never ask for more lock than the vehicle has (SpeedCurveScale can be > 1).
            target = Mathf.Clamp(target, -maxSteer, maxSteer);

            // Sideslip angle beta: velocity direction relative to the nose.
            // Positive = sliding right (rear stepped out to the left).
            float bodySlipDeg = 0f;
            if (preset.TractionClampEnabled || preset.OppositeLockBoost > 1f)
            {
                bodySlipDeg = Mathf.Atan2(localVel.x, forwardVel) * Mathf.Rad2Deg;
            }

            if (preset.TractionClampEnabled)
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

            // ETS feel: unwinding toward center is slower than winding on — the
            // wheel stays where it was put and eases back instead of snapping.
            // Only when the target is smaller than the current angle; held input
            // keeps the full wind rate.
            if (preset.CenterReturnScale < 1f && Mathf.Abs(smoothedTarget) < Mathf.Abs(__instance.angle))
            {
                rateLimit *= preset.CenterReturnScale;
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
