using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Keeps <see cref="TrackCatalog"/> in step with the track assets on disk.
    ///
    /// Adding a track should be adding its asset, not also remembering to append
    /// it to a list, so the catalog is rebuilt whenever anything under the track
    /// data folder is imported, moved or deleted.
    ///
    /// The flat reference track sorts first because it is where the simulator
    /// opens; the real tracks follow in asset-name order, which is the order the
    /// recovered <c>TRACKS[]</c> table uses.
    /// </summary>
    public sealed class TrackCatalogBuilder : AssetPostprocessor
    {
        private const string TrackDirectory = "Assets/_Project/Data/Tracks";
        private const string CatalogPath = "Assets/_Project/Data/Tracks/TrackCatalog.asset";
        private const string FlatTrack = "flat_test";

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
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
            }
            return false;
        }

        private static void Build()
        {
            var tracks = new List<TrackSpecAsset>();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:" + nameof(TrackSpecAsset), new[] { TrackDirectory }))
            {
                var track = AssetDatabase.LoadAssetAtPath<TrackSpecAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (track != null) tracks.Add(track);
            }

            tracks.Sort((a, b) =>
            {
                bool flatA = a.AssetName == FlatTrack;
                bool flatB = b.AssetName == FlatTrack;
                if (flatA != flatB) return flatA ? -1 : 1;
                return string.CompareOrdinal(a.AssetName, b.AssetName);
            });

            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(CatalogPath)));
                catalog = ScriptableObject.CreateInstance<TrackCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetTracks(tracks.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var names = new List<string>(tracks.Count);
            foreach (TrackSpecAsset track in tracks) names.Add(track.AssetName);
            Debug.Log($"Track catalog: {tracks.Count} tracks ({string.Join(", ", names)}).");
        }
    }
}
