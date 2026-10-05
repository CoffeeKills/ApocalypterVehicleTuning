using System.Reflection;
using HarmonyLib;

namespace ApocalypterDrivetrain.Patching
{
    public interface IPatchApplier
    {
        /// <summary>True once a Harmony instance exists. Stays false while OFF and while every flag is false.</summary>
        bool IsCreated { get; }
        void Apply(PatchPoint point, MethodInfo target);
        void RemoveAll();
    }

    /// <summary>
    /// The only code in the mod that touches Harmony. The Harmony instance (id = the locked GUID)
    /// is created lazily on the first Apply, so OFF - and ON with all flags false - never even
    /// constructs it.
    /// </summary>
    public sealed class HarmonyPatchApplier : IPatchApplier
    {
        private Harmony _harmony;

        public bool IsCreated { get { return _harmony != null; } }

        public void Apply(PatchPoint point, MethodInfo target)
        {
            if (_harmony == null) _harmony = new Harmony(ModInfo.Guid);
            _harmony.Patch(target, new HarmonyMethod(PatchPoints.PassThrough));
        }

        public void RemoveAll()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
