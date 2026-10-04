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
        }

        private void ScanVehicles()
        {
            // Forget destroyed vehicles only. Vehicles that are merely inactive keep their
            // record: re-capturing one later would read our own tuned values as "stock".
            PurgeDead();

            VehicleController[] vehicles = UnityEngine.Object.FindObjectsOfType<VehicleController>();
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
                    _order.Add(record);
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
            record.Gearbox = CaptureGearbox(vc);
            record.Assists = CreateAssistHandles(vc);
            record.HasTyreWear = HasTyreWearComponent(vc);
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

        /// <summary>Stock ratio of forward gear <paramref name="gear"/> (1..12) of the reference vehicle, continued past its own count; 0 when unknown.</summary>
        public float ReferenceGearStock(int gear)
        {
            VehicleRecord r = ReferenceRecord();
            if (r == null || r.Gearbox == null || r.Gearbox.Extended == null || gear < 1 || gear > r.Gearbox.Extended.Length)
            {
                return 0f;
            }
            return r.Gearbox.Extended[gear - 1];
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

        /// <summary>
        /// True when a gear-count change was skipped this pass: automatic
        /// transmissions keep their stock count (the game's shift logic expects
        /// it — resizing makes the car undrivable). Panel shows a note.
        /// </summary>
        public bool AnyResizeSkipped { get; private set; }

        // --------------------------------------------------------------- telemetry

        public struct TelemetrySample
        {
            public float SpeedKmh;
            public float Rpm;
            public string Gear;
            public float FrontSlip;   // mean |LateralSlip| of the front wheels (NWH's normalised slip)
        }

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
            s = new TelemetrySample();
            VehicleController vc = FindDrivenVehicle();
            if (vc == null || vc.powertrain == null)
            {
                return false;
            }
            s.SpeedKmh = vc.Speed * 3.6f;
            s.Rpm = vc.powertrain.engine != null ? vc.powertrain.engine.OutputRPM : 0f;
            s.Gear = vc.powertrain.transmission != null ? vc.powertrain.transmission.GearName : "-";
            float sum = 0f;
            int n = 0;
            VehicleRecord r;
            if (_records.TryGetValue(vc, out r))
            {
                foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
                {
                    if (wk.Key != null && wk.Value.IsFront)
                    {
                        sum += Mathf.Abs(wk.Key.LateralSlip);
                        n++;
                    }
                }
            }
            s.FrontSlip = n > 0 ? sum / n : 0f;
            return true;
        }

        /// <summary>
        /// The vehicle the player is driving: most live FSM input, else the last
        /// vehicle that had input (a stalled/off engine keeps the pick), else the
        /// fastest, else the first tracked. Allocation-free.
        /// </summary>
        private VehicleController _lastDriven;

        public VehicleController FindDrivenVehicle()
        {
            VehicleController driven = null;    // most live activity above the dead zone
            float bestActivity = 0.0001f;
            VehicleController fastest = null;
            float bestSpeed = 0f;
            VehicleController first = null;
            foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
            {
                VehicleController vc = kv.Key;
                if (vc == null || kv.Value.Vc == null)
                {
                    continue;
                }
                if (first == null)
                {
                    first = vc;
                }
                float input = Mathf.Abs(vc.input.Steering) + vc.input.Throttle + vc.input.Brakes + vc.input.Handbrake;
                // A running engine (idle or better) counts as a whisper of activity so a
                // parked player car beats parked NPCs with dead engines when no input
                // is held anywhere — otherwise the pick fell to the first tracked
                // vehicle and the strip showed a random parked car's zeros.
                bool running = vc.powertrain != null && vc.powertrain.engine != null && vc.powertrain.engine.OutputRPM > 10f;
                float activity = input + (running ? 0.0004f : 0f);
                if (activity > bestActivity)
                {
                    bestActivity = activity;
                    driven = vc;
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
            // No input anywhere: stay on the last car the player drove (its engine may
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
            return bestSpeed > 0.01f ? fastest : first;
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
                    return vc != null && vc == FindDrivenVehicle();
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
        }

        /// <summary>
        /// One category's per-record pass under the current target: targets get the
        /// category applied (idempotently) and their bit set; records that were
        /// applied but are no longer targets get restored and their bit cleared.
        /// </summary>
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
                    apply(r);
                    r.Applied |= cat;
                }
                else if ((r.Applied & cat) != 0)
                {
                    restore(r);
                    r.Applied &= ~cat;
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
                    restore(r);
                    r.Applied &= ~cat;
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
