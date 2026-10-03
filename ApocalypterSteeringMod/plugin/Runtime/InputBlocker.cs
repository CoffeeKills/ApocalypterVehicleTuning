using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// While the settings panel is open, suppresses the game's PlayMaker input
    /// actions (mouse look, movement axes, keys) and freezes time, so the game
    /// cannot consume the mouse/keyboard and cannot keep driving. The cursor is
    /// re-freed by the panel manager every frame while open. Same recipe as
    /// Apocasetter's InputBlocker, which is proven against this game's FSMs.
    /// </summary>
    public static class InputBlocker
    {
        public static bool Active { get; private set; }

        private static readonly Regex ActionRx = new Regex(
            @"^(GetAxis|GetButton|GetKey|GetMouse|MouseLook|MousePick|AnyKey|GetTouch|GetAxisKeyAxis|Input|Mouse)",
            RegexOptions.IgnoreCase);

        private static float _savedTimeScale = 1f;

        public static void Install()
        {
            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            HarmonyMethod prefix = new HarmonyMethod(typeof(InputBlocker).GetMethod("SkipWhenActive", BindingFlags.Static | BindingFlags.NonPublic));
            int patched = 0;

            // PlayMakerArrayListProxy lives in Assembly-CSharp (the game's input
            // action classes are compiled there); FsmStateAction in PlayMaker.dll.
            var assemblies = new[]
            {
                typeof(PlayMakerArrayListProxy).Assembly,
                typeof(FsmStateAction).Assembly
            };
            foreach (Assembly asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                foreach (Type t in types)
                {
                    if (t == null || !typeof(FsmStateAction).IsAssignableFrom(t) || t.IsAbstract)
                    {
                        continue;
                    }
                    if (!ActionRx.IsMatch(t.Name))
                    {
                        continue;
                    }
                    foreach (string mName in new[] { "OnUpdate", "OnFixedUpdate", "OnLateUpdate" })
                    {
                        MethodInfo m = t.GetMethod(mName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                        if (m == null)
                        {
                            continue;
                        }
                        try
                        {
                            harmony.Patch(m, prefix: prefix);
                            patched++;
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("InputBlocker: could not patch " + t.Name + "." + mName + ": " + e.Message);
                        }
                    }
                }
            }
            Plugin.Log.LogInfo("InputBlocker: patched " + patched + " PlayMaker input action methods.");
        }

        private static bool SkipWhenActive()
        {
            return !Active;
        }

        public static void Set(bool on)
        {
            if (on == Active)
            {
                return;
            }
            Active = on;
            if (on)
            {
                _savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                // Restore exactly what the game had, including a pause-menu 0.
                Time.timeScale = _savedTimeScale;
            }
        }
    }
}
