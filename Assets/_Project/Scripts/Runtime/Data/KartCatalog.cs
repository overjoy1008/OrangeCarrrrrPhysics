using System;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The twenty-six karts the <c>K</c> key walks, in <c>KARTS[]</c> order.
    ///
    /// Unlike a track, a kart is not a scene: switching one swaps the spec, the
    /// model and the geometry the simulation is initialised with, all inside the
    /// running scene.
    /// </summary>
    [CreateAssetMenu(
        fileName = "KartCatalog",
        menuName = "OrangeCarrrrr/Kart Catalog",
        order = 5)]
    public sealed class KartCatalog : ScriptableObject
    {
        [SerializeField] private KartSpecAsset[] _karts = Array.Empty<KartSpecAsset>();

        public int Count => _karts != null ? _karts.Length : 0;

        public KartSpecAsset At(int index)
            => _karts != null && index >= 0 && index < _karts.Length ? _karts[index] : null;

        public int IndexOf(KartSpecAsset kart)
        {
            if (kart == null || _karts == null) return -1;
            for (int i = 0; i < _karts.Length; ++i)
            {
                if (_karts[i] == kart) return i;
            }
            return -1;
        }

        /// <summary>The kart after this one, wrapping.</summary>
        public KartSpecAsset Next(KartSpecAsset kart)
        {
            if (Count == 0) return null;
            int index = IndexOf(kart);
            return At(index < 0 ? 0 : (index + 1) % Count);
        }

#if UNITY_EDITOR
        internal void SetKarts(KartSpecAsset[] karts) => _karts = karts;
#endif
    }
}
