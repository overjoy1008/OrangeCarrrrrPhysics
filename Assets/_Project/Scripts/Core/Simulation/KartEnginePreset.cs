using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Which <c>sound_fx_engine</c> folder a kart's motor comes out of.
    ///
    /// The later client ships one engine set per generation and gives each kart
    /// its own, so a paragon does not sound like a 2004 cotten. The demo's
    /// twenty-six are all one generation and all take <c>classic</c>.
    ///
    /// Only the four samples change. The pitch and volume laws under them are the
    /// recovered ones whichever set is loaded, so this picks a timbre and nothing
    /// that touches the simulation.
    /// </summary>
    public static class KartEnginePreset
    {
        /// <summary>The demo's own engine, and the fallback for anything unknown.</summary>
        public const string Classic = "Classic";

        public const string Sr = "Sr";
        public const string Z7 = "Z7";
        public const string Ht = "Ht";
        public const string New = "New";

        /// <summary>The 9th generation's. <c>jiu</c> is 9 read out in Korean.</summary>
        public const string Jiu = "Jiu";

        public const string X = "X";
        public const string V1 = "V1";

        /// <summary>
        /// The preset name for a kart, matched against
        /// <c>KartSoundSet.Preset</c>.
        ///
        /// Keyed off the same generation split the gallery rows use, so a kart
        /// added to one is not silently left out of the other.
        /// </summary>
        public static string For(string assetName)
        {
            switch (KartGalleryLayout.RowOf(assetName))
            {
                case KartGalleryLayout.SrRow: return Sr;
                case KartGalleryLayout.Z7Row: return Z7;
                case KartGalleryLayout.HtRow: return Ht;
                case KartGalleryLayout.NewRow: return New;
                case KartGalleryLayout.Paragon9Row: return Jiu;
                case KartGalleryLayout.ParagonXRow: return X;
                case KartGalleryLayout.ParagonV1Row: return V1;
                default: return Classic;
            }
        }

        public static bool Matches(string preset, string other)
            => string.Equals(preset, other, StringComparison.OrdinalIgnoreCase);
    }
}
