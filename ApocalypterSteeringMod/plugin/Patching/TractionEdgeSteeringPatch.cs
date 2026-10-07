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
    /// preset without the stability assist, and for raw-input / hold-position /
    /// reverse driving.
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

        // 0.12.0: a vanilla-mimic preset. The stability assist works with every
        // steering preset, including Vanilla; when only the assist is wanted, the
        // pipeline runs under this preset so Vanilla steering still happens —
        // just computed here instead of by NWH (same curves, same rates).
        private static readonly SteeringPreset VanillaLite = new SteeringPreset
        {
            Name = "VanillaLite",
            Label = "VanillaLite",
            UseVehicleCurve = true,
            LockCurve = EditableCurve.Flat(1f),
            ReturnCurve = EditableCurve.Flat(1f),
            TractionClampEnabled = false,
            OppositeLockBoost = 1f,
            LinearityOverride = false,
            LinearityExponent = 1f,
            MaxSteerAngle = 0f,
            SlipLimitMode = SlipLimitMode.Hard,
            SlipLimitStrength = 1f,
            AckermannAmount = 1f
        };

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
            // 0.12.0: the stability assist works with every steering preset, including
            // Vanilla. When it is wanted, a vanilla-mimic preset (VanillaLite) drives
            // the pipeline so the assist still steers; when it is not, Vanilla keeps
            // its pure fall-through.
            AssistsPreset assists = AssistsSettings.Book.ForVehicle(vc != null && vc.gameObject != null ? vc.gameObject.name : "");
            bool wantAssist = AssistsSettings.Enabled
                && assists.StabilityMode != StabilityMode.Off && assists.StabilityStrength > 0.001f;
            bool vanillaPreset = preset == null || preset.IsVanilla;
            if (vanillaPreset && !wantAssist)
            {
                return true;
            }
            SteeringPreset p = vanillaPreset ? VanillaLite : preset;
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
            float maxSteer = p.MaxSteerAngle > 0.1f ? p.MaxSteerAngle : __instance.maximumSteerAngle;
            float smoothTime = __instance.speedSensitiveSmoothingCurve.Evaluate(speedNorm) * p.SmoothingScale;

            // Lock-at-speed: the vehicle's own curve, or the preset's editable one.
            float curveValue = p.UseVehicleCurve
                ? __instance.speedSensitiveSteeringCurve.Evaluate(speedNorm)
                : p.LockCurve.Evaluate(speedNorm);

            float linearity = p.LinearityOverride
                ? Mathf.Pow(Mathf.Abs(steeringInput), p.LinearityExponent)
                : __instance.linearity.Evaluate(Mathf.Abs(steeringInput));

            float target = curveValue * maxSteer * linearity * (steeringInput < 0f ? -1f : 1f);

            // Never ask for more lock than the vehicle has (vehicle curves may exceed 1).
            target = Mathf.Clamp(target, -maxSteer, maxSteer);

            // Sideslip angle beta: velocity direction relative to the nose.
            // Positive = sliding right (rear stepped out to the left). The traction
            // model is only defined above MIN_TRACTION_SPEED, so it is skipped below
            // (the preset still steers there, without the clamp). 0.12.0: the
            // stability assist shares these inputs, so they are also computed when
            // only the assist needs them.
            float bodySlipDeg = 0f;
            float slipLeadDeg = 0f;
            if (forwardVel >= MIN_TRACTION_SPEED
                && (p.TractionClampEnabled || p.OppositeLockBoost > 1f || wantAssist))
            {
                bodySlipDeg = Mathf.Atan2(localVel.x, forwardVel) * Mathf.Rad2Deg;
                // Yaw-rate lead: front-axle slip contribution of the chassis rotation,
                // lead = (a / v) * yawRate with 'a' approximated from the wheelbase.
                float frontAxleOffset = Mathf.Max(0.5f, vc.wheelbase) * FRONT_AXLE_WHEELBASE_FRACTION;
                slipLeadDeg = frontAxleOffset / forwardVel * (rb.angularVelocity.y * Mathf.Rad2Deg);
            }

            if (p.TractionClampEnabled && forwardVel >= MIN_TRACTION_SPEED)
            {
                // Peak-grip limits. Steering beyond these scrubs the front tires:
                // opposite lock grows with the slide; steering into the slide is suppressed.
                // Bounds are clamped to the vehicle's lock BEFORE use. Without this, a slide
                // bigger than (slip window + max lock) makes low > high, and Mathf.Clamp then
                // returns a value beyond maximumSteerAngle.
                float lowLimit = Mathf.Clamp(bodySlipDeg + slipLeadDeg - p.SlipAngleDeg, -maxSteer, maxSteer);
                float highLimit = Mathf.Clamp(bodySlipDeg + slipLeadDeg + p.SlipAngleDeg, -maxSteer, maxSteer);

                // 0.12.0: slip-limit mode + strength (s). Hard@1 is the 0.11.3 brick
                // wall exactly; s=0 disables the clamp. Blend mixes the clamped value
                // into the target; Pushback reflects overshoot back inside the window.
                float s = p.SlipLimitStrength;
                switch (p.SlipLimitMode)
                {
                    case SlipLimitMode.Blend:
                        target = Mathf.Lerp(target, Mathf.Clamp(target, lowLimit, highLimit), s);
                        break;
                    case SlipLimitMode.Pushback:
                        if (target > highLimit)
                        {
                            target = Mathf.Clamp(highLimit - s * (target - highLimit), lowLimit, highLimit);
                        }
                        else if (target < lowLimit)
                        {
                            target = Mathf.Clamp(lowLimit + s * (lowLimit - target), lowLimit, highLimit);
                        }
                        break;
                    default: // Hard
                        target = Mathf.Clamp(target, Mathf.Lerp(-maxSteer, lowLimit, s), Mathf.Lerp(maxSteer, highLimit, s));
                        break;
                }
            }

            // 0.12.0: stability assist — shifts the steer target against the slide
            // (counter-steer), the yaw rate (dampen) or both, scaled by strength.
            // Applied after the slip limit so the rate limiter can't kill counter-steer.
            if (wantAssist && forwardVel >= MIN_TRACTION_SPEED)
            {
                float assistDeg;
                switch (assists.StabilityMode)
                {
                    case StabilityMode.CounterSteer: assistDeg = -bodySlipDeg; break;
                    case StabilityMode.YawDampen: assistDeg = -slipLeadDeg; break;
                    default: assistDeg = -(bodySlipDeg + slipLeadDeg); break;   // Both
                }
                target += assistDeg * assists.StabilityStrength;
                target = Mathf.Clamp(target, -maxSteer, maxSteer);
            }

            // Vanilla smoothing, with the vehicle's configured rate limit
            // (boosted while catching a slide). The game-steering-speed factor is
            // skipped for the vanilla mimic (0.12.0): it is meant for the mod's
            // own pipeline, not for scaling vanilla rates.
            float rateLimit = __instance.degreesPerSecondLimit * p.RateMultiplier;
            if (SteeringSettings.MatchGameSteeringSpeed && !vanillaPreset)
            {
                rateLimit *= SteeringSettings.GameSteeringSpeedFactor;
            }

            bool oppositeLock = (target > 0f && bodySlipDeg > OPPOSITE_LOCK_SLIP_THRESHOLD)
                             || (target < 0f && bodySlipDeg < -OPPOSITE_LOCK_SLIP_THRESHOLD);
            if (oppositeLock)
            {
                rateLimit *= p.OppositeLockBoost;
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
                rateLimit *= p.ReturnCurve.Evaluate(speedNorm);
            }

            __instance.angle = Mathf.MoveTowards(__instance.angle, smoothedTarget, rateLimit * vc.fixedDeltaTime);

            // Apply Ackermann geometry, blended per the preset's amount (0.12.0).
            ApplyWheelAngles(__instance, p.AckermannAmount);

            return false;
        }

        // Copy of NWH's wheel-angle application loop (Steering.CalculateSteerAngles),
        // so steer coefficients (4WS), Ackermann and externally added angle (motorcycle
        // balancing) behave like vanilla. 0.12.0: the Ackermann geometry is blended per
        // the preset's AckermannAmount (1 = the game's own geometry, 0 = parallel wheels).
        private static void ApplyWheelAngles(Steering steering, float amount)
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

                    // Blend toward the game's geometry: amount 1 = the vehicle's own
                    // Ackermann (up to float rounding), amount 0 = both wheels parallel.
                    float rightDeg = Mathf.Lerp(baseAngle, rightAngle * 57.29578f, amount);
                    float leftDeg = Mathf.Lerp(baseAngle, leftAngle * 57.29578f, amount);

                    if (baseAngle < 0f)
                    {
                        wheelGroup.RightWheel.wheelUAPI.SteerAngle = rightDeg;
                        wheelGroup.LeftWheel.wheelUAPI.SteerAngle = leftDeg;
                    }
                    else
                    {
                        wheelGroup.LeftWheel.wheelUAPI.SteerAngle = leftDeg;
                        wheelGroup.RightWheel.wheelUAPI.SteerAngle = rightDeg;
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
