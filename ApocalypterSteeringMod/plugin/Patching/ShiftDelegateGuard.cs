using HarmonyLib;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocalypterSteeringMod.Patching
{
    /// <summary>
    /// 0.7.3: NWH's ForwardStep re-assigns its own shift delegate whenever transmissionType
    /// changes (TransmissionComponent.cs:405-408), silently kicking the mod's ShiftController
    /// off a tuned box until the next apply pass (up to 2 s of the game's gear-skipping
    /// automatic on 12 tight gears — "launching from gear 8"). This postfix re-installs the
    /// controller in the same tick via VehicleTuner.RehookIfControlled, so the window is zero.
    /// </summary>
    internal static class ShiftDelegateGuard
    {
        internal static void Install(Harmony harmony)
        {
            var m = typeof(TransmissionComponent).GetMethod("AssignShiftDelegate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (m == null)
            {
                ApocalypterSteeringMod.Plugin.Log.LogWarning(
                    "ShiftDelegateGuard: TransmissionComponent.AssignShiftDelegate not found; the 0.7.3 re-hook stays off.");
                return;
            }
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(ShiftDelegateGuard).GetMethod("Postfix",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
        }

        private static void Postfix(TransmissionComponent __instance)
        {
            ApocalypterSteeringMod.Runtime.VehicleTuner.RehookIfControlled(__instance);
        }
    }
}
