using System;
using ApocalypterSteeringMod.Settings;
using BepInEx.Configuration;
using UnityEngine;

namespace ApocalypterSteeringMod.Persistence
{
    /// <summary>
    /// Binds mod settings to a BepInEx ConfigFile. ConfigEntries are the on-disk
    /// source of truth; runtime holders are pushed on load, on SettingChanged
    /// (e.g. edited through a config manager) and mirrored back on Save(). All
    /// numeric entries carry an AcceptableValueRange taken from Limits, so
    /// BepInEx clamps hand-edited values. v3.1 suspension files migrate once
    /// (user multipliers fold into the Custom preset, then the legacy keys are
    /// removed so the fold cannot run twice).
    /// </summary>
    public static class ModConfig
    {
        public static event Action SettingsChanged;

        private static ConfigFile _config;
        private static bool _syncing;

        // Steering
        private static ConfigEntry<bool> _steerEnabled;
        private static ConfigEntry<string> _steerPreset;
        private static ConfigEntry<bool> _matchGameSteeringSpeed;
        private static ConfigEntry<string> _steerBasedOn;
        private static ConfigEntry<float> _steerRate, _steerSmoothing, _steerCurveScale, _steerSlip, _steerOppLock, _steerReturn, _steerLinExp;
        private static ConfigEntry<bool> _steerTraction, _steerLinearityOverride;

        // Suspension
        private static ConfigEntry<bool> _suspEnabled;
        private static ConfigEntry<string> _suspPreset;
        private static ConfigEntry<bool> _suspSplit;
        private static ConfigEntry<string> _suspBasedOn;
        private static ConfigEntry<float> _suspSpringF, _suspSpringR, _suspHeightF, _suspHeightR,
            _suspBumpF, _suspBumpR, _suspReboundF, _suspReboundR, _suspArbF, _suspArbR;
        // Legacy v3.1 keys (bound only for the one-time migration, removed after).
        private static ConfigEntry<float> _legacyUSpringF, _legacyUSpringR, _legacyUHeightF, _legacyUHeightR,
            _legacyUBumpF, _legacyUBumpR, _legacyUReboundF, _legacyUReboundR, _legacyUArbF, _legacyUArbR;

        // Aero
        private static ConfigEntry<bool> _aeroEnabled;
        private static ConfigEntry<string> _aeroPreset, _aeroBasedOn;
        private static ConfigEntry<float> _aeroDownforce, _aeroDrag, _aeroMaxSpeed;

        // Brakes
        private static ConfigEntry<bool> _brakesEnabled;
        private static ConfigEntry<string> _brakesPreset, _brakesBasedOn;
        private static ConfigEntry<float> _brakesTorque, _brakesFront, _brakesRear, _brakesHandbrake, _brakesActuation;

        // Grip
        private static ConfigEntry<bool> _gripEnabled;
        private static ConfigEntry<string> _gripPreset, _gripBasedOn;
        private static ConfigEntry<float> _gripLng, _gripLat, _gripStiff;

        // Drivetrain
        private static ConfigEntry<bool> _drivetrainEnabled;
        private static ConfigEntry<string> _drivetrainPreset, _drivetrainBasedOn;
        private static ConfigEntry<float> _dtPower, _dtRevLimit, _dtLoss, _dtBoost, _dtFinalDrive,
            _dtUpshift, _dtDownshift, _dtShiftDur, _dtDiffStiff, _dtDiffBias;
        private static ConfigEntry<string> _dtDiffFront, _dtDiffRear;

        // Assists
        private static ConfigEntry<bool> _assistsEnabled;
        private static ConfigEntry<string> _assistsPreset, _assistsBasedOn;
        private static ConfigEntry<bool> _assistsAbsEnabled, _assistsTcsEnabled;
        private static ConfigEntry<float> _assistsAbsThr, _assistsAbsCut, _assistsAbsMult,
            _assistsTcsThr, _assistsTcsCut, _assistsTcsMult;

        private static ConfigEntry<string> _toggleKey;
        // Read by Apocasetter via Chainloader (not wired to OnSettingChanged — we never read it).
        private static ConfigEntry<bool> _apocasetter;

        public static string ToggleKeyString
        {
            get { return _toggleKey != null ? _toggleKey.Value : "F7"; }
        }

        private static ConfigEntry<float> BindRange(string section, string key, float def, float min, float max, string description)
        {
            return _config.Bind(section, key, def,
                new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        }

        public static void Load(ConfigFile config)
        {
            _config = config;

            // One file write at the end instead of one per Bind.
            bool autoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;

            BindSteering();
            BindSuspension();
            BindAero();
            BindBrakes();
            BindGrip();
            BindDrivetrain();
            BindAssists();
            WireAll();

            _toggleKey = config.Bind("UI", "ToggleKey", "F7",
                "Hotkey that opens/closes the tuning panel (a Unity KeyCode name, e.g. F7, F8, Home). Restart the game after changing.");
            _apocasetter = config.Bind("General", "Apocasetter", true,
                "Show this mod in the Apocasetter Mods menu (requires Apocasetter installed).");

            MigrateLegacySteeringPreset();
            PushAllToRuntime();
            MigrateLegacySuspension();

            config.SaveOnConfigSet = autoSave;
            config.Save();
        }

        // ---------------------------------------------------------------- binding

        private static void BindSteering()
        {
            _steerEnabled = _config.Bind("Steering", "Enabled", false,
                "Master switch for the mod's steering (opt-in).");
            _steerPreset = _config.Bind("Steering", "Preset", "Custom",
                "Active steering preset: Vanilla, GTA-style Keyboard, Euro Truck, Sim/Race, Drift, Custom.");
            _matchGameSteeringSpeed = _config.Bind("Steering", "MatchGameSteeringSpeed", true,
                "Scale the steering rate with the game's own steering speed setting.");
            _steerBasedOn = _config.Bind("Steering.Custom", "BasedOn", "",
                "Built-in preset the Custom tuning was copied from (restores that preset's speed curve). Leave empty for none.");
            _steerRate = BindRange("Steering.Custom", "RateMultiplier", 1f, Limits.RateMin, Limits.RateMax,
                "Multiplier on the vehicle's configured deg/s steering rate.");
            _steerSmoothing = BindRange("Steering.Custom", "SmoothingScale", 1f, Limits.SmoothMin, Limits.SmoothMax,
                "Multiplier on the vehicle's smoothing time.");
            _steerCurveScale = BindRange("Steering.Custom", "SpeedCurveScale", 1f, Limits.CurveScaleMin, Limits.CurveScaleMax,
                "Multiplier on the speed-sensitive steering curve (never exceeds the car's max steer angle).");
            _steerTraction = _config.Bind("Steering.Custom", "TractionClampEnabled", true,
                "Clamp the steer angle so front tires stay near their peak-grip slip angle.");
            _steerSlip = BindRange("Steering.Custom", "SlipAngleDeg", 8.5f, Limits.SlipMin, Limits.SlipMax,
                "Peak-grip tire slip angle in degrees.");
            _steerOppLock = BindRange("Steering.Custom", "OppositeLockBoost", 1.75f, Limits.OppLockMin, Limits.OppLockMax,
                "Steering rate boost while applying opposite lock.");
            _steerReturn = BindRange("Steering.Custom", "CenterReturnScale", 1f, Limits.ReturnScaleMin, Limits.ReturnScaleMax,
                "Steering rate multiplier while the wheel unwinds toward center (below 1 = lazier return, holds the angle).");
            _steerLinearityOverride = _config.Bind("Steering.Custom", "LinearityOverride", false,
                "Replace the vehicle's input linearity curve with pow(|input|, exponent).");
            _steerLinExp = BindRange("Steering.Custom", "LinearityExponent", 1f, Limits.LinExpMin, Limits.LinExpMax,
                "Input linearity exponent (1 = linear, below 1 = sharper near center, above 1 = gentler).");
        }

        private static void BindSuspension()
        {
            _suspEnabled = _config.Bind("Suspension", "Enabled", false,
                "Master switch for suspension tuning (opt-in; overrides per-vehicle tuning).");
            _suspPreset = _config.Bind("Suspension", "Preset", "Stock",
                "Active suspension preset: Stock, Comfort, Sport, Off-road, Race, Custom.");
            _suspSplit = _config.Bind("Suspension", "SplitFrontRear", false,
                "Show separate front/rear sliders in the panel.");
            _suspBasedOn = _config.Bind("Suspension.Custom", "BasedOn", "",
                "Built-in preset the Custom tuning was copied from. Leave empty for none.");
            _suspSpringF = BindRange("Suspension.Custom", "SpringFront", 1f, Limits.FactorMin, Limits.FactorMax, "Front spring stiffness factor.");
            _suspSpringR = BindRange("Suspension.Custom", "SpringRear", 1f, Limits.FactorMin, Limits.FactorMax, "Rear spring stiffness factor.");
            _suspHeightF = BindRange("Suspension.Custom", "RideHeightFront", 1f, Limits.FactorMin, Limits.FactorMax, "Front ride height factor.");
            _suspHeightR = BindRange("Suspension.Custom", "RideHeightRear", 1f, Limits.FactorMin, Limits.FactorMax, "Rear ride height factor.");
            _suspBumpF = BindRange("Suspension.Custom", "BumpFront", 1f, Limits.FactorMin, Limits.FactorMax, "Front bump damping factor.");
            _suspBumpR = BindRange("Suspension.Custom", "BumpRear", 1f, Limits.FactorMin, Limits.FactorMax, "Rear bump damping factor.");
            _suspReboundF = BindRange("Suspension.Custom", "ReboundFront", 1f, Limits.FactorMin, Limits.FactorMax, "Front rebound damping factor.");
            _suspReboundR = BindRange("Suspension.Custom", "ReboundRear", 1f, Limits.FactorMin, Limits.FactorMax, "Rear rebound damping factor.");
            _suspArbF = BindRange("Suspension.Custom", "ArbFront", 1f, Limits.FactorMin, Limits.FactorMax, "Front anti-roll bar factor.");
            _suspArbR = BindRange("Suspension.Custom", "ArbRear", 1f, Limits.FactorMin, Limits.FactorMax, "Rear anti-roll bar factor.");

            // Legacy v3.1 fine-tune multipliers — read once for migration, then removed.
            _legacyUSpringF = BindRange("Suspension.User", "SpringFront", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUSpringR = BindRange("Suspension.User", "SpringRear", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUHeightF = BindRange("Suspension.User", "RideHeightFront", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUHeightR = BindRange("Suspension.User", "RideHeightRear", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUBumpF = BindRange("Suspension.User", "BumpFront", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUBumpR = BindRange("Suspension.User", "BumpRear", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUReboundF = BindRange("Suspension.User", "ReboundFront", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUReboundR = BindRange("Suspension.User", "ReboundRear", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUArbF = BindRange("Suspension.User", "ArbFront", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
            _legacyUArbR = BindRange("Suspension.User", "ArbRear", 1f, Limits.UserMin, Limits.UserMax, "Legacy.");
        }

        private static void BindAero()
        {
            _aeroEnabled = _config.Bind("Aero", "Enabled", false, "Master switch for aero tuning (opt-in).");
            _aeroPreset = _config.Bind("Aero", "Preset", "Stock", "Active aero preset: Stock, Street, Sport, Off-road, Race, Custom.");
            _aeroBasedOn = _config.Bind("Aero.Custom", "BasedOn", "", "Built-in preset the Custom tuning was copied from.");
            _aeroDownforce = BindRange("Aero.Custom", "DownforceScale", 1f, Limits.AeroScaleMin, Limits.AeroScaleMax, "Downforce factor.");
            _aeroDrag = BindRange("Aero.Custom", "DragScale", 1f, Limits.AeroScaleMin, Limits.AeroScaleMax, "Drag factor.");
            _aeroMaxSpeed = BindRange("Aero.Custom", "MaxDownforceSpeedScale", 1f, Limits.AeroSpeedScaleMin, Limits.AeroSpeedScaleMax, "Downforce speed-range factor.");
        }

        private static void BindBrakes()
        {
            _brakesEnabled = _config.Bind("Brakes", "Enabled", false, "Master switch for brake tuning (opt-in).");
            _brakesPreset = _config.Bind("Brakes", "Preset", "Stock", "Active brake preset: Stock, Sport, Race, Off-road, Drift, Custom.");
            _brakesBasedOn = _config.Bind("Brakes.Custom", "BasedOn", "", "Built-in preset the Custom tuning was copied from.");
            _brakesTorque = BindRange("Brakes.Custom", "TorqueScale", 1f, Limits.BrakeTorqueMin, Limits.BrakeTorqueMax, "Brake torque factor.");
            _brakesFront = BindRange("Brakes.Custom", "FrontBrakeScale", 1f, Limits.BrakeAxleMin, Limits.BrakeAxleMax, "Front axle brake factor.");
            _brakesRear = BindRange("Brakes.Custom", "RearBrakeScale", 1f, Limits.BrakeAxleMin, Limits.BrakeAxleMax, "Rear axle brake factor.");
            _brakesHandbrake = BindRange("Brakes.Custom", "HandbrakeScale", 1f, Limits.BrakeAxleMin, Limits.BrakeAxleMax, "Handbrake factor.");
            _brakesActuation = BindRange("Brakes.Custom", "ActuationScale", 1f, Limits.BrakeTorqueMin, Limits.BrakeTorqueMax, "Brake apply time factor.");
        }

        private static void BindGrip()
        {
            _gripEnabled = _config.Bind("Grip", "Enabled", false, "Master switch for grip tuning (opt-in).");
            _gripPreset = _config.Bind("Grip", "Preset", "Stock", "Active grip preset: Stock, Sport, Race, Off-road, Drift, Custom.");
            _gripBasedOn = _config.Bind("Grip.Custom", "BasedOn", "", "Built-in preset the Custom tuning was copied from.");
            _gripLng = BindRange("Grip.Custom", "LongitudinalScale", 1f, Limits.GripMin, Limits.GripMax, "Longitudinal grip factor.");
            _gripLat = BindRange("Grip.Custom", "LateralScale", 1f, Limits.GripMin, Limits.GripMax, "Lateral grip factor.");
            _gripStiff = BindRange("Grip.Custom", "StiffnessScale", 1f, Limits.GripMin, Limits.GripMax, "Friction stiffness factor.");
        }

        private static void BindDrivetrain()
        {
            _drivetrainEnabled = _config.Bind("Drivetrain", "Enabled", false, "Master switch for drivetrain tuning (opt-in).");
            _drivetrainPreset = _config.Bind("Drivetrain", "Preset", "Stock", "Active drivetrain preset: Stock, Street, Off-road, Sport, Race, Drift, Custom.");
            _drivetrainBasedOn = _config.Bind("Drivetrain.Custom", "BasedOn", "", "Built-in preset the Custom tuning was copied from.");
            _dtPower = BindRange("Drivetrain.Custom", "PowerScale", 1f, Limits.PowerMin, Limits.PowerMax, "Engine power factor.");
            _dtRevLimit = BindRange("Drivetrain.Custom", "RevLimiterScale", 1f, Limits.RevLimitMin, Limits.RevLimitMax, "Rev limiter factor.");
            _dtLoss = BindRange("Drivetrain.Custom", "LossScale", 1f, Limits.FactorMin, Limits.FactorMax, "Engine loss (engine braking) factor.");
            _dtBoost = BindRange("Drivetrain.Custom", "BoostScale", 1f, Limits.FactorMin, Limits.FactorMax, "Turbo/boost gain factor.");
            _dtFinalDrive = BindRange("Drivetrain.Custom", "FinalDriveScale", 1f, Limits.FinalDriveMin, Limits.FinalDriveMax, "Final drive ratio factor.");
            _dtUpshift = BindRange("Drivetrain.Custom", "UpshiftScale", 1f, Limits.ShiftRpmMin, Limits.ShiftRpmMax, "Upshift RPM factor.");
            _dtDownshift = BindRange("Drivetrain.Custom", "DownshiftScale", 1f, Limits.ShiftRpmMin, Limits.ShiftRpmMax, "Downshift RPM factor.");
            _dtShiftDur = BindRange("Drivetrain.Custom", "ShiftDurationScale", 1f, Limits.FactorMin, Limits.FactorMax, "Shift duration factor.");
            _dtDiffStiff = BindRange("Drivetrain.Custom", "DiffStiffnessScale", 1f, Limits.DiffScaleMin, Limits.DiffScaleMax, "Differential stiffness factor.");
            _dtDiffBias = BindRange("Drivetrain.Custom", "DiffBiasScale", 1f, Limits.DiffScaleMin, Limits.DiffScaleMax, "Centre (AWD) differential front/rear bias factor; axle diffs keep their stock bias.");
            _dtDiffFront = _config.Bind("Drivetrain.Custom", "DiffFrontMode", "Stock", "Front-axle differential: Stock, Open, Locked, LimitedSlip (LSD accepted).");
            _dtDiffRear = _config.Bind("Drivetrain.Custom", "DiffRearMode", "Stock", "Rear-axle differential: Stock, Open, Locked, LimitedSlip (LSD accepted).");
        }

        private static void BindAssists()
        {
            _assistsEnabled = _config.Bind("Assists", "Enabled", false, "Master switch for ABS/TCS assists (opt-in).");
            _assistsPreset = _config.Bind("Assists", "Preset", "Off", "Active assists preset: Off, Standard, Sport, Off-road, Race, Custom.");
            _assistsBasedOn = _config.Bind("Assists.Custom", "BasedOn", "", "Built-in preset the Custom tuning was copied from.");
            _assistsAbsEnabled = _config.Bind("Assists.Custom", "AbsEnabled", false, "Anti-lock braking on.");
            _assistsAbsThr = BindRange("Assists.Custom", "AbsSlipThreshold", 0.1f, Limits.SlipThrMin, Limits.SlipThrMax, "ABS slip threshold.");
            _assistsAbsCut = BindRange("Assists.Custom", "AbsCutoffSpeed", 1f, Limits.CutoffSpeedMin, Limits.CutoffSpeedMax, "ABS cutoff speed (m/s).");
            _assistsAbsMult = BindRange("Assists.Custom", "AbsCutMultiplier", 0.01f, Limits.CutMultMin, Limits.CutMultMax, "Brake modifier while ABS releases.");
            _assistsTcsEnabled = _config.Bind("Assists.Custom", "TcsEnabled", false, "Traction control on.");
            _assistsTcsThr = BindRange("Assists.Custom", "TcsSlipThreshold", 0.1f, Limits.SlipThrMin, Limits.SlipThrMax, "TCS slip threshold.");
            _assistsTcsCut = BindRange("Assists.Custom", "TcsCutoffSpeed", 2f, Limits.CutoffSpeedMin, Limits.CutoffSpeedMax, "TCS cutoff speed (m/s).");
            _assistsTcsMult = BindRange("Assists.Custom", "TcsCutMultiplier", 0.01f, Limits.CutMultMin, Limits.CutMultMax, "Power modifier while TCS cuts.");
        }

        // ---------------------------------------------------------------- wiring

        private static void Wire<T>(ConfigEntry<T> entry)
        {
            entry.SettingChanged += OnSettingChanged;
        }

        private static void WireAll()
        {
            Wire(_steerEnabled); Wire(_steerPreset); Wire(_matchGameSteeringSpeed); Wire(_steerBasedOn);
            Wire(_steerRate); Wire(_steerSmoothing); Wire(_steerCurveScale); Wire(_steerTraction);
            Wire(_steerSlip); Wire(_steerOppLock); Wire(_steerReturn); Wire(_steerLinearityOverride); Wire(_steerLinExp);
            Wire(_suspEnabled); Wire(_suspPreset); Wire(_suspSplit); Wire(_suspBasedOn);
            Wire(_suspSpringF); Wire(_suspSpringR); Wire(_suspHeightF); Wire(_suspHeightR);
            Wire(_suspBumpF); Wire(_suspBumpR); Wire(_suspReboundF); Wire(_suspReboundR);
            Wire(_suspArbF); Wire(_suspArbR);
            Wire(_aeroEnabled); Wire(_aeroPreset); Wire(_aeroBasedOn);
            Wire(_aeroDownforce); Wire(_aeroDrag); Wire(_aeroMaxSpeed);
            Wire(_brakesEnabled); Wire(_brakesPreset); Wire(_brakesBasedOn);
            Wire(_brakesTorque); Wire(_brakesFront); Wire(_brakesRear); Wire(_brakesHandbrake); Wire(_brakesActuation);
            Wire(_gripEnabled); Wire(_gripPreset); Wire(_gripBasedOn);
            Wire(_gripLng); Wire(_gripLat); Wire(_gripStiff);
            Wire(_drivetrainEnabled); Wire(_drivetrainPreset); Wire(_drivetrainBasedOn);
            Wire(_dtPower); Wire(_dtRevLimit); Wire(_dtLoss); Wire(_dtBoost); Wire(_dtFinalDrive);
            Wire(_dtUpshift); Wire(_dtDownshift); Wire(_dtShiftDur); Wire(_dtDiffStiff); Wire(_dtDiffBias);
            Wire(_dtDiffFront); Wire(_dtDiffRear);
            Wire(_assistsEnabled); Wire(_assistsPreset); Wire(_assistsBasedOn);
            Wire(_assistsAbsEnabled); Wire(_assistsAbsThr); Wire(_assistsAbsCut); Wire(_assistsAbsMult);
            Wire(_assistsTcsEnabled); Wire(_assistsTcsThr); Wire(_assistsTcsCut); Wire(_assistsTcsMult);
        }

        // ---------------------------------------------------------------- migration

        /// <summary>
        /// v0.3.0: "Truck-sim" was replaced by the "Euro Truck" preset. The runtime
        /// Book already maps the old name; also rewrite the entries so the file stops
        /// carrying the dead name. The BasedOn rewrite matters: RestoreBaseCurve
        /// needs a resolvable name or Custom silently loses its speed curve.
        /// </summary>
        private static void MigrateLegacySteeringPreset()
        {
            bool wasSyncing = _syncing;
            _syncing = true;
            try
            {
                if (string.Equals(_steerPreset.Value, "Truck-sim", StringComparison.Ordinal))
                {
                    _steerPreset.Value = "Euro Truck";
                }
                if (string.Equals(_steerBasedOn.Value, "Truck-sim", StringComparison.Ordinal))
                {
                    _steerBasedOn.Value = "Euro Truck";
                }
            }
            finally
            {
                _syncing = wasSyncing;
            }
        }

        /// <summary>
        /// One-time v3.1 -> v3.2 migration: fold the legacy [Suspension.User]
        /// fine-tune multipliers into the Custom preset, then remove the legacy
        /// keys so the fold can never run twice.
        /// </summary>
        private static void MigrateLegacySuspension()
        {
            // (1) v3.0 "Street" preset name: the runtime already maps it to Stock; also
            // rewrite the entry so the file stops carrying the dead name (previously it
            // only changed once the panel was opened and closed).
            if (string.Equals(_suspPreset.Value, "Street", StringComparison.Ordinal))
            {
                bool wasSyncing = _syncing;
                _syncing = true;
                try
                {
                    _suspPreset.Value = "Stock";
                }
                finally
                {
                    _syncing = wasSyncing;
                }
                // The fold below maps Street -> Stock anyway (BasedOn ""), so the result is identical.
            }

            bool anyUser = Math.Abs(_legacyUSpringF.Value - 1f) > 1e-4f || Math.Abs(_legacyUSpringR.Value - 1f) > 1e-4f
                || Math.Abs(_legacyUHeightF.Value - 1f) > 1e-4f || Math.Abs(_legacyUHeightR.Value - 1f) > 1e-4f
                || Math.Abs(_legacyUBumpF.Value - 1f) > 1e-4f || Math.Abs(_legacyUBumpR.Value - 1f) > 1e-4f
                || Math.Abs(_legacyUReboundF.Value - 1f) > 1e-4f || Math.Abs(_legacyUReboundR.Value - 1f) > 1e-4f
                || Math.Abs(_legacyUArbF.Value - 1f) > 1e-4f || Math.Abs(_legacyUArbR.Value - 1f) > 1e-4f;

            if (anyUser)
            {
                string oldName = _suspPreset.Value;
                string mapped = oldName == "Street" ? "Stock" : oldName;
                SuspensionPreset old = SuspensionSettings.Book.FindBuiltIn(mapped);
                SuspensionPreset factors = old ?? SuspensionPreset.Defaults;

                _syncing = true;
                try
                {
                    SuspensionPreset custom = SuspensionPreset.Custom;
                    custom.BasedOn = old != null ? mapped : "";
                    custom.SpringFront = Mathf.Clamp(factors.SpringFront * _legacyUSpringF.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.SpringRear = Mathf.Clamp(factors.SpringRear * _legacyUSpringR.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.RideHeightFront = Mathf.Clamp(factors.RideHeightFront * _legacyUHeightF.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.RideHeightRear = Mathf.Clamp(factors.RideHeightRear * _legacyUHeightR.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.BumpFront = Mathf.Clamp(factors.BumpFront * _legacyUBumpF.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.BumpRear = Mathf.Clamp(factors.BumpRear * _legacyUBumpR.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.ReboundFront = Mathf.Clamp(factors.ReboundFront * _legacyUReboundF.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.ReboundRear = Mathf.Clamp(factors.ReboundRear * _legacyUReboundR.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.ArbFront = Mathf.Clamp(factors.ArbFront * _legacyUArbF.Value, Limits.FactorMin, Limits.FactorMax);
                    custom.ArbRear = Mathf.Clamp(factors.ArbRear * _legacyUArbR.Value, Limits.FactorMin, Limits.FactorMax);
                    SuspensionSettings.ActivePreset = custom;
                    // Persist the folded values into the Suspension.Custom entries so
                    // the end-of-Load save writes them to disk.
                    _suspBasedOn.Value = custom.BasedOn;
                    _suspSpringF.Value = custom.SpringFront;
                    _suspSpringR.Value = custom.SpringRear;
                    _suspHeightF.Value = custom.RideHeightFront;
                    _suspHeightR.Value = custom.RideHeightRear;
                    _suspBumpF.Value = custom.BumpFront;
                    _suspBumpR.Value = custom.BumpRear;
                    _suspReboundF.Value = custom.ReboundFront;
                    _suspReboundR.Value = custom.ReboundRear;
                    _suspArbF.Value = custom.ArbFront;
                    _suspArbR.Value = custom.ArbRear;
                    _suspPreset.Value = "Custom";
                }
                finally
                {
                    _syncing = false;
                }
            }

            string[] legacyKeys =
            {
                "SpringFront", "SpringRear", "RideHeightFront", "RideHeightRear", "BumpFront", "BumpRear",
                "ReboundFront", "ReboundRear", "ArbFront", "ArbRear"
            };
            for (int i = 0; i < legacyKeys.Length; i++)
            {
                _config.Remove(new ConfigDefinition("Suspension.User", legacyKeys[i]));
            }
        }

        // ---------------------------------------------------------------- save / push

        public static void Save()
        {
            if (_config == null)
            {
                return;
            }
            bool autoSave = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;
            _syncing = true;
            try
            {
                _steerEnabled.Value = SteeringSettings.Enabled;
                _steerPreset.Value = SteeringSettings.ActivePreset != null ? SteeringSettings.ActivePreset.Name : "Custom";
                _matchGameSteeringSpeed.Value = SteeringSettings.MatchGameSteeringSpeed;
                SteeringPreset sc = SteeringPreset.Custom;
                _steerBasedOn.Value = sc.BasedOn ?? "";
                _steerRate.Value = sc.RateMultiplier;
                _steerSmoothing.Value = sc.SmoothingScale;
                _steerCurveScale.Value = sc.SpeedCurveScale;
                _steerTraction.Value = sc.TractionClampEnabled;
                _steerSlip.Value = sc.SlipAngleDeg;
                _steerOppLock.Value = sc.OppositeLockBoost;
                _steerReturn.Value = sc.CenterReturnScale;
                _steerLinearityOverride.Value = sc.LinearityOverride;
                _steerLinExp.Value = sc.LinearityExponent;

                _suspEnabled.Value = SuspensionSettings.Enabled;
                _suspPreset.Value = SuspensionSettings.ActivePreset != null ? SuspensionSettings.ActivePreset.Name : "Stock";
                _suspSplit.Value = SuspensionSettings.SplitFrontRear;
                SuspensionPreset uc = SuspensionPreset.Custom;
                _suspBasedOn.Value = uc.BasedOn ?? "";
                _suspSpringF.Value = uc.SpringFront;
                _suspSpringR.Value = uc.SpringRear;
                _suspHeightF.Value = uc.RideHeightFront;
                _suspHeightR.Value = uc.RideHeightRear;
                _suspBumpF.Value = uc.BumpFront;
                _suspBumpR.Value = uc.BumpRear;
                _suspReboundF.Value = uc.ReboundFront;
                _suspReboundR.Value = uc.ReboundRear;
                _suspArbF.Value = uc.ArbFront;
                _suspArbR.Value = uc.ArbRear;

                _aeroEnabled.Value = AeroSettings.Enabled;
                _aeroPreset.Value = AeroSettings.ActivePreset != null ? AeroSettings.ActivePreset.Name : "Stock";
                AeroPreset ac = AeroPreset.Custom;
                _aeroBasedOn.Value = ac.BasedOn ?? "";
                _aeroDownforce.Value = ac.DownforceScale;
                _aeroDrag.Value = ac.DragScale;
                _aeroMaxSpeed.Value = ac.MaxDownforceSpeedScale;

                _brakesEnabled.Value = BrakesSettings.Enabled;
                _brakesPreset.Value = BrakesSettings.ActivePreset != null ? BrakesSettings.ActivePreset.Name : "Stock";
                BrakesPreset bc = BrakesPreset.Custom;
                _brakesBasedOn.Value = bc.BasedOn ?? "";
                _brakesTorque.Value = bc.TorqueScale;
                _brakesFront.Value = bc.FrontBrakeScale;
                _brakesRear.Value = bc.RearBrakeScale;
                _brakesHandbrake.Value = bc.HandbrakeScale;
                _brakesActuation.Value = bc.ActuationScale;

                _gripEnabled.Value = GripSettings.Enabled;
                _gripPreset.Value = GripSettings.ActivePreset != null ? GripSettings.ActivePreset.Name : "Stock";
                GripPreset gc = GripPreset.Custom;
                _gripBasedOn.Value = gc.BasedOn ?? "";
                _gripLng.Value = gc.LongitudinalScale;
                _gripLat.Value = gc.LateralScale;
                _gripStiff.Value = gc.StiffnessScale;

                _drivetrainEnabled.Value = DrivetrainSettings.Enabled;
                _drivetrainPreset.Value = DrivetrainSettings.ActivePreset != null ? DrivetrainSettings.ActivePreset.Name : "Stock";
                DrivetrainPreset dc = DrivetrainPreset.Custom;
                _drivetrainBasedOn.Value = dc.BasedOn ?? "";
                _dtPower.Value = dc.PowerScale;
                _dtRevLimit.Value = dc.RevLimiterScale;
                _dtLoss.Value = dc.LossScale;
                _dtBoost.Value = dc.BoostScale;
                _dtFinalDrive.Value = dc.FinalDriveScale;
                _dtUpshift.Value = dc.UpshiftScale;
                _dtDownshift.Value = dc.DownshiftScale;
                _dtShiftDur.Value = dc.ShiftDurationScale;
                _dtDiffStiff.Value = dc.DiffStiffnessScale;
                _dtDiffBias.Value = dc.DiffBiasScale;
                _dtDiffFront.Value = dc.DiffFrontMode.ToString();
                _dtDiffRear.Value = dc.DiffRearMode.ToString();

                _assistsEnabled.Value = AssistsSettings.Enabled;
                _assistsPreset.Value = AssistsSettings.ActivePreset != null ? AssistsSettings.ActivePreset.Name : "Off";
                AssistsPreset tc = AssistsPreset.Custom;
                _assistsBasedOn.Value = tc.BasedOn ?? "";
                _assistsAbsEnabled.Value = tc.AbsEnabled;
                _assistsAbsThr.Value = tc.AbsSlipThreshold;
                _assistsAbsCut.Value = tc.AbsCutoffSpeed;
                _assistsAbsMult.Value = tc.AbsCutMultiplier;
                _assistsTcsEnabled.Value = tc.TcsEnabled;
                _assistsTcsThr.Value = tc.TcsSlipThreshold;
                _assistsTcsCut.Value = tc.TcsCutoffSpeed;
                _assistsTcsMult.Value = tc.TcsCutMultiplier;
            }
            finally
            {
                _syncing = false;
                _config.SaveOnConfigSet = autoSave;
            }
            _config.Save();
        }

        private static void OnSettingChanged(object sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }
            PushAllToRuntime();
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// Names only (case-insensitive), plus the panel's "LSD" label. Enum.TryParse alone
        /// also accepts any integer ("7"), which produced an undefined DiffMode.
        /// </summary>
        internal static DiffMode ParseDiffMode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return DiffMode.Stock;
            }
            string v = value.Trim();
            if (string.Equals(v, "LSD", StringComparison.OrdinalIgnoreCase))
            {
                return DiffMode.LimitedSlip;
            }
            DiffMode mode;
            if (!char.IsDigit(v[0]) && v[0] != '-' && v[0] != '+'
                && Enum.TryParse(v, true, out mode) && Enum.IsDefined(typeof(DiffMode), mode))
            {
                return mode;
            }
            return DiffMode.Stock;
        }

        private static void PushAllToRuntime()
        {
            SteeringSettings.Enabled = _steerEnabled.Value;
            SteeringSettings.SetPresetByName(_steerPreset.Value);
            SteeringSettings.MatchGameSteeringSpeed = _matchGameSteeringSpeed.Value;
            SteeringPreset sc = SteeringPreset.Custom;
            sc.BasedOn = _steerBasedOn.Value ?? "";
            sc.RateMultiplier = _steerRate.Value;
            sc.SmoothingScale = _steerSmoothing.Value;
            sc.SpeedCurveScale = _steerCurveScale.Value;
            sc.TractionClampEnabled = _steerTraction.Value;
            sc.SlipAngleDeg = _steerSlip.Value;
            sc.OppositeLockBoost = _steerOppLock.Value;
            sc.CenterReturnScale = _steerReturn.Value;
            sc.LinearityOverride = _steerLinearityOverride.Value;
            sc.LinearityExponent = _steerLinExp.Value;
            sc.RestoreBaseCurve();

            SuspensionSettings.Enabled = _suspEnabled.Value;
            SuspensionSettings.SetPresetByName(_suspPreset.Value);
            SuspensionSettings.SplitFrontRear = _suspSplit.Value;
            SuspensionPreset uc = SuspensionPreset.Custom;
            uc.BasedOn = _suspBasedOn.Value ?? "";
            uc.SpringFront = _suspSpringF.Value;
            uc.SpringRear = _suspSpringR.Value;
            uc.RideHeightFront = _suspHeightF.Value;
            uc.RideHeightRear = _suspHeightR.Value;
            uc.BumpFront = _suspBumpF.Value;
            uc.BumpRear = _suspBumpR.Value;
            uc.ReboundFront = _suspReboundF.Value;
            uc.ReboundRear = _suspReboundR.Value;
            uc.ArbFront = _suspArbF.Value;
            uc.ArbRear = _suspArbR.Value;

            AeroSettings.Enabled = _aeroEnabled.Value;
            AeroSettings.SetPresetByName(_aeroPreset.Value);
            AeroPreset ac = AeroPreset.Custom;
            ac.BasedOn = _aeroBasedOn.Value ?? "";
            ac.DownforceScale = _aeroDownforce.Value;
            ac.DragScale = _aeroDrag.Value;
            ac.MaxDownforceSpeedScale = _aeroMaxSpeed.Value;

            BrakesSettings.Enabled = _brakesEnabled.Value;
            BrakesSettings.SetPresetByName(_brakesPreset.Value);
            BrakesPreset bc = BrakesPreset.Custom;
            bc.BasedOn = _brakesBasedOn.Value ?? "";
            bc.TorqueScale = _brakesTorque.Value;
            bc.FrontBrakeScale = _brakesFront.Value;
            bc.RearBrakeScale = _brakesRear.Value;
            bc.HandbrakeScale = _brakesHandbrake.Value;
            bc.ActuationScale = _brakesActuation.Value;

            GripSettings.Enabled = _gripEnabled.Value;
            GripSettings.SetPresetByName(_gripPreset.Value);
            GripPreset gc = GripPreset.Custom;
            gc.BasedOn = _gripBasedOn.Value ?? "";
            gc.LongitudinalScale = _gripLng.Value;
            gc.LateralScale = _gripLat.Value;
            gc.StiffnessScale = _gripStiff.Value;

            DrivetrainSettings.Enabled = _drivetrainEnabled.Value;
            DrivetrainSettings.SetPresetByName(_drivetrainPreset.Value);
            DrivetrainPreset dc = DrivetrainPreset.Custom;
            dc.BasedOn = _drivetrainBasedOn.Value ?? "";
            dc.PowerScale = _dtPower.Value;
            dc.RevLimiterScale = _dtRevLimit.Value;
            dc.LossScale = _dtLoss.Value;
            dc.BoostScale = _dtBoost.Value;
            dc.FinalDriveScale = _dtFinalDrive.Value;
            dc.UpshiftScale = _dtUpshift.Value;
            dc.DownshiftScale = _dtDownshift.Value;
            dc.ShiftDurationScale = _dtShiftDur.Value;
            dc.DiffStiffnessScale = _dtDiffStiff.Value;
            dc.DiffBiasScale = _dtDiffBias.Value;
            dc.DiffFrontMode = ParseDiffMode(_dtDiffFront.Value);
            dc.DiffRearMode = ParseDiffMode(_dtDiffRear.Value);

            AssistsSettings.Enabled = _assistsEnabled.Value;
            AssistsSettings.SetPresetByName(_assistsPreset.Value);
            AssistsPreset tc = AssistsPreset.Custom;
            tc.BasedOn = _assistsBasedOn.Value ?? "";
            tc.AbsEnabled = _assistsAbsEnabled.Value;
            tc.AbsSlipThreshold = _assistsAbsThr.Value;
            tc.AbsCutoffSpeed = _assistsAbsCut.Value;
            tc.AbsCutMultiplier = _assistsAbsMult.Value;
            tc.TcsEnabled = _assistsTcsEnabled.Value;
            tc.TcsSlipThreshold = _assistsTcsThr.Value;
            tc.TcsCutoffSpeed = _assistsTcsCut.Value;
            tc.TcsCutMultiplier = _assistsTcsMult.Value;

            SteeringSettings.UpdateGameSteeringSpeedFactor();
        }
    }
}
