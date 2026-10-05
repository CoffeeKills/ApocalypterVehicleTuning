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

        // 0.6.0 value formatters.
        public static readonly string TorqueFmt = "{0} N·m";
        public static readonly string DegSignedFmt = "{0}°";
        public static readonly string CmSignedFmt = "{0} cm";
        public static readonly string PxFmt = "{0} px";
        public static readonly string RatioFmt = "ratio {0}";

        public static string Torque(float nm) { return string.Format(TorqueFmt, nm.ToString("N0")); }
        public static string DegSigned(float v) { return string.Format(DegSignedFmt, v.ToString("+0.0;-0.0;0.0")); }
        public static string DegSigned2(float v) { return string.Format(DegSignedFmt, v.ToString("+0.00;-0.00;0.00")); }
        public static string CmSigned(float v) { return string.Format(CmSignedFmt, v.ToString("+0;-0;0")); }
        public static string Px(float v) { return string.Format(PxFmt, UnityEngine.Mathf.RoundToInt(v)); }
        public static string Ratio(float r) { return string.Format(RatioFmt, r.ToString("0.000")); }

        // 0.10.0 weight tab: signed whole kilograms ("+400 kg", "-250 kg", "0 kg").
        public static readonly string KgFmt = "{0} kg";
        public static string Kg(float kg) { return string.Format(KgFmt, kg.ToString("+0;-0;0")); }

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

        // ---- alignment tab (0.6.0) ------------------------------------------------

        // {0} = the reference vehicle's stock value, formatted with its unit ("-1.2°", "+0 cm").
        public static readonly string AlignmentStockFmt = "stock {0}";
        public static readonly string AppliedOneAlignFmt =
            "Applied to {0} vehicle. Moved wheels: the vehicle keeps its original wheelbase and track width for steering geometry.";
        public static readonly string AppliedManyAlignFmt =
            "Applied to {0} vehicles. Moved wheels: the vehicles keep their original wheelbase and track width for steering geometry.";

        // ---- gearbox tab (0.6.0) ---------------------------------------------------

        // {0} = gear number.
        public static readonly string GearTitleFmt = "Gear {0}";
        // {0} = a gear count.
        public static readonly string GearCountFmt = "{0} gears";
        public static readonly string AppliedOneGearFmt = "Applied to {0} vehicle.";
        public static readonly string AppliedManyGearFmt = "Applied to {0} vehicles.";

        // ---- weight tab (0.10.0) ---------------------------------------------------

        public static readonly string AppliedOneWeightFmt =
            "Applied to {0} vehicle. Springs are re-scaled with the mass so ballast does not bottom them out.";
        public static readonly string AppliedManyWeightFmt =
            "Applied to {0} vehicles. Springs are re-scaled with the mass so ballast does not bottom them out.";

        // ---- telemetry strip (0.6.0) ------------------------------------------------

        public static readonly string TargetMissingFmt = "{0} (not here)";

        // 0.7.0 drivetrain tab (centre diff, torque split, layout).
        public static readonly string CenterDiffFmt = "Centre (AWD) differential: {0}. Vehicles without one ignore it.";
        public static readonly string SplitNoVehicle = "Torque split: no vehicle found yet.";
        public static readonly string SplitOneAxleFmt = "{0} drives one axle. A custom layout below can make it AWD.";
        public static readonly string SplitFmt = "Torque split of the car you drive (nominal): {0}";
        public static readonly string AxleFrontFmt = "front {0}%";
        public static readonly string AxleRearFmt = "rear {0}%";
        public static readonly string AxleNFmt = "axle {0}";
        public static readonly string LayoutOffFmt = "Custom layout is off. Layout text: {0}";
        public static readonly string LayoutInvalidFmt = "Layout ignored: {0}";
        public static readonly string LayoutNeedsDrivetrainFmt = "Turn Drivetrain tuning on to use the layout: {0}";
        public static readonly string LayoutActiveFmt = "Layout active: {0}";
        public static readonly string LayoutUnfitFmt = "This vehicle keeps its own drivetrain: {0}.";
        public static readonly string LayoutTextFmt = "Layout: {0}";
        public static readonly string LayoutCopied = "This vehicle's layout copied to the clipboard.";
        public static readonly string LayoutCopyNone = "This vehicle's layout cannot be written as layout text.";
        public static readonly string LayoutPasted = "Layout pasted.";
        public static readonly string LayoutPasteFailedFmt = "Not a layout: {0}";

        // 0.7.0 gearbox shift controller.
        public static readonly string ShiftedOneFmt = "The mod shifts {0} vehicle (the game's gear display may lag a moment).";
        public static readonly string ShiftedManyFmt = "The mod shifts {0} vehicles (the game's gear display may lag a moment).";

        public static readonly string TelemetrySpeedFmt = "{0} km/h";
        public static readonly string TelemetryRpmFmt = "{0} rpm";
        public static readonly string TelemetryGearFmt = "Gear {0}";
        public static readonly string TelemetrySlipFmt = "Slip {0}°";


        public static string TelemetrySpeed(float kmh) { return string.Format(TelemetrySpeedFmt, UnityEngine.Mathf.RoundToInt(kmh < 0f ? -kmh : kmh)); }
        public static string TelemetryRpm(float rpm) { return string.Format(TelemetryRpmFmt, UnityEngine.Mathf.RoundToInt(rpm < 0f ? 0f : rpm)); }
        public static string TelemetryGear(string gear) { return string.Format(TelemetryGearFmt, string.IsNullOrEmpty(gear) ? "-" : gear); }
        /// <summary>NWH's LateralSlip is degrees x 0.01111 (WheelController.cs:1010) scaled by the
        /// friction stiffness; x90 brings it back to approximate degrees (exact at stiffness 1).</summary>
        public static string TelemetrySlip(float normalisedSlip) { return string.Format(TelemetrySlipFmt, (normalisedSlip * 90f).ToString("0.0")); }

        // ---- preset copy / paste (0.6.0) ---------------------------------------------

        // {0} = the copied preset's label.
        public static readonly string PresetCopiedFmt = "Copied the {0} preset. Paste it on this tab, here or on another PC.";
        // {0} = number of values applied.
        public static readonly string PresetPastedFmt = "Pasted into Custom: {0} values applied.";
        // {0} = number of unknown/unreadable values skipped.
        public static readonly string PresetPasteSkippedFmt = "{0} unknown values were skipped.";
        // {0} = one of the reasons below.
        public static readonly string PresetPasteFailedFmt = "Paste failed: {0}.";
        public static readonly string PasteReasonEmpty = "the clipboard is empty";
        public static readonly string PasteReasonWrongTag = "the clipboard does not hold a Vehicle Tuning preset";
        public static readonly string PasteReasonWrongCategory = "that preset belongs to another tab";
        public static readonly string PasteReasonMalformed = "the preset text is damaged";
        public static readonly string PasteReasonNoClipboard = "no clipboard access";
    }
}
