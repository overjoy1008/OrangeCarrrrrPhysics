namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// What a kart is called, as against what its files are called.
    ///
    /// The archive names are the client's and they are not a naming scheme: the
    /// grade 5 burst is <c>burst5</c>, the SR one <c>burst6</c>, the 9th
    /// <c>burst22</c>, and the 9th cotton is <c>cotton_9th</c> while the 9th
    /// marathon is <c>marathon19</c>. Read off the list they say nothing about
    /// what the kart is.
    ///
    /// The name is derived rather than stored, for the same reason
    /// <c>TrackSpecAsset.SceneName</c> is: there is exactly one right answer for
    /// each kart, and a second editable copy of it could only drift. It comes
    /// from the cell <see cref="KartGalleryLayout"/> puts the kart in, so a kart
    /// added to the grid is named by being added and nothing else needs touching.
    ///
    /// Renaming the assets themselves would be better still. This is the mapping
    /// in the meantime, which is why every list that shows a display name shows
    /// the asset name beside it.
    /// </summary>
    public static class KartDisplayName
    {
        /// <summary>
        /// The generation as the game writes it, by row. The demo's five grades
        /// carry a letter as well as the number.
        ///
        /// New is the odd one out: it goes in front of the series rather than
        /// after it — 뉴 버스트, not 버스트 뉴 — so it is held as null here and
        /// handled where the name is put together.
        /// </summary>
        private static readonly string[] Generations =
        {
            "C1", "E2", "G3", "R4", "PRO",
            "SR", "Z7", "HT",
            null,   // New: a prefix, see NewPrefix
            "9", "X", "V1",
        };

        /// <summary>The series, by column.</summary>
        private static readonly string[] Series =
        {
            "연습카트", "버스트", "코튼", "마라톤", "세이버", "솔리드",
            "스펙터", "백기사", "흑기사", "황금기사", "멘티스",
            "파라곤", "골든 파라곤", "골든 스톰 블레이드", "아르테미스",
        };

        /// <summary>
        /// The practice kart is named by hand, because the game does not name it
        /// to a pattern: it is 연습카트 with no generation at all, then 연습카트
        /// PRO, then 뉴 연카 and 스피드 연카 9 — which switch to the short form and
        /// put the generation in a different place — and then back to 연습카트 X
        /// and 연습카트 V1.
        /// </summary>
        private static readonly string[] PracticeNames =
        {
            "연습카트", null, null, null, "연습카트 PRO",
            null, null, null,
            "뉴 연카",
            "스피드 연카 9", "연습카트 X", "연습카트 V1",
        };

        private const string NewPrefix = "뉴 ";

        public static string For(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return string.Empty;

            string name = assetName.Trim();
            KartGalleryLayout.Find(name, out int row, out int column);
            if (row < 0) return name;

            if (column == KartGalleryLayout.PracticeColumn)
            {
                return PracticeNames[row] ?? name;
            }

            string series = Series[column];
            if (row == KartGalleryLayout.NewRow) return NewPrefix + series;

            string generation = Generations[row];
            return generation == null ? series : series + " " + generation;
        }
    }
}
