// Test-only stand-in for the three HarmonyLib members the steering patch uses.
using System;
using System.Reflection;
using System.Reflection.Emit;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string m) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    public static class AccessTools
    {
        public delegate ref F FieldRef<in T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            FieldInfo fi = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var dm = new DynamicMethod("ref_" + name, typeof(F).MakeByRefType(), new[] { typeof(T) }, typeof(T), true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, fi);
            il.Emit(OpCodes.Ret);
            return (FieldRef<T, F>)dm.CreateDelegate(typeof(FieldRef<T, F>));
        }
    }
}
