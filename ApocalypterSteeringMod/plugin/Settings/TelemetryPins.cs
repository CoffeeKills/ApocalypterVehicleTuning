using System;
using System.Collections.Generic;
using System.Text;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// Telemetry pins (0.7.0, FEATURES §4): slider values the player pinned to the telemetry strip.
    ///
    /// A pin key is "Category.Key" with the config/codec key of a numeric (slider) field, e.g.
    /// <c>Steering.RateMultiplier</c>, <c>Suspension.SpringFront</c>, <c>Gearbox.Gear3Scale</c> — the
    /// same names as the [Category.Custom] config keys, so the scheme is stable and needs no table of
    /// its own (PresetCodec owns the field list). A pin shows the value of the preset the category's
    /// sliders show (its active preset), sampled on the strip's 4 Hz tick.
    ///
    /// Persisted as <c>[Telemetry] Pins</c>, ';'-separated. Parsing drops unknown keys, duplicates and
    /// everything past <see cref="MaxPins"/>. Pure and UI-free (harness-tested).
    /// </summary>
    public static class TelemetryPins
    {
        public const int MaxPins = 12;
        public const char Separator = ';';

        private static readonly List<string> _pins = new List<string>();

        /// <summary>The pinned keys, in pin order (read-only view for the strip).</summary>
        public static IList<string> Pins
        {
            get { return _pins.AsReadOnly(); }
        }

        public static int Count
        {
            get { return _pins.Count; }
        }

        /// <summary>Bumped on every change, so the strip rebuilds its cells only when needed.</summary>
        public static int Version { get; private set; }

        /// <summary>"Category.Key" for a slider field; false for anything that is not a numeric field.</summary>
        public static bool TrySplit(string pin, out PresetCategory category, out string key)
        {
            category = PresetCategory.Steering;
            key = null;
            if (string.IsNullOrEmpty(pin))
            {
                return false;
            }
            int dot = pin.IndexOf('.');
            if (dot <= 0 || dot >= pin.Length - 1)
            {
                return false;
            }
            if (!PresetCodec.TryParseName(pin.Substring(0, dot), out category))
            {
                return false;
            }
            key = pin.Substring(dot + 1);
            return PresetCodec.IsNumericKey(category, key);
        }

        public static string Key(PresetCategory category, string fieldKey)
        {
            return category + "." + fieldKey;
        }

        public static bool IsKnown(string pin)
        {
            PresetCategory c;
            string k;
            return TrySplit(pin, out c, out k);
        }

        public static bool IsPinned(string pin)
        {
            return pin != null && _pins.Contains(pin);
        }

        /// <summary>Pin or unpin. False when the key is unknown, or pinning would exceed MaxPins.</summary>
        public static bool Toggle(string pin)
        {
            if (!IsKnown(pin))
            {
                return false;
            }
            if (_pins.Remove(pin))
            {
                Version++;
                return true;
            }
            if (_pins.Count >= MaxPins)
            {
                return false;
            }
            _pins.Add(pin);
            Version++;
            return true;
        }

        public static void Clear()
        {
            if (_pins.Count > 0)
            {
                _pins.Clear();
                Version++;
            }
        }

        /// <summary>Config text: the pins joined by ';'.</summary>
        public static string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _pins.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(Separator);
                }
                sb.Append(_pins[i]);
            }
            return sb.ToString();
        }

        /// <summary>Replace the pins from config text. Returns how many entries were dropped (unknown, duplicate, over the cap).</summary>
        public static int Load(string text)
        {
            _pins.Clear();
            Version++;
            int dropped = 0;
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            string[] parts = text.Split(Separator);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0)
                {
                    continue;
                }
                if (!IsKnown(p) || _pins.Contains(p) || _pins.Count >= MaxPins)
                {
                    dropped++;
                    continue;
                }
                _pins.Add(p);
            }
            return dropped;
        }

        /// <summary>The pinned value now (the shown preset of its category).</summary>
        public static bool TryGetValue(string pin, out float value)
        {
            PresetCategory c;
            string k;
            value = 0f;
            return TrySplit(pin, out c, out k) && PresetCodec.TryGetNumber(c, PresetCodec.ShownPreset(c), k, out value);
        }

        // ------------------------------------------------------------------ labels

        private static readonly Dictionary<string, string> _labels = new Dictionary<string, string>();

        /// <summary>The panel registers each slider's title for its key (its strip label).</summary>
        public static void SetLabel(string pin, string label)
        {
            if (!string.IsNullOrEmpty(pin) && !string.IsNullOrEmpty(label))
            {
                _labels[pin] = label;
            }
        }

        /// <summary>The slider's own title once the panel was built, else the key in words ("Spring front").</summary>
        public static string Label(string pin)
        {
            string l;
            if (pin != null && _labels.TryGetValue(pin, out l))
            {
                return l;
            }
            PresetCategory c;
            string k;
            return TrySplit(pin, out c, out k) ? Humanize(k) : pin ?? "";
        }

        /// <summary>"SpringFront" -> "Spring front", "Gear3Scale" -> "Gear 3 scale".</summary>
        public static string Humanize(string key)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                char ch = key[i];
                char prev = i > 0 ? key[i - 1] : ' ';
                bool boundary = i > 0 && ((char.IsUpper(ch) && !char.IsUpper(prev)) || (char.IsDigit(ch) && !char.IsDigit(prev)));
                if (boundary)
                {
                    sb.Append(' ');
                }
                // Lower-case a word start ("Front" -> "front"); acronyms (FL, XFL) stay upper.
                bool word = i > 0 && char.IsUpper(ch) && i + 1 < key.Length && char.IsLower(key[i + 1]);
                sb.Append(word ? char.ToLowerInvariant(ch) : ch);
            }
            return sb.ToString();
        }

        /// <summary>How a pinned value is shown (the same units as the slider).</summary>
        public enum Unit
        {
            Factor,      // x1.40
            Degrees,     // 8.5°
            DegSigned,   // +2.0°
            CmSigned,    // +4 cm
            Rpm,         // +200 rpm
            Count,       // 12 gears / own
            Plain,       // 0.10
            Speed,       // 2.0 m/s
            Percent      // 10%
        }

        public static Unit UnitOf(string pin)
        {
            PresetCategory c;
            string k;
            if (!TrySplit(pin, out c, out k))
            {
                return Unit.Factor;
            }
            if (c == PresetCategory.Alignment)
            {
                return k.StartsWith("Pos", StringComparison.Ordinal) ? Unit.CmSigned : Unit.DegSigned;
            }
            switch (k)
            {
                case "SlipAngleDeg": return Unit.Degrees;
                case "ClutchRpmOffset": return Unit.Rpm;
                case "GearCount": return Unit.Count;
                case "LinearityExponent":
                case "AbsSlipThreshold":
                case "TcsSlipThreshold": return Unit.Plain;
                case "AbsCutoffSpeed":
                case "TcsCutoffSpeed": return Unit.Speed;
                case "AbsCutMultiplier":
                case "TcsCutMultiplier": return Unit.Percent;
                default: return Unit.Factor;
            }
        }
    }
}
