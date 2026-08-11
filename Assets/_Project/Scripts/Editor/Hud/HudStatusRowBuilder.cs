using System.IO;
using OrangeCarrrrr.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Keeps the HUD prefab's status rows in step with
    /// <see cref="HudStatusLines.LineCount"/>.
    ///
    /// The panel writes its lines by index, so the constant and the prefab have to
    /// agree: one row short and the whole panel silently stops refreshing, because
    /// the writer bails rather than index past the end. Adding a line to the code
    /// should not be a thing you can forget to do to the prefab as well, so a
    /// missing row is cloned from the first one — which already carries the font,
    /// the size and the alignment every row shares. Extra rows are left alone.
    /// </summary>
    [InitializeOnLoad]
    internal static class HudStatusRowBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/SimulatorHUD.prefab";

        static HudStatusRowBuilder() => EditorApplication.delayCall += Build;

        internal static void Build()
        {
            if (!File.Exists(Path.GetFullPath(PrefabPath))) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return;

            var panel = prefab.GetComponentInChildren<HudStatusLines>(includeInactive: true);
            if (panel == null) return;

            var serialized = new SerializedObject(panel);
            var root = serialized.FindProperty("_lineRoot").objectReferenceValue as RectTransform;
            if (root == null || root.childCount >= HudStatusLines.LineCount) return;

            var template = root.childCount > 0 ? root.GetChild(0) as RectTransform : null;
            if (template == null)
            {
                Debug.LogWarning($"{PrefabPath} has no status row to clone.");
                return;
            }

            GameObject instance = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var live = instance.GetComponentInChildren<HudStatusLines>(includeInactive: true);
                var liveRoot = new SerializedObject(live)
                    .FindProperty("_lineRoot").objectReferenceValue as RectTransform;
                if (liveRoot == null || liveRoot.childCount == 0) return;

                int added = 0;
                while (liveRoot.childCount < HudStatusLines.LineCount)
                {
                    Transform copy = Object.Instantiate(liveRoot.GetChild(0), liveRoot);
                    copy.name = $"Line{liveRoot.childCount - 1:00}";
                    copy.SetAsLastSibling();

                    // The clone carries line 0's text until the panel next
                    // refreshes, which in a saved prefab is never; blanked so the
                    // authored asset does not ship a duplicated line.
                    var label = copy.GetComponent<TextMeshProUGUI>();
                    if (label != null) label.SetText(string.Empty);
                    ++added;
                }

                live.ApplyLayout();
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                Debug.Log($"HUD status rows: added {added} to reach {HudStatusLines.LineCount}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
        }
    }
}
