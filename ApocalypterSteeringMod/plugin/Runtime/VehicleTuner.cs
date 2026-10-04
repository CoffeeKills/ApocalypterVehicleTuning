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
    ///   effective = the vehicle's captured stock value x the active preset factor.
    /// Stock values are captured the first time a vehicle is seen and restored when
    /// a category is switched off. Per-system logic lives in VehicleTuner.Systems.cs
    /// and VehicleTuner.Assists.cs.
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
            public float SpringForce, SpringLength, BumpRate, ReboundRate;   // suspension
            public float LngGrip, LatGrip, LngStiff, LatStiff;               // grip
        }

        internal sealed class GroupData
        {
            public bool IsFront;
            public float ArbForce;               // suspension
            public float BrakeCoeff, HandbrakeCoeff;  // brakes
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
        }

        internal sealed class AeroData
        {
            public AerodynamicsModule Module;    // null = vehicle has none (not yet onboarded)
            public bool Onboarded;               // we created it (reused, never re-onboarded)
            public bool WasSimulateDrag, WasSimulateDownforce;
            public float FrontalCd, SideCd, MaxDownforceSpeed;
            public DownforcePoint[] Points;      // deep copy of baseline maxForce/position
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
            public AssistHandles Assists;
            public bool HasTyreWear;
        }

        private readonly Dictionary<VehicleController, VehicleRecord> _records = new Dictionary<VehicleController, VehicleRecord>();
        private readonly List<VehicleController> _dead = new List<VehicleController>();
        private float _nextScan;
        private bool _suspApplied, _aeroApplied, _brakesApplied, _gripApplied, _drivetrainApplied, _assistsApplied;

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
        }

        private void ScanVehicles()
        {
            // Forget destroyed vehicles only. Vehicles that are merely inactive keep their
            // record: re-capturing one later would read our own tuned values as "stock".
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
                _records.Remove(_dead[i]);
            }

            VehicleController[] vehicles = Object.FindObjectsOfType<VehicleController>();
            for (int i = 0; i < vehicles.Length; i++)
            {
                VehicleController vc = vehicles[i];
                if (vc == null || _records.ContainsKey(vc))
                {
                    continue;
                }
                VehicleRecord record = Capture(vc);
                if (record != null)
                {
                    _records[vc] = record;
                }
            }
        }

        /// <summary>
        /// Snapshot stock values. Returns null while the vehicle has no initialised wheels
        /// yet, so it is retried on the next scan instead of being recorded as empty.
        /// </summary>
        private static VehicleRecord Capture(VehicleController vc)
        {
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
                    record.Wheels[uapi] = new WheelData
                    {
                        IsFront = front,
                        SpringForce = uapi.SpringMaxForce,
                        SpringLength = uapi.SpringMaxLength,
                        BumpRate = uapi.DamperBumpRate,
                        ReboundRate = uapi.DamperReboundRate,
                        LngGrip = uapi.LongitudinalFrictionGrip,
                        LatGrip = uapi.LateralFrictionGrip,
                        LngStiff = uapi.LongitudinalFrictionStiffness,
                        LatStiff = uapi.LateralFrictionStiffness
                    };
                }
                record.Groups[group] = new GroupData
                {
                    IsFront = groupFront,
                    ArbForce = group.antiRollBarForce,
                    BrakeCoeff = group.brakeCoefficient,
                    HandbrakeCoeff = group.handbrakeCoefficient
                };
            }

            if (vc.brakes != null)
            {
                record.Brakes = new BrakesData
                {
                    MaxTorque = vc.brakes.maxTorque,
                    ActuationTime = vc.brakes.actuationTime
                };
            }

            record.Drivetrain = CaptureDrivetrain(vc);
            record.Aero = CaptureAero(vc);
            record.Assists = CreateAssistHandles(vc);
            record.HasTyreWear = HasTyreWearComponent(vc);
            return record;
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
            return dt;
        }

        internal enum Category
        {
            Suspension,
            Aero,
            Brakes,
            Grip,
            Drivetrain
        }

        /// <summary>
        /// Re-read one category's stock values on every tracked vehicle. Only called while
        /// that category is NOT applied (its fields hold the game's values, never ours), so
        /// this can never capture tuned values. Without it the baseline was frozen at first
        /// sight: a value the game changed later (while the category was off) was scaled from
        /// the stale number on enable and overwritten with it on disable. The category's own
        /// fields only: categories never share a field, so another category's applied state
        /// cannot leak in. Runs on a toggle, not per tick (drivetrain/aero re-capture allocates).
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
                }
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
            ArbForce
        }

        /// <summary>Mean stock baseline over tracked vehicles for one axle (0 when none tracked).</summary>
        public float MeanBaseline(Readout kind, bool front)
        {
            float sum = 0f;
            int count = 0;
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleRecord r = kv.Value;
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

        /// <summary>
        /// Restore only the categories this tuner actually applied. Writing captured
        /// values for a category that was never on would clobber anything the game
        /// changed on those fields since capture.
        /// </summary>
        private void RestoreAll()
        {
            if (_assistsApplied) { RestoreAllAssists(); }
            if (_aeroApplied) { RestoreAllAero(); }
            if (_suspApplied) { RestoreAllSuspension(); }
            if (_gripApplied) { RestoreAllGrip(); }
            if (_brakesApplied) { RestoreAllBrakes(); }
            if (_drivetrainApplied) { RestoreAllDrivetrain(); }
            _suspApplied = _aeroApplied = _brakesApplied = _gripApplied = _drivetrainApplied = _assistsApplied = false;
        }
    }
}
