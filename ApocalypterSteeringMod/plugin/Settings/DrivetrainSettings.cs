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
