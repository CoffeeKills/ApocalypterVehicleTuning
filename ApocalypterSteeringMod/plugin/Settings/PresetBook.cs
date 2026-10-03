using System;

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
    }
}
