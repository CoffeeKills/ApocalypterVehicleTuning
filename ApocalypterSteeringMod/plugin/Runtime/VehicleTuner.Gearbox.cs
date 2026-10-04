using System;
using System.Collections.Generic;
using System.Reflection;
using ApocalypterSteeringMod.Settings;
using HutongGames.PlayMaker;
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

        private static readonly HashSet<string> LoggedGearboxCapture = new HashSet<string>();

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
                // Self-heal: a game save made while the gearbox was tuned bakes the
                // extended gear list into the vehicle. Repair it — conservatively.
                // The geometric-continuation detection is the ONLY trigger (it can
                // never fire on a healthy car: it needs >= 7 forward gears AND an
                // exact geometric tail). The game's FSM gear count is read for a
                // log line only, until it is validated in-game.
                int reverse, forward;
                if (AnalyseLayout(t.gears.ToArray(), out reverse, out forward) && forward >= 7)
                {
                    int realCount = TryStripContinuation(t.gears, reverse);
                    int fsmCount = StockGearCountFromFsms(vc);
                    if (realCount >= 1 && realCount < forward)
                    {
                        if (Plugin.Log != null)
                        {
                            Plugin.Log.LogWarning("Gearbox: vehicle '" + VehicleName(vc) + "' carries " + forward
                                + " forward gears (a tuned save). Truncating the continuation tail back to "
                                + realCount + " forward gears (game FSM says " + fsmCount + ") and fixing the gear state.");
                        }
                        int neutral = reverse;
                        t.gears.RemoveRange(neutral + 1 + realCount, forward - realCount);
                        if (t.Gear > realCount)
                        {
                            t.Gear = realCount;
                        }
                        forward = realCount;
                    }
                }
                d.HasTransmission = true;
                d.Gears = t.gears.ToArray();
                d.Type = t.transmissionType;
                d.IsCvt = t.transmissionType == TransmissionComponent.TransmissionShiftType.CVT;
                d.Standard = AnalyseLayout(d.Gears, out reverse, out forward);
                d.Reverse = reverse;
                d.Forward = forward;
                d.Extended = ExtendRatios(d.Gears, reverse, forward);

                // One diagnostic line per vehicle per session: the save-file contents
                // as the tuner sees them (used to hunt poisoned saves).
                if (Plugin.Log != null && !LoggedGearboxCapture.Contains(VehicleName(vc)))
                {
                    LoggedGearboxCapture.Add(VehicleName(vc));
                    Plugin.Log.LogInfo("Gearbox capture: '" + VehicleName(vc) + "' gears=" + string.Join(",", t.gears)
                        + " gear=" + t.Gear + " shifting=" + t.isShifting + " layout=" + (d.Standard ? "ok" : "NON-STANDARD"));
                }
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
        /// The continuation the mod adds is exactly geometric (r[n] = r[n-1]^2 / r[n-2]).
        /// The first continuation gear satisfies the relation BY CONSTRUCTION with the
        /// stock's last two, so the smallest index whose suffix is geometric is one LESS
        /// than the real stock count; hence the returned count is that index + 1 (gears
        /// 1..count are kept). 0 = no continuation found (leave the list alone). Real
        /// gearboxes are not exact geometric continuations, so a false positive needs a
        /// long exact tail; the caller additionally requires forward >= 7 and prefers the
        /// game's own FSM gear count when readable.
        /// </summary>
        public static int TryStripContinuation(List<float> gears, int reverseCount)
        {
            int first = 1 + reverseCount;   // index of forward gear 1
            int n = gears.Count - first;
            if (n < 4)
            {
                return 0;
            }
            float[] r = new float[n];
            for (int i = 0; i < n; i++)
            {
                r[i] = Mathf.Abs(gears[first + i]);
            }
            for (int a = 2; a <= n - 3; a++)   // the stripped tail is at least 3 gears
            {
                bool ok = true;
                for (int j = a + 1; j < n && ok; j++)
                {
                    float expected = r[j - 1] * r[j - 1] / Mathf.Max(r[j - 2], 1e-6f);
                    // Tight: the mod's continuation is exact in float arithmetic, and
                    // survives ES3's float round-trip well within 1e-4 relative.
                    // Real stock ratios that are merely similar do not pass.
                    ok = Mathf.Abs(r[j] - expected) <= expected * 1e-4f;
                }
                if (ok)
                {
                    return a + 1;   // the geometric suffix starts at the first added gear (index a); keep gears 1..a
                }
            }
            return 0;
        }

        // ---------------------------------------------------------------- FSM stock-gear detection

        private static readonly Dictionary<Type, FieldInfo> ButtonNameFields = new Dictionary<Type, FieldInfo>();

        /// <summary>
        /// The vehicle's real forward gear count from the game's own shift-into
        /// actions: each INPUT FSM carries one GetButtonDown("ShiftIntoN") per stock
        /// gear (the InputManager names, research-verified). 0 = not readable.
        /// </summary>
        private static int StockGearCountFromFsms(VehicleController vc)
        {
            int max = 0;
            PlayMakerFSM[] fsms = vc != null ? vc.GetComponentsInChildren<PlayMakerFSM>(true) : null;
            if (fsms == null)
            {
                return 0;
            }
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] == null || fsms[i].Fsm == null || fsms[i].Fsm.States == null)
                {
                    continue;
                }
                FsmState[] states = fsms[i].Fsm.States;
                for (int s = 0; s < states.Length; s++)
                {
                    if (states[s] == null)
                    {
                        continue;
                    }
                    int n = CountShiftIntos(states[s].Actions);
                    if (n > max)
                    {
                        max = n;
                    }
                }
            }
            return max;
        }

        /// <summary>The highest ShiftIntoN button among these actions, or 0.</summary>
        public static int CountShiftIntos(FsmStateAction[] actions)
        {
            if (actions == null)
            {
                return 0;
            }
            int max = 0;
            for (int i = 0; i < actions.Length; i++)
            {
                FsmStateAction a = actions[i];
                if (a == null)
                {
                    continue;
                }
                string name = GetButtonName(a);
                if (name != null && name.StartsWith("ShiftInto", StringComparison.Ordinal))
                {
                    int n;
                    if (name.Length > 9 && int.TryParse(name.Substring(9), out n) && n > max)
                    {
                        max = n;
                    }
                }
            }
            return max;
        }

        private static string GetButtonName(FsmStateAction a)
        {
            Type t = a.GetType();
            FieldInfo f;
            if (!ButtonNameFields.TryGetValue(t, out f))
            {
                f = t.GetField("buttonName");
                ButtonNameFields[t] = f;
            }
            if (f == null)
            {
                return null;
            }
            FsmString s = f.GetValue(a) as FsmString;
            return s != null ? s.Value : null;
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
            AnyResizeSkipped = false;
            TargetPass(AppliedCat.Gearbox, r => ApplyGearbox(r, p), RestoreGearbox);
        }

        private void RestoreAllGearbox()
        {
            RestorePass(AppliedCat.Gearbox, RestoreGearbox);
        }

        private static bool GearsEditable(GearboxData d)
        {
            return d.HasTransmission && d.Standard && !d.IsCvt
                && d.Type != TransmissionComponent.TransmissionShiftType.External;
        }

        private void ApplyGearbox(VehicleRecord r, GearboxPreset p)
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
                // 0.6.1 safety: the game's shift logic expects the stock gear count on
                // automatic transmissions — a resized list makes the car undrivable
                // (engine revs, no drive). Resize only on manual transmissions, where
                // the player has deliberately opted in; ratios and clutch still apply.
                if (n != current
                    && (t.transmissionType == TransmissionComponent.TransmissionShiftType.Automatic
                        || t.transmissionType == TransmissionComponent.TransmissionShiftType.AutomaticSequential_Obsolete))
                {
                    AnyResizeSkipped = true;
                    n = current;
                }
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
