using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The kart figure inside the wheel-load panel: a body rectangle with a nose
    /// mark so it reads front-up, and one rectangle per wheel shaded by that
    /// wheel's suspension compression.
    ///
    /// All of it is one graphic. The geometry constants are
    /// <c>kart_demo_draw_wheel_load</c>'s, expressed in the panel's own top-left
    /// pixel space and flipped into uGUI's bottom-up space when the quads are
    /// emitted.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Wheel Load Figure")]
    // Graphic declares this too, but a component added from script does not
    // always get it, which leaves the widget with nothing to draw into.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WheelLoadFigure : MaskableGraphic
    {
        public const float BodyHalfWidth = 26f;
        public const float BodyTop = 30f;
        public const float BodyBottomInset = 12f;
        public const float WheelWidth = 11f;
        public const float WheelHeight = 20f;
        public const float NoseHalfWidth = 8f;
        public const float NoseDrop = 7f;
        public const float NoseRise = 1f;
        public const float NoseThickness = 2f;

        /// <summary>Screen offsets per wheel, in the order the state stores them.</summary>
        private static readonly int[] WheelSide = { 1, -1, 1, -1 };
        private static readonly int[] WheelEnd = { 0, 0, 1, 1 };

        private readonly float[] _compression = new float[4];

        [SerializeField] private bool _grounded = true;

        public bool Grounded
        {
            get => _grounded;
            set
            {
                if (_grounded == value) return;
                _grounded = value;
                SetVerticesDirty();
            }
        }

        public void SetCompression(float wheel0, float wheel1, float wheel2, float wheel3)
        {
            if (Mathf.Approximately(_compression[0], wheel0) &&
                Mathf.Approximately(_compression[1], wheel1) &&
                Mathf.Approximately(_compression[2], wheel2) &&
                Mathf.Approximately(_compression[3], wheel3))
            {
                return;
            }
            _compression[0] = wheel0;
            _compression[1] = wheel1;
            _compression[2] = wheel2;
            _compression[3] = wheel3;
            SetVerticesDirty();
        }

        public float GetCompression(int wheel) => _compression[wheel];

        /// <summary>Where wheel <paramref name="wheel"/>'s label sits, in panel pixels.</summary>
        public static Rect LabelRect(int wheel, float panelWidth, float panelHeight)
        {
            float centerX = panelWidth * 0.5f;
            float y = WheelTop(wheel, panelHeight) - 1f;
            bool left = WheelSide[wheel] < 0;
            float minX = left ? 6f : centerX + 30f;
            float maxX = left ? centerX - 30f : panelWidth - 6f;
            return Rect.MinMaxRect(minX, y, maxX, y + WheelHeight);
        }

        public static bool LabelIsRightAligned(int wheel) => WheelSide[wheel] < 0;

        private static float WheelTop(int wheel, float panelHeight)
        {
            float bodyBottom = panelHeight - BodyBottomInset;
            return WheelEnd[wheel] == 0
                ? BodyTop + 6f
                : bodyBottom - 6f - WheelHeight;
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            Rect rect = GetPixelAdjustedRect();
            float width = rect.width;
            float height = rect.height;
            float centerX = width * 0.5f;
            float bodyBottom = height - BodyBottomInset;

            // Body.
            AddPanelQuad(helper, rect,
                centerX - BodyHalfWidth, BodyTop, centerX + BodyHalfWidth, bodyBottom,
                HudPalette.WheelBodyFill);
            AddPanelOutline(helper, rect,
                centerX - BodyHalfWidth, BodyTop, centerX + BodyHalfWidth, bodyBottom,
                HudPalette.WheelBodyBorder, 1f);

            // The nose mark, as two thick segments.
            AddPanelSegment(helper, rect,
                new Vector2(centerX - NoseHalfWidth, BodyTop + NoseDrop),
                new Vector2(centerX, BodyTop + NoseRise),
                HudPalette.WheelBodyBorder, NoseThickness);
            AddPanelSegment(helper, rect,
                new Vector2(centerX, BodyTop + NoseRise),
                new Vector2(centerX + NoseHalfWidth, BodyTop + NoseDrop),
                HudPalette.WheelBodyBorder, NoseThickness);

            // Wheels.
            for (int wheel = 0; wheel < 4; ++wheel)
            {
                float value = _compression[wheel];
                float x = centerX + WheelSide[wheel] * (BodyHalfWidth + 3f) -
                          (WheelSide[wheel] < 0 ? WheelWidth : 0f);
                float y = WheelTop(wheel, height);

                Color fill = value > 0.001f
                    ? HudPalette.WheelLoadFill(value)
                    : HudPalette.WheelIdleFill;

                AddPanelQuad(helper, rect, x, y, x + WheelWidth, y + WheelHeight, fill);
                AddPanelOutline(helper, rect, x, y, x + WheelWidth, y + WheelHeight,
                    HudPalette.WheelBodyBorder, 1f);
            }
        }

        /// <summary>Panel pixel space (top-left origin, y down) to uGUI local space.</summary>
        private static Vector2 ToLocal(Rect rect, float x, float y)
            => new Vector2(rect.xMin + x, rect.yMax - y);

        private static void AddPanelQuad(
            VertexHelper helper, Rect rect,
            float left, float top, float right, float bottom, Color color)
        {
            Vector2 a = ToLocal(rect, left, bottom);
            Vector2 b = ToLocal(rect, right, top);
            PanelBox.AddQuad(helper, Rect.MinMaxRect(a.x, a.y, b.x, b.y), color);
        }

        private static void AddPanelOutline(
            VertexHelper helper, Rect rect,
            float left, float top, float right, float bottom, Color color, float thickness)
        {
            AddPanelQuad(helper, rect, left, top, right, top + thickness, color);
            AddPanelQuad(helper, rect, left, bottom - thickness, right, bottom, color);
            AddPanelQuad(helper, rect, left, top, left + thickness, bottom, color);
            AddPanelQuad(helper, rect, right - thickness, top, right, bottom, color);
        }

        private static void AddPanelSegment(
            VertexHelper helper, Rect rect, Vector2 from, Vector2 to, Color color, float thickness)
        {
            Vector2 a = ToLocal(rect, from.x, from.y);
            Vector2 b = ToLocal(rect, to.x, to.y);
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
