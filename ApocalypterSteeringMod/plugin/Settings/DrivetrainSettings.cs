namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime drivetrain settings: effective = captured stock value x active
    /// preset factor; differential modes replace the type per axle unless
    /// "Stock". Opt-in.
    /// </summary>
    public static class DrivetrainSettings
    {
        public static readonly PresetBook<DrivetrainPreset> Book = new PresetBook<DrivetrainPreset>(
            DrivetrainPreset.Presets, DrivetrainPreset.Stock, DrivetrainPreset.Custom,
            DrivetrainPreset.Stock, DrivetrainPreset.Stock);

        public static bool Enabled = false;

        public static DrivetrainPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static DrivetrainPreset Shown
        {
            get { return Book.Active ?? DrivetrainPreset.Stock; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static DrivetrainPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static DrivetrainPreset Reference()
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
