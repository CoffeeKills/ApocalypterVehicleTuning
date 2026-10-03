namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime assist settings. The tuner registers one delegate per vehicle
    /// into NWH's brakeTorqueModifiers / powerModifiers lists; the delegates
    /// read these live fields every tick, so slider changes apply instantly.
    /// </summary>
    public static class AssistsSettings
    {
        public static readonly PresetBook<AssistsPreset> Book = new PresetBook<AssistsPreset>(
            AssistsPreset.Presets, AssistsPreset.Off, AssistsPreset.Custom, AssistsPreset.Off, AssistsPreset.Off);

        public static bool Enabled = false;

        public static AssistsPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static AssistsPreset Shown
        {
            get { return Book.Active ?? AssistsPreset.Off; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static AssistsPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static AssistsPreset Reference()
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
