namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime grip settings: effective = captured stock friction x active
    /// preset factor. Opt-in.
    /// </summary>
    public static class GripSettings
    {
        public static readonly PresetBook<GripPreset> Book = new PresetBook<GripPreset>(
            GripPreset.Presets, GripPreset.Stock, GripPreset.Custom, GripPreset.Stock, GripPreset.Stock);

        public static bool Enabled = false;

        public static GripPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static GripPreset Shown
        {
            get { return Book.Active ?? GripPreset.Stock; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static GripPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static GripPreset Reference()
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
