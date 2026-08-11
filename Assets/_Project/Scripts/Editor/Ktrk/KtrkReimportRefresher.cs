using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Rebuilds the kart models in open scenes when a <c>.ktrk</c> is reimported.
    ///
    /// A <see cref="KartView"/> instantiates its model once and then skips the
    /// work while the selected kart is unchanged. That is right for the common
    /// case and wrong for exactly one: a reimport replaces the source asset under
    /// an instance that still looks current, so the scene keeps showing the
    /// previous mesh with nothing to indicate it is stale. Re-exporting a model or
    /// changing the importer would otherwise appear to do nothing until the scene
    /// was reopened.
    /// </summary>
    public sealed class KtrkReimportRefresher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = false;
            foreach (string path in imported)
            {
                if (path.EndsWith(".ktrk", System.StringComparison.OrdinalIgnoreCase))
                {
                    touched = true;
                    break;
                }
            }
            if (!touched) return;

            // Deferred: the instances being rebuilt are in scenes the pipeline is
            // still importing into.
            EditorApplication.delayCall += Refresh;
        }

        private static void Refresh()
        {
            int rebuilt = 0;

            foreach (KartView view in Object.FindObjectsByType<KartView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (view == null) continue;
                view.ForceRebuild();
                ++rebuilt;
            }

            if (rebuilt > 0) Debug.Log($"Rebuilt {rebuilt} kart models after a KTRK reimport.");
        }
    }
}
