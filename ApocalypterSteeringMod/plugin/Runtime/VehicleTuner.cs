using System;
using System.Collections.Generic;
using ApocalypterSteeringMod.Persistence;
using ApocalypterSteeringMod.Settings;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Modules.Aerodynamics;
using NWH.VehiclePhysics2.Powertrain;
using NWH.VehiclePhysics2.Powertrain.Wheel;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Applies every tuning category to all live vehicles:
    ///   effective = the vehicle's captured stock value x the active preset factor
    ///   (Alignment: stock geometry + offset).
    /// Stock values are captured the first time a vehicle is seen and restored when
    /// a category is switched off. Per-system logic lives in VehicleTuner.Systems.cs,
    /// VehicleTuner.Assists.cs, VehicleTuner.Alignment.cs and VehicleTuner.Gearbox.cs.
    ///
    /// Cost model: a scene scan (FindObjectsOfType) runs every few seconds and on
    /// explicit request; dragging a slider only re-applies to already-known vehicles.
    /// </summary>
    public sealed partial class VehicleTuner : MonoBehaviour
    {
        private const float SCAN_INTERVAL = 2f;

        internal sealed class WheelData
        {
            public bool IsFront;
            public bool IsLeft;                                              // transform.localPosition.x < 0 at capture
            public WheelRole Role;
            public float SpringForce, SpringLength, BumpRate, ReboundRate;   // suspension
            public float LngGrip, LatGrip, LngStiff, LatStiff;               // grip
            public float Camber;                                             // alignment
            public Vector3 LocalPos, LocalEuler;                             // alignment
            public bool CamberLocked;                                        // CamberController or solid axle: never write camber
            public bool HasCamberController;
        }

        internal sealed class GroupData
        {
            public bool IsFront;
            public float ArbForce;                    // suspension
            public float BrakeCoeff, HandbrakeCoeff;  // brakes
            public float Caster, Toe;                 // alignment
            public bool ApplyCaster, ApplyToe;        // alignment gates (restored on OFF)
            public bool SolidCamber;                  // isSolid && 2 wheels && trackWidth != 0 (WheelGroup.Update overwrites camber)
        }

        internal sealed class BrakesData
        {
            public float MaxTorque, ActuationTime;
        }

        /// <summary>Which axle a differential feeds (resolved from its outputs, not its list index).</summary>
        internal enum DiffRole
        {
            Front,
            Rear,
            Center
        }

        internal sealed class DrivetrainData
        {
            public bool HasEngine, HasTransmission;
            public float MaxPower, RevLimiterRPM, LossPercent, BoostGain;
            public float FinalGearRatio, UpshiftRPM, DownshiftRPM, ShiftDuration;
            public DifferentialComponent.Type[] DiffModes;   // per differential index
            public float[] DiffBias, DiffStiff;
            public bool[] DiffCaptured;                      // false = slot was null at capture: never written
            public LayoutData Layout;                        // 0.6.2 stock wiring + custom layout state (null: no transmission/wheels)
            // 0.7.0 drift tracking: what ApplyDrivetrain last wrote (see Drift).
            public bool Written;
            public float LastMaxPower, LastRevLimiterRPM, LastLossPercent, LastBoostGain;
            public float LastFinalGearRatio, LastUpshiftRPM, LastDownshiftRPM, LastShiftDuration;
        }

        internal sealed class AeroData
        {
            public AerodynamicsModule Module;    // null = vehicle has none (not yet onboarded)
            public bool Onboarded;               // we created it (reused, never re-onboarded)
            public bool WasSimulateDrag, WasSimulateDownforce;
            public float FrontalCd, SideCd, MaxDownforceSpeed;
            public DownforcePoint[] Points;      // deep copy of baseline maxForce/position
        }

        internal sealed class GearboxData
        {
            public bool HasTransmission, HasClutch;
            public bool Standard;              // NWH layout (negatives, one 0, positives): else gears are never touched
            public float[] Gears;             // deep copy of the stock list (reverse..., 0, forward...)
            public int Reverse;                // entries before the neutral 0
            public int Forward;                // stock forward gear count
            public float[] Extended;           // stock forward ratios continued to 12 (index 0 = 1st gear)
            public bool IsCvt;
            public TransmissionComponent.TransmissionShiftType Type;
            public float SlipTorque, EngagementRange, EngagementRpm;
            public bool PendingTrim;           // restore left placeholder gears behind an in-flight shift
            // 0.7.0 shift controller + drift tracking.
            public TransmissionComponent.Shift StockShift;   // the vehicle's own delegate at capture (diagnostic)
            public ShiftController Shifter;                  // created once per record, reused across hooks
            public bool Hooked;                              // our delegate is installed
            public bool ClutchWritten;                       // Last* hold what we last wrote
            public float LastSlipTorque, LastEngagementRange, LastEngagementRpm;
        }

        internal sealed class AssistHandles
        {
            public Brakes.BrakeTorqueModifier Abs;
            public EngineComponent.PowerModifier Tcs;
            public bool AbsRegistered, TcsRegistered;
        }

        internal sealed class VehicleRecord
        {
            public VehicleController Vc;
            public Dictionary<WheelUAPI, WheelData> Wheels = new Dictionary<WheelUAPI, WheelData>();
            public Dictionary<WheelGroup, GroupData> Groups = new Dictionary<WheelGroup, GroupData>();
            public BrakesData Brakes;
            public DrivetrainData Drivetrain;
            public AeroData Aero;
            public GearboxData Gearbox;
            public AssistHandles Assists;
            public bool HasTyreWear;
            public bool AlignmentMoved;     // wheel positions currently offset (wheelbase/trackWidth stale)
            public AppliedCat Applied;      // categories THIS record currently has applied (targeting)
            // Telemetry pick liveness (0.6.4): the input sum at the last sample and when it
            // last changed. The game's FSM freezes a parked car's input at its exit values
            // (handbrake held, brakes last pressed); only recently-changed input is "live".
            public float InputPrev;
            public bool InputSeen;
            public float InputLastChange = -1f;
        }

        [Flags]
        internal enum AppliedCat
        {
            None = 0,
            Suspension = 1,
            Aero = 2,
            Brakes = 4,
            Grip = 8,
            Drivetrain = 16,
            Assists = 32,
            Alignment = 64,
            Gearbox = 128
        }

        private readonly Dictionary<VehicleController, VehicleRecord> _records = new Dictionary<VehicleController, VehicleRecord>();
        // Insertion order of the records: the "reference vehicle" for per-wheel UI is the first one.
        private readonly List<VehicleRecord> _order = new List<VehicleRecord>();
        private readonly List<VehicleController> _dead = new List<VehicleController>();
        private float _nextScan;
        private bool _suspApplied, _aeroApplied, _brakesApplied, _gripApplied, _drivetrainApplied, _assistsApplied,
            _alignmentApplied, _gearboxApplied;

        /// <summary>Number of vehicles currently tracked (shown in the panel).</summary>
        public int TrackedVehicles
        {
            get { return _records.Count; }
        }

        /// <summary>True when any tracked vehicle carries a TyreWear component (it rewrites grip).</summary>
        public bool AnyTyreWear
        {
            get
            {
                foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
                {
                    if (kv.Value.HasTyreWear)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private void OnEnable()
        {
            ModConfig.SettingsChanged += ReapplyNow;
            _nextScan = 0f;   // re-apply on the next frame after a disable/enable cycle
        }

        private void OnDisable()
        {
            ModConfig.SettingsChanged -= ReapplyNow;
            // A disabled tuner stops applying, so it must hand the vehicles back now.
            // Otherwise a replacement runner would capture our tuned values as "stock"
            // (factors compound, and OFF would restore to tuned values). OnEnable's
            // next scan re-applies.
            RestoreAll();
        }

        private void OnDestroy()
        {
            RestoreAll();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan)
            {
                return;
            }
            _nextScan = Time.unscaledTime + SCAN_INTERVAL;
            ScanVehicles();
            ApplyLive();
        }

        /// <summary>Find new vehicles, then apply (or restore, when disabled). Use after toggles / on open.</summary>
        public void ReapplyNow()
        {
            _nextScan = Time.unscaledTime + SCAN_INTERVAL;
            ScanVehicles();
            ApplyLive();
        }

        /// <summary>
        /// Apply the current settings to vehicles already tracked. Cheap; safe to call every slider tick.
        /// A category that goes from not-applied to applied first re-reads its stock values
        /// (RefreshBaselines): while it was off the fields belonged to the game, so whatever
        /// the game set since the vehicle was first seen is the stock to scale and to restore.
        /// </summary>
        public void ApplyLive()
        {
            BeginTargetPass();
            try
            {
                ApplyLiveCore();
            }
            finally
            {
                _passActive = false;
            }
        }

        // 0.7.0: "Last driven" is resolved ONCE per pass. 0.6.x called FindDrivenVehicle (a scan of
        // every record that also advances each record's input-liveness state) from IsTarget, i.e.
        // once per record per category: O(categories x vehicles^2) per slider tick, and the pick
        // could in principle change half-way through a pass.
        private bool _passActive;
        private VehicleController _passDriven;

        private void BeginTargetPass()
        {
            _passDriven = TargetSettings.Mode == TargetMode.LastDriven ? FindDrivenVehicle() : null;
            _passActive = true;
        }

        private void ApplyLiveCore()
        {
            // 0.6.0: drop destroyed vehicles first. ApplyLive runs on every slider tick, i.e.
            // between scans; 0.5.0 then wrote into a destroyed vehicle (and could onboard an
            // aero module into it, which throws inside NWH and aborted every later category).
            PurgeDead();

            if (SuspensionSettings.Enabled) { if (!_suspApplied) RefreshBaselines(Category.Suspension); ApplyAllSuspension(); _suspApplied = true; }
            else if (_suspApplied) { RestoreAllSuspension(); _suspApplied = false; }

            if (AeroSettings.Enabled) { if (!_aeroApplied) RefreshBaselines(Category.Aero); ApplyAllAero(); _aeroApplied = true; }
            else if (_aeroApplied) { RestoreAllAero(); _aeroApplied = false; }

            if (BrakesSettings.Enabled) { if (!_brakesApplied) RefreshBaselines(Category.Brakes); ApplyAllBrakes(); _brakesApplied = true; }
            else if (_brakesApplied) { RestoreAllBrakes(); _brakesApplied = false; }

            if (GripSettings.Enabled) { if (!_gripApplied) RefreshBaselines(Category.Grip); ApplyAllGrip(); _gripApplied = true; }
            else if (_gripApplied) { RestoreAllGrip(); _gripApplied = false; }

            if (DrivetrainSettings.Enabled) { if (!_drivetrainApplied) RefreshBaselines(Category.Drivetrain); ApplyAllDrivetrain(); _drivetrainApplied = true; }
            else if (_drivetrainApplied) { RestoreAllDrivetrain(); _drivetrainApplied = false; }

            if (AssistsSettings.Enabled) { ApplyAllAssists(); _assistsApplied = true; }
            else if (_assistsApplied) { RestoreAllAssists(); _assistsApplied = false; }

            if (AlignmentSettings.Enabled) { if (!_alignmentApplied) RefreshBaselines(Category.Alignment); ApplyAllAlignment(); _alignmentApplied = true; }
            else if (_alignmentApplied) { RestoreAllAlignment(); _alignmentApplied = false; }

            if (GearboxSettings.Enabled) { if (!_gearboxApplied) RefreshBaselines(Category.Gearbox); ApplyAllGearbox(); _gearboxApplied = true; }
            else
            {
                if (_gearboxApplied) { RestoreAllGearbox(); _gearboxApplied = false; }
                TrimPendingGearbox();
            }
        }

        /// <summary>Forget destroyed vehicles (allocation-free: reuses _dead).</summary>
        private void PurgeDead()
        {
            _dead.Clear();
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                if (kv.Key == null)
                {
                    _dead.Add(kv.Key);
                }
            }
            for (int i = 0; i < _dead.Count; i++)
            {
                VehicleRecord r;
                if (_records.TryGetValue(_dead[i], out r))
                {
                    _order.Remove(r);
                }
                _records.Remove(_dead[i]);
            }
            _dead.Clear();
            if (_captureFailures.Count > 0)
            {
                foreach (KeyValuePair<VehicleController, int> kv in _captureFailures)
                {
                    if (kv.Key == null)
                    {
                        _dead.Add(kv.Key);
                    }
                }
                for (int i = 0; i < _dead.Count; i++)
                {
                    _captureFailures.Remove(_dead[i]);
                }
                _dead.Clear();
            }
        }

        // ---- spawn-wave hardening (0.6.3, docs/crash-2026-10-04.md) -------------------
        // The game loads a save by spawning hundreds of objects right after sceneLoaded. The
        // tuner's scan is read-only, but staying out of that wave entirely is cheap insurance.
        public const float SpawnQuietSeconds = 5f;   // no scans this long after a scene load
        public const int SpawnJump = 2;              // more new vehicles than this in one scan = still spawning
        public const int MaxSpawnDeferrals = 3;      // never defer capture for more than this many scans in a row
        private static float _quietUntil;            // static: a runner recreated on scene load sees it too
        private int _lastScanCount = -1;             // -1 = no scan yet: nothing to compare a jump against
        private int _spawnDeferrals;

        /// <summary>Called from Plugin.OnSceneLoaded: start the post-load quiet window.</summary>
        public static void NotifySceneLoaded()
        {
            _quietUntil = Time.unscaledTime + SpawnQuietSeconds;
        }

        /// <summary>
        /// Pure scan gate. Skip while inside the post-load quiet window; defer capture for one
        /// scan when the vehicle count jumped by more than SpawnJump since the last scan (the
        /// spawn wave is still running), but never more than MaxSpawnDeferrals scans in a row.
        /// </summary>
        public static bool ShouldSkipScan(float now, float quietUntil)
        {
            return now < quietUntil;
        }

        public static bool ShouldDeferCapture(int count, int lastCount, int deferralsSoFar)
        {
            return lastCount >= 0 && count > lastCount + SpawnJump && deferralsSoFar < MaxSpawnDeferrals;
        }

        private void ScanVehicles()
        {
            // Forget destroyed vehicles only. Vehicles that are merely inactive keep their
            // record: re-capturing one later would read our own tuned values as "stock".
            PurgeDead();
            if (ShouldSkipScan(Time.unscaledTime, _quietUntil))
            {
                return;
            }

            VehicleController[] vehicles = UnityEngine.Object.FindObjectsOfType<VehicleController>();
            int count = vehicles.Length;
            if (ShouldDeferCapture(count, _lastScanCount, _spawnDeferrals))
            {
                _lastScanCount = count;
                _spawnDeferrals++;
                return;
            }
            _lastScanCount = count;
            _spawnDeferrals = 0;
            for (int i = 0; i < vehicles.Length; i++)
            {
                VehicleController vc = vehicles[i];
                if (vc == null || _records.ContainsKey(vc))
                {
                    continue;
                }
                VehicleRecord record = TryCapture(vc);
                if (record != null)
                {
                    _records[vc] = record;
                    _order.Add(record);
                }
            }
        }

        // ---- per-category exception guards (0.6.3) -------------------------------------
        public const int MaxCaptureAttempts = 3;     // retries for a vehicle whose capture threw somewhere
        private readonly Dictionary<VehicleController, int> _captureFailures = new Dictionary<VehicleController, int>();
        private static readonly HashSet<string> LoggedFaults = new HashSet<string>();

        /// <summary>Log a fault once per key (exception path only; allocates there, never on the hot path).</summary>
        internal static void LogFault(string category, VehicleController vc, Exception ex)
        {
            string key = category + "|" + VehicleName(vc) + "|" + (ex != null ? ex.GetType().Name : "");
            if (Plugin.Log == null || LoggedFaults.Count > 256 || !LoggedFaults.Add(key))
            {
                return;
            }
            Plugin.Log.LogWarning(category + " on '" + VehicleName(vc) + "' failed and was skipped (other vehicles/categories unaffected): " + ex);
        }

        /// <summary>
        /// Capture with retries: a vehicle captured mid-spawn may throw in one category (half
        /// initialised). Such a capture is dropped and retried on the next scans; after
        /// MaxCaptureAttempts the vehicle is kept with the categories that did capture (the
        /// failed ones stay null and every apply/restore skips them).
        /// </summary>
        private VehicleRecord TryCapture(VehicleController vc)
        {
            bool incomplete;
            VehicleRecord record;
            try
            {
                record = Capture(vc, out incomplete);
            }
            catch (Exception ex)
            {
                // The wheel/axle pass itself threw: nothing usable, always retry later.
                LogFault("Capture", vc, ex);
                return null;
            }
            if (record == null)
            {
                return null;
            }
            if (incomplete)
            {
                int failures;
                _captureFailures.TryGetValue(vc, out failures);
                failures++;
                if (failures < MaxCaptureAttempts)
                {
                    _captureFailures[vc] = failures;
                    return null;
                }
            }
            _captureFailures.Remove(vc);
            return record;
        }

        /// <summary>
        /// Snapshot stock values. Returns null while the vehicle has no initialised wheels
        /// yet, so it is retried on the next scan instead of being recorded as empty.
        /// </summary>
        private static VehicleRecord Capture(VehicleController vc, out bool incomplete)
        {
            incomplete = false;
            if (vc.powertrain == null || vc.powertrain.wheelGroups == null)
            {
                return null;
            }

            // Front/rear is decided relative to the mean wheel position, not the transform
            // origin: many prefabs put the pivot at an axle, which would call every wheel "front".
            float sumZ = 0f;
            int count = 0;
            foreach (WheelGroup group in vc.powertrain.wheelGroups)
            {
                if (group == null)
                {
                    continue;
                }
                foreach (WheelComponent wc in group.Wheels)
                {
                    if (wc != null && wc.wheelUAPI != null)
                    {
                        sumZ += vc.transform.InverseTransformPoint(wc.wheelUAPI.transform.position).z;
                        count++;
                    }
                }
            }
            if (count == 0)
            {
                return null;
            }
            float meanZ = sumZ / count;

            var record = new VehicleRecord { Vc = vc };
            foreach (WheelGroup group in vc.powertrain.wheelGroups)
            {
                if (group == null)
                {
                    continue;
                }
                bool groupFront = false;
                bool groupFrontKnown = false;
                bool solid = group.isSolid && group.Wheels.Count == 2 && group.trackWidth != 0f;
                foreach (WheelComponent wc in group.Wheels)
                {
                    WheelUAPI uapi = wc != null ? wc.wheelUAPI : null;
                    if (uapi == null)
                    {
                        continue;
                    }
                    bool front = vc.transform.InverseTransformPoint(uapi.transform.position).z > meanZ;
                    if (!groupFrontKnown)
                    {
                        groupFront = front;
                        groupFrontKnown = true;
                    }
                    bool left = uapi.transform.localPosition.x < 0f;
                    bool camberController = uapi.GetComponent<NWH.WheelController3D.CamberController>() != null;
                    record.Wheels[uapi] = new WheelData
                    {
                        IsFront = front,
                        IsLeft = left,
                        Role = RoleOf(front, left),
                        SpringForce = uapi.SpringMaxForce,
                        SpringLength = uapi.SpringMaxLength,
                        BumpRate = uapi.DamperBumpRate,
                        ReboundRate = uapi.DamperReboundRate,
                        LngGrip = uapi.LongitudinalFrictionGrip,
                        LatGrip = uapi.LateralFrictionGrip,
                        LngStiff = uapi.LongitudinalFrictionStiffness,
                        LatStiff = uapi.LateralFrictionStiffness,
                        Camber = uapi.Camber,
                        LocalPos = uapi.transform.localPosition,
                        LocalEuler = uapi.transform.localEulerAngles,
                        HasCamberController = camberController,
                        CamberLocked = camberController || solid
                    };
                }
                record.Groups[group] = new GroupData
                {
                    IsFront = groupFront,
                    ArbForce = group.antiRollBarForce,
                    BrakeCoeff = group.brakeCoefficient,
                    HandbrakeCoeff = group.handbrakeCoefficient,
                    Caster = group.CasterAngle,
                    Toe = group.ToeAngle,
                    ApplyCaster = group.applyCasterAngle,
                    ApplyToe = group.applyToeAngle,
                    SolidCamber = solid
                };
            }

            // Each category on its own: one that throws (a half-initialised vehicle mid-spawn)
            // is left null and logged; the others still capture. TryCapture decides on retries.
            try
            {
                if (vc.brakes != null)
                {
                    record.Brakes = new BrakesData
                    {
                        MaxTorque = vc.brakes.maxTorque,
                        ActuationTime = vc.brakes.actuationTime
                    };
                }
            }
            catch (Exception ex) { record.Brakes = null; incomplete = true; LogFault("Brakes capture", vc, ex); }
            try
            {
                record.Drivetrain = CaptureDrivetrain(vc);
                LogStockLayout(vc, record.Drivetrain.Layout);
            }
            catch (Exception ex) { record.Drivetrain = null; incomplete = true; LogFault("Drivetrain capture", vc, ex); }
            try { record.Aero = CaptureAero(vc); }
            catch (Exception ex) { record.Aero = null; incomplete = true; LogFault("Aero capture", vc, ex); }
            try { record.Gearbox = CaptureGearbox(vc); }
            catch (Exception ex) { record.Gearbox = null; incomplete = true; LogFault("Gearbox capture", vc, ex); }
            try { record.Assists = CreateAssistHandles(vc); }
            catch (Exception ex) { record.Assists = null; incomplete = true; LogFault("Assists capture", vc, ex); }
            try { record.HasTyreWear = HasTyreWearComponent(vc); }
            catch (Exception ex) { incomplete = true; LogFault("TyreWear check", vc, ex); }
            return record;
        }

        internal static WheelRole RoleOf(bool front, bool left)
        {
            return front ? (left ? WheelRole.FL : WheelRole.FR) : (left ? WheelRole.RL : WheelRole.RR);
        }

        private static DrivetrainData CaptureDrivetrain(VehicleController vc)
        {
            var dt = new DrivetrainData();
            if (vc.powertrain.engine != null)
            {
                dt.HasEngine = true;
                dt.MaxPower = vc.powertrain.engine.maxPower;
                dt.RevLimiterRPM = vc.powertrain.engine.revLimiterRPM;
                dt.LossPercent = vc.powertrain.engine.engineLossPercent;
                dt.BoostGain = vc.powertrain.engine.forcedInduction != null
                    ? vc.powertrain.engine.forcedInduction.powerGainMultiplier : 1.4f;
            }
            if (vc.powertrain.transmission != null)
            {
                dt.HasTransmission = true;
                dt.FinalGearRatio = vc.powertrain.transmission.finalGearRatio;
                dt.UpshiftRPM = vc.powertrain.transmission.UpshiftRPM;
                dt.DownshiftRPM = vc.powertrain.transmission.DownshiftRPM;
                dt.ShiftDuration = vc.powertrain.transmission.shiftDuration;
            }
            if (vc.powertrain.differentials != null)
            {
                int n = vc.powertrain.differentials.Count;
                dt.DiffModes = new DifferentialComponent.Type[n];
                dt.DiffBias = new float[n];
                dt.DiffStiff = new float[n];
                dt.DiffCaptured = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    // Count-guarded array access — the vc.DiffFrontType convenience
                    // getters index unguarded and throw on short differential lists.
                    DifferentialComponent diff = vc.powertrain.differentials[i];
                    if (diff == null)
                    {
                        continue;
                    }
                    dt.DiffModes[i] = diff.DifferentialType;
                    dt.DiffBias[i] = diff.biasAB;
                    dt.DiffStiff[i] = diff.stiffness;
                    dt.DiffCaptured[i] = true;
                }
            }
            dt.Layout = CaptureLayout(vc);
            return dt;
        }

        internal enum Category
        {
            Suspension,
            Aero,
            Brakes,
            Grip,
            Drivetrain,
            Alignment,
            Gearbox
        }

        /// <summary>
        /// Re-read one category's stock values on every tracked vehicle. Only called while
        /// that category is NOT applied (its fields hold the game's values, never ours), so
        /// this can never capture tuned values. Without it the baseline was frozen at first
        /// sight: a value the game changed later (while the category was off) was scaled from
        /// the stale number on enable and overwritten with it on disable. The category's own
        /// fields only: categories never share a field, so another category's applied state
        /// cannot leak in. Runs on a toggle, not per tick (drivetrain/aero/gearbox re-capture allocates).
        /// </summary>
        private void RefreshBaselines(Category category)
        {
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
                if (kv.Key == null || r.Vc == null)
                {
                    continue;
                }
                try
                {
                    RefreshBaseline(r, category);
                }
                catch (Exception ex)
                {
                    LogFault(category + " baseline refresh", kv.Key, ex);
                }
            }
        }

        private static void RefreshBaseline(VehicleRecord r, Category category)
        {
            switch (category)
            {
                case Category.Suspension:
                    foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
                    {
                        WheelUAPI u = wk.Key;
                        if (u == null)
                        {
                            continue;
                        }
                        wk.Value.SpringForce = u.SpringMaxForce;
                        wk.Value.SpringLength = u.SpringMaxLength;
                        wk.Value.BumpRate = u.DamperBumpRate;
                        wk.Value.ReboundRate = u.DamperReboundRate;
                    }
                    foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
                    {
                        if (gk.Key != null)
                        {
                            gk.Value.ArbForce = gk.Key.antiRollBarForce;
                        }
                    }
                    break;

                case Category.Grip:
                    foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
                    {
                        WheelUAPI u = wk.Key;
                        if (u == null)
                        {
                            continue;
                        }
                        wk.Value.LngGrip = u.LongitudinalFrictionGrip;
                        wk.Value.LatGrip = u.LateralFrictionGrip;
                        wk.Value.LngStiff = u.LongitudinalFrictionStiffness;
                        wk.Value.LatStiff = u.LateralFrictionStiffness;
                    }
                    break;

                case Category.Brakes:
                    if (r.Vc.brakes != null)
                    {
                        if (r.Brakes == null)
                        {
                            r.Brakes = new BrakesData();
                        }
                        r.Brakes.MaxTorque = r.Vc.brakes.maxTorque;
                        r.Brakes.ActuationTime = r.Vc.brakes.actuationTime;
                    }
                    foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
                    {
                        if (gk.Key != null)
                        {
                            gk.Value.BrakeCoeff = gk.Key.brakeCoefficient;
                            gk.Value.HandbrakeCoeff = gk.Key.handbrakeCoefficient;
                        }
                    }
                    break;

                case Category.Drivetrain:
                    if (r.Vc.powertrain != null)
                    {
                        r.Drivetrain = CaptureDrivetrain(r.Vc);
                    }
                    break;

                case Category.Aero:
                    // A module we onboarded keeps its own (inert) baseline; a shipped one,
                    // or a vehicle that gained a module since, is read again.
                    if (r.Aero == null || !r.Aero.Onboarded)
                    {
                        r.Aero = CaptureAero(r.Vc);
                    }
                    break;

                case Category.Alignment:
                    RefreshAlignmentBaseline(r);
                    break;

                case Category.Gearbox:
                    if (r.Vc.powertrain != null && (r.Gearbox == null || !r.Gearbox.PendingTrim))
                    {
                        r.Gearbox = CaptureGearbox(r.Vc);
                    }
                    break;
            }
        }

        private static bool HasTyreWearComponent(VehicleController vc)
        {
            // TyreWear rewrites the grip properties every frame — flag it so the
            // panel can warn. Never referenced by game code, but some prefab may
            // still carry the component.
            var found = vc.GetComponentsInChildren<NWH.WheelController3D.TyreWear>(true);
            return found != null && found.Length > 0;
        }

        // --------------------------------------------------------------- readouts

        public enum Readout
        {
            SpringForce,
            RideHeight,
            BumpRate,
            ReboundRate,
            ArbForce,
            BrakeTorque   // 0.6.0: brakes.maxTorque (axle-independent; 'front' is ignored)
        }

        /// <summary>Mean stock baseline over tracked vehicles for one axle (0 when none tracked).</summary>
        public float MeanBaseline(Readout kind, bool front)
        {
            float sum = 0f;
            int count = 0;
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
                if (kind == Readout.BrakeTorque)
                {
                    if (r.Brakes != null)
                    {
                        sum += r.Brakes.MaxTorque;
                        count++;
                    }
                    continue;
                }
                if (kind == Readout.ArbForce)
                {
                    foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
                    {
                        if (gk.Key != null && gk.Value.IsFront == front)
                        {
                            sum += gk.Value.ArbForce;
                            count++;
                        }
                    }
                    continue;
                }
                foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
                {
                    if (wk.Key == null || wk.Value.IsFront != front)
                    {
                        continue;
                    }
                    switch (kind)
                    {
                        case Readout.SpringForce: sum += wk.Value.SpringForce; break;
                        case Readout.RideHeight: sum += wk.Value.SpringLength; break;
                        case Readout.BumpRate: sum += wk.Value.BumpRate; break;
                        case Readout.ReboundRate: sum += wk.Value.ReboundRate; break;
                    }
                    count++;
                }
            }
            return count == 0 ? 0f : sum / count;
        }

        // ------------------------------------------------- reference vehicle (per-wheel UI)

        /// <summary>Stock geometry of one wheel of the reference (first tracked) vehicle.</summary>
        public struct WheelStock
        {
            public bool Valid;
            public float Camber;
            public Vector3 LocalPos;
            public Vector3 LocalEuler;
            public bool CamberLocked;
            public bool HasCamberController;
        }

        /// <summary>Stock geometry of one axle of the reference vehicle.</summary>
        public struct GroupStock
        {
            public bool Valid;
            public float Caster, Toe;
            public bool Solid;
        }

        private VehicleRecord ReferenceRecord()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                VehicleRecord r = _order[i];
                if (r != null && r.Vc != null)
                {
                    return r;
                }
            }
            return null;
        }

        public bool HasReferenceVehicle
        {
            get { return ReferenceRecord() != null; }
        }

        public WheelStock ReferenceWheelStock(WheelRole role)
        {
            VehicleRecord r = ReferenceRecord();
            if (r != null)
            {
                foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
                {
                    if (wk.Key != null && wk.Value.Role == role)
                    {
                        return new WheelStock
                        {
                            Valid = true,
                            Camber = wk.Value.Camber,
                            LocalPos = wk.Value.LocalPos,
                            LocalEuler = wk.Value.LocalEuler,
                            CamberLocked = wk.Value.CamberLocked,
                            HasCamberController = wk.Value.HasCamberController
                        };
                    }
                }
            }
            return new WheelStock();
        }

        public GroupStock ReferenceGroupStock(bool front)
        {
            VehicleRecord r = ReferenceRecord();
            if (r != null)
            {
                foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
                {
                    if (gk.Key != null && gk.Value.IsFront == front)
                    {
                        return new GroupStock { Valid = true, Caster = gk.Value.Caster, Toe = gk.Value.Toe, Solid = gk.Value.SolidCamber };
                    }
                }
            }
            return new GroupStock();
        }

        /// <summary>Stock forward gear count of the reference vehicle (0 = none / no transmission).</summary>
        public int ReferenceGearCount
        {
            get
            {
                VehicleRecord r = ReferenceRecord();
                return r != null && r.Gearbox != null && r.Gearbox.HasTransmission ? r.Gearbox.Forward : 0;
            }
        }

        public bool ReferenceIsCvt
        {
            get
            {
                VehicleRecord r = ReferenceRecord();
                return r != null && r.Gearbox != null && r.Gearbox.IsCvt;
            }
        }

        /// <summary>
        /// Base ratio of forward gear <paramref name="gear"/> (1..12) of the reference vehicle under the
        /// shown preset (its own ratio, continued past its count, or 0.7.0's spread), before the
        /// per-gear factor; 0 when unknown.
        /// </summary>
        public float ReferenceGearStock(int gear)
        {
            VehicleRecord r = ReferenceRecord();
            if (r == null || r.Gearbox == null || r.Gearbox.Extended == null || gear < 1 || gear > r.Gearbox.Extended.Length)
            {
                return 0f;
            }
            GearboxPreset p = GearboxSettings.Shown;
            return BaseRatio(r.Gearbox, p, gear - 1, TargetForward(r.Gearbox, p));
        }

        /// <summary>Any tracked vehicle with a camber the mod must not write (CamberController / solid axle).</summary>
        public bool AnyCamberLocked
        {
            get
            {
                foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
                {
                    foreach (KeyValuePair<WheelUAPI, WheelData> wk in kv.Value.Wheels)
                    {
                        if (wk.Value.CamberLocked)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public bool AnyCvt
        {
            get
            {
                foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
                {
                    if (kv.Value.Gearbox != null && kv.Value.Gearbox.IsCvt)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Tracked vehicles whose gearbox the mod's shift controller currently drives.</summary>
        public int ShiftControlledCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
                {
                    if (kv.Key != null && kv.Value.Gearbox != null && kv.Value.Gearbox.Hooked)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        // --------------------------------------------------------------- telemetry

        public struct TelemetrySample
        {
            public float SpeedKmh;
            public float Rpm;
            public string Gear;
            public float FrontSlip;   // mean |LateralSlip| of the front wheels (NWH's normalised slip)
            public float RearSlip;    // mean |LateralSlip| of the rear wheels
            public float LatG;        // |yaw rate| x speed / g
            public float LongG;       // speed delta per second / g (smoothed)
            public float SteeringDeg; // mean front wheel steer angle
            public float Throttle;    // 0..1 input
            public float Brakes;      // 0..1 input
        }

        private float _telemetryPrevSpeed = -1f;
        private float _telemetryPrevTime;

        /// <summary>
        /// The driven vehicle's live numbers. False when no vehicle is tracked.
        /// Read-only; called at 4 Hz by the telemetry strip (allocation there is fine).
        /// NWH's Vehicle.ActiveVehicle is unreliable here: the game never sets
        /// isPlayerControllable, so ActiveVehicles stays empty (Vehicle.cs:153-156).
        /// The driven car is instead the tracked vehicle with the most live FSM input
        /// (the game writes vc.input every frame for exactly one car), falling back to
        /// the fastest, then the first tracked.
        /// </summary>
        public bool TryGetTelemetry(out TelemetrySample s)
        {
            DebugDumpPick();
            s = new TelemetrySample();
            VehicleController vc = FindDrivenVehicle();
            if (vc == null || vc.powertrain == null)
            {
                return false;
            }
            s.SpeedKmh = vc.Speed * 3.6f;
            s.Rpm = vc.powertrain.engine != null ? vc.powertrain.engine.OutputRPM : 0f;
            s.Gear = vc.powertrain.transmission != null ? vc.powertrain.transmission.GearName : "-";
            s.Throttle = vc.input.Throttle;
            s.Brakes = vc.input.Brakes;

            float frontSlip = 0f, rearSlip = 0f, steer = 0f;
            int nf = 0, nr = 0, ns = 0;
            VehicleRecord r;
            if (_records.TryGetValue(vc, out r))
            {
                foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
                {
                    WheelUAPI w = wk.Key;
                    if (w == null)
                    {
                        continue;
                    }
                    if (wk.Value.IsFront)
                    {
                        frontSlip += Mathf.Abs(w.LateralSlip);
                        nf++;
                        steer += w.SteerAngle;
                        ns++;
                    }
                    else
                    {
                        rearSlip += Mathf.Abs(w.LateralSlip);
                        nr++;
                    }
                }
            }
            s.FrontSlip = nf > 0 ? frontSlip / nf : 0f;
            s.RearSlip = nr > 0 ? rearSlip / nr : 0f;
            s.SteeringDeg = ns > 0 ? steer / ns : 0f;

            float yawRate = vc.vehicleRigidbody != null ? Mathf.Abs(vc.vehicleRigidbody.angularVelocity.y) : 0f;
            s.LatG = yawRate * Mathf.Abs(vc.Speed) / 9.81f;

            float now = Time.unscaledTime;
            float dt = now - _telemetryPrevTime;
            if (_telemetryPrevSpeed >= 0f && dt > 0.001f)
            {
                s.LongG = Mathf.Clamp((vc.Speed - _telemetryPrevSpeed) / dt / 9.81f, -2f, 2f);
            }
            _telemetryPrevSpeed = vc.Speed;
            _telemetryPrevTime = now;
            return true;
        }

        /// <summary>
        /// The vehicle the player is driving: most live input above the dead zone, else the
        /// last vehicle that had input (a stalled/off engine keeps the pick), else the
        /// fastest, else the first tracked. Allocation-free.
        ///
        /// 0.6.4: "live" is input that changed recently. The game's FSM freezes a parked
        /// car's input at its exit values (handbrake held, brakes last pressed, …), so raw
        /// input alone lets a parked car steal the pick from a hands-off player — the strip
        /// showed that car's zeros until the player steered. A frozen value stays "live"
        /// only for the 2 s hold window after its last change, then falls silent.
        /// </summary>
        private VehicleController _lastDriven;

        public const float InputDeadZone = 0.05f;    // input below this is noise / hands-off
        public const float InputHoldSeconds = 2f;    // unchanged input counts as live this long after its last change
        public const float InputChangeEpsilon = 0.02f;

        /// <summary>
        /// Samples one vehicle's input for the pick: records the value and reports whether
        /// it counts as live driving right now. Pure (only the caller's state fields) so the
        /// harness tests it without Unity time.
        /// </summary>
        public static bool UpdateInputLiveness(float input, float now, ref float prev, ref bool seen, ref float lastChange, out bool live)
        {
            bool changed = !seen || Mathf.Abs(input - prev) > InputChangeEpsilon;
            seen = true;
            prev = input;
            if (changed && input > InputDeadZone)
            {
                lastChange = now;
            }
            live = input > InputDeadZone && (changed || now - lastChange <= InputHoldSeconds);
            return changed;
        }

        public VehicleController FindDrivenVehicle()
        {
            float now = Time.unscaledTime;
            VehicleController driven = null;    // most live input above the dead zone
            float bestInput = 0f;
            VehicleController running = null;   // first tracked vehicle with a running engine
            VehicleController fastest = null;
            float bestSpeed = 0f;
            VehicleController first = null;
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleController vc = kv.Key;
                VehicleRecord r = kv.Value;
                if (vc == null || r == null || r.Vc == null)
                {
                    continue;
                }
                if (first == null)
                {
                    first = vc;
                }
                float input = Mathf.Abs(vc.input.Steering) + vc.input.Throttle + vc.input.Brakes + vc.input.Handbrake;
                bool live;
                UpdateInputLiveness(input, now, ref r.InputPrev, ref r.InputSeen, ref r.InputLastChange, out live);
                if (live && input > bestInput)
                {
                    bestInput = input;
                    driven = vc;
                }
                // A running engine is a FALLBACK below the last-driven memory, never input
                // (0.6.2 added it to the input score, which let parked idlers steal the pick).
                if (running == null && vc.powertrain != null && vc.powertrain.engine != null && vc.powertrain.engine.OutputRPM > 10f)
                {
                    running = vc;
                }
                if (vc.Speed > bestSpeed)
                {
                    bestSpeed = vc.Speed;
                    fastest = vc;
                }
            }
            if (driven != null)
            {
                _lastDriven = driven;
                return driven;
            }
            // No live input anywhere: stay on the last car the player drove (its engine may
            // have stalled, or the panel is open and hands are on the mouse).
            if (_lastDriven != null)
            {
                VehicleRecord r;
                if (_records.TryGetValue(_lastDriven, out r) && r != null && r.Vc != null)
                {
                    return _lastDriven;
                }
                _lastDriven = null;
            }
            if (running != null)
            {
                return running;
            }
            return bestSpeed > 0.01f ? fastest : first;
        }

        // ------------------------------------------------------ pick diagnostics (0.6.4)

        private float _nextDebugDump = -1f;

        /// <summary>
        /// Once per second while [Telemetry] DebugPick is on, log one line per tracked
        /// vehicle: name, input sum, liveness, speed, RPM — and the pick. The in-game
        /// check for a wrong telemetry car reads this from BepInEx\LogOutput.log.
        /// Allocates only while the diagnostic is enabled (it is off by default).
        /// </summary>
        private void DebugDumpPick()
        {
            float now = Time.unscaledTime;
            if (!UiSettings.TelemetryDebugPick || now < _nextDebugDump)
            {
                return;
            }
            _nextDebugDump = now + 1f;
            System.Text.StringBuilder sb = new System.Text.StringBuilder("Telemetry pick: ");
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleController vc = kv.Key;
                VehicleRecord r = kv.Value;
                if (vc == null || r == null)
                {
                    continue;
                }
                float input = Mathf.Abs(vc.input.Steering) + vc.input.Throttle + vc.input.Brakes + vc.input.Handbrake;
                bool live;
                UpdateInputLiveness(input, now, ref r.InputPrev, ref r.InputSeen, ref r.InputLastChange, out live);
                float rpm = vc.powertrain != null && vc.powertrain.engine != null ? vc.powertrain.engine.OutputRPM : 0f;
                sb.Append('\'').Append(VehicleName(vc)).Append("' in=").Append(input.ToString("0.00"))
                  .Append(live ? " LIVE" : " stale").Append(" v=").Append((vc.Speed * 3.6f).ToString("0"))
                  .Append("km/h rpm=").Append(rpm.ToString("0")).Append(" | ");
            }
            VehicleController picked = FindDrivenVehicle();
            sb.Append("-> ").Append(picked != null ? VehicleName(picked) : "(none)");
            Plugin.Log.LogInfo(sb.ToString());
        }

        // ---------------------------------------------------------------- targeting (0.6.1)

        /// <summary>The stable identity a target selection stores (the GameObject name).</summary>
        public static string VehicleName(VehicleController vc)
        {
            return vc != null && vc.gameObject != null ? vc.gameObject.name : "";
        }

        /// <summary>Does the current target selection cover this vehicle?</summary>
        public bool IsTarget(VehicleController vc)
        {
            switch (TargetSettings.Mode)
            {
                case TargetMode.LastDriven:
                    return vc != null && vc == (_passActive ? _passDriven : FindDrivenVehicle());
                case TargetMode.Selected:
                    return vc != null && string.Equals(TargetSettings.SelectedName, VehicleName(vc), StringComparison.Ordinal);
                default:
                    return true;
            }
        }

        /// <summary>Names of every tracked vehicle, in first-seen order (panel list; UI path).</summary>
        public List<string> TrackedNames()
        {
            List<string> names = new List<string>(_order.Count);
            for (int i = 0; i < _order.Count; i++)
            {
                VehicleRecord r = _order[i];
                if (r != null && r.Vc != null && !names.Contains(VehicleName(r.Vc)))
                {
                    names.Add(VehicleName(r.Vc));
                }
            }
            return names;
        }

        /// <summary>Vehicles the current target selection covers (the panel status lines).</summary>
        public int TargetedCount
        {
            get
            {
                bool outer = _passActive;
                if (!outer)
                {
                    BeginTargetPass();
                }
                try
                {
                    int n = 0;
                    foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
                    {
                        if (kv.Key != null && kv.Value.Vc != null && IsTarget(kv.Key))
                        {
                            n++;
                        }
                    }
                    return n;
                }
                finally
                {
                    if (!outer)
                    {
                        _passActive = false;
                    }
                }
            }
        }

        /// <summary>
        /// One category's per-record pass under the current target: targets get the
        /// category applied (idempotently) and their bit set; records that were
        /// applied but are no longer targets get restored and their bit cleared.
        /// </summary>
        /// <summary>
        /// TargetPass with the active preset passed through. 0.6.2: the per-category passes used
        /// capturing lambdas (r => ApplyX(r, p)), a fresh closure + delegate on every ApplyLive,
        /// i.e. every 2 s scan and every slider tick — the apply path was not allocation-free as
        /// documented. Static method groups are cached by the compiler; the preset rides along.
        /// </summary>
        /// <summary>One category's per-record pass with a PER-RECORD preset: the 0.9.0
        /// per-vehicle-tune path (a vehicle with its own saved tune gets it; everyone else
        /// gets the global). Flag first: a pass that throws half-way has still written some
        /// fields, and the flag is what makes OFF restore them.</summary>
        private void TargetPass<T>(AppliedCat cat, Func<VehicleRecord, T> pick, Action<VehicleRecord, T> apply, Action<VehicleRecord> restore)
        {
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
                if (kv.Key == null || r == null || r.Vc == null)
                {
                    continue;
                }
                if (IsTarget(kv.Key))
                {
                    r.Applied |= cat;
                    try { apply(r, pick(r)); }
                    catch (Exception ex) { LogFault(cat + " apply", kv.Key, ex); }
                }
                else if ((r.Applied & cat) != 0)
                {
                    r.Applied &= ~cat;
                    try { restore(r); }
                    catch (Exception ex) { LogFault(cat + " restore", kv.Key, ex); }
                }
            }
        }

        private void TargetPass(AppliedCat cat, Action<VehicleRecord> apply, Action<VehicleRecord> restore)
        {
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
                if (kv.Key == null || r == null || r.Vc == null)
                {
                    continue;
                }
                if (IsTarget(kv.Key))
                {
                    r.Applied |= cat;
                    try { apply(r); }
                    catch (Exception ex) { LogFault(cat + " apply", kv.Key, ex); }
                }
                else if ((r.Applied & cat) != 0)
                {
                    r.Applied &= ~cat;
                    try { restore(r); }
                    catch (Exception ex) { LogFault(cat + " restore", kv.Key, ex); }
                }
            }
        }

        /// <summary>Restore one category on every record that has it applied.</summary>
        private void RestorePass(AppliedCat cat, Action<VehicleRecord> restore)
        {
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
                if (r != null && (r.Applied & cat) != 0)
                {
                    r.Applied &= ~cat;
                    try { restore(r); }
                    catch (Exception ex) { LogFault(cat + " restore", kv.Key, ex); }
                }
            }
        }

        /// <summary>
        /// Restore only the categories this tuner actually applied. Writing captured
        /// values for a category that was never on would clobber anything the game
        /// changed on those fields since capture.
        /// </summary>
        private void RestoreAll()
        {
            PurgeDead();
            if (_assistsApplied) { RestoreAllAssists(); }
            if (_aeroApplied) { RestoreAllAero(); }
            if (_suspApplied) { RestoreAllSuspension(); }
            if (_gripApplied) { RestoreAllGrip(); }
            if (_brakesApplied) { RestoreAllBrakes(); }
            if (_drivetrainApplied) { RestoreAllDrivetrain(); }
            if (_alignmentApplied) { RestoreAllAlignment(); }
            if (_gearboxApplied) { RestoreAllGearbox(); }
            _suspApplied = _aeroApplied = _brakesApplied = _gripApplied = _drivetrainApplied = _assistsApplied = false;
            _alignmentApplied = _gearboxApplied = false;
        }
    }
}
