using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Hand controls for the passes that normally run on their own.
    ///
    /// Every one of these builders is automatic: they watch for what is missing
    /// or wrong and fix it when the editor loads. That is the right default, and
    /// it is why there is no menu for the ordinary case. This is for when the
    /// automatic pass cannot help — most of all the one that sent us here, a
    /// fresh clone whose track materials were baked before the render pipeline's
    /// shaders were available and which therefore draws every track magenta.
    ///
    /// The order in <see cref="ReloadEverything"/> is the dependency order:
    /// meshes and materials, then the courses read out of the original's tables,
    /// then the catalog that binds them to the specs, then the scenes that
    /// reference all of it.
    /// </summary>
    internal static class OrangeCarrrrrMenu
    {
        private const string Menu = "OrangeCarrrrr/";

        // Grouped in tens so separators fall between the groups rather than
        // between neighbours.
        private const int ReloadGroup = 0;
        private const int RebuildGroup = 20;
        private const int EverythingGroup = 40;

        [MenuItem(Menu + "Reimport Track Meshes and Materials", priority = ReloadGroup)]
        private static void ReimportTracks() => KtrkMaterialRepair.ReimportAll();

        [MenuItem(Menu + "Repair Broken Track Materials", priority = ReloadGroup + 1)]
        private static void RepairTracks()
        {
            // The repair is quiet when it finds nothing, which is what you want on
            // every editor load and not what you want from a menu you just clicked.
            if (KtrkMaterialRepair.RepairBroken(out int repaired) && repaired == 0)
            {
                Debug.Log("Track materials: nothing to repair, every one is on the right shader.");
            }
        }

        [MenuItem(Menu + "Rebuild Track Courses", priority = RebuildGroup)]
        private static void RebuildCourses() => TrackCourseBuilder.Build(force: true);

        [MenuItem(Menu + "Rebuild Track Catalog", priority = RebuildGroup + 1)]
        private static void RebuildCatalog() => TrackCatalogBuilder.Build();

        [MenuItem(Menu + "Build Missing Track Scenes", priority = RebuildGroup + 2)]
        private static void BuildScenes() => TrackSceneBuilder.Build();

        /// <summary>
        /// The whole pipeline, in order. This is the one to reach for on a clone
        /// that looks wrong and where it is not obvious which step went bad.
        /// </summary>
        [MenuItem(Menu + "Reload Everything", priority = EverythingGroup)]
        private static void ReloadEverything()
        {
            KtrkMaterialRepair.ReimportAll();
            TrackCourseBuilder.Build(force: true);
            TrackCatalogBuilder.Build();
            TrackSceneBuilder.Build();
            Debug.Log("OrangeCarrrrr: reloaded meshes, courses, catalog and scenes.");
        }
    }
}
