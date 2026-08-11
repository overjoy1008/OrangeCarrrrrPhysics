using System;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Every track the simulator can switch to, in menu order.
    ///
    /// The C build compiles its fourteen-row <c>TRACKS[]</c> in and switches by
    /// swapping which embedded scene the renderer reads. Here a track is a whole
    /// Unity scene — 375 objects and its own collision set on village_R01 — so
    /// switching means loading one, and this is the list the <c>T</c> key walks.
    ///
    /// Rebuilt from the track assets on disk, so adding a track is adding its
    /// asset rather than editing a list by hand.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TrackCatalog",
        menuName = "OrangeCarrrrr/Track Catalog",
        order = 4)]
    public sealed class TrackCatalog : ScriptableObject
    {
        [SerializeField] private TrackSpecAsset[] _tracks = Array.Empty<TrackSpecAsset>();

        public int Count => _tracks != null ? _tracks.Length : 0;

        public TrackSpecAsset At(int index)
            => _tracks != null && index >= 0 && index < _tracks.Length ? _tracks[index] : null;

        /// <summary>Where a track sits in the list, or -1 when it is not in it.</summary>
        public int IndexOf(TrackSpecAsset track)
        {
            if (track == null || _tracks == null) return -1;
            for (int i = 0; i < _tracks.Length; ++i)
            {
                if (_tracks[i] == track) return i;
            }
            return -1;
        }

        /// <summary>
        /// The track after <paramref name="track"/>, wrapping. A track that is not
        /// in the list starts the walk from the beginning.
        /// </summary>
        public TrackSpecAsset Next(TrackSpecAsset track)
        {
            if (Count == 0) return null;
            int index = IndexOf(track);
            return At(index < 0 ? 0 : (index + 1) % Count);
        }

#if UNITY_EDITOR
        internal void SetTracks(TrackSpecAsset[] tracks) => _tracks = tracks;
#endif
    }
}
