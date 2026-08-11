namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// One point on the road's centreline and the direction of travel there,
    /// ported from <c>KartCourseRecord</c>.
    ///
    /// The gate's normal is record 0's direction, and the point list a node
    /// carries is these records in order.
    /// </summary>
    public struct KartCourseRecord
    {
        public KartVec3 Position;
        public KartVec3 Direction;
    }

    /// <summary>One triangle of a gate quad.</summary>
    public struct KartCourseTriangle
    {
        public KartVec3 A;
        public KartVec3 B;
        public KartVec3 C;

        public KartVec3 this[int corner] => corner == 0 ? A : corner == 1 ? B : C;
    }

    /// <summary>
    /// One road slice, ported from <c>KartCourseElement</c>: the gate quad
    /// standing across the road, and the centreline behind it.
    /// </summary>
    public sealed class KartCourseElement
    {
        /// <summary>Null unless the asset names it; start/end/final match against it.</summary>
        public string Name;

        /// <summary>
        /// The gate quad's two triangles, corners in the asset's index order.
        /// The graph builder permutes them by <see cref="KartCourseSection.Reverse"/>.
        /// </summary>
        public KartCourseTriangle First;

        public KartCourseTriangle Second;

        /// <summary>ToRoad element +0x1c. Only ice_R01 carries "warpnext".</summary>
        public string Extra;

        public KartCourseRecord[] Records;

        public KartCourseTriangle Face(int face) => face == 0 ? First : Second;

        public int RecordCount => Records != null ? Records.Length : 0;
    }

    /// <summary>
    /// One <c>road</c> or <c>branch</c> tag, ported from <c>KartCourseSection</c>.
    ///
    /// A road walks <see cref="Elements"/> from <see cref="Start"/> to
    /// <see cref="End"/>, wrapping. A branch has no elements of its own: its
    /// alternatives are sub-courses in their own right.
    /// </summary>
    public sealed class KartCourseSection
    {
        public KartCourseElement[] Elements;
        public string Start;
        public string End;
        public string Final;
        public bool Reverse;
        public KartCourseSection[][] Alternatives;

        public bool IsRoad => Elements != null;

        public int ElementCount => Elements != null ? Elements.Length : 0;

        public int AlternativeCount => Alternatives != null ? Alternatives.Length : 0;
    }

    /// <summary>
    /// A track's whole <c>course</c> tag, ported from <c>KartCourseAsset</c>.
    ///
    /// Positions are already in the simulator's world space — the same transform
    /// the scene vertices go through — so a gate lines up with the road under it
    /// with nothing further applied.
    /// </summary>
    public sealed class KartCourseAsset
    {
        public string Track;
        public KartCourseSection[] Sections;

        public int SectionCount => Sections != null ? Sections.Length : 0;
    }
}
