using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The oriented triangle that marks the kart on the minimap, and the grid the
    /// panel falls back to when a track has no authored artwork.
    ///
    /// The marker's vertex list and its 0.5 scale are the original's.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Minimap Marker")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapMarker : MaskableGraphic
    {
        [SerializeField] private Color _markerColor = new Color32(255, 90, 160, 255);
        [SerializeField] private Color _gridColor = new Color32(86, 98, 108, 255);
        [SerializeField] private Color _boundsColor = new Color32(75, 220, 255, 255);

        [Tooltip("Draw the quarter grid. Off when the track supplies a minimap image.")]
        [SerializeField] private bool _showGrid;

        private Vector2 _normalized = new Vector2(0.5f, 0.5f);
        private Vector2 _heading = new Vector2(0f, -1f);

        /// <summary>
        /// The three corners in panel space when the rotating map is on, where
        /// the marker is not a rotated triangle of fixed size but the projection
        /// of one through the same camera the map is drawn with.
        /// </summary>
        private readonly Vector2[] _corners = new Vector2[3];

        private bool _projected;

        public bool ShowGrid
        {
            get => _showGrid;
            set
            {
                if (_showGrid == value) return;
                _showGrid = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Places the marker. <paramref name="normalized"/> has 0,0 at the panel's
        /// top-left; <paramref name="heading"/> is a unit vector in the same
        /// space, so its y grows downward.
        /// </summary>
        public void SetKart(Vector2 normalized, Vector2 heading)
        {
            if (!_projected && _normalized == normalized && _heading == heading) return;
            _projected = false;
            _normalized = normalized;
            _heading = heading;
            SetVerticesDirty();
        }

        /// <summary>
        /// Places the marker as three already-projected corners, in the same
        /// top-left-origin panel space <see cref="SetKart"/> takes.
        /// </summary>
        public void SetKartCorners(Vector2 a, Vector2 b, Vector2 c)
        {
            if (_projected && _corners[0] == a && _corners[1] == b && _corners[2] == c) return;
            _projected = true;
            _corners[0] = a;
            _corners[1] = b;
            _corners[2] = c;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect rect = GetPixelAdjustedRect();

            if (_showGrid)
            {
                for (int division = 1; division < 4; ++division)
                {
                    float x = rect.xMin + rect.width * division / 4f;
                    float y = rect.yMin + rect.height * division / 4f;
                    AddLine(helper, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax),
                        _gridColor, 1f);
                    AddLine(helper, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y),
                        _gridColor, 1f);
                }
            }

            // The cyan boundary the original draws around the map area.
            AddLine(helper, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), _boundsColor, 3f);
            AddLine(helper, new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), _boundsColor, 3f);
            AddLine(helper, new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), _boundsColor, 3f);
            AddLine(helper, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), _boundsColor, 3f);

            // Panel space has y growing downward; uGUI's grows up.
            var centre = new Vector2(
                rect.xMin + _normalized.x * rect.width,
                rect.yMax - _normalized.y * rect.height);

            var forward = new Vector2(_heading.x, -_heading.y);
            var right = new Vector2(forward.y, -forward.x);

            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = _markerColor;
            for (int corner = 0; corner < 3; ++corner)
            {
                if (_projected)
                {
                    vertex.position = new Vector2(
                        rect.xMin + _corners[corner].x * rect.width,
                        rect.yMax - _corners[corner].y * rect.height);
                    helper.AddVert(vertex);
                    continue;
                }

                float across = KartMinimap.MarkerVertices[corner, 0] * KartMinimap.MarkerScale;
                float along = KartMinimap.MarkerVertices[corner, 1] * KartMinimap.MarkerScale;
                vertex.position = centre + right * across + forward * along;
                helper.AddVert(vertex);
            }
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 1, index);
        }

        private static void AddLine(
            VertexHelper helper, Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude <= 1e-6f) return;
            direction.Normalize();
            var perpendicular = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);

            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = a - perpendicular; helper.AddVert(vertex);
            vertex.position = a + perpendicular; helper.AddVert(vertex);
            vertex.position = b + perpendicular; helper.AddVert(vertex);
            vertex.position = b - perpendicular; helper.AddVert(vertex);
            helper.AddTriangle(index + 0, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 3, index + 0);
        }
    }
}
