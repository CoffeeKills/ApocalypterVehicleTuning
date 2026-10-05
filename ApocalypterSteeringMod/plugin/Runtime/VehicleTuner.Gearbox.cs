using System;
using System.Collections.Generic;
using System.Reflection;
using ApocalypterSteeringMod.Persistence;
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
    ///  - transmissionType changes make ForwardStep re-assign the shift delegate (:405), so 0.7.0
    ///    never writes the type: the ShiftController is installed as the delegate instead.
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
                d.StockShift = t.shiftDelegate;
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

        /// <summary>Progressive spacing exponent for spread gears (&lt; 1 = bigger steps low, smaller high).</summary>
        public const float SpreadCurve = 0.85f;

        /// <summary>
        /// Gear k (0-based) of n spread over [first, top]: log-interpolated with progressive spacing,
        /// r_k = first x (top / first)^((k / (n-1))^0.85). Deliberately not geometric: a geometric
        /// list would look like the 0.6.0 continuation to the save self-heal (TryStripContinuation).
        /// </summary>
        public static float SpreadRatio(float first, float top, int k, int n)
        {
            if (n <= 1 || first <= 0f || top <= 0f)
            {
                return first;
            }
            double t = Math.Pow((double)k / (n - 1), SpreadCurve);
            return (float)(first * Math.Pow(top / first, t));
        }

        /// <summary>The base ratio (before the per-gear factor) of forward gear k (0-based) in an n-gear box.</summary>
        internal static float BaseRatio(GearboxData d, GearboxPreset p, int k, int n)
        {
            if (p != null && p.SpreadRatios && d.Forward >= 1)
            {
                return SpreadRatio(d.Extended[0], d.Extended[d.Forward - 1], k, n);
            }
            return k < d.Extended.Length ? d.Extended[k] : d.Extended[d.Extended.Length - 1];
        }

        /// <summary>Target forward count: the preset's, or the vehicle's own for 0.</summary>
        internal static int TargetForward(GearboxData d, GearboxPreset p)
        {
            return p.GearCount <= 0 ? d.Forward : p.GearCount;
        }

        private void ApplyAllGearbox()
        {
            GearboxPreset p = GearboxSettings.ActivePreset ?? GearboxPreset.Stock;
            TargetPass(AppliedCat.Gearbox, ApplyGearbox, p, RestoreGearbox);
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

        /// <summary>CVT / External boxes run their own shifting; the controller never takes them over.</summary>
        private static bool OwnShiftingType(TransmissionComponent.TransmissionShiftType type)
        {
            return type == TransmissionComponent.TransmissionShiftType.CVT
                || type == TransmissionComponent.TransmissionShiftType.External;
        }

        private static void ApplyGearbox(VehicleRecord r, GearboxPreset p)
        {
            GearboxData d = r.Gearbox;
            if (d == null || r.Vc.powertrain == null)
            {
                return;
            }
            TransmissionComponent t = r.Vc.powertrain.transmission;

            // 0.7.0: the mod owns shifting on every tunable box (FEATURES §1). The 0.6.x
            // "automatic transmissions are skipped" gate and the transmissionType writes are gone:
            // the mode only selects the controller's logic (writing the type would make NWH
            // re-assign its own delegate on the next tick, TransmissionComponent.cs:405).
            if (t != null && t.gears != null && GearsEditable(d) && !OwnShiftingType(t.transmissionType))
            {
                // 1. Ratios, then the count (CVT keeps its own list).
                int n = TargetForward(d, p);
                int current = t.gears.Count - d.Reverse - 1;
                if (!(n < current && t.isShifting))   // shrinking under an in-flight shift: retry next pass
                {
                    WriteGears(t, d, n, p);
                    d.PendingTrim = false;
                    ClampGear(t, n);
                }
                // 2. The shift controller (idempotent; re-hooks when NWH re-assigned its delegate),
                // only for presets that change shifting — Stock / clutch-only keep NWH's own.
                if (p.NeedsShiftController())
                {
                    HookShifter(d, t);
                }
                else if (d.Hooked)
                {
                    UnhookShifter(d, t);
                }
            }
            else if (t != null && d.Hooked)
            {
                UnhookShifter(d, t);   // the game turned the box into CVT/External while we owned it
            }

            // 3. Clutch (CVT included). 0.7.0: a value the game changed since our last write
            // (e.g. an engine swap re-sizing the clutch) becomes the new stock first.
            ClutchComponent c = r.Vc.powertrain.clutch;
            if (d.HasClutch && c != null)
            {
                if (d.ClutchWritten)
                {
                    d.SlipTorque = Drift.Adopt(d.SlipTorque, d.LastSlipTorque, c.slipTorque);
                    d.EngagementRange = Drift.Adopt(d.EngagementRange, d.LastEngagementRange, c.engagementRange);
                    d.EngagementRpm = Drift.Adopt(d.EngagementRpm, d.LastEngagementRpm, c.engagementRPM);
                }
                c.slipTorque = Mathf.Max(1f, d.SlipTorque * p.ClutchGripScale);
                c.engagementRange = Mathf.Max(1f, d.EngagementRange * p.ClutchRangeScale);
                float idle = r.Vc.powertrain.engine != null ? r.Vc.powertrain.engine.idleRPM : 0f;
                c.engagementRPM = EngagementRpm(d.EngagementRpm, p.ClutchRpmOffset, idle);
                d.LastSlipTorque = c.slipTorque;
                d.LastEngagementRange = c.engagementRange;
                d.LastEngagementRpm = c.engagementRPM;
                d.ClutchWritten = true;
            }
        }

        /// <summary>
        /// Install the vehicle's ShiftController as its transmission's shift delegate. The delegate
        /// found there is captured as the stock one — unless it is a ShiftController's own (left by
        /// a runner that died without restoring), whose captured stock is taken instead. When NWH
        /// has replaced our delegate (the game changed transmissionType, NWH re-assigned on the
        /// next tick) the new one is the game's intent: it becomes the stock and we hook again.
        /// </summary>
        internal static void HookShifter(GearboxData d, TransmissionComponent t)
        {
            if (d.Shifter == null)
            {
                d.Shifter = new ShiftController();
            }
            if (d.Hooked && t.shiftDelegate == d.Shifter.Delegate)
            {
                // Already ours. 0.7.6: the game may have changed the type under us without a
                // delegate reassignment (both flips guarded) — keep the mode decision live.
                d.Shifter.StockType = t.transmissionType;
                d.Type = t.transmissionType;
                return;
            }
            TransmissionComponent.Shift current = t.shiftDelegate;
            var stale = current != null ? current.Target as ShiftController : null;
            d.Shifter.StockDelegate = stale != null ? stale.StockDelegate : current;
            d.Shifter.StockType = t.transmissionType;
            d.Type = t.transmissionType;
            t.shiftDelegate = d.Shifter.Delegate;
            d.Hooked = true;
            Controlled[t] = d.Shifter;
            if (Plugin.Log != null && ModConfig.GearboxDebugHooks)
            {
                Plugin.Log.LogInfo("Shift controller hooked on '" + VehicleName(t.vehicleController) + "' (type "
                    + t.transmissionType + ", " + t.forwardGearCount + " forward gears, mode "
                    + (GearboxSettings.ActivePreset != null ? GearboxSettings.ActivePreset.TransmissionMode.ToString() : "?") + ")");
            }
        }

        /// <summary>Put the captured delegate back (only if ours is still installed).</summary>
        internal static void UnhookShifter(GearboxData d, TransmissionComponent t)
        {
            if (d.Hooked && d.Shifter != null && t.shiftDelegate == d.Shifter.Delegate)
            {
                t.shiftDelegate = d.Shifter.StockDelegate;
            }
            d.Hooked = false;
            Controlled.Remove(t);
        }

        // ---- 0.7.3: the instant re-hook ----------------------------------------------
        // NWH's ForwardStep re-assigns its own shift delegate whenever transmissionType
        // changes (:405-408) — the game's CheckTag FSM writes the type, and until the next
        // ApplyLive pass (up to 2 s) the game's own automatic ran the tuned box. With 12
        // tightly-spaced gears its gear-skipping branch (:617-632) lands the box in a tall
        // gear from a standstill ("launching from gear 8"). The Harmony postfix on
        // AssignShiftDelegate (Patching/ShiftDelegateGuard.cs) re-installs the controller in
        // the same tick, so the window is zero.

        /// <summary>Transmissions whose shifting the mod currently owns (the guard's lookup).</summary>
        internal static readonly Dictionary<TransmissionComponent, ShiftController> Controlled =
            new Dictionary<TransmissionComponent, ShiftController>();

        /// <summary>
        /// If this transmission is controlled, put its controller delegate back (called from
        /// the Harmony postfix after NWH re-assigns its own; also testable directly). 0.7.6:
        /// when NWH has just assigned a fresh delegate for a changed type, adopt BOTH — the
        /// delegate and the type — so a controller hooked on the prefab default (Manual) learns
        /// the game's real type (Automatic) the tick the game writes it. Without this, a
        /// Stock-mode controller on a Manual-captured box stays manual forever and throttle
        /// from neutral does nothing (the load bug).
        /// </summary>
        public static void RehookIfControlled(TransmissionComponent t)
        {
            ShiftController c;
            if (t != null && Controlled.TryGetValue(t, out c) && c != null && t.shiftDelegate != c.Delegate)
            {
                if (t.shiftDelegate != null && t.shiftDelegate.Target is TransmissionComponent)
                {
                    c.StockDelegate = t.shiftDelegate;
                    c.StockType = t.transmissionType;
                }
                t.shiftDelegate = c.Delegate;
            }
        }

        /// <summary>
        /// Rewrite the gear list in place: reverse + neutral copied from stock (never altered),
        /// then n forward gears = stock (or continued) ratio x factor. Allocation-free except
        /// when the list has to grow past its capacity (only on a count change).
        /// </summary>
        internal static void WriteGears(TransmissionComponent t, GearboxData d, int n, GearboxPreset p)
        {
            float[] stock = d.Gears;
            int reverse = d.Reverse;
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
                g[reverse + 1 + k] = BaseRatio(d, p, k, n) * (p != null ? p.Scale(k + 1) : 1f);
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
            if (t != null)
            {
                UnhookShifter(d, t);
            }
            ClutchComponent c = r.Vc.powertrain.clutch;
            if (d.HasClutch && c != null)
            {
                // A clutch value the game changed since our last write is the game's: keep it.
                c.slipTorque = d.ClutchWritten ? Drift.Adopt(d.SlipTorque, d.LastSlipTorque, c.slipTorque) : d.SlipTorque;
                c.engagementRange = d.ClutchWritten ? Drift.Adopt(d.EngagementRange, d.LastEngagementRange, c.engagementRange) : d.EngagementRange;
                c.engagementRPM = d.ClutchWritten ? Drift.Adopt(d.EngagementRpm, d.LastEngagementRpm, c.engagementRPM) : d.EngagementRpm;
                d.ClutchWritten = false;
            }
            // 0.7.0: transmissionType is never written by the mod any more, so nothing to restore.
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
