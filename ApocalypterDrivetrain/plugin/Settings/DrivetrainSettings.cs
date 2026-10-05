// BepInEx config surface. Config continuity starts at 0.1.0: these section/key names are
// pinned by a verify/ test; renaming one requires a migration, never a silent drop.
using System;
using BepInEx.Configuration;
using UnityEngine;

namespace ApocalypterDrivetrain.Settings
{
    public sealed class DrivetrainSettings
    {
        public const string Section = "Drivetrain";
        public const string DefaultToggleKey = "F9";

        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<string> ToggleKey;
        public readonly ConfigEntry<string> ConfigPath;

        public DrivetrainSettings(ConfigFile cfg)
        {
            Enabled = cfg.Bind(Section, "Enabled", false,
                "A/B master switch. OFF (default): the mod never touches NWH - no patch applied, nothing written. "
                + "ON: loads and validates the drivetrain JSON and stages the replacement points (0.1.0: NWH stays fully active).");
            ToggleKey = cfg.Bind(Section, "ToggleKey", DefaultToggleKey,
                "Hotkey that flips Enabled (a Unity KeyCode name, e.g. F9, F10, Home). An unrecognised name disables the hotkey and is logged.");
            ConfigPath = cfg.Bind(Section, "ConfigPath", "",
                "Drivetrain JSON file. Empty = the built-in example (README section 4). A relative path is resolved against BepInEx/config/. "
                + "A missing or invalid file is reported in the log; there is no fallback to the example.");
        }

        /// <summary>KeyCode name -> KeyCode. Case-insensitive; numeric strings and None are rejected.</summary>
        public static bool TryParseKey(string name, out KeyCode key)
        {
            key = KeyCode.None;
            if (string.IsNullOrEmpty(name)) return false;
            string trimmed = name.Trim();
            if (trimmed.Length == 0 || char.IsDigit(trimmed[0]) || trimmed[0] == '-') return false;   // Enum.Parse accepts "282"
            try
            {
                key = (KeyCode)Enum.Parse(typeof(KeyCode), trimmed, true);
            }
            catch (ArgumentException)
            {
                return false;
            }
            return key != KeyCode.None && Enum.IsDefined(typeof(KeyCode), key);
        }
    }
}
