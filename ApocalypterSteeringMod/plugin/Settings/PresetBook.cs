using System;
using System.Collections.Generic;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// The steering-tab semantics, shared by every tuning category: a list of
    /// presets, an identity preset (Stock / Off / Vanilla), a Custom slot that
    /// edits copy into, a defaults reset target, and a not-found fallback.
    /// </summary>
    public interface ITunablePreset
    {
        string Name { get; }          // stable id stored in the config file
        string Label { get; }         // preset button text
        string BasedOn { get; set; }  // Custom only: preset it was copied from ("" = none)
        bool CanEdit { get; }         // false = identity preset that cannot be edited (steering Vanilla)
        void CopyValuesFrom(ITunablePreset source);
    }

    public sealed class PresetBook<T> where T : class, ITunablePreset
    {
        public readonly T[] Presets;   // button order, includes Identity and Custom
        public readonly T Identity;
        public readonly T Custom;
        public readonly T Defaults;    // reset target when BasedOn resolves to nothing
        public readonly T NotFound;    // SetByName fallback
        public T Active;

        private readonly Func<string, string> _legacyName;   // optional (Street -> Stock)

        // 0.9.0 per-vehicle tunes: a deep copy of the preset per vehicle name. The tuner
        // applies a vehicle's copy when one exists (any apply-to mode); the global Active
        // remains the default for everyone else.
        private readonly Dictionary<string, T> _perVehicle = new Dictionary<string, T>();

        public PresetBook(T[] presets, T identity, T custom, T defaults, T notFound,
            Func<string, string> legacyName = null)
        {
            Presets = presets;
            Identity = identity;
            Custom = custom;
            Defaults = defaults;
            NotFound = notFound;
            _legacyName = legacyName;
            Active = identity;
        }

        public void SetByName(string name)
        {
            string mapped = _legacyName != null ? _legacyName(name) : name;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Presets[i].Name == mapped)
                {
                    Active = Presets[i];
                    return;
                }
            }
            Active = NotFound;
        }

        public void Select(T preset)
        {
            Active = preset ?? Identity;
        }

        /// <summary>
        /// Call before changing any tunable value. If a built-in preset is active it
        /// is copied into Custom and Custom becomes active, so presets are never
        /// mutated. Returns the preset to write to, or null when the active identity
        /// cannot be edited (steering Vanilla).
        /// </summary>
        public T BeginEdit()
        {
            T active = Active ?? Identity;
            if (active == Custom)
            {
                return Custom;
            }
            if (active == Identity && !Identity.CanEdit)
            {
                return null;
            }
            Custom.CopyValuesFrom(active);
            Custom.BasedOn = active == Identity ? "" : active.Name;
            Active = Custom;
            return Custom;
        }

        /// <summary>What a slider's Reset returns to: the active preset's origin, or the defaults.</summary>
        public T Reference()
        {
            T active = Active ?? Identity;
            if (active != Custom)
            {
                return active;
            }
            return FindBuiltIn(active.BasedOn) ?? Defaults;
        }

        public T FindBuiltIn(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            for (int i = 0; i < Presets.Length; i++)
            {
                T p = Presets[i];
                if (p != Custom && p != Identity && p.Name == name)
                {
                    return p;
                }
            }
            return null;
        }

        public void ResetCustom()
        {
            Custom.CopyValuesFrom(Defaults);
            Custom.BasedOn = "";
        }

        // ----------------------------------------------------- per-vehicle tunes (0.9.0)

        /// <summary>Does this vehicle have its own saved tune?</summary>
        public bool HasVehicle(string vehicleName)
        {
            return !string.IsNullOrEmpty(vehicleName) && _perVehicle.ContainsKey(vehicleName);
        }

        /// <summary>The vehicle's own tune, or the global Active when it has none.</summary>
        public T ForVehicle(string vehicleName)
        {
            T v;
            if (!string.IsNullOrEmpty(vehicleName) && _perVehicle.TryGetValue(vehicleName, out v))
            {
                return v;
            }
            return Active ?? Identity;
        }

        /// <summary>Deep-copy the active preset into the vehicle's own slot.</summary>
        public bool SaveVehicle(string vehicleName)
        {
            if (string.IsNullOrEmpty(vehicleName))
            {
                return false;
            }
            T source = Active ?? Identity;
            T copy = _perVehicle.ContainsKey(vehicleName)
                ? _perVehicle[vehicleName]
                : (T)Activator.CreateInstance(typeof(T));
            copy.CopyValuesFrom(source);
            // Mirror BeginEdit + ExportBasedOn: a built-in source records its own name
            // as the origin, so the blob round-trip keeps the preset it forked from.
            copy.BasedOn = source == Identity ? "" : (source == Custom ? source.BasedOn : source.Name);
            _perVehicle[vehicleName] = copy;
            return true;
        }

        /// <summary>Store an imported copy under the vehicle's name (config load path).</summary>
        public void ImportVehicle(string vehicleName, T preset)
        {
            if (string.IsNullOrEmpty(vehicleName) || preset == null)
            {
                return;
            }
            T copy = _perVehicle.ContainsKey(vehicleName)
                ? _perVehicle[vehicleName]
                : (T)Activator.CreateInstance(typeof(T));
            copy.CopyValuesFrom(preset);
            copy.BasedOn = preset.BasedOn;
            _perVehicle[vehicleName] = copy;
        }

        public bool RemoveVehicle(string vehicleName)
        {
            return !string.IsNullOrEmpty(vehicleName) && _perVehicle.Remove(vehicleName);
        }

        /// <summary>Drop every saved per-vehicle tune (blob re-import starts from scratch).</summary>
        public void ClearVehicles()
        {
            _perVehicle.Clear();
        }

        /// <summary>Vehicle names with saved tunes, in insertion order (config serialization).</summary>
        public ICollection<string> VehicleNames
        {
            get { return _perVehicle.Keys; }
        }

        public T VehicleCopy(string vehicleName)
        {
            T v;
            return !string.IsNullOrEmpty(vehicleName) && _perVehicle.TryGetValue(vehicleName, out v) ? v : null;
        }
    }
}
