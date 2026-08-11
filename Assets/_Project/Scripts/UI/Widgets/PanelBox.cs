using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// GDI's <c>Rectangle(dc, ...)</c> as a uGUI graphic: the interior is filled
    /// with the brush colour and the boundary is stroked with a pen of a given
    /// width, centred on the edge the way GDI centres it.
    ///
    /// One graphic rather than a filled Image plus four border Images, because
    /// the HUD has several of these and each extra Image is another batch break.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Panel Box")]
    // Graphic declares this too, but a component added from script does not
    // always get it, which leaves the widget with nothing to draw into.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PanelBox : MaskableGraphic
    {
        [SerializeField] private Color _borderColor = HudPalette.PanelBorder;
        [SerializeField, Min(0f)] private float _borderWidth = 1f;

        [Tooltip("Fill only this many pixels down from the top. 0 fills the whole box.")]
        [SerializeField, Min(0f)] private float _fillHeight;

        public Color BorderColor
        {
            get => _borderColor;
            set
            {
                if (_borderColor == value) return;
                _borderColor = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Pixels of fill measured down from the top edge. Zero fills the box,
        /// which is what every panel but the minimap's wants.
        /// </summary>
        public float FillHeight
        {
            get => _fillHeight;
            set
            {
                if (Mathf.Approximately(_fillHeight, value)) return;
                _fillHeight = value;
                SetVerticesDirty();
            }
        }

        public float BorderWidth
        {
            get => _borderWidth;
            set
            {
                if (Mathf.Approximately(_borderWidth, value)) return;
                _borderWidth = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            Rect rect = GetPixelAdjustedRect();

            // The fill can stop short of the bottom, which is what the minimap
            // panel does on a track that has artwork: only the header strip is
            // filled and the map area is left clear for the scene to show
            // through. Everything else fills the whole box.
            Rect fill = _fillHeight > 0f && _fillHeight < rect.height
                ? Rect.MinMaxRect(rect.xMin, rect.yMax - _fillHeight, rect.xMax, rect.yMax)
                : rect;
            AddQuad(helper, fill, color);

            if (_borderWidth <= 0f) return;

            float half = _borderWidth * 0.5f;
            Rect outer = Rect.MinMaxRect(
                rect.xMin - half, rect.yMin - half, rect.xMax + half, rect.yMax + half);
            Rect inner = Rect.MinMaxRect(
                rect.xMin + half, rect.yMin + half, rect.xMax - half, rect.yMax - half);

            if (inner.width <= 0f || inner.height <= 0f)
            {
                AddQuad(helper, outer, _borderColor);
                return;
            }

            // Top, bottom, left, right of the ring between outer and inner.
            AddQuad(helper, Rect.MinMaxRect(outer.xMin, inner.yMax, outer.xMax, outer.yMax), _borderColor);
            AddQuad(helper, Rect.MinMaxRect(outer.xMin, outer.yMin, outer.xMax, inner.yMin), _borderColor);
            AddQuad(helper, Rect.MinMaxRect(outer.xMin, inner.yMin, inner.xMin, inner.yMax), _borderColor);
            AddQuad(helper, Rect.MinMaxRect(inner.xMax, inner.yMin, outer.xMax, inner.yMax), _borderColor);
        }

        internal static void AddQuad(VertexHelper helper, Rect rect, Color color)
        {
            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = new Vector3(rect.xMin, rect.yMin);
            helper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMin, rect.yMax);
            helper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMax);
            helper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMin);
            helper.AddVert(vertex);

            helper.AddTriangle(index + 0, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 3, index + 0);
        }
    }
}
