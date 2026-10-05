// The A/B state machine. OFF = provably no touch: no config read, no reflection on NWH,
// no Harmony instance, no write. ON = load + validate the JSON, resolve (stage) the patch
// points, apply only the points whose runtime flag is set (none in 0.1.0).
// Pure logic: everything game-facing comes in through interfaces, so verify/ drives it headless.
using System.Collections.Generic;
using System.Reflection;
using ApocalypterDrivetrain.Model;
using ApocalypterDrivetrain.Patching;

namespace ApocalypterDrivetrain.Runtime
{
    public sealed class DrivetrainController
    {
        public const string OffStatus = "OFF: NWH untouched (no patch applied, nothing written)";

        private readonly ILog _log;
        private readonly IConfigTextSource _source;
        private readonly IPatchApplier _applier;
        private readonly PatchPoint[] _points;
        private readonly List<PatchPoint> _staged = new List<PatchPoint>();

        public bool IsOn { get; private set; }
        public Drivetrain Model { get; private set; }
        public BuildResult LastBuild { get; private set; }
        public int StagedCount { get { return _staged.Count; } }
        public int AppliedCount { get; private set; }
        public int ConfigReads { get; private set; }
        public int Resolutions { get; private set; }
        public string Status { get; private set; }

        public DrivetrainController(ILog log, IConfigTextSource source, IPatchApplier applier, PatchPoint[] points)
        {
            _log = log;
            _source = source;
            _applier = applier;
            _points = points;
            Status = OffStatus;
        }

        public void SetEnabled(bool on)
        {
            if (on == IsOn) return;
            if (on) TurnOn();
            else TurnOff();
        }

        /// <summary>Re-reads and re-validates the config (e.g. ConfigPath changed). No-op while OFF.</summary>
        public void Reload()
        {
            if (!IsOn) return;
            TurnOff();
            TurnOn();
        }

        private void TurnOn()
        {
            IsOn = true;
            Model = null;
            _staged.Clear();
            AppliedCount = 0;

            string text, origin, error;
            ConfigReads++;
            if (!_source.TryRead(out text, out origin, out error))
            {
                LastBuild = null;
                _log.Error(error);
                SetStatus("skeleton: NWH active, config FAILED (" + error + "), 0 replacement points staged", true);
                return;
            }

            LastBuild = DrivetrainBuilder.Build(text);
            foreach (Diagnostic diag in LastBuild.Diagnostics.Items)
            {
                if (diag.IsError) _log.Error("config " + origin + ": " + diag);
                else _log.Warning("config " + origin + ": " + diag);
            }
            if (!LastBuild.Ok)
            {
                SetStatus("skeleton: NWH active, config FAILED (" + LastBuild.Diagnostics.ErrorCount + " error(s) in " + origin
                    + "), 0 replacement points staged", true);
                return;
            }
            Model = LastBuild.Drivetrain;
            _log.Info("drivetrain " + Model.Summary() + " (from " + origin + ")");

            foreach (PatchPoint p in _points)
            {
                Resolutions++;
                MethodInfo target = p.Resolve();
                if (target == null)
                {
                    _log.Warning("patch point " + p.Id + ": " + p.TargetName + " not found in this game build; not staged");
                    continue;
                }
                _staged.Add(p);
                if (p.Enabled)
                {
                    _applier.Apply(p, target);
                    AppliedCount++;
                    _log.Info("patch point " + p.Id + " applied (pass-through)");
                }
            }
            SetStatus("skeleton: NWH active, config loaded, " + _staged.Count + " replacement points staged", false);
        }

        private void TurnOff()
        {
            IsOn = false;
            if (AppliedCount > 0) _applier.RemoveAll();
            AppliedCount = 0;
            _staged.Clear();
            Model = null;
            SetStatus(OffStatus, false);
        }

        private void SetStatus(string s, bool error)
        {
            Status = s;
            if (error) _log.Error(s); else _log.Info(s);
        }
    }
}
