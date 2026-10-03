namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime brake settings: effective = captured stock value x active preset
    /// factor (with the game's own range clamps applied on top). Opt-in.
    /// </summary>
    public static class BrakesSettings
    {
        public static readonly PresetBook<BrakesPreset> Book = new PresetBook<BrakesPreset>(
            BrakesPreset.Presets, BrakesPreset.Stock, BrakesPreset.Custom, BrakesPreset.Stock, BrakesPreset.Stock);

        public static bool Enabled = false;

        public static BrakesPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static BrakesPreset Shown
        {
            get { return Book.Active ?? BrakesPreset.Stock; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static BrakesPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static BrakesPreset Reference()
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
