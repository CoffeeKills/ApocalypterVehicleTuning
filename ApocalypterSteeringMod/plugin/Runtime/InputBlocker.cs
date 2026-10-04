using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using HutongGames.PlayMaker;
using InsaneSystems.InputManager;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// While the settings panel is open, keeps the game from reacting to anything but
    /// DRIVING input, so the player can drive with the panel open (0.6.0 "live" mode) while
    /// the mouse works the UI. Optionally also freezes time (the 0.5.0 behaviour).
    ///
    /// Layer A — action-name whitelist on InsaneSystems.InputManager.InputController. The
    ///   game's driving FSM actions are HutongGames forks that route through these statics
    ///   (GetButton -> GetKeyActionIsActive(buttonName), GetAxis -> GetAnyAxisActionValue(axisName)).
    ///   Whitelisted names run the original; any other name returns false / 0 without reaching
    ///   InputStorage (which THROWS on unknown names, InputStorage.cs:62-72). Fail-closed: each
    ///   unknown name is logged once so the in-game check can harvest missing driving names.
    /// Layer B — the 0.5.0 PlayMaker class-name patch set (mouse look, mouse buttons, AnyKey,
    ///   direct Input/Mouse actions, the renamed GetAxisOrig for "Mouse X/Y" and scroll), now
    ///   also on OnEnter. The name-routed fork classes (GetButton*, GetAxis, GetAxisKeyAxis)
    ///   match the same regex but are decided by their action name through layer A's
    ///   whitelist; blocking them by class (what the spec's "redundant" entries would do)
    ///   would kill throttle and steering.
    ///
    /// Every routing decision is a pure static function (Route*), tested by the harness.
    /// </summary>
    public static class InputBlocker
    {
        /// <summary>Input blocked (both layers). Set by the panel manager.</summary>
        public static bool Active { get; private set; }

        /// <summary>Time frozen by the panel (FreezeWhileOpen).</summary>
        public static bool Frozen { get; private set; }

        private static float _savedTimeScale = 1f;

        // ------------------------------------------------------------- whitelist

        /// <summary>Key actions the game's driving FSMs read (from the FSM dump; verify in game).</summary>
        public static readonly string[] DrivingKeys =
        {
            "Throttle", "Brakes", "Handbrake", "Clutch", "Horn", "Headlight", "ShiftUp", "ShiftDown", "Cruise Control"
        };

        /// <summary>Axis actions the game's driving FSMs read.</summary>
        public static readonly string[] DrivingAxes = { "input", "Steering Keyboard" };

        private static readonly HashSet<string> Whitelist = BuildWhitelist();

        private static HashSet<string> BuildWhitelist()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string s in DrivingKeys) set.Add(s);
            foreach (string s in DrivingAxes) set.Add(s);
            return set;
        }

        public enum Route
        {
            Allow,
            Block
        }

        /// <summary>Layer A: may this InputController action name reach the game while the panel is open?</summary>
        public static Route RouteControllerAction(string actionName)
        {
            return actionName != null && Whitelist.Contains(actionName) ? Route.Allow : Route.Block;
        }

        /// <summary>The HutongGames forks that read through InputController by name.</summary>
        private static readonly HashSet<string> NameRoutedClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "GetButton", "GetButtonDown", "GetButtonUp", "GetAxis", "GetAxisKeyAxis"
        };

        /// <summary>
        /// Layer B: a matched PlayMaker action class. Name-routed forks follow the layer-A
        /// whitelist by their action name; everything else (direct Input / mouse / AnyKey /
        /// GetAxisOrig) is blocked.
        /// </summary>
        public static Route RouteFsmAction(string typeName, string actionName)
        {
            if (typeName != null && NameRoutedClasses.Contains(typeName))
            {
                return RouteControllerAction(actionName);
            }
            return Route.Block;
        }

        public enum EnterRoute
        {
            Run,            // let OnEnter run
            Skip,           // skip; the action keeps running (its OnUpdate is gated too)
            SkipAndFinish   // skip and Finish(): a one-shot action must not leave its FSM state hanging
        }

        /// <summary>
        /// OnEnter needs more care than OnUpdate: a one-shot action (everyFrame = false) calls
        /// Finish() in OnEnter, so skipping it blindly would leave the FSM stuck in that state
        /// after the panel closes. Only actions that declare an everyFrame flag can be decided
        /// safely; any other OnEnter runs (its per-frame methods stay gated).
        /// </summary>
        public static EnterRoute RouteOnEnter(Route route, bool hasEveryFrame, bool everyFrame)
        {
            if (route == Route.Allow || !hasEveryFrame)
            {
                return EnterRoute.Run;
            }
            return everyFrame ? EnterRoute.Skip : EnterRoute.SkipAndFinish;
        }

        // ------------------------------------------------------------- unknown-name log

        private const int MaxLoggedNames = 64;
        private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Names blocked so far (for the harness and the log).</summary>
        public static int LoggedNameCount { get { return _logged.Count; } }

        /// <summary>Once per name, at most 64 names: the in-game check reads these from the log.</summary>
        internal static bool NoteBlocked(string name)
        {
            string key = name ?? "(null)";
            if (_logged.Count >= MaxLoggedNames || !_logged.Add(key))
            {
                return false;
            }
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("InputBlocker: blocked input action '" + key
                    + "' while the panel was open. If it is a driving control, add it to the whitelist.");
            }
            return true;
        }

        // ------------------------------------------------------------- state

        /// <summary>Gate both layers. Never touches Time.timeScale.</summary>
        public static void SetInputBlocked(bool on)
        {
            Active = on;
        }

        /// <summary>
        /// Freeze: save the current timeScale and set 0; unfreeze restores exactly what was
        /// saved (a pause-menu 0 included). Idempotent in both directions.
        /// </summary>
        public static void SetFreeze(bool on)
        {
            if (on == Frozen)
            {
                return;
            }
            Frozen = on;
            if (on)
            {
                _savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _savedTimeScale;
            }
        }

        // ------------------------------------------------------------- install

        private static readonly Regex ActionRx = new Regex(
            @"^(GetAxis|GetButton|GetKey|GetMouse|MouseLook|MousePick|AnyKey|GetTouch|GetAxisKeyAxis|Input|Mouse)",
            RegexOptions.IgnoreCase);

        private sealed class ActionInfo
        {
            public string TypeName;
            public FieldInfo NameField;    // FsmString buttonName / axisName (name-routed forks)
            public FieldInfo EveryFrame;   // bool everyFrame, when declared
        }

        private static readonly Dictionary<Type, ActionInfo> _infos = new Dictionary<Type, ActionInfo>();

        public static void Install()
        {
            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            int patchedA = InstallLayerA(harmony);
            int patchedB = InstallLayerB(harmony);
            Plugin.Log.LogInfo("InputBlocker: patched " + patchedA + " InputController methods (driving whitelist) and "
                + patchedB + " PlayMaker input action methods.");
        }

        private static int InstallLayerA(Harmony harmony)
        {
            int patched = 0;
            HarmonyMethod keyPrefix = new HarmonyMethod(typeof(InputBlocker).GetMethod("KeyPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            HarmonyMethod axisPrefix = new HarmonyMethod(typeof(InputBlocker).GetMethod("AxisPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            string[] keyMethods = { "GetKeyActionIsActive", "GetKeyActionIsDown", "GetKeyActionIsUp" };
            string[] axisMethods = { "GetAnyAxisActionValue", "GetAxisActionValue", "GetKeyAxisActionValue" };
            Type t = typeof(InputController);
            foreach (string name in keyMethods)
            {
                patched += PatchStatic(harmony, t, name, keyPrefix);
            }
            foreach (string name in axisMethods)
            {
                patched += PatchStatic(harmony, t, name, axisPrefix);
            }
            return patched;
        }

        private static int PatchStatic(Harmony harmony, Type t, string name, HarmonyMethod prefix)
        {
            MethodInfo m = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
            if (m == null)
            {
                Plugin.Log.LogWarning("InputBlocker: InputController." + name + "(string) not found; that input path stays unfiltered.");
                return 0;
            }
            try
            {
                harmony.Patch(m, prefix: prefix);
                return 1;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("InputBlocker: could not patch InputController." + name + ": " + e.Message);
                return 0;
            }
        }

        private static int InstallLayerB(Harmony harmony)
        {
            HarmonyMethod update = new HarmonyMethod(typeof(InputBlocker).GetMethod("FsmUpdatePrefix", BindingFlags.Static | BindingFlags.NonPublic));
            HarmonyMethod enter = new HarmonyMethod(typeof(InputBlocker).GetMethod("FsmEnterPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            int patched = 0;

            // PlayMakerArrayListProxy lives in Assembly-CSharp (the game's input
            // action classes are compiled there); FsmStateAction in PlayMaker.dll.
            var assemblies = new[]
            {
                typeof(PlayMakerArrayListProxy).Assembly,
                typeof(FsmStateAction).Assembly
            };
            foreach (Assembly asm in assemblies.Distinct())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(x => x != null).ToArray();
                }
                foreach (Type t in types)
                {
                    if (t == null || !typeof(FsmStateAction).IsAssignableFrom(t) || t.IsAbstract || !ActionRx.IsMatch(t.Name))
                    {
                        continue;
                    }
                    _infos[t] = Describe(t);
                    foreach (string mName in new[] { "OnUpdate", "OnFixedUpdate", "OnLateUpdate", "OnEnter" })
                    {
                        MethodInfo m = t.GetMethod(mName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                        if (m == null)
                        {
                            continue;
                        }
                        try
                        {
                            harmony.Patch(m, prefix: mName == "OnEnter" ? enter : update);
                            patched++;
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("InputBlocker: could not patch " + t.Name + "." + mName + ": " + e.Message);
                        }
                    }
                }
            }
            return patched;
        }

        /// <summary>Reflection facts about one action class (cached once per type).</summary>
        private static ActionInfo Describe(Type t)
        {
            var info = new ActionInfo { TypeName = t.Name };
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo name = t.GetField("buttonName", F) ?? t.GetField("axisName", F);
            if (name != null && name.FieldType == typeof(FsmString))
            {
                info.NameField = name;
            }
            FieldInfo every = t.GetField("everyFrame", F);
            if (every != null && every.FieldType == typeof(bool))
            {
                info.EveryFrame = every;
            }
            return info;
        }

        private static ActionInfo InfoFor(FsmStateAction action)
        {
            Type t = action.GetType();
            ActionInfo info;
            if (!_infos.TryGetValue(t, out info))
            {
                info = Describe(t);
                _infos[t] = info;
            }
            return info;
        }

        /// <summary>Layer-B decision for a live action instance (used by both prefixes and the harness).</summary>
        public static Route RouteInstance(FsmStateAction action)
        {
            ActionInfo info = InfoFor(action);
            string actionName = null;
            if (info.NameField != null)
            {
                var fs = info.NameField.GetValue(action) as FsmString;
                actionName = fs != null ? fs.Value : null;
            }
            Route r = RouteFsmAction(info.TypeName, actionName);
            if (r == Route.Block && info.NameField != null)
            {
                NoteBlocked(actionName);
            }
            return r;
        }

        /// <summary>Layer-B OnEnter decision for a live action instance.</summary>
        public static EnterRoute RouteEnterInstance(FsmStateAction action)
        {
            ActionInfo info = InfoFor(action);
            Route r = RouteInstance(action);
            bool hasEvery = info.EveryFrame != null;
            bool every = hasEvery && (bool)info.EveryFrame.GetValue(action);
            return RouteOnEnter(r, hasEvery, every);
        }

        // ------------------------------------------------------------- prefixes

        private static bool KeyPrefix(string __0, ref bool __result)
        {
            if (!Active || RouteControllerAction(__0) == Route.Allow)
            {
                return true;
            }
            NoteBlocked(__0);
            __result = false;
            return false;
        }

        private static bool AxisPrefix(string __0, ref float __result)
        {
            if (!Active || RouteControllerAction(__0) == Route.Allow)
            {
                return true;
            }
            NoteBlocked(__0);
            __result = 0f;
            return false;
        }

        private static bool FsmUpdatePrefix(FsmStateAction __instance)
        {
            if (!Active)
            {
                return true;
            }
            if (Frozen)
            {
                return false;   // Freeze ON = the 0.5.0 recipe: every matched action class is skipped
            }
            return RouteInstance(__instance) == Route.Allow;
        }

        private static bool FsmEnterPrefix(FsmStateAction __instance)
        {
            if (!Active || Frozen)
            {
                return true;    // 0.5.0 never gated OnEnter; Freeze ON keeps that
            }
            switch (RouteEnterInstance(__instance))
            {
                case EnterRoute.Run:
                    return true;
                case EnterRoute.SkipAndFinish:
                    __instance.Finish();
                    return false;
                default:
                    return false;
            }
        }
    }
}
