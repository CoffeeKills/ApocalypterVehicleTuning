namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// One steering behavior preset. Non-Vanilla presets feed the traction-edge
    /// patch; Vanilla skips the patch body entirely and leaves stock NWH steering.
    /// The Custom instance is what the settings panel edits: touching any control
    /// while a built-in preset is active copies that preset into Custom first
    /// (curves are deep-copied — presets are never mutated).
    /// </summary>
    public sealed class SteeringPreset : ITunablePreset
    {
        public string Name { get; set; }         // stable id, stored in the config file
        public string Label { get; set; }        // short text for the preset button
        public string Description { get; set; }  // plain-language summary shown in the panel
        public bool IsVanilla;

        public bool CanEdit => !IsVanilla;

        public void CopyValuesFrom(ITunablePreset src)
        {
            CopyFrom((SteeringPreset)src);
        }

        // Multiplier on the vehicle's configured degreesPerSecondLimit.
        public float RateMultiplier = 1f;

        // true = evaluate each vehicle's own speedSensitiveSteeringCurve;
        // false = use LockCurve instead.
        public bool UseVehicleCurve;

        // Lock-at-speed curve (x = Speed/50, y = fraction of maximumSteerAngle).
        public EditableCurve LockCurve;

        // Return-to-center curve (x = Speed/50, y = fraction of the steer-in rate
        // used while unwinding toward center). Flat 1 = symmetric return; a ramp
        // from 0 = holds the angle when stopped, straightens out as speed builds.
        public EditableCurve ReturnCurve;

        // Multiplier on the vehicle's speedSensitiveSmoothingCurve value.
        public float SmoothingScale = 1f;

        // Clamp the steer target so front tires stay near their peak-grip slip angle.
        public bool TractionClampEnabled = true;
        public float SlipAngleDeg = 8.5f;

        // Steering rate boost while applying opposite lock (1.0 = none).
        public float OppositeLockBoost = 1.75f;

        // true = use pow(|input|, LinearityExponent) instead of the vehicle's linearity curve.
        public bool LinearityOverride;
        public float LinearityExponent = 1f;

        // 0.8.0: override the vehicle's own steering lock (degrees; 0 = the vehicle's own).
        // High values give drift-style extra angle; the whole steer pipeline uses this cap.
        public float MaxSteerAngle = 0f;

        // Custom only: the built-in preset this was copied from ("" = none).
        // Persisted so the preset's curves can be restored after a restart.
        public string BasedOn { get; set; } = "";

        public static readonly SteeringPreset[] Presets;
        public static readonly SteeringPreset Vanilla;
        public static readonly SteeringPreset Custom;

        /// <summary>The v2.0.0 behaviour. Not selectable; used as the reset target.</summary>
        public static readonly SteeringPreset Defaults;

        static SteeringPreset()
        {
            EditableCurve templateLock = TemplateLockCurve();
            EditableCurve flatReturn = EditableCurve.Flat(1f);

            Defaults = new SteeringPreset
            {
                Name = "Defaults",
                Label = "Defaults",
                UseVehicleCurve = true,
                LockCurve = templateLock.Clone(),
                ReturnCurve = flatReturn.Clone()
            };

            Vanilla = new SteeringPreset
            {
                Name = "Vanilla",
                Label = "Vanilla",
                IsVanilla = true,
                Description = "The game's original steering. The mod changes nothing. Pick another preset to tune.",
                UseVehicleCurve = true,
                LockCurve = templateLock.Clone(),   // editor display/reference only
                ReturnCurve = flatReturn.Clone()
            };

            Custom = new SteeringPreset
            {
                Name = "Custom",
                Label = "Custom",
                Description = "Your own tuning.",
                UseVehicleCurve = true,
                LockCurve = templateLock.Clone(),
                ReturnCurve = flatReturn.Clone()
            };

            Presets = new SteeringPreset[]
            {
                Vanilla,
                new SteeringPreset
                {
                    Name = "GTA-style Keyboard",
                    Label = "GTA-style",
                    Description = "Quick, responsive arcade steering built for keyboards. Turns in fast and eases off at speed.",
                    RateMultiplier = 1.6f,
                    UseVehicleCurve = false,
                    LockCurve = EditableCurve.FromPoints(0f, 1f, 0.35f, 0.45f, 1f, 0.15f),
                    ReturnCurve = EditableCurve.Flat(1f),
                    SmoothingScale = 0.8f,
                    SlipAngleDeg = 8.5f,
                    OppositeLockBoost = 1.75f,
                    LinearityOverride = true,
                    LinearityExponent = 1.3f
                },
                new SteeringPreset
                {
                    Name = "Euro Truck",
                    Label = "Euro Truck",
                    Description = "Heavy highway-truck feel: slow steering, soft response and very little lock at speed. Holds the wheels where you leave them when stopped, then straightens out as you drive.",
                    RateMultiplier = 0.5f,
                    UseVehicleCurve = false,
                    LockCurve = EditableCurve.FromPoints(0f, 1f, 0.2f, 0.6f, 0.5f, 0.3f, 1f, 0.12f),
                    ReturnCurve = EditableCurve.FromPoints(0f, 0f, 0.3f, 0.4f, 1f, 0.7f),
                    SmoothingScale = 1.7f,
                    SlipAngleDeg = 6.5f,
                    OppositeLockBoost = 1f,
                    LinearityOverride = true,
                    LinearityExponent = 1.35f
                },
                new SteeringPreset
                {
                    Name = "Sim/Race",
                    Label = "Sim / Race",
                    Description = "Balanced and precise. Smooth response and firm stability at high speed.",
                    RateMultiplier = 1f,
                    UseVehicleCurve = false,
                    LockCurve = EditableCurve.FromPoints(0f, 1f, 0.5f, 0.35f, 1f, 0.22f),
                    ReturnCurve = EditableCurve.Flat(1f),
                    SmoothingScale = 1f,
                    SlipAngleDeg = 8.5f,
                    OppositeLockBoost = 1.5f,
                    LinearityOverride = false,
                    LinearityExponent = 1f
                },
                new SteeringPreset
                {
                    Name = "Drift",
                    Label = "Drift",
                    Description = "Loose and playful. Fast counter-steer and a wide grip window make slides easy to hold.",
                    RateMultiplier = 1.4f,
                    UseVehicleCurve = false,
                    LockCurve = EditableCurve.FromPoints(0f, 1f, 0.4f, 0.55f, 1f, 0.3f),
                    ReturnCurve = EditableCurve.Flat(1f),
                    SmoothingScale = 0.6f,
                    SlipAngleDeg = 12f,
                    OppositeLockBoost = 2f,
                    LinearityOverride = true,
                    LinearityExponent = 1f
                },
                Custom
            };
        }

        /// <summary>The starter curve shown to users who turn "use the vehicle's own curve" off.</summary>
        private static EditableCurve TemplateLockCurve()
        {
            return EditableCurve.FromPoints(0f, 1f, 0.5f, 0.35f, 1f, 0.22f);
        }

        /// <summary>Copies the tuning values (not the identity) from another preset.</summary>
        public void CopyFrom(SteeringPreset src)
        {
            RateMultiplier = src.RateMultiplier;
            UseVehicleCurve = src.UseVehicleCurve;
            LockCurve = src.LockCurve != null ? src.LockCurve.Clone() : null;   // curves are mutable: clone, never share
            ReturnCurve = src.ReturnCurve != null ? src.ReturnCurve.Clone() : null;
            SmoothingScale = src.SmoothingScale;
            TractionClampEnabled = src.TractionClampEnabled;
            SlipAngleDeg = src.SlipAngleDeg;
            OppositeLockBoost = src.OppositeLockBoost;
            LinearityOverride = src.LinearityOverride;
            LinearityExponent = src.LinearityExponent;
            MaxSteerAngle = src.MaxSteerAngle;
        }

        /// <summary>A real tunable preset by config name (never Custom / Vanilla), or null.</summary>
        public static SteeringPreset FindBuiltIn(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            for (int i = 0; i < Presets.Length; i++)
            {
                SteeringPreset p = Presets[i];
                if (p != Custom && !p.IsVanilla && p.Name == name)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>Custom only: back to the v2.0.0 defaults.</summary>
        public void ResetToDefaults()
        {
            CopyFrom(Defaults);
            BasedOn = "";
        }

        /// <summary>
        /// Custom only: restore UseVehicleCurve and both curves from the preset
        /// Custom was copied from (BasedOn), or the defaults when that does not
        /// resolve. Now a fallback for missing or garbage config strings — the
        /// config itself stores the curves.
        /// </summary>
        public void RestoreBaseCurve()
        {
            SteeringPreset b = FindBuiltIn(BasedOn);
            SteeringPreset src = b ?? Defaults;
            UseVehicleCurve = src.UseVehicleCurve;
            LockCurve = src.LockCurve.Clone();
            ReturnCurve = src.ReturnCurve.Clone();
        }
    }
}
