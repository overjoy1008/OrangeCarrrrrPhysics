using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Where every kart sits in the gallery grid: one generation per row, one
    /// series per column.
    ///
    /// This is a display choice, not anything the archives dictate — the original
    /// has no gallery. It lives here rather than on the MonoBehaviour so the
    /// grid can be checked without a scene, because getting it wrong is silent: a
    /// kart put in the wrong cell still renders fine and only looks odd to
    /// someone who already knows the line-up.
    ///
    /// The grid is written out in full rather than derived. Nothing in a kart's
    /// own files says which generation it is — <c>burst6</c> is the SR burst,
    /// <c>burst10</c> the Z7 and <c>burst22</c> the 9th — and the numbers do not
    /// line up across series: the 9th cotton is <c>cotton_9th</c> while the 9th
    /// marathon is <c>marathon19</c>. The generations come from the client's
    /// <c>etc_/itemTable.kml</c>, where every kart carries a <c>grade</c>: 1 to 5
    /// are the demo's, then 6 to 12 are SR, Z7, HT, New, 9th, X and V1. The last
    /// three are pinned by the paragons, whose own grades are 10, 11 and 12. See
    /// <see cref="KartDemoData.Karts"/> for the cross-check against engine sounds.
    ///
    /// Writing it out also removes a trap: the practice kart's name ends in a 1
    /// without being a grade 1, and read off the trailing digit it would land in
    /// the middle of the first generation.
    /// </summary>
    public static class KartGalleryLayout
    {
        public const int RowCount = 12;
        public const int ColumnCount = 15;

        public const int C1Row = 0;
        public const int E2Row = 1;
        public const int G3Row = 2;
        public const int R4Row = 3;
        public const int ProRow = 4;
        public const int SrRow = 5;
        public const int Z7Row = 6;
        public const int HtRow = 7;
        public const int NewRow = 8;
        public const int Paragon9Row = 9;
        public const int ParagonXRow = 10;
        public const int ParagonV1Row = 11;

        public const int PracticeColumn = 0;
        public const int BurstColumn = 1;
        public const int CottonColumn = 2;
        public const int MarathonColumn = 3;
        public const int SaberColumn = 4;
        public const int SolidColumn = 5;
        public const int SpectorColumn = 6;
        public const int WhiteKnightColumn = 7;
        public const int BlackKnightColumn = 8;
        public const int GoldKnightColumn = 9;
        public const int MantisColumn = 10;
        public const int ParagonColumn = 11;
        public const int GoldenParagonColumn = 12;
        public const int GoldenStormBladeColumn = 13;
        public const int ArtemisColumn = 14;

        /// <summary>
        /// What each row is called, for the label written on the floor beside it.
        ///
        /// The 9th generation reads <c>JIU</c> here rather than the <c>9</c> the
        /// kart names use — 구, the engine set's own name, which is also what the
        /// client calls that engine folder. A row header names the generation; a
        /// kart name says which one of them the kart is.
        /// </summary>
        private static readonly string[] RowLabels =
        {
            "C1", "E2", "G3", "R4", "PRO",
            "SR", "Z7", "HT", "NEW",
            "JIU", "X", "V1",
        };

        public static string RowLabel(int row)
            => row >= 0 && row < RowCount ? RowLabels[row] : string.Empty;

        /// <summary>
        /// The grid, row by row. A null is a cell the client ships no kart for —
        /// the practice kart has no SR, Z7 or HT, and the paragon only starts at
        /// the 9th — and those stay empty rather than being filled with a
        /// stand-in.
        ///
        /// Column order is the series in the order they are shown: practice, the
        /// five families, then the nine that only run from the 9th on.
        ///
        /// Two of those took finding, and neither name says what it is. The black
        /// knight's assets are <c>slrPro</c>, and the golden storm blade was a
        /// bike — <c>bike22</c> at Z7 — before it became
        /// <c>stormbladeV1_gold</c>. Both came from the Korean names the client
        /// leaves as comments beside the kart ids in
        /// <c>etc_/itemFeatureCheckFilter@kr.xml</c>.
        ///
        /// Two columns are shorter than the rest, and not for want of looking:
        ///
        /// <list type="bullet">
        /// <item>The spector has no 9th. <c>itemTable.kml</c> grades
        /// <c>spector1</c> an HT and the next one is <c>spectorX</c>.</item>
        /// <item>The golden storm blade has no 9th or X <em>model</em>. The items
        /// existed — <c>X_골드스톰블레이드</c> is a part card in the table — but
        /// <c>DataPack2</c> ships <c>bike58</c>, <c>bike60</c> and <c>bike62</c>
        /// as <c>param.xml</c> and nothing else, and <c>bike52</c> is not in any
        /// pack at all. There is no geometry to show.</item>
        /// </list>
        /// </summary>
        private static readonly string[][] Grid =
        {
            //         0 practice        1 burst    2 cotton      3 marathon    4 saber             5 solid     6 spector     7 white knight   8 black knight  9 gold knight   10 mantis   11 paragon     12 golden paragon     13 golden storm blade  14 artemis
            /* C1  */ new[] { "practice1",      "burst1",  "cotten1",    "marathon1",  "saber1",           "solid1",   null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* E2  */ new[] { null,             "burst2",  "cotten2",    "marathon2",  "saber2",           "solid2",   null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* G3  */ new[] { null,             "burst3",  "cotten3",    "marathon3",  "saber3",           "solid3",   null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* R4  */ new[] { null,             "burst4",  "cotten4",    "marathon4",  "saber4",           "solid4",   null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* PRO */ new[] { "practice5",      "burst5",  "cotten5",    "marathon5",  "saber5",           "solid5",   null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* SR  */ new[] { null,             "burst6",  "cotton6",    "marathon6",  "saber6",           "solid6",   null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* Z7  */ new[] { null,             "burst10", "cotton11",   "marathon11", "saber9",           "solid10",  null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* HT  */ new[] { null,             "burst11", "cotton15",   "marathon12", "saber14",          "solid13",  null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* New */ new[] { "practice6",      "burst12", "cotton19",   "marathon13", "saber18_newsaber", "solid17",  null,         null,            null,           null,           null,       null,          null,                 null,                  null },
            /* 9   */ new[] { "practice7speed", "burst22", "cotton_9th", "marathon19", "saber23",          "solid26",  null,         "whiteKnight9",  "slrPro7",      "goldKnight9",  "mantis3",  "paragon_9th", "paragon_9th_golden", null,                  "artemis9" },
            /* X   */ new[] { "practiceX",      "burstX",  "cottonX",    "marathonX",  "saberX",           "solidX",   "spectorX",   "whiteKnightX",  "slrProX",      "goldKnightX",  "mantisX",  "paragonX",    "paragonX_gold",      null,                  "artemisX" },
            /* V1  */ new[] { "practiceV1",     "burstV1", "cottonV1",   "marathonV1", "saberV1",          "solidV1",  "spectorV1",  "whiteKnightV1", "slrProV1",     "goldKnightV1", "mantisV1", "paragonV1",   "paragonV1_gold",     "stormbladeV1_gold",   "artemisV1" },
        };

        /// <summary>The kart in a cell, or null when the client ships none.</summary>
        public static string At(int row, int column)
            => row >= 0 && row < RowCount && column >= 0 && column < ColumnCount
                ? Grid[row][column]
                : null;

        /// <summary>The row a kart belongs in, or -1 for one not in the grid.</summary>
        public static int RowOf(string assetName)
        {
            Find(assetName, out int row, out _);
            return row;
        }

        /// <summary>The column a kart belongs in, or -1 for one not in the grid.</summary>
        public static int ColumnOf(string assetName)
        {
            Find(assetName, out _, out int column);
            return column;
        }

        public static void Find(string assetName, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (string.IsNullOrWhiteSpace(assetName)) return;

            string name = assetName.Trim();
            for (int r = 0; r < RowCount; ++r)
            {
                for (int c = 0; c < ColumnCount; ++c)
                {
                    if (!string.Equals(Grid[r][c], name, StringComparison.OrdinalIgnoreCase)) continue;

                    row = r;
                    column = c;
                    return;
                }
            }
        }

        /// <summary>
        /// The colour a kart is shown in: one per series, so a row reads as the
        /// series side by side and a column as one series through its generations.
        ///
        /// Taken from the column rather than from the name, so it cannot disagree
        /// with where the kart is standing. Anything off the grid falls back to
        /// red — visible as an odd colour rather than as a crash.
        /// </summary>
        public static int ColourOf(string assetName)
        {
            switch (ColumnOf(assetName))
            {
                case BurstColumn: return Teal;
                case CottonColumn: return Green;
                case SaberColumn: return Blue;
                case SolidColumn: return Yellow;
                case WhiteKnightColumn: return Blue;
                case MantisColumn: return White;
                case GoldenParagonColumn: return Orange;
                case GoldenStormBladeColumn: return Black;

                // practice, marathon, spector, both dark knights, the paragon,
                // artemis, and anything unrecognised.
                default: return Red;
            }
        }

        /// <summary>Indices into <see cref="KartColorTable"/>, named for readability.</summary>
        private const int Red = 0;

        private const int Yellow = 1;
        private const int Orange = 2;
        private const int Green = 3;

        /// <summary>The table's nearest thing to a sky blue.</summary>
        private const int Teal = 4;

        private const int Blue = 5;
        private const int Purple = 6;
        private const int Black = 7;
        private const int White = 9;
    }
}
