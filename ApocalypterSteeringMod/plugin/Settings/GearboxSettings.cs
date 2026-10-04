namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime gearbox settings: per-gear ratio factors, optional gear count, clutch
    /// "type" (emulated through capacity and engagement), transmission mode. Opt-in.
    /// </summary>
    public static class GearboxSettings
    {
        public static readonly PresetBook<GearboxPreset> Book = new PresetBook<GearboxPreset>(
            GearboxPreset.Presets, GearboxPreset.Stock, GearboxPreset.Custom,
            GearboxPreset.Defaults, GearboxPreset.Stock);

        public static bool Enabled = false;

        /// <summary>
        /// Release gate: the gearbox category is hidden behind "coming soon" until the
        /// mod owns shifting (the game's FSM shift logic fights resized boxes on
        /// automatic transmissions). The tests flip this off to exercise the logic.
        /// </summary>
        public static bool ComingSoon = true;

        public static GearboxPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        public static GearboxPreset Shown
        {
            get { return Book.Active ?? GearboxPreset.Stock; }
        }

        /// <summary>Ratio factor of forward gear <paramref name="gear"/> (1-based) in the shown preset.</summary>
        public static float PerGearScale(int gear)
        {
            return Shown.Scale(gear);
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static GearboxPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static GearboxPreset Reference()
        {
            return Book.Reference();
        }

        /// <summary>Index into GearboxPreset.ClutchTypes of the matching type, or -1 (= Custom).</summary>
        public static int ClutchTypeIndex(GearboxPreset p)
        {
            for (int i = 0; i < GearboxPreset.ClutchTypes.Length; i++)
            {
                if (GearboxPreset.ClutchTypes[i].Matches(p))
                {
                    return i;
                }
            }
            return -1;
        }

        public static void ResetAll()
        {
            Enabled = false;
            Book.ResetCustom();
            Book.Active = Book.Identity;
        }
    }
}
