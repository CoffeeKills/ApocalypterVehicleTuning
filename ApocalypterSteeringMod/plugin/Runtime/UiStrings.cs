namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Every dynamic panel string as a {0} template plus the value formatters.
    /// ApocaLanguage (if installed) translates these like any other UI text —
    /// see docs/strings.md. Rules: exactly {0} as the only placeholder (repeat
    /// allowed), units live inside the template, singular/plural are separate
    /// templates, and no angle brackets (labels render rich text).
    /// Static labels stay literals in SettingsPanel.cs and need no entry here.
    /// </summary>
    public static class UiStrings
    {
        // ---- value formatters -------------------------------------------------

        public static readonly string TimesFmt = "×{0}";
        public static readonly string ForceFmt = "{0} N";
        public static readonly string RateFmt = "{0} N·s/m";
        public static readonly string LengthFmt = "{0} cm";
        public static readonly string PercentFmt = "{0}%";
        public static readonly string DegFmt = "{0} deg";
        public static readonly string SpeedFmt = "{0} m/s";

        public static string Times(float v) { return string.Format(TimesFmt, v.ToString("0.00")); }
        public static string Force(float n) { return string.Format(ForceFmt, n.ToString("N0")); }
        public static string Rate(float r) { return string.Format(RateFmt, r.ToString("N0")); }
        public static string Length(float m) { return string.Format(LengthFmt, UnityEngine.Mathf.RoundToInt(m * 100f)); }
        public static string Percent(float v) { return string.Format(PercentFmt, UnityEngine.Mathf.RoundToInt(v * 100f)); }
        public static string Deg(float v) { return string.Format(DegFmt, v.ToString("0.0")); }
        public static string SpeedMps(float v) { return string.Format(SpeedFmt, v.ToString("0.0")); }

        // ---- preset semantics -------------------------------------------------

        // {0} = the built-in preset's Label the Custom slot was copied from.
        public static readonly string CustomPresetFmt = "Custom ({0})";
        // {0} = the same Label, twice.
        public static readonly string CustomBasedOnDescFmt =
            "Your tuning, based on {0}. Reset on a slider returns it to the {0} value.";
        public static readonly string CustomPlainDesc =
            "Your own tuning. Moving a slider on any preset copies it here, so presets stay intact.";
        // {0} = the built-in preset's Description this note is appended to.
        public static readonly string PresetEditNoteFmt = "{0} Move any slider to customise it.";

        // ---- steering tab ------------------------------------------------------

        // {0} = the game's steering-speed setting (integer string).
        public static readonly string GameSpeedHintFoundFmt =
            "Also scale by the game's own steering speed option (currently {0}, 50 = normal)";
        public static readonly string GameSpeedHintMissing =
            "Also scale by the game's own steering speed option (not found, using 50)";

        // ---- vehicle status lines (per-category suffixes are separate templates) ----

        // {0} = vehicle count.
        public static readonly string AppliedOneFmt = "Applied to {0} vehicle.";
        public static readonly string AppliedManyFmt = "Applied to {0} vehicles.";
        public static readonly string AppliedOneArbFmt =
            "Applied to {0} vehicle. A vehicle without an anti-roll bar is never given one.";
        public static readonly string AppliedManyArbFmt =
            "Applied to {0} vehicles. A vehicle without an anti-roll bar is never given one.";
        public static readonly string AppliedOneAeroFmt =
            "Applied to {0} vehicle. Vehicles without a downforce setup get drag tuning only.";
        public static readonly string AppliedManyAeroFmt =
            "Applied to {0} vehicles. Vehicles without a downforce setup get drag tuning only.";
        public static readonly string AppliedOneGripFmt =
            "Applied to {0} vehicle. Surface changes (mud, asphalt) still apply on top.";
        public static readonly string AppliedManyGripFmt =
            "Applied to {0} vehicles. Surface changes (mud, asphalt) still apply on top.";
        public static readonly string AppliedOneGripWarnFmt =
            "Applied to {0} vehicle. Surface changes (mud, asphalt) still apply on top. Warning: a vehicle has a tire-wear component that rewrites grip.";
        public static readonly string AppliedManyGripWarnFmt =
            "Applied to {0} vehicles. Surface changes (mud, asphalt) still apply on top. Warning: a vehicle has a tire-wear component that rewrites grip.";
        public static readonly string AppliedOneDtFmt =
            "Applied to {0} vehicle. The engine sound's max RPM follows the stock value.";
        public static readonly string AppliedManyDtFmt =
            "Applied to {0} vehicles. The engine sound's max RPM follows the stock value.";
        public static readonly string ActiveOnOneFmt = "Active on {0} vehicle.";
        public static readonly string ActiveOnManyFmt = "Active on {0} vehicles.";

        // ---- drivetrain tab ----------------------------------------------------

        // {0} = the diff-mode label.
        public static readonly string FrontAxleFmt =
            "Front axle: {0}. Axles are found from the wheels each diff drives; an axle without a diff keeps its stock setup.";
        public static readonly string RearAxleFmt =
            "Rear axle: {0}. Centre (AWD) diffs always keep their stock type.";
    }
}
