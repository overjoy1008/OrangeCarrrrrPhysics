using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Creates a <see cref="TrackSpecAsset"/> for every row of the recovered
    /// <c>TRACKS[]</c> table and gathers them into the catalog the <c>T</c> key
    /// walks.
    ///
    /// Bounds, start line and start-pose evidence level are never typed in here:
    /// each asset takes them from <see cref="KartDemoData"/>, which was
    /// transcribed from the decoded <c>track.1s</c>. Re-running refreshes them, so
    /// an asset edited in the inspector goes back to the recovered numbers.
    ///
    /// The minimap artwork is attached the same way, by the same derivation the
    /// rest of the pipeline uses: a track's files are all named after its id, so
    /// the map is <c>Art/Tracks/&lt;id&gt;/&lt;id&gt;_minimap.png</c>. A track
    /// whose archive has none simply keeps an empty field and draws the grid.
    ///
    /// Existing assets are updated in place, so the GUIDs scenes already reference
    /// survive.
    /// </summary>
    public sealed class TrackCatalogBuilder : AssetPostprocessor
    {
        private const string TrackDirectory = "Assets/_Project/Data/Tracks";
        private const string CatalogPath = TrackDirectory + "/TrackCatalog.asset";
        private const string ArtDirectory = "Assets/_Project/Art/Tracks";
        private const string SceneDirectory = "Assets/_Project/Scenes";
        private const string CourseDirectory = "Assets/_Project/Data/Courses";

        private static bool _building;

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_building) return;
            if (!Touches(imported) && !Touches(deleted) && !Touches(moved) && !Touches(movedFrom))
            {
                return;
            }

            // Deferred: creating assets from inside the import callback re-enters
            // the asset pipeline while it is still running.
            EditorApplication.delayCall += Build;
        }

        private static bool Touches(string[] paths)
        {
            foreach (string path in paths)
            {
                string normalised = path.Replace('\\', '/');
                if (normalised == CatalogPath) continue;
                if (normalised.StartsWith(TrackDirectory + "/") && normalised.EndsWith(".asset"))
                {
                    return true;
                }
                if (normalised.StartsWith(ArtDirectory + "/") && normalised.EndsWith(".ktrk"))
                {
                    return true;
                }
                if (normalised.StartsWith(CourseDirectory + "/") && normalised.EndsWith(".asset"))
                {
                    return true;
                }
            }
            return false;
        }

        public static void Build()
        {
            if (_building) return;
            _building = true;
            try
            {
                Directory.CreateDirectory(Path.GetFullPath(TrackDirectory));
                AssetDatabase.Refresh();

                var specs = new List<TrackSpecAsset>(KartDemoData.Tracks.Count);
                int meshes = 0;
                int scenes = 0;
                int minimaps = 0;
                int courses = 0;

                foreach (TrackSpec recovered in KartDemoData.Tracks)
                {
                    string id = recovered.AssetName;
                    string specPath = $"{TrackDirectory}/{id}.asset";

                    var spec = AssetDatabase.LoadAssetAtPath<TrackSpecAsset>(specPath);
                    if (spec == null)
                    {
                        spec = ScriptableObject.CreateInstance<TrackSpecAsset>();
                        AssetDatabase.CreateAsset(spec, specPath);
                    }

                    spec.ApplySpec(recovered);

                    // Written through SerializedObject because the asset exposes
                    // the map read-only: nothing outside this pipeline should be
                    // choosing which artwork a track carries.
                    var minimap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"{ArtDirectory}/{id}/{id}_minimap.png");
                    if (minimap != null) ++minimaps;

                    var course = AssetDatabase.LoadAssetAtPath<TrackCourseAsset>(
                        $"{CourseDirectory}/{id}.asset");
                    if (course != null) ++courses;

                    var serialized = new SerializedObject(spec);
                    serialized.FindProperty("_minimap").objectReferenceValue = minimap;
                    serialized.FindProperty("_course").objectReferenceValue = course;
                    serialized.ApplyModifiedPropertiesWithoutUndo();

                    EditorUtility.SetDirty(spec);
                    specs.Add(spec);

                    // The scene name is the id, so both of these are checks rather
                    // than lookups: a missing one is reported, never guessed at.
                    if (recovered.HasScene &&
                        File.Exists(Path.GetFullPath($"{ArtDirectory}/{id}/track_{id}.ktrk")))
                    {
                        ++meshes;
                    }
                    if (File.Exists(Path.GetFullPath($"{SceneDirectory}/{id}.unity"))) ++scenes;
                }

                var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<TrackCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
                catalog.SetTracks(specs.ToArray());
                EditorUtility.SetDirty(catalog);

                AssetDatabase.SaveAssets();

                Debug.Log(
                    $"Track catalog: {specs.Count} specs, {meshes}/13 KTRK meshes, " +
                    $"{minimaps}/13 minimaps, {courses}/13 courses, " +
                    $"{scenes}/{specs.Count} scenes. Bounds and " +
                    "start poses come from the recovered TRACKS[] table; the scene " +
                    "name is the track id.");
            }
            finally { _building = false; }
        }
    }
}
