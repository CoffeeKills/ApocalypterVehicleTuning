using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// ABS + TCS implemented through NWH's own public delegate hooks
    /// (brakes.brakeTorqueModifiers / engine.powerModifiers) — no modules, no
    /// vehicle fields. One delegate instance per vehicle, created once and
    /// reused across re-registrations; the bodies read the live preset fields
    /// every tick, so slider changes apply instantly.
    /// </summary>
    public sealed partial class VehicleTuner
    {
        private static AssistHandles CreateAssistHandles(VehicleController vc)
        {
            var h = new AssistHandles();
            h.Abs = MakeAbs(vc);
            h.Tcs = MakeTcs(vc);
            return h;
        }

        private static Brakes.BrakeTorqueModifier MakeAbs(VehicleController vc)
        {
            // Mirrors NWH's ABSModule semantics (slip threshold + low-speed cutoff),
            // but reads the live preset fields instead of a module's own.
            return () =>
            {
                if (!AssistsSettings.Enabled || vc == null)
                {
                    return 1f;
                }
                AssistsPreset p = AssistsSettings.ActivePreset;
                if (p == null || !p.AbsEnabled || vc.Speed < p.AbsCutoffSpeed)
                {
                    return 1f;
                }
                // NWH's ABSModule stands down while the handbrake is pulled. Brakes
                // multiplies this modifier into the HANDBRAKE torque too, so without the
                // guard a handbrake-locked rear wheel trips ABS and the handbrake is
                // cut to 1% — no handbrake turns with assists on. Read-only input access.
                // 0.6.0: also the handbrake VALUE — with HandbrakeType.Latching the handbrake
                // stays applied after the input is released (Brakes.cs:113-136), and the
                // input-only guard (NWH's own) cut the latched handbrake to 1%.
                if ((vc.input != null && vc.input.Handbrake >= 0.1f)
                    || (vc.brakes != null && vc.brakes.handbrakeValue >= 0.1f))
                {
                    return 1f;
                }
                float fwd = Mathf.Sign(vc.LocalForwardVelocity);
                List<WheelComponent> wheels = vc.powertrain.wheels;
                for (int i = 0; i < wheels.Count; i++)
                {
                    WheelUAPI u = wheels[i].wheelUAPI;
                    if (u != null && u.IsGrounded && u.LongitudinalSlip * fwd > p.AbsSlipThreshold)
                    {
                        return p.AbsCutMultiplier;
                    }
                }
                return 1f;
            };
        }

        private static EngineComponent.PowerModifier MakeTcs(VehicleController vc)
        {
            return () =>
            {
                if (!AssistsSettings.Enabled || vc == null)
                {
                    return 1f;
                }
                AssistsPreset p = AssistsSettings.ActivePreset;
                if (p == null || !p.TcsEnabled || vc.Speed < p.TcsCutoffSpeed)
                {
                    return 1f;
                }
                // 0.6.0: like NWH's TCSModule, never cut while a shift is in progress: the
                // clutch is open, wheel slip is not engine-driven, and a cut there only
                // leaves 1% power when the clutch re-engages (a stumble on every upshift).
                TransmissionComponent tr = vc.powertrain.transmission;
                if (tr != null && tr.isShifting)
                {
                    return 1f;
                }
                float fwd = Mathf.Sign(vc.LocalForwardVelocity);
                List<WheelComponent> wheels = vc.powertrain.wheels;
                for (int i = 0; i < wheels.Count; i++)
                {
                    WheelUAPI u = wheels[i].wheelUAPI;
                    if (u != null && u.IsGrounded && -u.LongitudinalSlip * fwd > p.TcsSlipThreshold)
                    {
                        return p.TcsCutMultiplier;
                    }
                }
                return 1f;
            };
        }

        private void ApplyAllAssists()
        {
            AssistsPreset p = AssistsSettings.ActivePreset;
            TargetPass(AppliedCat.Assists, ApplyAssists, p, RestoreAssists);
        }

        private void RestoreAllAssists()
        {
            RestorePass(AppliedCat.Assists, RestoreAssists);
        }

        private static void ApplyAssists(VehicleRecord r, AssistsPreset p)
        {
            AssistHandles h = r.Assists;
            if (h == null)
            {
                return;   // capture failed for this vehicle (0.6.3 guard)
            }
            bool wantAbs = AssistsSettings.Enabled && p != null && p.AbsEnabled;
            bool wantTcs = AssistsSettings.Enabled && p != null && p.TcsEnabled;

            if (wantAbs && !h.AbsRegistered && r.Vc.brakes != null)
            {
                // NWH's own ABSModule guard compares freshly created delegates (never
                // equal) — do not copy it; the flag is the guard.
                List<Brakes.BrakeTorqueModifier> list = r.Vc.brakes.brakeTorqueModifiers;
                if (list == null)
                {
                    list = new List<Brakes.BrakeTorqueModifier>();
                    r.Vc.brakes.brakeTorqueModifiers = list;
                }
                list.Add(h.Abs);
                h.AbsRegistered = true;
            }
            else if (!wantAbs && h.AbsRegistered && r.Vc.brakes != null)
            {
                List<Brakes.BrakeTorqueModifier> list = r.Vc.brakes.brakeTorqueModifiers;
                if (list != null)
                {
                    list.Remove(h.Abs);
                }
                h.AbsRegistered = false;
            }

            if (wantTcs && !h.TcsRegistered && r.Vc.powertrain != null && r.Vc.powertrain.engine != null)
            {
                List<EngineComponent.PowerModifier> list = r.Vc.powertrain.engine.powerModifiers;
                if (list == null)
                {
                    list = new List<EngineComponent.PowerModifier>();
                    r.Vc.powertrain.engine.powerModifiers = list;
                }
                list.Add(h.Tcs);
                h.TcsRegistered = true;
            }
            else if (!wantTcs && h.TcsRegistered && r.Vc.powertrain != null && r.Vc.powertrain.engine != null)
            {
                List<EngineComponent.PowerModifier> list = r.Vc.powertrain.engine.powerModifiers;
                if (list != null)
                {
                    list.Remove(h.Tcs);
                }
                h.TcsRegistered = false;
            }
        }

        private static void RestoreAssists(VehicleRecord r)
        {
            AssistHandles h = r.Assists;
            if (h == null)
            {
                return;
            }
            if (h.AbsRegistered && r.Vc.brakes != null)
            {
                List<Brakes.BrakeTorqueModifier> list = r.Vc.brakes.brakeTorqueModifiers;
                if (list != null)
                {
                    list.Remove(h.Abs);
                }
                h.AbsRegistered = false;
            }
            if (h.TcsRegistered && r.Vc.powertrain != null && r.Vc.powertrain.engine != null)
            {
                List<EngineComponent.PowerModifier> list = r.Vc.powertrain.engine.powerModifiers;
                if (list != null)
                {
                    list.Remove(h.Tcs);
                }
                h.TcsRegistered = false;
            }
        }
    }
}
