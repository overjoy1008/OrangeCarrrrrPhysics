using System;
using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Every engine sound preset, in the original's order.
    ///
    /// Fifteen of them — classic, and seven engines each with a bike variant —
    /// swapping which four samples the sound driver is holding. Which one plays
    /// follows the kart, through <see cref="Find"/> and
    /// <c>OrangeCarrrrr.Core.KartEnginePreset</c>; it used to be the <c>U</c> key
    /// walking the list by hand.
    ///
    /// Nothing about the driving changes: the pitch and volume laws are the
    /// recovered ones either way, and only the waveform under them differs.
    ///
    /// Rebuilt from the sample folders on disk, so adding a preset is adding its
    /// four samples rather than editing a list by hand.
    /// </summary>
    [CreateAssetMenu(
        fileName = "KartSoundCatalog",
        menuName = "OrangeCarrrrr/Kart Sound Catalog",
        order = 11)]
    public sealed class KartSoundCatalog : ScriptableObject
    {
        [SerializeField] private KartSoundSet[] _presets = Array.Empty<KartSoundSet>();

        public int Count => _presets != null ? _presets.Length : 0;

        public KartSoundSet At(int index)
            => _presets != null && index >= 0 && index < _presets.Length ? _presets[index] : null;

        /// <summary>Where a preset sits in the list, or -1 when it is not in it.</summary>
        public int IndexOf(KartSoundSet set)
        {
            if (set == null || _presets == null) return -1;
            for (int i = 0; i < _presets.Length; ++i)
            {
                if (_presets[i] == set) return i;
            }
            return -1;
        }

        /// <summary>
        /// The set whose <c>Preset</c> is this name, or null.
        ///
        /// Matched on the name rather than an index because the catalog is built
        /// from whatever engine folders the project imported — the bike sets sit
        /// in the same list, and the order is the builder's, not something to
        /// depend on.
        /// </summary>
        public KartSoundSet Find(string preset)
        {
            if (_presets == null || string.IsNullOrWhiteSpace(preset)) return null;

            foreach (KartSoundSet set in _presets)
            {
                if (set != null && KartEnginePreset.Matches(set.Preset, preset)) return set;
            }
            return null;
        }

        /// <summary>The preset after this one, wrapping.</summary>
        public KartSoundSet Next(KartSoundSet set)
        {
            if (Count == 0) return null;
            int index = IndexOf(set);
            return At(index < 0 ? 0 : (index + 1) % Count);
        }

#if UNITY_EDITOR
        internal void SetPresets(KartSoundSet[] presets) => _presets = presets;
#endif
    }
}
