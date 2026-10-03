namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime suspension settings. Effective value = vehicle's stock value x the
    /// active preset's factor (the preset factor IS the slider value). Default
    /// disabled: enabling overrides per-vehicle tuning, so it is opt-in.
    /// </summary>
    public static class SuspensionSettings
    {
        public static readonly PresetBook<SuspensionPreset> Book = new PresetBook<SuspensionPreset>(
            SuspensionPreset.Presets, SuspensionPreset.Stock, SuspensionPreset.Custom,
            SuspensionPreset.Defaults, SuspensionPreset.Stock, name => name == "Street" ? "Stock" : name);

        public static bool Enabled = false;

        // UI preference: show separate front/rear sliders.
        public static bool SplitFrontRear = false;

        public static SuspensionPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        /// <summary>The preset whose factors the sliders display (never null).</summary>
        public static SuspensionPreset Shown
        {
            get { return Book.Active ?? SuspensionPreset.Stock; }
        }

        public static float Spring(bool front)
        {
            return front ? Shown.SpringFront : Shown.SpringRear;
        }

        public static float RideHeight(bool front)
        {
            return front ? Shown.RideHeightFront : Shown.RideHeightRear;
        }

        public static float Bump(bool front)
        {
            return front ? Shown.BumpFront : Shown.BumpRear;
        }

        public static float Rebound(bool front)
        {
            return front ? Shown.ReboundFront : Shown.ReboundRear;
        }

        public static float Arb(bool front)
        {
            return front ? Shown.ArbFront : Shown.ArbRear;
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static SuspensionPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static SuspensionPreset Reference()
        {
            return Book.Reference();
        }

        /// <summary>True when the shown preset has any rear factor different from its front one.</summary>
        public static bool AxlesDiffer()
        {
            SuspensionPreset p = Shown;
            return p.SpringRear != p.SpringFront || p.RideHeightRear != p.RideHeightFront
                || p.BumpRear != p.BumpFront || p.ReboundRear != p.ReboundFront || p.ArbRear != p.ArbFront;
        }

        /// <summary>
        /// Copy front factors to rear (used when turning split mode off).
        /// Copies into Custom first — a built-in preset is never mutated.
        /// </summary>
        public static void LinkRearToFront()
        {
            SuspensionPreset p = BeginEdit();
            if (p == null)
            {
                return;
            }
            p.SpringRear = p.SpringFront;
            p.RideHeightRear = p.RideHeightFront;
            p.BumpRear = p.BumpFront;
            p.ReboundRear = p.ReboundFront;
            p.ArbRear = p.ArbFront;
        }

        public static void ResetAll()
        {
            Enabled = false;
            Book.ResetCustom();
            Book.Active = Book.Identity;
            SplitFrontRear = false;
        }
    }
}
