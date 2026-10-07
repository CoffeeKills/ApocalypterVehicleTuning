namespace ApocalypterSteeringMod
{
    internal static class PluginInfo
    {
        // GUID deliberately unchanged from the steering-only versions so the
        // BepInEx config file (keyed by GUID) keeps carrying the user's settings.
        public const string PLUGIN_GUID = "dev.apocalypter.tractionsteering";
        public const string PLUGIN_NAME = "Apocalypter Vehicle Tuning";
        // BepInEx 5 parses this with System.Version: numeric-only, no pre-release
        // tags. "-alpha" makes BepInEx skip the whole plugin at load.
        public const string PLUGIN_VERSION = "0.11.1";
    }
}
