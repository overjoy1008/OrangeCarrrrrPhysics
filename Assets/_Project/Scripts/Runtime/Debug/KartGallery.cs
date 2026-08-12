using System.Collections.Generic;
using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Every kart in the catalog, lined up and painted — the asset-inspection
    /// scene, not something the simulator can drive to.
    ///
    /// It exists because seventy-odd models cannot be checked one track load at a
    /// time. Seeing them side by side is what makes an artifact that affects some
    /// models and not others visible as a pattern rather than a hunch.
    ///
    /// A real grid: one generation per row, one series per column, each series in
    /// its own colour. A row is every series at one generation and a column is
    /// one series through all of them, so either reading works.
    /// <see cref="KartGalleryLayout"/> decides which kart goes in which cell, and
    /// a cell the client ships no kart for stays empty rather than closing up.
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

        [Tooltip("Gap between one generation's row and the next, in engine units.")]
        [SerializeField, Min(0f)] private float _rowGap = 1.2f;

        [Tooltip(
            "Row of colortable.xml for every kart. Negative paints each family in " +
            "its own colour, which is what the line-up is arranged to show.")]
        [SerializeField] private int _colourIndex = -1;

        [Tooltip(
            "Ignore the family colours and walk the ten paints across the whole " +
            "line-up instead, to see the colour table at once.")]
        [SerializeField] private bool _walkColourTable;

        [Tooltip("Turn the karts so their far side faces the camera.")]
        [SerializeField] private bool _facingAway;

        [Header("Row labels")]
        [Tooltip("Write each generation's name on the floor beside its row.")]
        [SerializeField] private bool _showRowLabels = true;

        [Tooltip("Left empty, the project's Segoe UI Black is used.")]
        [SerializeField] private TMP_FontAsset _labelFont;

        [Tooltip("TextMeshPro point size, which is roughly a tenth of a world unit.")]
        [SerializeField, Min(1f)] private float _labelSize = 20f;

        [Tooltip("How much room the label has, in engine units. It is right-aligned into this.")]
        [SerializeField, Min(0.1f)] private float _labelWidth = 10f;

        [Tooltip("A light grey, so the floor reads as a label rather than as a kart.")]
        [SerializeField] private Color _labelColour = new Color(0.72f, 0.72f, 0.72f, 1f);

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

            // The grid: one generation per row, one series per column. Cells the
            // client ships no kart for are left empty, so a column stays a column —
            // the practice kart has no SR, Z7 or HT, and the paragon starts at the 9th.
            var cell = new KartSpecAsset[KartGalleryLayout.RowCount, KartGalleryLayout.ColumnCount];
            var unplaced = new List<KartSpecAsset>();

            for (int i = 0; i < catalog.Count; ++i)
            {
                KartSpecAsset spec = catalog.At(i);
                if (spec == null) continue;

                KartGalleryLayout.Find(spec.AssetName, out int row, out int column);

                // Anything off the grid goes in a row of its own behind it rather
                // than being dropped — the gallery's job is to show what is there,
                // including something newly added and not yet placed.
                if (row < 0) unplaced.Add(spec);
                else cell[row, column] = spec;
            }

            // Column x is fixed across the whole grid, from the widest kart in each
            // column, so the columns line up whatever sits in them and an empty cell
            // reads as a gap rather than closing up.
            var columnWidth = new float[KartGalleryLayout.ColumnCount];
            for (int c = 0; c < KartGalleryLayout.ColumnCount; ++c)
            {
                for (int r = 0; r < KartGalleryLayout.RowCount; ++r)
                {
                    if (cell[r, c] != null) columnWidth[c] = Mathf.Max(columnWidth[c], cell[r, c].Width);
                }
            }

            float total = 0f;
            int columns = 0;
            foreach (float width in columnWidth)
            {
                if (width <= 0f) continue;
                total += width;
                ++columns;
            }
            total += _gap * Mathf.Max(0, columns - 1);

            float leftEdge = -total * 0.5f;

            var columnX = new float[KartGalleryLayout.ColumnCount];
            float cursor = leftEdge;
            for (int c = 0; c < KartGalleryLayout.ColumnCount; ++c)
            {
                if (columnWidth[c] <= 0f) continue;
                columnX[c] = cursor + columnWidth[c] * 0.5f;
                cursor += columnWidth[c] + _gap;
            }

            // The camera looks down +Z, so a larger z is further away. Rows are
            // assigned from the newest generation backwards: V1 stands nearest and
            // C1 at the far end, so the grid reads front to back rather than back to
            // front. Each row is spaced by the deepest kart standing in it.
            int rowCount = KartGalleryLayout.RowCount + (unplaced.Count > 0 ? 1 : 0);
            var rowZ = new float[rowCount];
            float back = 0f;
            for (int r = rowCount - 1; r >= 0; --r)
            {
                float deepest = 0f;
                if (r < KartGalleryLayout.RowCount)
                {
                    for (int c = 0; c < KartGalleryLayout.ColumnCount; ++c)
                    {
                        if (cell[r, c] != null) deepest = Mathf.Max(deepest, cell[r, c].Length);
                    }
                }
                else
                {
                    foreach (KartSpecAsset spec in unplaced) deepest = Mathf.Max(deepest, spec.Length);
                }

                if (deepest <= 0f) continue;

                rowZ[r] = back;
                back += deepest + _rowGap;
            }

            int placed = 0;

            for (int r = 0; r < KartGalleryLayout.RowCount; ++r)
            {
                bool any = false;
                for (int c = 0; c < KartGalleryLayout.ColumnCount; ++c)
                {
                    if (cell[r, c] == null) continue;
                    Place(cell[r, c], columnX[c], rowZ[r], placed++);
                    any = true;
                }

                if (any && _showRowLabels) Label(KartGalleryLayout.RowLabel(r), leftEdge, rowZ[r]);
            }

            // The strays, spread along the back row on the same gap.
            if (unplaced.Count > 0)
            {
                float width = _gap * (unplaced.Count - 1);
                foreach (KartSpecAsset spec in unplaced) width += spec.Width;

                float x = -width * 0.5f;
                foreach (KartSpecAsset spec in unplaced)
                {
                    x += spec.Width * 0.5f;
                    Place(spec, x, rowZ[KartGalleryLayout.RowCount], placed++);
                    x += spec.Width * 0.5f + _gap;
                }
            }
        }

        /// <summary>
        /// The generation's name, written on the floor to the left of its row.
        ///
        /// Laid flat rather than stood up, so it reads as part of the floor and
        /// never occludes a kart.
        ///
        /// The rotation is given as the two axes rather than as Euler angles,
        /// because the readable side is the part that is easy to get wrong.
        /// Text is readable when it is <em>looked along</em> its own +Z, not when
        /// it is looked at from +Z — Unity's own default camera sits at negative
        /// Z and reads a canvas at the origin. So the text's forward is
        /// <see cref="Vector3.down"/>, the way this camera is looking, and its up
        /// is world +Z so the lettering runs left to right. Pointing the forward
        /// at the sky instead is what mirrored it.
        /// </summary>
        private void Label(string text, float leftEdge, float z)
        {
            if (string.IsNullOrEmpty(text)) return;

            var holder = new GameObject($"row {text}");
            holder.transform.SetParent(transform, worldPositionStays: false);

            // Lifted a hair off the ground: at exactly zero it fights the floor.
            holder.transform.localPosition = new Vector3(
                leftEdge - _gap - _labelWidth * 0.5f, 0.02f, z);
            holder.transform.localRotation =
                Quaternion.LookRotation(Vector3.down, Vector3.forward);

            var label = holder.AddComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = _labelSize;
            label.color = _labelColour;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.enableWordWrapping = false;

            TMP_FontAsset font = ResolveLabelFont();
            if (font != null) label.font = font;

            // Sized after the component exists, because adding it creates the
            // RectTransform this is setting. TextMeshPro's point size is about a
            // tenth of a world unit, so the box is scaled off that rather than off
            // the number.
            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(_labelWidth, _labelSize * 0.2f);
        }

        /// <summary>One kart, in the cell the grid puts it in.</summary>
        private void Place(KartSpecAsset spec, float x, float z, int placed)
        {
            var holder = new GameObject($"{placed:00} {spec.AssetName}");
            holder.transform.SetParent(transform, worldPositionStays: false);
            holder.transform.localPosition = new Vector3(x, 0f, z);
            holder.transform.localRotation = _facingAway
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;

            var view = holder.AddComponent<KartView>();
            view.ColourIndex = ColourFor(spec, placed);
            view.Kart = spec;
        }

        /// <summary>
        /// The paint a kart is shown in: one forced colour, the whole table walked
        /// across the line-up, or — the default — the family colour the rows are
        /// arranged to show off.
        /// </summary>
        private int ColourFor(KartSpecAsset spec, int placed)
        {
            if (_colourIndex >= 0) return _colourIndex;
            if (_walkColourTable) return placed % KartColorTable.Count;
            return KartGalleryLayout.ColourOf(spec.AssetName);
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

        /// <summary>
        /// The HUD's heaviest face, resolved the way the catalog is so the scene
        /// does not have to carry a reference. Left to TextMeshPro's default only
        /// if the project's own is missing.
        /// </summary>
        private TMP_FontAsset ResolveLabelFont()
        {
#if UNITY_EDITOR
            if (_labelFont == null)
            {
                _labelFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/_Project/Art/UI/Fonts/Segoe UI Black SDF.asset");
            }
#endif
            return _labelFont;
        }
    }
}
