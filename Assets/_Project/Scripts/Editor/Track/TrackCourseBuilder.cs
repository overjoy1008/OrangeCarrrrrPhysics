using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Bakes each track's checkpoint course into a <see cref="TrackCourseAsset"/>.
    ///
    /// The source is the original's own decoded table, read by
    /// <see cref="CourseTableReader"/>. That table lives in the sibling checkout
    /// this project was recovered from, so this runs once and the assets it makes
    /// are the artefact; a checkout without that sibling keeps the assets and
    /// simply never runs this.
    ///
    /// Existing assets are refreshed in place rather than replaced, so the GUIDs
    /// the track specs reference survive a re-run.
    /// </summary>
    [InitializeOnLoad]
    public static class TrackCourseBuilder
    {
        private const string CourseDirectory = "Assets/_Project/Data/Courses";

        /// <summary>
        /// The original's generated table, relative to this project's folder. It
        /// is the file <c>derive_course_gates.py</c> writes out of each track.1s.
        /// </summary>
        private const string SourcePath =
            "../KartriderDemoPhysics/Scripts/Runtime/Gameplay/kart_course_data.c";

        static TrackCourseBuilder() => EditorApplication.delayCall += Build;

        public static void Build()
        {
            var missing = new List<string>();
            foreach (TrackSpec track in KartDemoData.Tracks)
            {
                if (!track.HasScene) continue;             // the flat reference track
                if (File.Exists(Path.GetFullPath($"{CourseDirectory}/{track.AssetName}.asset")))
                {
                    continue;
                }
                missing.Add(track.AssetName);
            }

            if (missing.Count == 0) return;

            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourcePath));
            if (!File.Exists(source))
            {
                Debug.LogWarning(
                    $"No course asset for {string.Join(", ", missing)}, and the original's " +
                    $"table is not at {SourcePath}. Checkpoints are off on those tracks.");
                return;
            }

            List<CourseTableReader.Course> courses = CourseTableReader.Read(File.ReadAllText(source));
            Directory.CreateDirectory(Path.GetFullPath(CourseDirectory));

            var written = new List<string>();
            foreach (CourseTableReader.Course course in courses)
            {
                if (KartDemoData.FindTrack(course.Track) == null)
                {
                    Debug.LogWarning($"The course table has {course.Track}, which is not a track.");
                    continue;
                }

                Write(course);
                written.Add(course.Track);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"Track courses: {written.Count} baked ({string.Join(", ", written)}) from " +
                "the original's decoded track.1s tables.");
        }

        private static void Write(CourseTableReader.Course course)
        {
            var flat = new Flattener();
            flat.Sections(course.Sections);

            string path = $"{CourseDirectory}/{course.Track}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TrackCourseAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TrackCourseAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.SetTables(
                course.Track,
                flat.SectionTable,
                flat.AlternativeTable,
                flat.ElementTable,
                flat.Corners,
                flat.Records,
                course.Sections.Length);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// Turns the nested tables into the flat runs the asset stores.
        ///
        /// A run of sections is always contiguous, which is what lets a section
        /// list be an index and a count. That has to be reserved before its
        /// branches are walked, because a branch appends its own alternatives to
        /// the same list.
        /// </summary>
        private sealed class Flattener
        {
            public readonly List<TrackCourseAsset.Section> SectionTable =
                new List<TrackCourseAsset.Section>();

            public readonly List<TrackCourseAsset.Alternative> AlternativeTable =
                new List<TrackCourseAsset.Alternative>();

            public readonly List<TrackCourseAsset.Element> ElementTable =
                new List<TrackCourseAsset.Element>();

            public readonly List<float> Corners = new List<float>();
            public readonly List<float> Records = new List<float>();

            /// <summary>
            /// Element runs by the array they came from. Several sections walk the
            /// same road with different start and end points — village_I02 walks
            /// one road three times — and they share the run rather than each
            /// carrying a copy of it.
            /// </summary>
            private readonly Dictionary<CourseTableReader.Element[], int> _roads =
                new Dictionary<CourseTableReader.Element[], int>();

            public int Sections(CourseTableReader.Section[] sections)
            {
                int first = SectionTable.Count;
                for (int index = 0; index < sections.Length; ++index)
                {
                    SectionTable.Add(default);
                }

                for (int index = 0; index < sections.Length; ++index)
                {
                    CourseTableReader.Section source = sections[index];
                    var section = new TrackCourseAsset.Section
                    {
                        ElementFirst = -1,
                        Start = source.Start,
                        End = source.End,
                        Final = source.Final,
                        Reverse = source.Reverse,
                    };

                    if (source.Elements != null)
                    {
                        section.ElementFirst = Road(source.Elements);
                        section.ElementCount = source.Elements.Length;
                    }

                    if (source.Alternatives != null)
                    {
                        // The alternatives' own sections land after this run, which
                        // is why the run above is reserved first.
                        var runs = new List<TrackCourseAsset.Alternative>();
                        foreach (CourseTableReader.Section[] alternative in source.Alternatives)
                        {
                            runs.Add(new TrackCourseAsset.Alternative
                            {
                                SectionFirst = Sections(alternative),
                                SectionCount = alternative.Length,
                            });
                        }

                        section.AlternativeFirst = AlternativeTable.Count;
                        section.AlternativeCount = runs.Count;
                        AlternativeTable.AddRange(runs);
                    }

                    SectionTable[first + index] = section;
                }
                return first;
            }

            private int Road(CourseTableReader.Element[] elements)
            {
                if (_roads.TryGetValue(elements, out int existing)) return existing;

                int first = ElementTable.Count;
                foreach (CourseTableReader.Element element in elements)
                {
                    ElementTable.Add(new TrackCourseAsset.Element
                    {
                        Name = element.Name,
                        Extra = element.Extra,
                        CornerFirst = Corners.Count,
                        RecordFirst = Records.Count,
                        RecordCount = element.RecordCount,
                    });
                    Corners.AddRange(element.Corners);
                    Records.AddRange(element.Records);
                }

                _roads[elements] = first;
                return first;
            }
        }
    }
}
