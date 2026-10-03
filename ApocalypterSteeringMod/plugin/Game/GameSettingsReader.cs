using System;
using System.IO;
using UnityEngine;

namespace ApocalypterSteeringMod.Game
{
    /// <summary>
    /// Read-only import of the game's own settings from SaveSettings.es3 (Easy Save 3).
    /// Never writes: ES3.Load with an explicit absolute path opens a reader only.
    /// The absolute path is mandatory because the game's ES3SettingsMod mutates the
    /// shared static ES3Settings.defaultSettings.path.
    /// </summary>
    public static class GameSettingsReader
    {
        public static float Steeringspeed = 50f;
        public static bool SmoothInput = true;
        public static bool NormalizeInput = true;
        public static string SaveFilePath = "";
        public static bool Loaded;

        public static void Read()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "SaveSettings.es3");
                SaveFilePath = path;
                if (!File.Exists(path))
                {
                    Loaded = false;
                    return;
                }

                Steeringspeed = ES3.Load("steeringspeed", path, 50f);
                SmoothInput = ES3.Load("smoothinput", path, true);
                NormalizeInput = ES3.Load("normalizeinput", path, true);
                Loaded = true;
            }
            catch (Exception ex)
            {
                Loaded = false;
                Debug.LogWarning("[ApocalypterSteeringMod] Could not read game settings from SaveSettings.es3: " + ex.Message);
            }
        }
    }
}
