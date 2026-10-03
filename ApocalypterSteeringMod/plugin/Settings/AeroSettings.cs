namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime aero settings: effective = captured stock value x active preset
    /// factor. Opt-in; the applier onboards an AerodynamicsModule on vehicles
    /// that do not ship one and disables it again on restore.
    /// </summary>
    public static class AeroSettings
    {
        public static readonly PresetBook<AeroPreset> Book = new PresetBook<AeroPreset>(
            AeroPreset.Presets, AeroPreset.Stock, AeroPreset.Custom, AeroPreset.Stock, AeroPreset.Stock);

        public static bool Enabled = false;

        public static AeroPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static AeroPreset Shown
        {
            get { return Book.Active ?? AeroPreset.Stock; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static AeroPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static AeroPreset Reference()
        {
            return Book.Reference();
        }

        public static void ResetAll()
        {
            Enabled = false;
            Book.ResetCustom();
            Book.Active = Book.Identity;
        }
    }
}
