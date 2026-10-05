// Hidden-runner survival architecture (README fact 3, proven in ApocalypterSteeringMod):
// the game's PlayMaker scene sweep destroys unknown scene-root objects, so all per-frame
// logic lives on a HideAndDontSave GameObject that is recreated on every sceneLoaded.
using UnityEngine;

namespace ApocalypterDrivetrain.Runtime
{
    public static class RunnerHost
    {
        public const string ObjectName = "ApocalypterDrivetrain.Runner";
        private static Runner _runner;

        /// <summary>How many runners were created (diagnostics + tests).</summary>
        public static int Created { get; private set; }

        /// <summary>Creates the runner if it does not exist (Unity fake-null aware: a destroyed runner == null).</summary>
        public static void Ensure()
        {
            if (_runner != null) return;
            var go = new GameObject(ObjectName);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();
            Created++;
        }
    }

    public sealed class Runner : MonoBehaviour
    {
        private void Update()
        {
            ModHost.Tick();
        }
    }
}
