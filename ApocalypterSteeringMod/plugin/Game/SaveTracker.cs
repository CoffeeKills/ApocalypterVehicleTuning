using System;
using System.IO;
using UnityEngine;

namespace ApocalypterSteeringMod.Game
{
    /// <summary>
    /// 0.8.0: tracks which save slot is loaded, so a session on a different (e.g. older) save
    /// can be detected — the game's saves serialize vehicle state, and a stale save can fight
    /// the config. NewestSave is pure (harness-tested); Refresh() reads the real save
    /// directory (Application.persistentDataPath = the LocalLow Apocalypter folder).
    /// </summary>
    public static class SaveTracker
    {
        public static string CurrentName { get; private set; } = "";
        public static long CurrentStamp { get; private set; }

        /// <summary>Path of the newest SaveGameN.es3 in the directory, or null. Pure.</summary>
        public static string NewestSave(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return null;
            }
            string best = null;
            long bestStamp = 0;
            try
            {
                string[] files = Directory.GetFiles(dir, "SaveGame*.es3");
                for (int i = 0; i < files.Length; i++)
                {
                    long stamp = File.GetLastWriteTimeUtc(files[i]).Ticks;
                    if (best == null || stamp > bestStamp)
                    {
                        best = files[i];
                        bestStamp = stamp;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return best;
        }

        /// <summary>Refresh the current save identity from the game's save directory.</summary>
        public static void Refresh()
        {
            string f = NewestSave(Application.persistentDataPath);
            CurrentName = f != null ? Path.GetFileName(f) : "";
            CurrentStamp = f != null ? File.GetLastWriteTimeUtc(f).Ticks : 0;
        }
    }
}
