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

        // ------------------------------------------------ custom layout (0.6.2, config only)

        /// <summary>
        /// The example written into a fresh config: full-time AWD, open centre diff sending
        /// 40% to the front, open front axle, limited-slip rear axle.
        /// </summary>
        public const string DefaultLayoutText =
            "gearbox -> transfer; transfer: Open split=0.4 -> front, rear; front: Open -> FL, FR; rear: LSD -> RL, RR";

        /// <summary>
        /// Layout templates for the panel (0.7.0, FEATURES §2): one click writes the text. Wheel
        /// names FL/FR/RL/RR are the first/last axle. The game has no 6x6 vehicles, so no 6x6
        /// template (0.7.1) — but the layout parser still accepts multi-axle text from older configs.
        /// </summary>
        public static readonly string[] LayoutTemplateNames = { "RWD", "FWD", "AWD", "4x4 locked" };
        public static readonly string[] LayoutTemplates =
        {
            "gearbox -> rear; rear: LSD -> RL, RR",
            "gearbox -> front; front: Open -> FL, FR",
            DefaultLayoutText,
            "gearbox -> transfer; transfer: Locked -> front, rear; front: Open -> FL, FR; rear: Open -> RL, RR"
        };

        /// <summary>Index of the template the current text equals, or -1 (a hand-written layout).</summary>
        public static int LayoutTemplateIndex()
        {
            for (int i = 0; i < LayoutTemplates.Length; i++)
            {
                if (string.Equals(LayoutTemplates[i], LayoutText, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Use <see cref="Layout"/> instead of each vehicle's own wiring (needs Enabled too).</summary>
        public static bool LayoutEnabled = false;

        private static string _layoutText;
        private static DrivetrainLayout _layout;
        private static string _layoutError;

        /// <summary>The parsed layout; null when the text is invalid (see LayoutError).</summary>
        public static DrivetrainLayout Layout
        {
            get { EnsureLayoutParsed(); return _layout; }
        }

        /// <summary>Why the layout text was rejected, or null.</summary>
        public static string LayoutError
        {
            get { EnsureLayoutParsed(); return _layoutError; }
        }

        /// <summary>
        /// The raw layout text, kept verbatim (the config round-trips exactly what the user
        /// typed). Parsed once per distinct text; an unchanged value keeps the same parsed
        /// object, which the tuner uses as its "layout changed" signal.
        /// </summary>
        public static string LayoutText
        {
            get { return _layoutText ?? DefaultLayoutText; }
            set
            {
                string v = value ?? "";
                if (_layoutText != null && string.Equals(_layoutText, v, System.StringComparison.Ordinal))
                {
                    return;
                }
                _layoutText = v;
                DrivetrainLayout parsed;
                string error;
                _layout = DrivetrainLayout.TryParse(v, out parsed, out error) ? parsed : null;
                _layoutError = _layout == null ? error : null;
            }
        }

        private static void EnsureLayoutParsed()
        {
            if (_layoutText == null)
            {
                LayoutText = DefaultLayoutText;
            }
        }

        /// <summary>The layout the tuner should apply right now, or null for each vehicle's own wiring.</summary>
        public static DrivetrainLayout ActiveLayout
        {
            get { return LayoutEnabled ? Layout : null; }
        }

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
            LayoutEnabled = false;   // the layout text is kept (it is the user's work)
            Book.ResetCustom();
            Book.Active = Book.Identity;
        }
    }
}
