using UnityEngine;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// One steering behavior preset. Non-Vanilla presets feed the traction-edge
    /// patch; Vanilla skips the patch body entirely and leaves stock NWH steering.
    /// The Custom instance is what the settings panel edits: touching any slider
    /// while a built-in preset is active copies that preset into Custom first.
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
        // true = replace the vehicle's speedSensitiveSteeringCurve with SpeedCurve
        // (evaluated at Speed/50f, same normalization as vanilla).
        public bool CurveOverride;
        public AnimationCurve SpeedCurve;

        // Scales the speed curve (the vehicle's own, or SpeedCurve when overridden).
        public float SpeedCurveScale = 1f;

        // Multiplier on the vehicle's speedSensitiveSmoothingCurve value.
        public float SmoothingScale = 1f;

        // Clamp the steer target so front tires stay near their peak-grip slip angle.
        public bool TractionClampEnabled = true;
        public float SlipAngleDeg = 8.5f;

        // Steering rate boost while applying opposite lock (1.0 = none).
        public float OppositeLockBoost = 1.75f;

        // Multiplier on the steering rate while the wheel unwinds toward center
        // (|target| < |angle|). Below 1 = the wheel stays where it was put and
        // eases back instead of snapping — the ETS/truck feel. 1.0 = symmetric.
        public float CenterReturnScale = 1f;

        // true = use pow(|input|, LinearityExponent) instead of the vehicle's linearity curve.
        public bool LinearityOverride;
        public float LinearityExponent = 1f;

        // Custom only: the built-in preset this was copied from ("" = none).
        // Persisted so the preset's speed curve can be restored after a restart.
        public string BasedOn { get; set; } = "";

        public static readonly SteeringPreset[] Presets;
        public static readonly SteeringPreset Vanilla;
        public static readonly SteeringPreset Custom;

        /// <summary>The v2.0.0 behaviour. Not selectable; used as the reset target.</summary>
        public static readonly SteeringPreset Defaults;

        static SteeringPreset()
        {
            Defaults = new SteeringPreset { Name = "Defaults", Label = "Defaults" };

            Vanilla = new SteeringPreset
            {
                Name = "Vanilla",
                Label = "Vanilla",
                IsVanilla = true,
                Description = "The game's original steering. The mod changes nothing. Pick another preset to tune."
            };

            Custom = new SteeringPreset
            {
                Name = "Custom",
                Label = "Custom",
                Description = "Your own tuning."
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
                    CurveOverride = true,
                    SpeedCurve = new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(0.35f, 0.45f),
                        new Keyframe(1f, 0.15f)),
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
                    Description = "Heavy highway-truck feel: slow steering, soft response and very little lock at speed.",
                    RateMultiplier = 0.5f,
                    CurveOverride = true,
                    SpeedCurve = new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(0.2f, 0.6f),
                        new Keyframe(0.5f, 0.3f),
                        new Keyframe(1f, 0.12f)),
                    SmoothingScale = 1.7f,
                    SlipAngleDeg = 6.5f,
                    OppositeLockBoost = 1f,
                    CenterReturnScale = 0.25f,
                    LinearityOverride = true,
                    LinearityExponent = 1.35f
                },
                new SteeringPreset
                {
                    Name = "Sim/Race",
                    Label = "Sim / Race",
                    Description = "Balanced and precise. Smooth response and firm stability at high speed.",
                    RateMultiplier = 1f,
                    CurveOverride = true,
                    SpeedCurve = new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(0.5f, 0.35f),
                        new Keyframe(1f, 0.22f)),
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
                    CurveOverride = true,
                    SpeedCurve = new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(0.4f, 0.55f),
                        new Keyframe(1f, 0.3f)),
                    SmoothingScale = 0.6f,
                    SlipAngleDeg = 12f,
                    OppositeLockBoost = 2f,
                    LinearityOverride = true,
                    LinearityExponent = 1f
                },
                Custom
            };
        }

        /// <summary>Copies the tuning values (not the identity) from another preset.</summary>
        public void CopyFrom(SteeringPreset src)
        {
            RateMultiplier = src.RateMultiplier;
            CurveOverride = src.CurveOverride;
            SpeedCurve = src.SpeedCurve;   // curves are never mutated, safe to share
            SpeedCurveScale = src.SpeedCurveScale;
            SmoothingScale = src.SmoothingScale;
            TractionClampEnabled = src.TractionClampEnabled;
            SlipAngleDeg = src.SlipAngleDeg;
            OppositeLockBoost = src.OppositeLockBoost;
            LinearityOverride = src.LinearityOverride;
            LinearityExponent = src.LinearityExponent;
            CenterReturnScale = src.CenterReturnScale;
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
        /// Custom only: the config file stores BasedOn but not the speed curve itself,
        /// so after loading, re-attach the curve of the preset Custom was copied from.
        /// </summary>
        public void RestoreBaseCurve()
        {
            SteeringPreset b = FindBuiltIn(BasedOn);
            if (b != null && b.CurveOverride)
            {
                CurveOverride = true;
                SpeedCurve = b.SpeedCurve;
            }
            else
            {
                CurveOverride = false;
                SpeedCurve = null;
            }
        }
    }
}
