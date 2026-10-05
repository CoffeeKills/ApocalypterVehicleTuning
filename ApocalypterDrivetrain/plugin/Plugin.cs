using ApocalypterDrivetrain.Patching;
using ApocalypterDrivetrain.Runtime;
using BepInEx;
using BepInEx.Logging;
using UnityEngine.SceneManagement;

namespace ApocalypterDrivetrain
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ModHost.Init(Config, new BepInExLog(Logger), Paths.ConfigPath, new HarmonyPatchApplier());
            SceneManager.sceneLoaded += ModHost.OnSceneLoaded;
            RunnerHost.Ensure();
        }

        private sealed class BepInExLog : ILog
        {
            private readonly ManualLogSource _src;
            public BepInExLog(ManualLogSource src) { _src = src; }
            public void Info(string m) { _src.LogInfo(m); }
            public void Warning(string m) { _src.LogWarning(m); }
            public void Error(string m) { _src.LogError(m); }
        }
    }
}
