// Static owner of the mod's state. Static on purpose: the BepInEx plugin object can be
// destroyed by the game's scene sweep (README fact 3); the state and the sceneLoaded handler
// must not depend on it.
using System;
using ApocalypterDrivetrain.Patching;
using ApocalypterDrivetrain.Settings;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ApocalypterDrivetrain.Runtime
{
    public static class ModHost
    {
        public static DrivetrainSettings Settings { get; private set; }
        public static DrivetrainController Controller { get; private set; }
        public static ILog Log { get; private set; }
        public static KeyCode ToggleKey { get; private set; }
        public static bool ToggleKeyValid { get; private set; }

        public static void Init(ConfigFile cfg, ILog log, string configDir, IPatchApplier applier)
        {
            Log = log;
            Settings = new DrivetrainSettings(cfg);
            Controller = new DrivetrainController(log, new FileConfigSource(() => Settings.ConfigPath.Value, configDir), applier, PatchPoints.All);
            ParseToggleKey();

            Settings.Enabled.SettingChanged += (s, e) => Controller.SetEnabled(Settings.Enabled.Value);
            Settings.ToggleKey.SettingChanged += (s, e) => ParseToggleKey();
            Settings.ConfigPath.SettingChanged += (s, e) => Controller.Reload();

            log.Info(ModInfo.Name + " " + ModInfo.DisplayVersion + " (" + ModInfo.Guid + ") loaded: "
                + (Settings.Enabled.Value ? "Enabled=true in config, turning ON" : "OFF, NWH untouched")
                + (ToggleKeyValid ? "; toggle with " + ToggleKey : "; hotkey disabled"));
            if (Settings.Enabled.Value) Controller.SetEnabled(true);
        }

        private static void ParseToggleKey()
        {
            KeyCode key;
            ToggleKeyValid = DrivetrainSettings.TryParseKey(Settings.ToggleKey.Value, out key);
            ToggleKey = key;
            if (!ToggleKeyValid)
                Log.Error("[Drivetrain] ToggleKey '" + Settings.ToggleKey.Value + "' is not a Unity KeyCode name; the hotkey is disabled until it is fixed");
        }

        /// <summary>Called by the hidden runner every frame. Allocation-free.</summary>
        public static void Tick()
        {
            if (Settings == null || !ToggleKeyValid) return;
            if (Input.GetKeyDown(ToggleKey)) Toggle();
        }

        /// <summary>The hotkey action: flips the config entry; SettingChanged drives the controller.</summary>
        public static void Toggle()
        {
            Settings.Enabled.Value = !Settings.Enabled.Value;
        }

        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try { RunnerHost.Ensure(); }
            catch (Exception e) { if (Log != null) Log.Error("runner recreate failed: " + e); }
        }
    }
}
