namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime alignment settings: effective geometry = each vehicle's captured stock
    /// geometry + the active preset's offsets. Opt-in. PerWheel is a UI preference:
    /// off = one slider per axle that writes both wheels of that axle.
    /// </summary>
    public static class AlignmentSettings
    {
        public static readonly PresetBook<AlignmentPreset> Book = new PresetBook<AlignmentPreset>(
            AlignmentPreset.Presets, AlignmentPreset.Stock, AlignmentPreset.Custom,
            AlignmentPreset.Defaults, AlignmentPreset.Stock);

        public static bool Enabled = false;

        // UI preference: per-wheel camber/position sliders.
        public static bool PerWheel = false;

        public static AlignmentPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        /// <summary>The preset whose offsets the sliders display (never null).</summary>
        public static AlignmentPreset Shown
        {
            get { return Book.Active ?? AlignmentPreset.Stock; }
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static AlignmentPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static AlignmentPreset Reference()
        {
            return Book.Reference();
        }

        /// <summary>Axle helper: write the same camber to both wheels of an axle.</summary>
        public static void SetAxleCamber(AlignmentPreset p, bool front, float v)
        {
            p.SetCamber(front ? WheelRole.FL : WheelRole.RL, v);
            p.SetCamber(front ? WheelRole.FR : WheelRole.RR, v);
        }

        /// <summary>Axle helper: write the same position offset to both wheels of an axle.</summary>
        public static void SetAxlePos(AlignmentPreset p, bool front, int axis, float v)
        {
            p.SetPos(front ? WheelRole.FL : WheelRole.RL, axis, v);
            p.SetPos(front ? WheelRole.FR : WheelRole.RR, axis, v);
        }

        /// <summary>
        /// Copy left wheel values to the right wheel of each axle (used when turning
        /// per-wheel mode off). Copies into Custom first — built-ins are never mutated.
        /// </summary>
        public static void LinkSides()
        {
            AlignmentPreset p = BeginEdit();
            if (p == null)
            {
                return;
            }
            p.CamberFR = p.CamberFL;
            p.CamberRR = p.CamberRL;
            for (int axis = 0; axis < 3; axis++)
            {
                p.SetPos(WheelRole.FR, axis, p.Pos(WheelRole.FL, axis));
                p.SetPos(WheelRole.RR, axis, p.Pos(WheelRole.RL, axis));
            }
        }

        public static void ResetAll()
        {
            Enabled = false;
            PerWheel = false;
            Book.ResetCustom();
            Book.Active = Book.Identity;
        }
    }
}
