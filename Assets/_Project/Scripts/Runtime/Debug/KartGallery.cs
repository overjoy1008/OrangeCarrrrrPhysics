using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Every kart in the catalog, lined up and painted — the asset-inspection
    /// scene, not something the simulator can drive to.
    ///
    /// It exists because twenty-six models cannot be checked one track load at a
    /// time. Seeing them side by side is what makes an artifact that affects some
    /// models and not others visible as a pattern rather than a hunch.
    ///
    /// Each kart is a real <see cref="KartView"/>, so the gallery exercises the
    /// same model instantiation and the same runtime paint the simulator does; a
    /// kart that looks wrong here looks wrong there for the same reason.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class KartGallery : MonoBehaviour
    {
        [Tooltip("Left empty, the project's catalog is used.")]
        [SerializeField] private KartCatalog _catalog;

        [Tooltip("Gap between one kart's box and the next, in engine units.")]
        [SerializeField, Min(0f)] private float _gap = 0.6f;

        [Tooltip(
            "Row of colortable.xml for every kart. Negative walks the ten paints " +
            "across the row instead, which shows the whole colour table at once.")]
        [SerializeField] private int _colourIndex = -1;

        [Tooltip("Turn the karts so their far side faces the camera.")]
        [SerializeField] private bool _facingAway;

        private void OnEnable() => Rebuild();

        private void OnValidate()
        {
#if UNITY_EDITOR
            // Deferred: OnValidate runs during deserialisation, where creating and
            // destroying objects is not allowed.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) Rebuild();
            };
#endif
        }

        /// <summary>Rebuilds the row from the catalog.</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            if (!gameObject.scene.IsValid()) return;

            KartCatalog catalog = ResolveCatalog();
            if (catalog == null || catalog.Count == 0) return;

            for (int i = transform.childCount - 1; i >= 0; --i)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            float x = 0f;
            float previousHalfWidth = 0f;

            for (int i = 0; i < catalog.Count; ++i)
            {
                KartSpecAsset spec = catalog.At(i);
                if (spec == null) continue;

                float halfWidth = spec.Width * 0.5f;
                if (i > 0) x += previousHalfWidth + _gap + halfWidth;
                previousHalfWidth = halfWidth;

                var holder = new GameObject($"{i:00} {spec.AssetName}");
                holder.transform.SetParent(transform, worldPositionStays: false);
                holder.transform.localPosition = new Vector3(x, 0f, 0f);
                holder.transform.localRotation = _facingAway
                    ? Quaternion.Euler(0f, 180f, 0f)
                    : Quaternion.identity;

                var view = holder.AddComponent<KartView>();
                view.ColourIndex = _colourIndex >= 0
                    ? _colourIndex
                    : i % KartColorTable.Count;
                view.Kart = spec;
            }
        }

        private KartCatalog ResolveCatalog()
        {
#if UNITY_EDITOR
            if (_catalog == null)
            {
                _catalog = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<KartCatalog>("Assets/_Project/Data/Karts/KartCatalog.asset");
            }
#endif
            return _catalog;
        }
    }
}
