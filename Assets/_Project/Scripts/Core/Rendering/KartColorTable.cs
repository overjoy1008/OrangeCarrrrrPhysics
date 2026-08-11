namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// One row of the demo's <c>colortable.xml</c>: the paint a driver picks.
    ///
    /// Only <see cref="Base"/> is used by the kart's skin — the racing number and
    /// the body both take it. <see cref="High"/> is the second slot of the same
    /// row, kept because the row is the unit the original ships and dropping half
    /// of it would make the table something other than the recovered data.
    /// </summary>
    public readonly struct KartColorSet
    {
        public readonly string Name;
        public readonly byte BaseRed;
        public readonly byte BaseGreen;
        public readonly byte BaseBlue;
        public readonly byte HighRed;
        public readonly byte HighGreen;
        public readonly byte HighBlue;

        public KartColorSet(
            string name,
            byte baseRed, byte baseGreen, byte baseBlue,
            byte highRed, byte highGreen, byte highBlue)
        {
            Name = name;
            BaseRed = baseRed;
            BaseGreen = baseGreen;
            BaseBlue = baseBlue;
            HighRed = highRed;
            HighGreen = highGreen;
            HighBlue = highBlue;
        }
    }

    /// <summary>
    /// The ten paints from <c>Data/etc.rho</c>'s <c>colortable.xml</c>.
    ///
    /// The names are the ones riderData.1s's editor enum uses; the numbers are
    /// the file's own. <c>riderData.1s</c> ships this profile on index 8, so a
    /// kart that nobody has repainted is pink.
    /// </summary>
    public static class KartColorTable
    {
        /// <summary>The index riderData.1s ships.</summary>
        public const int DefaultIndex = 8;

        private static readonly KartColorSet[] Sets =
        {
            new KartColorSet("red",    232,  39,   6, 255, 186,   0),
            new KartColorSet("yellow", 255, 186,   0, 255, 255,   0),
            new KartColorSet("orange", 255, 130,   0, 255, 186,  82),
            new KartColorSet("green",   58, 174,  25, 210, 255,   0),
            new KartColorSet("teal",     0, 199, 206, 255, 255, 255),
            new KartColorSet("blue",    19, 121, 219,   0, 252, 255),
            new KartColorSet("purple", 140,  56, 239, 255, 255, 255),
            new KartColorSet("black",   40,  40,  40, 150, 150, 150),
            new KartColorSet("pink",   248,   1, 122, 255, 190, 190),
            new KartColorSet("white",  243, 243, 243, 255, 255, 255),
        };

        public static int Count => Sets.Length;

        /// <summary>Wraps out-of-range indices the way the original clamps them.</summary>
        public static KartColorSet At(int index)
            => Sets[index >= 0 && index < Sets.Length ? index : 0];

        public static string NameAt(int index) => At(index).Name;

        /// <summary>Steps the <c>L</c> key's cycle.</summary>
        public static int Next(int index) => (index + 1) % Sets.Length;
    }
}
