using ApocalypterSteeringMod.Game;
using ApocalypterSteeringMod.Persistence;
using ApocalypterSteeringMod.Runtime;
using ApocalypterSteeringMod.Settings;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ApocalypterSteeringMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        // The game destroys plugin-created objects on scene load (its scene FSMs
        // sweep the scene roots), so all runtime logic lives on a hidden runner
        // object the cleanup cannot find — the same recipe Apocasetter uses.
        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;

            ModConfig.Load(Config);
            GameSettingsReader.Read();
            SteeringSettings.UpdateGameSteeringSpeedFactor();

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            InputBlocker.Install();

            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureRunner("Awake");

            Log.LogInfo(PluginInfo.PLUGIN_NAME + " " + PluginInfo.PLUGIN_VERSION + " loaded.");
            Log.LogInfo("Game settings: steeringspeed=" + GameSettingsReader.Steeringspeed.ToString("0.##")
                + " smoothinput=" + GameSettingsReader.SmoothInput
                + " normalizeinput=" + GameSettingsReader.NormalizeInput
                + (GameSettingsReader.Loaded ? " (from " + GameSettingsReader.SaveFilePath + ")" : " (defaults — no save file yet)"));
            Log.LogInfo("Steering: " + (SteeringSettings.Enabled ? SteeringSettings.ActivePreset.Name : "off")
                + " | suspension: " + (SuspensionSettings.Enabled ? SuspensionSettings.ActivePreset.Name : "off")
                + " | panel: " + ModConfig.ToggleKeyString + " or the 'Vehicle Tuning' button in the game menu");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRunner("scene " + scene.name);
        }

        private static void EnsureRunner(string why)
        {
            // HideAndDontSave keeps the object out of the game's scene-cleanup sweeps;
            // the game cannot destroy what it cannot find.
            if (_runner != null && _runner.activeInHierarchy)
            {
                return;
            }
            if (_runner != null)
            {
                // Deactivated but alive: retire it before building the replacement, so
                // two tuners never coexist (the old one's OnDisable already handed the
                // vehicles back) and its injected menu buttons/panel go with it.
                Destroy(_runner);
                Log.LogDebug("Stale (inactive) runtime runner destroyed.");
            }
            _runner = new GameObject("ApocalypterSteeringModRuntime")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(_runner);
            _runner.AddComponent<VehicleTuner>();
            _runner.AddComponent<SettingsPanelManager>();
            Log.LogDebug("Runtime runner created (" + why + ").");
        }

        private void OnDestroy()
        {
            // The game destroys the plugin object on scene load; this is expected.
            // The Harmony patches and the runner keep working regardless.
            ModConfig.Save();
        }
    }
}
