using System;
using ApocalypterSteeringMod.Settings;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// The mod's own shifting for tuned gearboxes (0.7.0, FEATURES §1).
    ///
    /// Where the game's shifting really lives (docs/fsm-template-dump.md, gamecode/):
    ///  - The game's FSMs never write <c>transmission.Gear</c>. They write shift REQUESTS through
    ///    PlayMaker SetProperty: <c>input.ShiftInto</c> (R=-1, N=0, 1..5), <c>input.ShiftUp</c>,
    ///    <c>input.ShiftDown</c>.
    ///  - NWH's <c>TransmissionComponent.ForwardStep</c> calls <c>shiftDelegate(vc)</c> every physics
    ///    tick (ManualShift / AutomaticShift / CVTShift, TransmissionComponent.cs:405-420), then
    ///    <c>input.ResetShiftFlags()</c>. That delegate is the only code that APPLIES a shift, and it
    ///    is NWH's documented extension point ("Use transmissionType External and assign this
    ///    delegate to use your own gear shift code", :93).
    ///
    /// So while Gearbox is ON the tuner swaps this controller's delegate in for the vehicle's own
    /// (VehicleTuner.Gearbox HookShifter) instead of Harmony-patching every PlayMaker SetProperty in
    /// the game: the game's request writes still happen and are READ here (never written — README
    /// §2.2), NWH's own shift application is replaced, and OFF puts the captured delegate back (one
    /// field write). transmissionType is never written: changing it makes NWH re-assign its own
    /// delegate on the next tick (:405), so the mode only selects this controller's logic.
    ///
    /// Shift points: per gear, from the live ratio list. An upshift must land ABOVE the next gear's
    /// downshift point with hysteresis (otherwise the box hunts: every shift opens the clutch for
    /// shiftDuration and the car revs without driving). All decision math is pure static (harness).
    /// The per-tick path is allocation-free.
    /// </summary>
    public sealed class ShiftController
    {
        // ---------------------------------------------------------------- tuning constants
        public const float Hysteresis = 0.9f;          // downshift point <= 90 % of the RPM an upshift lands on
        public const float LimiterCeiling = 0.97f;     // NWH's own variable-shift ceiling (TransmissionComponent.cs:600)
        public const float DownIdleFloor = 1.1f;       // NWH clamps its target downshift to >= 1.1 x idle (:601)
        public const float UpIdleFloor = 1.25f;        // an upshift point never sits at idle
        public const float KickdownThrottle = 0.8f;    // full-throttle kickdown above this
        public const float KickdownRaise = 0.15f;      // shift points move up 15 % (x KickdownScale)
        public const float CreepSpeed = 2f;            // m/s: below this an automatic holds 1st
        public const float MinShiftInterval = 0.6f;    // s between the controller's own shifts (Manual-type boxes have no post-shift ban)
        public const float PedalDeadZone = 0.05f;      // NWH's INPUT_DEADZONE
        public const int NoRequest = -999;             // VehicleInputHandler.ResetShiftFlags value

        // ---------------------------------------------------------------- pure math

        /// <summary>Shift-point multiplier at this throttle: 1, or 1 + 15 % x scale at full throttle.</summary>
        public static float KickdownFactor(float throttle, float kickdownScale)
        {
            return throttle > KickdownThrottle ? 1f + KickdownRaise * kickdownScale : 1f;
        }

        /// <summary>
        /// Upshift RPM out of a gear whose ratio step to the next gear is <paramref name="step"/>
        /// (= next ratio / this ratio, below 1 for a normal box). Starts at the vehicle's own
        /// upshift RPM x factor x kickdown, never below 1.25 x idle, and is raised until the RPM the
        /// shift lands on (up x step) clears the next gear's lowest downshift point (1.1 x idle) with
        /// the hysteresis margin. Capped at 97 % of the rev limiter; when even the cap cannot land the
        /// next gear above that floor, the next gear is unusable from here: +Infinity (no upshift).
        /// </summary>
        public static float UpshiftPoint(float baseUp, float upFactor, float step, float idle, float limiter, float kick)
        {
            float ceiling = limiter > 0f ? limiter * LimiterCeiling : float.MaxValue;
            float up = baseUp * upFactor * kick;
            float floor = idle * UpIdleFloor;
            if (up < floor)
            {
                up = floor;
            }
            float need = 0f;
            if (step > 0f && step < 1f)
            {
                need = idle * DownIdleFloor / (Hysteresis * step);
                if (up < need)
                {
                    up = need;
                }
            }
            if (up > ceiling)
            {
                if (need > ceiling)
                {
                    return float.PositiveInfinity;
                }
                up = ceiling;
            }
            return up;
        }

        /// <summary>
        /// Downshift RPM of a gear entered from the gear below with ratio step <paramref name="step"/>
        /// and that gear's upshift point <paramref name="upBelow"/>: the vehicle's own downshift RPM x
        /// factor x kickdown, capped at hysteresis x the RPM the upshift lands on (so a downshift
        /// never lands above the lower gear's upshift point — no hunting), floored at 1.1 x idle.
        /// </summary>
        public static float DownshiftPoint(float baseDown, float downFactor, float upBelow, float step, float idle, float kick)
        {
            float down = baseDown * downFactor * kick;
            if (step > 0f && step < 1f && !float.IsInfinity(upBelow))
            {
                float cap = upBelow * step * Hysteresis;
                if (down > cap)
                {
                    down = cap;
                }
            }
            float floor = idle * DownIdleFloor;
            return down < floor ? floor : down;
        }

        /// <summary>
        /// Automatic target from a forward gear (1..forward): hold 1st below the creep speed (a
        /// higher gear drops straight to 1st), else one gear up above <paramref name="up"/>, one
        /// down below <paramref name="down"/>, else stay.
        /// </summary>
        public static int AutoForwardTarget(int gear, int forward, float rpm, float speed, float up, float down)
        {
            if (gear < 1)
            {
                return gear;
            }
            if (gear > forward)
            {
                return forward;
            }
            if (speed < CreepSpeed)
            {
                return 1;
            }
            if (gear < forward && rpm > up)
            {
                return gear + 1;
            }
            if (gear > 1 && rpm < down)
            {
                return gear - 1;
            }
            return gear;
        }

        /// <summary>
        /// NWH's drive/neutral/reverse rules for a forward gear (AutomaticShift's "speed &lt;= 0.4"
        /// branches, TransmissionComponent.cs:660-680). <paramref name="requireShiftInput"/> =
        /// AutomaticTransmissionDNRShiftType.RequireShiftInput. Returns the gear when nothing applies.
        /// </summary>
        public static int DnrFromForward(int gear, float speed, float throttle, bool requireShiftInput,
            bool shiftDown, int shiftInto, float dnrThreshold)
        {
            if (gear < 1 || speed > dnrThreshold)
            {
                return gear;
            }
            if (!requireShiftInput)
            {
                return throttle < PedalDeadZone ? 0 : gear;
            }
            if (shiftDown || shiftInto == 0)
            {
                return 0;
            }
            if (shiftInto == -1 && speed < dnrThreshold)
            {
                return -1;
            }
            return gear;
        }

        /// <summary>
        /// NWH's "Auto" DNR rules in neutral/reverse (used when the vehicle's own delegate is not an
        /// automatic one, i.e. a Manual-type car run in the controller's Automatic mode):
        /// N + throttle -> 1st, N + brakes -> R1; R + slow with brakes (or no throttle) -> N.
        /// Throttle/brakes are NWH's input-swapped values.
        /// </summary>
        public static int DnrFromNeutralOrReverse(int gear, float speed, float throttle, float brakes, float dnrThreshold)
        {
            if (gear == 0)
            {
                if (throttle > PedalDeadZone)
                {
                    return 1;
                }
                return brakes > PedalDeadZone ? -1 : 0;
            }
            if (gear < 0 && speed < dnrThreshold && (brakes > PedalDeadZone || throttle < PedalDeadZone))
            {
                return 0;
            }
            return gear;
        }

        /// <summary>
        /// Manual request mapping (NWH ManualShift semantics, :711-735): ShiftUp, else ShiftDown,
        /// else a direct ShiftInto (&gt; -100), clamped to the tuned box [-reverse, forward]. No
        /// request = stay.
        /// </summary>
        public static int ManualTarget(int gear, bool shiftUp, bool shiftDown, int shiftInto, int forward, int reverse)
        {
            int t;
            if (shiftUp)
            {
                t = gear + 1;
            }
            else if (shiftDown)
            {
                t = gear - 1;
            }
            else if (shiftInto > -100)
            {
                t = shiftInto;
            }
            else
            {
                return gear;
            }
            if (t > forward)
            {
                t = forward;
            }
            if (t < -reverse)
            {
                t = -reverse;
            }
            return t;
        }

        /// <summary>Does the controller run its automatic logic? Mode Stock follows the vehicle's own type.</summary>
        public static bool IsAutomatic(GearboxMode mode, TransmissionComponent.TransmissionShiftType vehicleType)
        {
            switch (mode)
            {
                case GearboxMode.Manual: return false;
                case GearboxMode.Automatic: return true;
                default: return vehicleType == TransmissionComponent.TransmissionShiftType.Automatic;
            }
        }

        // ---------------------------------------------------------------- per-vehicle instance

        /// <summary>The delegate the tuner installs (created once per vehicle, reused across hooks).</summary>
        public readonly TransmissionComponent.Shift Delegate;

        /// <summary>The vehicle's own delegate and type at hook time (put back on OFF).</summary>
        public TransmissionComponent.Shift StockDelegate;
        public TransmissionComponent.TransmissionShiftType StockType;

        private float _clock;              // physics time seen by this controller (sum of fixedDeltaTime)
        private float _lastShift = -10f;
        private bool _faulted;

        /// <summary>Test/diagnostic: shifts this controller started.</summary>
        public int ShiftCount { get; private set; }

        public ShiftController()
        {
            Delegate = Step;
        }

        private void Step(VehicleController vc)
        {
            if (_faulted)
            {
                // A previous tick threw: hand shifting back to the vehicle's own logic.
                if (StockDelegate != null)
                {
                    StockDelegate(vc);
                }
                return;
            }
            try
            {
                StepCore(vc);
            }
            catch (Exception ex)
            {
                // Never let a fault escape into NWH's ForwardStep (it would throw every tick).
                _faulted = true;
                VehicleTuner.LogFault("Shift controller", vc, ex);
            }
        }

        private void StepCore(VehicleController vc)
        {
            TransmissionComponent t = vc.powertrain.transmission;
            _clock += vc.fixedDeltaTime;
            GearboxPreset p = GearboxSettings.ActivePreset ?? GearboxPreset.Stock;
            int gear = t.Gear;

            if (!IsAutomatic(p.TransmissionMode, StockType))
            {
                if (StockType == TransmissionComponent.TransmissionShiftType.Manual && StockDelegate != null)
                {
                    // The vehicle's own ManualShift: exactly the game's manual behaviour (H-shifter
                    // hold included). Its ShiftInto bounds check already respects the tuned count.
                    StockDelegate(vc);
                    return;
                }
                if (t.isShifting)
                {
                    return;
                }
                int m = ManualTarget(gear, vc.input.ShiftUp, vc.input.ShiftDown, vc.input.ShiftInto,
                    t.forwardGearCount, t.reverseGearCount);
                if (m != gear)
                {
                    Shift(t, m);
                }
                return;
            }

            float speed = vc.Speed;
            float throttle = vc.input.InputSwappedThrottle;
            if (gear <= 0)
            {
                if (StockType == TransmissionComponent.TransmissionShiftType.Automatic && StockDelegate != null)
                {
                    // Drive/neutral/reverse stays NWH's (incl. the game's DNR type and reverse gears).
                    StockDelegate(vc);
                    NoteStuckNeutral(vc, t, gear, speed, throttle);
                    return;
                }
                int dnr = DnrFromNeutralOrReverse(gear, speed, throttle, vc.input.InputSwappedBrakes, t.dnrSpeedThreshold);
                if (dnr != gear)
                {
                    Shift(t, dnr);
                }
                return;
            }
            if (t.isShifting)
            {
                return;
            }

            bool require = t.automaticTransmissionDNRShiftType
                == TransmissionComponent.AutomaticTransmissionDNRShiftType.RequireShiftInput;
            int target = DnrFromForward(gear, speed, throttle, require, vc.input.ShiftDown, vc.input.ShiftInto, t.dnrSpeedThreshold);
            if (target == gear)
            {
                if (_clock - _lastShift < MinShiftInterval)
                {
                    return;
                }
                int forward = t.forwardGearCount;
                int rev = t.reverseGearCount;
                float idle = vc.powertrain.engine != null ? vc.powertrain.engine.idleRPM : 0f;
                float limiter = vc.powertrain.engine != null ? vc.powertrain.engine.revLimiterRPM : 0f;
                float kick = KickdownFactor(throttle, p.KickdownScale);
                float up = float.PositiveInfinity, down = 0f;
                float r = t.gears[rev + gear];
                if (gear < forward && r > 0f)
                {
                    up = UpshiftPoint(t.UpshiftRPM, p.ShiftUpFactor, t.gears[rev + gear + 1] / r, idle, limiter, kick);
                }
                if (gear > 1)
                {
                    float rBelow = t.gears[rev + gear - 1];
                    float step = rBelow > 0f ? r / rBelow : 1f;
                    float upBelow = UpshiftPoint(t.UpshiftRPM, p.ShiftUpFactor, step, idle, limiter, kick);
                    down = DownshiftPoint(t.DownshiftRPM, p.ShiftDownFactor, upBelow, step, idle, kick);
                }
                target = AutoForwardTarget(gear, forward, t.ReferenceShiftRPM, speed, up, down);
            }
            if (target != gear)
            {
                Shift(t, target);
            }
        }

        private void Shift(TransmissionComponent t, int target)
        {
            int before = t.Gear;
            t.ShiftInto(target);
            if (t.isShifting || t.Gear != before)
            {
                _lastShift = _clock;
                ShiftCount++;
            }
        }

        // -------------------------------------------------------------- diagnostics (0.7.5)

        private float _neutralThrottleTime;
        private float _lastNeutralLog;

        /// <summary>
        /// The user report "wheels don't turn when the game loads with a custom gearbox enabled"
        /// had no fault lines, so this logs the stuck state directly: a controller that has been
        /// in neutral with the throttle held for 10+ s reports the DNR type, the gear, the
        /// throttle/speed and whose delegate is running. One line per 10 s.
        /// </summary>
        private void NoteStuckNeutral(VehicleController vc, TransmissionComponent t, int gear, float speed, float throttle)
        {
            if (throttle > PedalDeadZone)
            {
                _neutralThrottleTime += vc.fixedDeltaTime;
                if (_neutralThrottleTime - _lastNeutralLog >= 10f)
                {
                    _lastNeutralLog = _neutralThrottleTime;
                    if (Plugin.Log != null)
                    {
                        bool ownDelegate = StockDelegate != null && StockDelegate.Target is TransmissionComponent;
                        Plugin.Log.LogWarning("Shift controller: '" + VehicleTuner.VehicleName(vc) + "' has been in neutral for "
                            + _neutralThrottleTime.ToString("0") + "s while throttle = " + throttle.ToString("0.00")
                            + ", speed = " + speed.ToString("0.0") + " m/s, gear = " + gear
                            + ", dnr = " + t.automaticTransmissionDNRShiftType
                            + ", running " + (ownDelegate ? "the game's own delegate" : "the fallback delegate")
                            + ", forward gears = " + t.forwardGearCount + ", isShifting = " + t.isShifting);
                    }
                }
            }
            else
            {
                _neutralThrottleTime = 0f;
            }
        }
    }
}
