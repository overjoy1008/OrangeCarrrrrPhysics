using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Makes the missing per-track scenes by copying one that already works and
    /// swapping what the track owns.
    ///
    /// A track's scene is the same rig every time — cameras, HUD, kart, a track
    /// world — differing only in which spec, which collision set and which mesh it
    /// carries. Authoring thirteen copies of that by hand is thirteen chances to
    /// wire one of them slightly differently, so it is done from a template.
    ///
    /// Nothing here matches on object names: the template's pieces are found by
    /// component type and the fields are written through
    /// <see cref="SerializedObject"/>, so renaming anything in the template does
    /// not silently produce half-built scenes. A track whose mesh or collision set
    /// is missing is reported and skipped rather than written out broken.
    ///
    /// Existing scenes are left alone. Delete one to have it rebuilt, which is
    /// also the only thing that makes this run: like the track catalog it builds
    /// itself when the editor notices something missing, rather than waiting to be
    /// invoked from a menu.
    /// </summary>
    [InitializeOnLoad]
    public static class TrackSceneBuilder
    {
        private const string SceneDirectory = "Assets/_Project/Scenes";
        private const string ArtDirectory = "Assets/_Project/Art/Tracks";

        /// <summary>
        /// Where a track's mesh is.
        ///
        /// The demo's thirteen sit directly under the art directory; anything from
        /// the later client is kept in a folder of its own so the two sets never
        /// blur together on disk. The demo is checked first, so a name in both
        /// resolves to the demo's. <see cref="TrackCatalogBuilder"/> resolves the
        /// same way, and the two have to agree or a track builds a catalog entry
        /// with no scene behind it.
        /// </summary>
        internal static string KtrkPath(string id)
        {
            string demo = $"{ArtDirectory}/{id}/track_{id}.ktrk";
            if (File.Exists(Path.GetFullPath(demo))) return demo;

            string later = $"{ArtDirectory}/TCGames/{id}/track_{id}.ktrk";
            return File.Exists(Path.GetFullPath(later)) ? later : demo;
        }
        private const string SpecDirectory = "Assets/_Project/Data/Tracks";

        /// <summary>The scene copied for every track that has none.</summary>
        private const string TemplateTrack = "village_R01";

        static TrackSceneBuilder() => EditorApplication.delayCall += Build;

        public static void Build()
        {
            var missing = new List<string>();
            foreach (TrackSpec recovered in KartDemoData.Tracks)
            {
                string id = recovered.AssetName;
                if (!recovered.HasScene) continue;                 // the flat reference track
                if (id == TemplateTrack) continue;
                if (File.Exists(Path.GetFullPath($"{SceneDirectory}/{id}.unity"))) continue;
                missing.Add(id);
            }

            // Nothing to do is the normal case, and it has to cost nothing: this
            // runs on every domain reload, and opening scenes would close the one
            // the user is working in.
            if (missing.Count == 0)
            {
                AddToBuildSettings();
                return;
            }

            string templatePath = $"{SceneDirectory}/{TemplateTrack}.unity";
            if (!File.Exists(Path.GetFullPath(templatePath)))
            {
                Debug.LogError($"No template scene at {templatePath}.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Building walks scene by scene, so the scene that was open has to be
            // put back afterwards rather than left on whichever track came last.
            string openScene = SceneManager.GetActiveScene().path;

            var built = new List<string>();
            var skipped = new List<string>();

            foreach (string id in missing)
            {
                string scenePath = $"{SceneDirectory}/{id}.unity";
                if (TryBuild(id, templatePath, scenePath, out string reason)) built.Add(id);
                else skipped.Add($"{id} ({reason})");
            }

            if (!string.IsNullOrEmpty(openScene) && File.Exists(Path.GetFullPath(openScene)))
            {
                EditorSceneManager.OpenScene(openScene, OpenSceneMode.Single);
            }

            AssetDatabase.Refresh();
            AddToBuildSettings();

            Debug.Log(
                $"Track scenes: built {built.Count} ({string.Join(", ", built)})." +
                (skipped.Count > 0 ? $" Skipped {skipped.Count}: {string.Join(", ", skipped)}." : string.Empty));
        }

        private static bool TryBuild(
            string id, string templatePath, string scenePath, out string reason)
        {
            string ktrkPath = KtrkPath(id);
            string specPath = $"{SpecDirectory}/{id}.asset";

            if (!File.Exists(Path.GetFullPath(ktrkPath))) { reason = "no KTRK mesh"; return false; }
            if (!File.Exists(Path.GetFullPath(specPath))) { reason = "no track spec"; return false; }

            if (!AssetDatabase.CopyAsset(templatePath, scenePath))
            {
                reason = "could not copy the template";
                return false;
            }
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Loaded after the scene is open, never before. Opening a scene single
            // unloads what the previous one was holding, and an asset loaded on the
            // far side of that is a dead wrapper: it still passes a null check, then
            // writes as null into every field it is assigned to. That produces
            // scenes which open cleanly and reference no track at all.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ktrkPath);
            if (model == null) { reason = "no KTRK mesh"; return false; }

            var collision = LoadCollision(ktrkPath);
            if (collision == null) { reason = "no collision sub-asset"; return false; }

            var spec = AssetDatabase.LoadAssetAtPath<TrackSpecAsset>(specPath);
            if (spec == null) { reason = "no track spec"; return false; }

            var world = Object.FindFirstObjectByType<TrackCollisionWorld>(FindObjectsInactive.Include);
            if (world == null)
            {
                reason = "template has no TrackCollisionWorld";
                return false;
            }

            var serialized = new SerializedObject(world);
            SerializedProperty renderRootProperty = serialized.FindProperty("_renderRoot");

            // The template's own mesh goes before the new one is parented in its
            // place, so the scene never holds two tracks at once.
            var previous = renderRootProperty?.objectReferenceValue as Transform;
            Transform parent = previous != null ? previous.parent : world.transform;
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            instance.name = $"track_{id}";
            instance.transform.SetParent(parent, worldPositionStays: false);

            serialized.FindProperty("_track").objectReferenceValue = spec;
            serialized.FindProperty("_collision").objectReferenceValue = collision;
            if (renderRootProperty != null) renderRootProperty.objectReferenceValue = instance.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Read back rather than trusted. A reference that did not take leaves a
            // scene that opens fine and has no track in it, which is worth catching
            // here rather than in the simulator.
            var written = new SerializedObject(world);
            if (written.FindProperty("_track").objectReferenceValue == null ||
                written.FindProperty("_collision").objectReferenceValue == null)
            {
                AssetDatabase.DeleteAsset(scenePath);
                reason = "spec or collision reference did not take";
                return false;
            }

            // The mirror, the AABB re-centring and the ground drop. The instance
            // is created at identity, so without this the drawn track would sit
            // where the template's did rather than on its own collision.
            world.ApplyToRenderRoot();

            foreach (SimulatorRoot simulator in
                     Object.FindObjectsByType<SimulatorRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var simulatorObject = new SerializedObject(simulator);
                simulatorObject.FindProperty("_track").objectReferenceValue = spec;
                simulatorObject.ApplyModifiedPropertiesWithoutUndo();
            }

            PlaceKartOnStartLine(spec.ToSpec());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);

            reason = null;
            return true;
        }

        /// <summary>
        /// Puts the authored kart on this track's start line rather than leaving it
        /// on the template's.
        ///
        /// The simulator computes the same pose itself on every reset, so this is
        /// only what the scene looks like when it is opened — but a copied scene
        /// that shows the kart parked out in a field beside another track's start
        /// line reads as a broken import, and on the tracks whose bounds do not
        /// overlap village_R01's it is off the map entirely.
        /// </summary>
        private static void PlaceKartOnStartLine(TrackSpec track)
        {
            if (!KartTrackStart.Position(track, out KartVec3 position)) return;

            foreach (KartView view in
                     Object.FindObjectsByType<KartView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                view.transform.SetPositionAndRotation(
                    KartSpace.ToUnity(position),
                    KartSpace.ToUnity(KartTrackStart.Orientation(track)));

                // The kart comes from a prefab, so the move is an override and has
                // to be recorded as one or it is dropped on save.
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.transform);
            }
        }

        /// <summary>
        /// The collision set the KTRK importer bakes in beside the meshes. It is a
        /// sub-asset, so it has to be picked out of everything at that path.
        /// </summary>
        private static TrackCollisionAsset LoadCollision(string ktrkPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ktrkPath))
            {
                if (asset is TrackCollisionAsset collision) return collision;
            }
            return null;
        }

        /// <summary>
        /// Puts every track scene in the build list. <c>SceneManager.LoadScene</c>
        /// by name only finds scenes that are in it, so the T key cannot reach one
        /// that has been left out.
        /// </summary>
        private static void AddToBuildSettings()
        {
            var entries = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int before = entries.Count;

            foreach (TrackSpec recovered in KartDemoData.Tracks)
            {
                string scenePath = $"{SceneDirectory}/{recovered.AssetName}.unity";
                if (!File.Exists(Path.GetFullPath(scenePath))) continue;
                if (entries.Exists(entry => entry.path == scenePath)) continue;

                entries.Add(new EditorBuildSettingsScene(scenePath, enabled: true));
            }

            // Assigning is a write to the project settings, so it only happens
            // when the list actually gained a scene.
            if (entries.Count != before) EditorBuildSettings.scenes = entries.ToArray();
        }
    }
}
