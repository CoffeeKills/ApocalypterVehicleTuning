namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime weight settings: positive kg = real ballast (mass + centre of
    /// mass), negative kg = balloon lift at that axle. Opt-in; the applier
    /// re-scales the suspension springs so the extra mass doesn't bottom them
    /// out, and VehicleTuner.FixedUpdate applies the lift (0.11.0).
    /// </summary>
    public static class WeightSettings
    {
        public static readonly PresetBook<WeightPreset> Book = new PresetBook<WeightPreset>(
            WeightPreset.Presets, WeightPreset.Stock, WeightPreset.Custom, WeightPreset.Stock, WeightPreset.Stock);

        public static bool Enabled = false;

        public static WeightPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static WeightPreset Shown
        {
            get { return Book.Active ?? WeightPreset.Stock; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static WeightPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static WeightPreset Reference()
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
