using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Modules.Aerodynamics;
using NWH.VehiclePhysics2.Powertrain;
using NWH.VehiclePhysics2.Powertrain.Wheel;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>Per-system apply/restore. All hot paths are allocation-free.</summary>
    public sealed partial class VehicleTuner
    {
        // ---------------------------------------------------------------- suspension

        private void ApplyAllSuspension()
        {
            TargetPass(AppliedCat.Suspension, ApplySuspension, RestoreSuspension);
        }

        private void RestoreAllSuspension()
        {
            RestorePass(AppliedCat.Suspension, RestoreSuspension);
        }

        private static void ApplySuspension(VehicleRecord r)
        {
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                WheelData b = wk.Value;
                u.SpringMaxForce = b.SpringForce * SuspensionSettings.Spring(b.IsFront);
                u.SpringMaxLength = b.SpringLength * SuspensionSettings.RideHeight(b.IsFront);
                u.DamperBumpRate = b.BumpRate * SuspensionSettings.Bump(b.IsFront);
                u.DamperReboundRate = b.ReboundRate * SuspensionSettings.Rebound(b.IsFront);
            }
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g == null)
                {
                    continue;
                }
                g.antiRollBarForce = gk.Value.ArbForce * SuspensionSettings.Arb(gk.Value.IsFront);
            }
        }

        private static void RestoreSuspension(VehicleRecord r)
        {
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                u.SpringMaxForce = wk.Value.SpringForce;
                u.SpringMaxLength = wk.Value.SpringLength;
                u.DamperBumpRate = wk.Value.BumpRate;
                u.DamperReboundRate = wk.Value.ReboundRate;
            }
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g != null)
                {
                    g.antiRollBarForce = gk.Value.ArbForce;
                }
            }
        }

        // ---------------------------------------------------------------- aero

        /// <summary>Find the vehicle's AerodynamicsModule (incl. one we onboarded earlier).</summary>
        private static AeroData CaptureAero(VehicleController vc)
        {
            var d = new AeroData();
            AerodynamicsModule m = null;
            if (vc.moduleManager != null && vc.moduleManager.Components != null)
            {
                for (int i = 0; i < vc.moduleManager.Components.Count; i++)
                {
                    if (vc.moduleManager.Components[i] is AerodynamicsModule a)
                    {
                        m = a;
                        break;
                    }
                }
            }
            if (m == null)
            {
                return d;
            }
            d.Module = m;
            d.WasSimulateDrag = m.simulateDrag;
            d.WasSimulateDownforce = m.simulateDownforce;
            d.FrontalCd = m.frontalCd;
            d.SideCd = m.sideCd;
            d.MaxDownforceSpeed = m.maxDownforceSpeed;
            if (m.downforcePoints != null)
            {
                d.Points = new DownforcePoint[m.downforcePoints.Count];
                for (int i = 0; i < m.downforcePoints.Count; i++)
                {
                    DownforcePoint src = m.downforcePoints[i];
                    d.Points[i] = src != null ? new DownforcePoint { maxForce = src.maxForce, position = src.position } : null;
                }
            }
            else
            {
                d.Points = new DownforcePoint[0];
            }
            return d;
        }

        private void ApplyAllAero()
        {
            AeroPreset p = AeroSettings.ActivePreset ?? AeroPreset.Stock;
            TargetPass(AppliedCat.Aero, ApplyAero, p, RestoreAero);
        }

        private void RestoreAllAero()
        {
            RestorePass(AppliedCat.Aero, RestoreAero);
        }

        /// <summary>
        /// Drag factor for a vehicle that shipped WITHOUT aero (an onboarded module).
        /// Its stock drag is none, so "x factor on stock" can only mean the excess:
        /// default-module Cd x (DragScale - 1). Continuous at 1 (Stock, Street and any
        /// drag x1.0 or below add nothing), and an onboarded module never simulates
        /// downforce (no point synthesis), so downforce factors cannot onboard one.
        /// 0.4.0 used Cd x DragScale whenever ANY factor differed from 1: Street
        /// ("stock drag") gave every aero-less vehicle a full 0.35 Cd, and a drag
        /// factor below 1 ADDED drag to them.
        /// </summary>
        internal static float OnboardedDragFactor(AeroPreset p)
        {
            float extra = p.DragScale - 1f;
            return extra > 1e-4f ? extra : 0f;
        }

        private static void ApplyAero(VehicleRecord r, AeroPreset p)
        {
            AeroData d = r.Aero;
            AerodynamicsModule m = d.Module;
            float onboardedDrag = OnboardedDragFactor(p);
            if (m == null)
            {
                // Onboard only when there is extra drag to add, so no preset that
                // leaves drag at or below stock ever adds a module to a vehicle.
                if (onboardedDrag <= 0f || r.Vc.moduleManager == null)
                {
                    return;
                }
                m = new AerodynamicsModule();
                r.Vc.moduleManager.AddAndOnboardNewComponent(m);
                d.Module = m;
                d.Onboarded = true;
                // Baseline of an onboarded module = inert (the vehicle had no aero).
                d.WasSimulateDrag = false;
                d.WasSimulateDownforce = false;
                d.FrontalCd = m.frontalCd;
                d.SideCd = m.sideCd;
                d.MaxDownforceSpeed = m.maxDownforceSpeed;
                d.Points = new DownforcePoint[0];
            }

            float dragFactor = p.DragScale;
            if (d.Onboarded)
            {
                // No extra drag requested while enabled: the vehicle must behave as
                // shipped, i.e. without aero. Park the module exactly like a restore.
                if (onboardedDrag <= 0f)
                {
                    ParkOnboarded(d);
                    return;
                }
                dragFactor = onboardedDrag;
                // IsActive, not state.isEnabled: onboarding can load isEnabled = true from
                // the vehicle's state settings without initialising the module, and an
                // uninitialised module never runs. VC_Enable initialises it.
                if (!m.IsActive)
                {
                    m.VC_Enable(false);
                }
                m.simulateDrag = true;
                m.simulateDownforce = false;   // no downforce-point synthesis
            }
            else
            {
                // Shipped module: keep its own switches. A designer who turned drag or
                // downforce off gets the same vehicle on Stock, and scaling never turns
                // a disabled effect on.
                m.simulateDrag = d.WasSimulateDrag;
                m.simulateDownforce = d.WasSimulateDownforce;
            }

            m.frontalCd = Mathf.Clamp(d.FrontalCd * dragFactor, 0f, 1f);
            m.sideCd = Mathf.Clamp(d.SideCd * dragFactor, 0f, 2f);
            m.maxDownforceSpeed = d.MaxDownforceSpeed * p.MaxDownforceSpeedScale;
            List<DownforcePoint> points = m.downforcePoints;
            if (points != null)
            {
                for (int i = 0; i < points.Count && i < d.Points.Length; i++)
                {
                    DownforcePoint src = d.Points[i];
                    if (src != null && points[i] != null)
                    {
                        points[i].maxForce = src.maxForce * p.DownforceScale;
                    }
                }
            }
        }

        /// <summary>
        /// Make an onboarded module inert AND disabled. Disabling alone is not enough:
        /// NWH's LOD system (UpdateLOD with a state-settings lodIndex) or a parent
        /// re-enable can switch the module back on, and it would then add drag to a
        /// vehicle that never had aero.
        /// </summary>
        private static void ParkOnboarded(AeroData d)
        {
            AerodynamicsModule m = d.Module;
            m.simulateDrag = false;
            m.simulateDownforce = false;
            m.frontalCd = d.FrontalCd;
            m.sideCd = d.SideCd;
            m.maxDownforceSpeed = d.MaxDownforceSpeed;
            if (m.state.isEnabled)
            {
                m.VC_Disable(false);
            }
        }

        private static void RestoreAero(VehicleRecord r)
        {
            AeroData d = r.Aero;
            if (d.Module == null)
            {
                return;
            }
            if (d.Onboarded)
            {
                // Module stays in Components for reuse; re-enabling reapplies fields.
                ParkOnboarded(d);
                return;
            }
            AerodynamicsModule m = d.Module;
            m.simulateDrag = d.WasSimulateDrag;
            m.simulateDownforce = d.WasSimulateDownforce;
            m.frontalCd = d.FrontalCd;
            m.sideCd = d.SideCd;
            m.maxDownforceSpeed = d.MaxDownforceSpeed;
            List<DownforcePoint> points = m.downforcePoints;
            if (points != null)
            {
                for (int i = 0; i < points.Count && i < d.Points.Length; i++)
                {
                    DownforcePoint src = d.Points[i];
                    if (src != null && points[i] != null)
                    {
                        points[i].maxForce = src.maxForce;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- brakes

        private void ApplyAllBrakes()
        {
            BrakesPreset p = BrakesSettings.ActivePreset ?? BrakesPreset.Stock;
            TargetPass(AppliedCat.Brakes, ApplyBrakes, p, RestoreBrakes);
        }

        private void RestoreAllBrakes()
        {
            RestorePass(AppliedCat.Brakes, RestoreBrakes);
        }

        /// <summary>
        /// Effective per-axle torque = stock maxTorque x TorqueScale x stock coefficient x
        /// axle factor, exactly. NWH caps every wheel at brakes.maxTorque and the game
        /// clamps brakeCoefficient to 0..1, so on the usual stock coefficient of 1.0 any
        /// axle/handbrake factor above 1 used to do nothing. When the factors push the
        /// peak coefficient beyond the stock peak by k, maxTorque is raised by k and every
        /// coefficient divided by k: every brake path (pedal, handbrake, off-throttle,
        /// idle, disabled) goes through AddBrakeTorque x coefficient, so the torques are
        /// exactly the requested ones and only the per-wheel cap moves with them.
        /// Stock factors give k = 1, i.e. the shipped values unchanged.
        /// </summary>
        internal static float BrakeNormalization(VehicleRecord r, BrakesPreset p)
        {
            float stockBrake = 0f, stockHand = 0f, wantBrake = 0f, wantHand = 0f;
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                if (gk.Key == null)
                {
                    continue;
                }
                GroupData g = gk.Value;
                float a = g.BrakeCoeff * (g.IsFront ? p.FrontBrakeScale : p.RearBrakeScale);
                float h = g.HandbrakeCoeff * p.HandbrakeScale;
                if (g.BrakeCoeff > stockBrake) stockBrake = g.BrakeCoeff;
                if (g.HandbrakeCoeff > stockHand) stockHand = g.HandbrakeCoeff;
                if (a > wantBrake) wantBrake = a;
                if (h > wantHand) wantHand = h;
            }
            float k = 1f;
            float kb = wantBrake / Mathf.Max(1f, stockBrake);
            float kh = wantHand / Mathf.Max(1f, stockHand);
            if (kb > k) k = kb;
            if (kh > k) k = kh;
            return k;
        }

        private static void ApplyBrakes(VehicleRecord r, BrakesPreset p)
        {
            float k = BrakeNormalization(r, p);
            if (r.Brakes != null && r.Vc.brakes != null)
            {
                r.Vc.brakes.maxTorque = r.Brakes.MaxTorque * p.TorqueScale * k;
                r.Vc.brakes.actuationTime = r.Brakes.ActuationTime * p.ActuationScale;
            }
            else
            {
                k = 1f;   // no maxTorque to compensate with: plain clamped factors
            }
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g == null)
                {
                    continue;
                }
                bool front = gk.Value.IsFront;
                g.brakeCoefficient = Mathf.Clamp(gk.Value.BrakeCoeff * (front ? p.FrontBrakeScale : p.RearBrakeScale) / k, 0f, 1f);
                g.handbrakeCoefficient = Mathf.Clamp(gk.Value.HandbrakeCoeff * p.HandbrakeScale / k, 0f, 2f);
            }
        }

        private static void RestoreBrakes(VehicleRecord r)
        {
            if (r.Brakes != null && r.Vc.brakes != null)
            {
                r.Vc.brakes.maxTorque = r.Brakes.MaxTorque;
                r.Vc.brakes.actuationTime = r.Brakes.ActuationTime;
            }
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g == null)
                {
                    continue;
                }
                g.brakeCoefficient = gk.Value.BrakeCoeff;
                g.handbrakeCoefficient = gk.Value.HandbrakeCoeff;
            }
        }

        // ---------------------------------------------------------------- grip

        private void ApplyAllGrip()
        {
            GripPreset p = GripSettings.ActivePreset ?? GripPreset.Stock;
            TargetPass(AppliedCat.Grip, ApplyGrip, p, RestoreGrip);
        }

        private void RestoreAllGrip()
        {
            RestorePass(AppliedCat.Grip, RestoreGrip);
        }

        private static void ApplyGrip(VehicleRecord r, GripPreset p)
        {
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                WheelData b = wk.Value;
                u.LongitudinalFrictionGrip = b.LngGrip * p.LongitudinalScale;
                u.LateralFrictionGrip = b.LatGrip * p.LateralScale;
                u.LongitudinalFrictionStiffness = b.LngStiff * p.StiffnessScale;
                u.LateralFrictionStiffness = b.LatStiff * p.StiffnessScale;
            }
        }

        private static void RestoreGrip(VehicleRecord r)
        {
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                u.LongitudinalFrictionGrip = wk.Value.LngGrip;
                u.LateralFrictionGrip = wk.Value.LatGrip;
                u.LongitudinalFrictionStiffness = wk.Value.LngStiff;
                u.LateralFrictionStiffness = wk.Value.LatStiff;
            }
        }

        // ---------------------------------------------------------------- drivetrain

        private void ApplyAllDrivetrain()
        {
            DrivetrainPreset p = DrivetrainSettings.ActivePreset ?? DrivetrainPreset.Stock;
            LogLayoutParseError();
            TargetPass(AppliedCat.Drivetrain, ApplyDrivetrain, p, RestoreDrivetrain);
        }

        private void RestoreAllDrivetrain()
        {
            RestorePass(AppliedCat.Drivetrain, RestoreDrivetrain);
        }

        // Shift-point guards (see ShiftPoints).
        internal const float UpshiftLimiterRatio = 0.97f;   // NWH clamps its own target upshift here
        internal const float MinShiftHysteresis = 0.9f;     // downshift stays below 90% of upshift

        /// <summary>
        /// Scaled shift points that can never lock the gearbox. Without variableShiftPoint
        /// NWH shifts at UpshiftRPM raw (only its variable path clamps to 97% of the
        /// limiter), so an upshift at/above the scaled limiter leaves the box bouncing on
        /// the limiter (e.g. Race = rev x1.10 with upshift x1.15 on a 96%-upshift car).
        /// The upshift is capped at 97% of the scaled limiter (NWH's own variable-target
        /// ceiling) and the downshift at 90% of the upshift (no gear hunting). Each cap is
        /// relaxed to the vehicle's own stock ratio when the stock setup already exceeds
        /// it, so the Stock preset reproduces the shipped values exactly.
        /// </summary>
        internal static void ShiftPoints(DrivetrainData b, DrivetrainPreset p, out float up, out float down)
        {
            up = b.UpshiftRPM * p.UpshiftScale;
            if (b.HasEngine && b.RevLimiterRPM > 0f)
            {
                float stockRatio = b.UpshiftRPM / b.RevLimiterRPM;
                float cap = b.RevLimiterRPM * p.RevLimiterScale * Mathf.Max(stockRatio, UpshiftLimiterRatio);
                if (up > cap)
                {
                    up = cap;
                }
            }
            down = b.DownshiftRPM * p.DownshiftScale;
            if (b.UpshiftRPM > 0f)
            {
                float stockGap = b.DownshiftRPM / b.UpshiftRPM;
                float cap = up * Mathf.Max(stockGap, MinShiftHysteresis);
                if (down > cap)
                {
                    down = cap;
                }
            }
        }

        /// <summary>
        /// Which axle a differential drives, from its outputs: an axle diff's OutputB is a
        /// wheel (look up that wheel's captured front/rear flag), a centre diff's OutputB is
        /// another differential. Falls back to NWH's list convention (0 front, 1 rear,
        /// 2+ centre, as used by vc.DiffFrontType/DiffRearType/DiffCenterType) only when
        /// the outputs are not wired yet. Index-only mapping put the FRONT mode on the
        /// only (rear) diff of a RWD car. Allocation-free: a cast and a dictionary lookup.
        /// </summary>
        internal static DiffRole ClassifyDiff(VehicleRecord r, DifferentialComponent diff, int index)
        {
            WheelComponent wheel = diff.OutputB as WheelComponent;
            WheelData wd;
            if (wheel != null && wheel.wheelUAPI != null && r.Wheels.TryGetValue(wheel.wheelUAPI, out wd))
            {
                return wd.IsFront ? DiffRole.Front : DiffRole.Rear;
            }
            if (diff.OutputB is DifferentialComponent)
            {
                return DiffRole.Center;
            }
            return index == 0 ? DiffRole.Front : index == 1 ? DiffRole.Rear : DiffRole.Center;
        }

        private static void ApplyDrivetrain(VehicleRecord r, DrivetrainPreset p)
        {
            DrivetrainData b = r.Drivetrain;
            if (b == null)
            {
                return;
            }
            EngineComponent engine = r.Vc.powertrain.engine;
            if (b.HasEngine && engine != null)
            {
                engine.maxPower = b.MaxPower * p.PowerScale;
                // Rev limiter first, then shift points (belt and braces ordering).
                engine.revLimiterRPM = b.RevLimiterRPM * p.RevLimiterScale;
                engine.engineLossPercent = Mathf.Clamp(b.LossPercent * p.LossScale, 0f, 1f);
                if (engine.forcedInduction != null)
                {
                    engine.forcedInduction.powerGainMultiplier = Mathf.Clamp(b.BoostGain * p.BoostScale, 1f, 3f);
                }
            }
            TransmissionComponent transmission = r.Vc.powertrain.transmission;
            if (b.HasTransmission && transmission != null)
            {
                float up, down;
                ShiftPoints(b, p, out up, out down);
                transmission.finalGearRatio = b.FinalGearRatio * p.FinalDriveScale;
                transmission.UpshiftRPM = up;
                transmission.DownshiftRPM = down;
                transmission.shiftDuration = b.ShiftDuration * p.ShiftDurationScale;
            }

            // Differentials: count-guarded list access (the vc.Diff* convenience
            // properties index unguarded and throw on short lists).
            List<DifferentialComponent> diffs = r.Vc.powertrain.differentials;
            if (b.DiffModes != null && diffs != null)
            {
                for (int i = 0; i < b.DiffModes.Length && i < diffs.Count; i++)
                {
                    DifferentialComponent diff = diffs[i];
                    if (diff == null || !b.DiffCaptured[i])
                    {
                        continue;
                    }
                    DifferentialComponent.Type stock = b.DiffModes[i];
                    DiffRole role = ClassifyDiff(r, diff, i);

                    // Mode: the axle's mode, or the captured stock type for "Stock" (so
                    // switching back to Stock while enabled really restores it). Centre
                    // diffs and External (script-driven) diffs always keep their type:
                    // assigning External does not re-assign NWH's split delegate.
                    DifferentialComponent.Type want = stock;
                    if (stock != DifferentialComponent.Type.External)
                    {
                        DiffMode mode = role == DiffRole.Front ? p.DiffFrontMode
                            : role == DiffRole.Rear ? p.DiffRearMode : DiffMode.Stock;
                        if (mode != DiffMode.Stock)
                        {
                            want = ToNwhType(mode);
                        }
                    }
                    // Only assign on change: the setter allocates a new split delegate.
                    if (diff.DifferentialType != want)
                    {
                        diff.DifferentialType = want;
                    }

                    // Bias is the A/B torque split. On a centre diff that is the
                    // front/rear split the slider is for; on an axle diff it would send
                    // more torque to one side wheel (the car pulls / one-wheel-peels),
                    // so axle diffs keep their stock bias.
                    diff.biasAB = role == DiffRole.Center
                        ? Mathf.Clamp(b.DiffBias[i] * p.DiffBiasScale, 0.05f, 0.95f)
                        : b.DiffBias[i];
                    // NWH's range is 0..1; above 1 the locking diff winds up and oscillates.
                    diff.stiffness = Mathf.Min(b.DiffStiff[i] * p.DiffStiffnessScale, Mathf.Max(1f, b.DiffStiff[i]));
                }
            }

            // 0.6.2: custom layout last (it bypasses the vehicle's own diffs, never edits them).
            ApplyLayout(r);
        }

        private static void RestoreDrivetrain(VehicleRecord r)
        {
            DrivetrainData b = r.Drivetrain;
            if (b == null)
            {
                return;
            }
            if (b.Layout != null && b.Layout.Applied)
            {
                RestoreLayout(b.Layout);
            }
            if (b.HasEngine && r.Vc.powertrain.engine != null)
            {
                EngineComponent engine = r.Vc.powertrain.engine;
                engine.maxPower = b.MaxPower;
                engine.revLimiterRPM = b.RevLimiterRPM;
                engine.engineLossPercent = b.LossPercent;
                if (engine.forcedInduction != null)
                {
                    engine.forcedInduction.powerGainMultiplier = b.BoostGain;
                }
            }
            if (b.HasTransmission && r.Vc.powertrain.transmission != null)
            {
                TransmissionComponent transmission = r.Vc.powertrain.transmission;
                transmission.finalGearRatio = b.FinalGearRatio;
                transmission.UpshiftRPM = b.UpshiftRPM;
                transmission.DownshiftRPM = b.DownshiftRPM;
                transmission.shiftDuration = b.ShiftDuration;
            }
            List<DifferentialComponent> diffs = r.Vc.powertrain.differentials;
            if (b.DiffModes != null && diffs != null)
            {
                for (int i = 0; i < b.DiffModes.Length && i < diffs.Count; i++)
                {
                    DifferentialComponent diff = diffs[i];
                    if (diff == null || !b.DiffCaptured[i])
                    {
                        continue;
                    }
                    if (diff.DifferentialType != b.DiffModes[i])
                    {
                        diff.DifferentialType = b.DiffModes[i];
                    }
                    diff.biasAB = b.DiffBias[i];
                    diff.stiffness = b.DiffStiff[i];
                }
            }
        }

        private static DifferentialComponent.Type ToNwhType(DiffMode mode)
        {
            switch (mode)
            {
                case DiffMode.Open: return DifferentialComponent.Type.Open;
                case DiffMode.Locked: return DifferentialComponent.Type.Locked;
                default: return DifferentialComponent.Type.LimitedSlip;
            }
        }
    }
}
