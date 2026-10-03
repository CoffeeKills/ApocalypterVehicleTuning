using UnityEngine;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Runtime steering settings read by the Harmony prefix. Plain static fields;
    /// the worst case on a mid-frame change is one physics tick of mixed values.
    /// Preset semantics (BeginEdit/Reference/reset) live in PresetBook.
    /// </summary>
    public static class SteeringSettings
    {
        public static readonly PresetBook<SteeringPreset> Book = new PresetBook<SteeringPreset>(
            SteeringPreset.Presets, SteeringPreset.Vanilla, SteeringPreset.Custom,
            SteeringPreset.Defaults, SteeringPreset.Custom,
            name => name == "Truck-sim" ? "Euro Truck" : name);

        // v3.2: opt-in — the mod changes nothing until the player enables it.
        public static bool Enabled = false;

        public static SteeringPreset ActivePreset
        {
            get { return Book.Active; }
            set { Book.Active = value; }
        }

        // Scale the steering rate with the game's own "steering speed" setting.
        public static bool MatchGameSteeringSpeed = true;
        public static float GameSteeringSpeedFactor = 1f;

        public static void UpdateGameSteeringSpeedFactor()
        {
            GameSteeringSpeedFactor = Mathf.Clamp(Game.GameSettingsReader.Steeringspeed / 50f, 0.35f, 2.5f);
        }

        public static void SetPresetByName(string name)
        {
            Book.SetByName(name);
        }

        public static void Select(SteeringPreset preset)
        {
            Book.Select(preset);
        }

        public static SteeringPreset BeginEdit()
        {
            return Book.BeginEdit();
        }

        public static SteeringPreset Reference()
        {
            return Book.Reference();
        }

        public static void ResetAll()
        {
            Enabled = false;
            MatchGameSteeringSpeed = true;
            Book.ResetCustom();
            Book.Active = Book.Custom;
        }
    }
}
