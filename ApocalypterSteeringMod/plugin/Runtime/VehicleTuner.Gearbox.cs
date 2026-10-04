using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Gearbox (0.6.0): per-gear ratio factors, gear-count resize, clutch "type" and
    /// transmission mode. Shares no field with Drivetrain (never shiftDuration, shift
    /// RPMs or finalGearRatio), so the RefreshBaselines invariant holds.
    ///
    /// NWH facts this relies on (TransmissionComponent.cs):
    ///  - gears = [reverse (negative)..., 0 (neutral), forward (positive)...] (:50);
    ///    forward/reverseGearCount are recomputed every ForwardStep (:410), and
    ///    Gear = gearIndex - reverseGearCount (:184, :698).
    ///  - CalculateTotalGearRatio reads gears[gearIndex] UNGUARDED (:311-322): after a
    ///    shrink the current gear must be pulled back into range at once. ShiftInto is NOT
    ///    usable for that: it silently refuses during the post-shift ban and while a shift
    ///    is in flight (:436-453; 'instant' does not bypass the ban), which would leave
    ///    gearIndex out of range and throw every physics tick. The Gear setter is used.
    ///  - A shift coroutine in flight sets Gear = target LATER (:480): shrinking below that
    ///    target would point it past the end. Shrinks are deferred while isShifting; a
    ///    restore during a shift keeps placeholder copies of the top gear until it lands.
    ///  - CVT needs exactly 3 gears (VC_Validate :277): counts/ratios are never touched on
    ///    CVT (or External) boxes; the clutch still applies.
    ///  - transmissionType is live-safe (ForwardStep re-assigns the shift delegate, :405).
    /// </summary>
    public sealed partial class VehicleTuner
    {
        public const float MinContinuedRatio = 0.05f;

        private static GearboxData CaptureGearbox(VehicleController vc)
        {
            var d = new GearboxData();
            if (vc.powertrain == null)
            {
                return d;
            }
            TransmissionComponent t = vc.powertrain.transmission;
            if (t != null && t.gears != null)
            {
                d.HasTransmission = true;
                d.Gears = t.gears.ToArray();
                d.Type = t.transmissionType;
                d.IsCvt = t.transmissionType == TransmissionComponent.TransmissionShiftType.CVT;
                int reverse, forward;
                d.Standard = AnalyseLayout(d.Gears, out reverse, out forward);
                d.Reverse = reverse;
                d.Forward = forward;
                d.Extended = ExtendRatios(d.Gears, reverse, forward);
            }
            ClutchComponent c = vc.powertrain.clutch;
            if (c != null)
            {
                d.HasClutch = true;
                d.SlipTorque = c.slipTorque;
                d.EngagementRange = c.engagementRange;
                d.EngagementRpm = c.engagementRPM;
            }
            return d;
        }

        /// <summary>
        /// True for NWH's documented layout: every negative first, exactly one 0, then only
        /// positives (VC_Validate warns on anything else). Anything else is left untouched.
        /// </summary>
        public static bool AnalyseLayout(float[] gears, out int reverse, out int forward)
        {
            reverse = 0;
            forward = 0;
            int i = 0;
            while (i < gears.Length && gears[i] < 0f)
            {
                reverse++;
                i++;
            }
            if (i >= gears.Length || gears[i] != 0f)
            {
                return false;
            }
            i++;
            while (i < gears.Length && gears[i] > 0f)
            {
                forward++;
                i++;
            }
            return i == gears.Length && forward > 0;
        }

        /// <summary>
        /// Stock forward ratios, continued geometrically to at least 12 gears:
        /// r[n] = r[n-1] x r[n-1] / r[n-2] (one forward gear: x0.75), never below 0.05.
        /// </summary>
        public static float[] ExtendRatios(float[] gears, int reverse, int forward)
        {
            int len = forward > GearboxPreset.MaxGears ? forward : GearboxPreset.MaxGears;
            var ext = new float[len];
            if (forward <= 0)
            {
                return ext;
            }
            for (int k = 0; k < forward; k++)
            {
                ext[k] = gears[reverse + 1 + k];
            }
            for (int k = forward; k < len; k++)
            {
                float prev = ext[k - 1];
                float next = k >= 2 && ext[k - 2] > 0f ? prev * prev / ext[k - 2] : prev * 0.75f;
                ext[k] = next < MinContinuedRatio ? MinContinuedRatio : next;
            }
            return ext;
        }

        /// <summary>Engagement point = stock + offset, but an offset never pushes it below
        /// min(stock, 1.1 x idle): below idle the clutch stays engaged while idling (NWH's own
        /// VC_Validate warning), so the vehicle drags/creeps in gear at a standstill.</summary>
        public static float EngagementRpm(float stock, float offset, float idleRpm)
        {
            float want = stock + offset;
            float floor = idleRpm > 0f ? Mathf.Min(stock, idleRpm * 1.1f) : 0f;
            return want < floor ? floor : want;
        }

        /// <summary>Target forward count: the preset's, or the vehicle's own for 0.</summary>
        internal static int TargetForward(GearboxData d, GearboxPreset p)
        {
            return p.GearCount <= 0 ? d.Forward : p.GearCount;
        }

        private void ApplyAllGearbox()
        {
            GearboxPreset p = GearboxSettings.ActivePreset ?? GearboxPreset.Stock;
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                ApplyGearbox(kv.Value, p);
            }
        }

        private void RestoreAllGearbox()
        {
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                RestoreGearbox(kv.Value);
            }
        }

        private static bool GearsEditable(GearboxData d)
        {
            return d.HasTransmission && d.Standard && !d.IsCvt
                && d.Type != TransmissionComponent.TransmissionShiftType.External;
        }

        private static void ApplyGearbox(VehicleRecord r, GearboxPreset p)
        {
            GearboxData d = r.Gearbox;
            if (d == null || r.Vc.powertrain == null)
            {
                return;
            }
            TransmissionComponent t = r.Vc.powertrain.transmission;

            // 1+2. Ratios, then the count (with the re-shift guard).
            if (t != null && t.gears != null && GearsEditable(d))
            {
                int n = TargetForward(d, p);
                int current = t.gears.Count - d.Reverse - 1;
                if (!(n < current && t.isShifting))   // shrinking under an in-flight shift: retry next pass
                {
                    WriteGears(t, d.Gears, d.Reverse, d.Extended, n, p);
                    d.PendingTrim = false;
                    ClampGear(t, n);
                }
            }

            // 3. Clutch.
            ClutchComponent c = r.Vc.powertrain.clutch;
            if (d.HasClutch && c != null)
            {
                c.slipTorque = Mathf.Max(1f, d.SlipTorque * p.ClutchGripScale);
                c.engagementRange = Mathf.Max(1f, d.EngagementRange * p.ClutchRangeScale);
                float idle = r.Vc.powertrain.engine != null ? r.Vc.powertrain.engine.idleRPM : 0f;
                c.engagementRPM = EngagementRpm(d.EngagementRpm, p.ClutchRpmOffset, idle);
            }

            // 4. Transmission mode (CVT / External: Stock only).
            if (t != null && d.HasTransmission)
            {
                TransmissionComponent.TransmissionShiftType want = ModeFor(d, p.TransmissionMode);
                if (t.transmissionType != want)
                {
                    t.transmissionType = want;
                }
            }
        }

        internal static TransmissionComponent.TransmissionShiftType ModeFor(GearboxData d, GearboxMode mode)
        {
            if (d.IsCvt || d.Type == TransmissionComponent.TransmissionShiftType.External)
            {
                return d.Type;
            }
            switch (mode)
            {
                case GearboxMode.Manual: return TransmissionComponent.TransmissionShiftType.Manual;
                case GearboxMode.Automatic: return TransmissionComponent.TransmissionShiftType.Automatic;
                default: return d.Type;
            }
        }

        /// <summary>
        /// Rewrite the gear list in place: reverse + neutral copied from stock (never altered),
        /// then n forward gears = stock (or continued) ratio x factor. Allocation-free except
        /// when the list has to grow past its capacity (only on a count change).
        /// </summary>
        internal static void WriteGears(TransmissionComponent t, float[] stock, int reverse, float[] extended, int n, GearboxPreset p)
        {
            List<float> g = t.gears;
            int len = reverse + 1 + n;
            while (g.Count > len)
            {
                g.RemoveAt(g.Count - 1);
            }
            while (g.Count < len)
            {
                g.Add(0f);
            }
            for (int i = 0; i <= reverse; i++)
            {
                g[i] = stock[i];
            }
            for (int k = 0; k < n; k++)
            {
                float baseRatio = k < extended.Length ? extended[k] : extended[extended.Length - 1];
                g[reverse + 1 + k] = baseRatio * (p != null ? p.Scale(k + 1) : 1f);
            }
            t.forwardGearCount = n;
        }

        /// <summary>Pull the current gear back into range at once (Gear setter: synchronous, no ban).</summary>
        internal static void ClampGear(TransmissionComponent t, int forward)
        {
            if (t.Gear > forward)
            {
                t.Gear = forward;
            }
        }

        private static void RestoreGearbox(VehicleRecord r)
        {
            GearboxData d = r.Gearbox;
            if (d == null || r.Vc.powertrain == null)
            {
                return;
            }
            TransmissionComponent t = r.Vc.powertrain.transmission;
            if (t != null && t.gears != null && GearsEditable(d))
            {
                List<float> g = t.gears;
                int stockLen = d.Gears.Length;
                if (t.isShifting && g.Count > stockLen)
                {
                    // An in-flight shift may land on a gear beyond the stock count. Keep the
                    // extra slots as copies of the stock top gear until it lands (harmless:
                    // same ratio), then TrimPendingGearbox cuts them.
                    for (int i = 0; i < stockLen; i++)
                    {
                        g[i] = d.Gears[i];
                    }
                    for (int i = stockLen; i < g.Count; i++)
                    {
                        g[i] = d.Gears[stockLen - 1];
                    }
                    d.PendingTrim = true;
                }
                else
                {
                    while (g.Count > stockLen)
                    {
                        g.RemoveAt(g.Count - 1);
                    }
                    while (g.Count < stockLen)
                    {
                        g.Add(0f);
                    }
                    for (int i = 0; i < stockLen; i++)
                    {
                        g[i] = d.Gears[i];
                    }
                    t.forwardGearCount = d.Forward;
                    ClampGear(t, d.Forward);   // keep the current gear if it is still valid
                    d.PendingTrim = false;
                }
            }
            ClutchComponent c = r.Vc.powertrain.clutch;
            if (d.HasClutch && c != null)
            {
                c.slipTorque = d.SlipTorque;
                c.engagementRange = d.EngagementRange;
                c.engagementRPM = d.EngagementRpm;
            }
            if (t != null && d.HasTransmission && t.transmissionType != d.Type)
            {
                t.transmissionType = d.Type;
            }
        }

        /// <summary>Finish restores that had to wait for a shift to land (runs while the category is OFF).</summary>
        private void TrimPendingGearbox()
        {
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
                GearboxData d = r.Gearbox;
                if (d == null || !d.PendingTrim || r.Vc.powertrain == null)
                {
                    continue;
                }
                TransmissionComponent t = r.Vc.powertrain.transmission;
                if (t == null || t.isShifting)
                {
                    continue;
                }
                RestoreGearbox(r);
            }
        }
    }
}
