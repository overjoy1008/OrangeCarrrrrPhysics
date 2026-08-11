using System;
using System.Collections.Generic;
using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// One track's <c>course</c> tag, as an editable asset.
    ///
    /// This is the checkpoint data the original keeps in each <c>track.1s</c>:
    /// the road slices, each with the gate quad standing across it and the
    /// centreline records behind it, and the <c>road</c>/<c>branch</c> sections
    /// that say which order they are walked in.
    ///
    /// The tables are stored flat — one float array for the records, one for the
    /// gate corners, and index ranges into them — because Unity's serializer
    /// cannot hold the nested arrays the C tables are, and because the shape a
    /// track carries (up to a few hundred slices of a few records each) is
    /// exactly what a flat run reads well. <see cref="ToAsset"/> rebuilds the
    /// nested form the course builder wants, once, when a scene loads.
    /// </summary>
    public sealed class TrackCourseAsset : ScriptableObject
    {
        [Serializable]
        public struct Element
        {
            /// <summary>Empty unless the asset names it; start/end/final match against it.</summary>
            public string Name;

            /// <summary>Only ice_R01 carries "warpnext".</summary>
            public string Extra;

            /// <summary>First of this element's 18 floats in <see cref="_corners"/>.</summary>
            public int CornerFirst;

            /// <summary>First of this element's records in <see cref="_records"/>, in floats.</summary>
            public int RecordFirst;

            public int RecordCount;
        }

        [Serializable]
        public struct Section
        {
            /// <summary>-1 for a branch, which has no element run of its own.</summary>
            public int ElementFirst;

            public int ElementCount;
            public string Start;
            public string End;
            public string Final;
            public bool Reverse;

            /// <summary>
            /// First of this branch's alternatives in <see cref="_alternatives"/>.
            /// A branch's alternatives are sections in the same flat list, which
            /// is what lets a branch inside a branch stay one array.
            /// </summary>
            public int AlternativeFirst;

            public int AlternativeCount;
        }

        /// <summary>One alternative of a branch: a run of sections.</summary>
        [Serializable]
        public struct Alternative
        {
            public int SectionFirst;
            public int SectionCount;
        }

        [SerializeField] private string _track;

        [SerializeField] private Section[] _sections = Array.Empty<Section>();
        [SerializeField] private Alternative[] _alternatives = Array.Empty<Alternative>();
        [SerializeField] private Element[] _elements = Array.Empty<Element>();

        /// <summary>Gate corners, 18 floats per element: two triangles of three points.</summary>
        [SerializeField] private float[] _corners = Array.Empty<float>();

        /// <summary>Centreline records, six floats each: position then direction.</summary>
        [SerializeField] private float[] _records = Array.Empty<float>();

        /// <summary>
        /// The top-level sections. A course's own sections are the ones no branch
        /// claims, and they are always the leading run of the list.
        /// </summary>
        [SerializeField] private int _rootSectionCount;

        public string Track => _track;

        public int SectionCount => _rootSectionCount;

        public int ElementCount => _elements.Length;

        private KartCourseAsset _built;

        /// <summary>
        /// The nested form the course builder walks. Built once and kept: a track
        /// only has one course, and rebuilding it per query would allocate the
        /// whole table again on every reset.
        /// </summary>
        public KartCourseAsset ToAsset()
        {
            if (_built != null) return _built;
            if (_rootSectionCount == 0) return null;

            _built = new KartCourseAsset
            {
                Track = _track,
                Sections = ReadSections(0, _rootSectionCount),
            };
            return _built;
        }

        private KartCourseSection[] ReadSections(int first, int count)
        {
            var sections = new KartCourseSection[count];
            for (int index = 0; index < count; ++index)
            {
                Section source = _sections[first + index];
                sections[index] = new KartCourseSection
                {
                    Elements = source.ElementFirst < 0
                        ? null
                        : ReadElements(source.ElementFirst, source.ElementCount),
                    Start = Name(source.Start),
                    End = Name(source.End),
                    Final = Name(source.Final),
                    Reverse = source.Reverse,
                    Alternatives = ReadAlternatives(source),
                };
            }
            return sections;
        }

        private KartCourseSection[][] ReadAlternatives(Section source)
        {
            if (source.AlternativeCount == 0) return null;

            var alternatives = new KartCourseSection[source.AlternativeCount][];
            for (int index = 0; index < source.AlternativeCount; ++index)
            {
                Alternative alternative = _alternatives[source.AlternativeFirst + index];
                alternatives[index] = ReadSections(alternative.SectionFirst, alternative.SectionCount);
            }
            return alternatives;
        }

        private KartCourseElement[] ReadElements(int first, int count)
        {
            var elements = new KartCourseElement[count];
            for (int index = 0; index < count; ++index)
            {
                Element source = _elements[first + index];
                var records = new KartCourseRecord[source.RecordCount];
                for (int record = 0; record < source.RecordCount; ++record)
                {
                    int at = source.RecordFirst + record * 6;
                    records[record] = new KartCourseRecord
                    {
                        Position = new KartVec3(_records[at], _records[at + 1], _records[at + 2]),
                        Direction = new KartVec3(_records[at + 3], _records[at + 4], _records[at + 5]),
                    };
                }

                elements[index] = new KartCourseElement
                {
                    Name = Name(source.Name),
                    Extra = Name(source.Extra),
                    First = ReadTriangle(source.CornerFirst),
                    Second = ReadTriangle(source.CornerFirst + 9),
                    Records = records,
                };
            }
            return elements;
        }

        private KartCourseTriangle ReadTriangle(int at) => new KartCourseTriangle
        {
            A = new KartVec3(_corners[at], _corners[at + 1], _corners[at + 2]),
            B = new KartVec3(_corners[at + 3], _corners[at + 4], _corners[at + 5]),
            C = new KartVec3(_corners[at + 6], _corners[at + 7], _corners[at + 8]),
        };

        /// <summary>
        /// The C tables use a null name for "unnamed" and the builder matches on
        /// it; Unity serializes a null string as empty, so the two are folded here
        /// rather than at every comparison.
        /// </summary>
        private static string Name(string value) => string.IsNullOrEmpty(value) ? null : value;

#if UNITY_EDITOR
        /// <summary>
        /// Fills the asset from the flat tables the generator produces. Editor
        /// only: the course data comes out of the original's own files, and
        /// nothing at runtime has any business rewriting it.
        /// </summary>
        public void SetTables(
            string track,
            IReadOnlyList<Section> sections,
            IReadOnlyList<Alternative> alternatives,
            IReadOnlyList<Element> elements,
            IReadOnlyList<float> corners,
            IReadOnlyList<float> records,
            int rootSectionCount)
        {
            _track = track;
            _sections = new List<Section>(sections).ToArray();
            _alternatives = new List<Alternative>(alternatives).ToArray();
            _elements = new List<Element>(elements).ToArray();
            _corners = new List<float>(corners).ToArray();
            _records = new List<float>(records).ToArray();
            _rootSectionCount = rootSectionCount;
            _built = null;
        }
#endif
    }
}
