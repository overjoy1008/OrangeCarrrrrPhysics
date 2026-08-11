using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The arrow art of <c>kart_demo_draw_axis_gizmo</c>.
    ///
    /// Each axis is supplied as its screen-space direction plus how much it points
    /// at the viewer. An axis pointing nearly straight at or away from the viewer
    /// has no useful screen direction, so it is drawn with the usual "into/out of
    /// the page" ring symbol instead of an arrow: a ring with a filled centre
    /// reads as pointing at the viewer, a plain ring as pointing away.
    ///
    /// Axes are drawn furthest-first so nearer arrows overlap.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Axis Triad")]
    // Graphic declares this too, but a component added from script does not
    // always get it, which leaves the widget with nothing to draw into.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AxisTriadGraphic : MaskableGraphic
    {
        public const float Radius = 34f;
        public const float Margin = 14f;

        /// <summary>Clears the HUD's debug lines, which the triad sits directly under.</summary>
        public const float Top = 154f;

        /// <summary>Leaves room for an arrowhead plus its label in any direction.</summary>
        public const float Span = Radius + 20f;

        public const float FlatThreshold = 0.18f;
        public const float RingRadius = 7f;
        public const float HeadLength = 9f;
        public const float HeadHalfWidth = 4f;
        public const float ShaftThickness = 2f;

        public struct Axis
        {
            /// <summary>Screen right, in units of the gizmo radius.</summary>
            public float ScreenX;
            /// <summary>Screen down.</summary>
            public float ScreenY;
            /// <summary>+1 straight at the viewer, -1 straight away.</summary>
            public float Toward;
            public Color Color;
        }

        private readonly Axis[] _axes = new Axis[3];
        private readonly int[] _order = { 0, 1, 2 };

        public static readonly Color[] AxisColors =
        {
            HudPalette.AxisX,
            HudPalette.AxisY,
            HudPalette.AxisZ,
        };

        public static readonly string[] AxisLabels = { "X", "Y", "Z" };

        public void SetAxes(Vector3 screenX, Vector3 screenY, Vector3 toward)
        {
            for (int i = 0; i < 3; ++i)
            {
                _axes[i] = new Axis
                {
                    ScreenX = screenX[i],
                    ScreenY = screenY[i],
                    Toward = toward[i],
                    Color = AxisColors[i],
                };
            }
            SortByDepth();
            SetVerticesDirty();
        }

        public Axis GetAxis(int index) => _axes[index];

        /// <summary>Where the axis label goes, in local uGUI coordinates.</summary>
        public Vector2 LabelPosition(int index)
        {
            Vector2 center = Center;
            Axis axis = _axes[index];
            float length = Mathf.Sqrt(axis.ScreenX * axis.ScreenX + axis.ScreenY * axis.ScreenY);

            if (length < FlatThreshold)
            {
                // Up and to the left, the quadrant the in-plane axes are least
                // likely to occupy.
                return new Vector2(
                    center.x - RingRadius - 12f,
                    center.y + RingRadius + 12f);
            }

            float unitX = axis.ScreenX / length;
            float unitY = axis.ScreenY / length;
            float tipX = center.x + axis.ScreenX * Radius;
            float tipY = center.y - axis.ScreenY * Radius;
            return new Vector2(tipX + unitX * 9f, tipY - unitY * 9f);
        }

        /// <summary>The triad's origin in local uGUI coordinates.</summary>
        public Vector2 Center
        {
            get
            {
                Rect rect = GetPixelAdjustedRect();
                return new Vector2(rect.xMin + Span, rect.yMax - Span);
            }
        }

        private void SortByDepth()
        {
            _order[0] = 0;
            _order[1] = 1;
            _order[2] = 2;
            for (int i = 0; i < 3; ++i)
            {
                for (int j = i + 1; j < 3; ++j)
                {
                    if (_axes[_order[j]].Toward < _axes[_order[i]].Toward)
                    {
                        (_order[i], _order[j]) = (_order[j], _order[i]);
                    }
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Vector2 center = Center;

            for (int i = 0; i < 3; ++i)
            {
                Axis axis = _axes[_order[i]];
                float length = Mathf.Sqrt(axis.ScreenX * axis.ScreenX + axis.ScreenY * axis.ScreenY);

                if (length < FlatThreshold)
                {
                    AddRing(helper, center, RingRadius, ShaftThickness, axis.Color);
                    if (axis.Toward >= 0f) AddDisc(helper, center, 3f, axis.Color);
                    continue;
                }

                float unitX = axis.ScreenX / length;
                float unitY = -axis.ScreenY / length;
                var tip = new Vector2(
                    center.x + axis.ScreenX * Radius,
                    center.y - axis.ScreenY * Radius);

                AddSegment(helper, center, tip, axis.Color, ShaftThickness);

                var direction = new Vector2(unitX, unitY);
                var side = new Vector2(-direction.y, direction.x) * HeadHalfWidth;
                Vector2 back = tip - direction * HeadLength;
                AddTriangle(helper, tip, back + side, back - side, axis.Color);
            }
        }

        private static void AddSegment(
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

        private static void AddTriangle(
            VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = a; helper.AddVert(vertex);
            vertex.position = b; helper.AddVert(vertex);
            vertex.position = c; helper.AddVert(vertex);
            helper.AddTriangle(index + 0, index + 1, index + 2);
        }

        private static void AddRing(
            VertexHelper helper, Vector2 center, float radius, float thickness, Color color)
        {
            const int segments = 24;
            float inner = radius - thickness * 0.5f;
            float outer = radius + thickness * 0.5f;

            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            for (int i = 0; i <= segments; ++i)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertex.position = center + direction * inner; helper.AddVert(vertex);
                vertex.position = center + direction * outer; helper.AddVert(vertex);
            }
            for (int i = 0; i < segments; ++i)
            {
                int a = index + i * 2;
                helper.AddTriangle(a, a + 1, a + 3);
                helper.AddTriangle(a + 3, a + 2, a);
            }
        }

        private static void AddDisc(VertexHelper helper, Vector2 center, float radius, Color color)
        {
            const int segments = 16;
            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = center;
            helper.AddVert(vertex);
            for (int i = 0; i <= segments; ++i)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                vertex.position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                helper.AddVert(vertex);
            }
            for (int i = 0; i < segments; ++i)
            {
                helper.AddTriangle(index, index + 1 + i, index + 2 + i);
            }
        }
    }
}
