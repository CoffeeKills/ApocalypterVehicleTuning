using System;
using System.IO;
using ApocalypterDrivetrain.Model;

namespace ApocalypterDrivetrain.Runtime
{
    public interface ILog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    public interface IConfigTextSource
    {
        /// <summary>Reads the drivetrain JSON. origin names where it came from (for the log).</summary>
        bool TryRead(out string text, out string origin, out string error);
    }

    /// <summary>
    /// [Drivetrain] ConfigPath: empty = built-in example; relative = under BepInEx/config/.
    /// A missing file is an error — never a silent fall back to the example.
    /// </summary>
    public sealed class FileConfigSource : IConfigTextSource
    {
        private readonly Func<string> _path;
        private readonly string _configDir;

        public FileConfigSource(Func<string> path, string configDir)
        {
            _path = path;
            _configDir = configDir;
        }

        public string ResolvePath()
        {
            string p = (_path() ?? "").Trim();
            if (p.Length == 0) return null;
            return Path.IsPathRooted(p) ? p : Path.Combine(_configDir, p);
        }

        public bool TryRead(out string text, out string origin, out string error)
        {
            text = null;
            error = null;
            string full = ResolvePath();
            if (full == null)
            {
                origin = "built-in example";
                text = BuiltInConfig.Json;
                return true;
            }
            origin = full;
            if (!File.Exists(full))
            {
                error = "ConfigPath file not found: " + full;
                return false;
            }
            try
            {
                text = File.ReadAllText(full);
                return true;
            }
            catch (Exception e)
            {
                error = "could not read " + full + ": " + e.Message;
                return false;
            }
        }
    }
}
