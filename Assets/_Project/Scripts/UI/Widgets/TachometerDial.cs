using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The tachometer's face: the dial arc, the red band, the ticks, the needle,
    /// and the marker for the uncapped ramp.
    ///
    /// Ported from <c>kart_demo_draw_tachometer</c>. Every measurement is the
    /// original's — the dial runs 0.25 to 1.75, sweeping 240 degrees from 210,
    /// on a radius of 52 — and so is the reason the band starts at 1.5: the ramp
    /// climbs to about 1.75 just under speed 128 and then the cap drops it back,
    /// so the needle jumps <em>backwards</em> out of the red at that point. The
    /// dim marker is the uncapped ramp, which is what makes that visible.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Tachometer Dial")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TachometerDial : MaskableGraphic
    {
        public const float DialMin = 0.25f;
        public const float DialMax = 1.75f;
        public const float SweepStartDegrees = 210f;
        public const float SweepDegrees = 240f;
        public const float Radius = 52f;

        /// <summary>Where the red band starts: the pitch cap.</summary>
        public const float BandStart = 1.5f;

        public const int Ticks = 6;

        [SerializeField] private Color _dialColor = new Color32(96, 110, 122, 255);
        [SerializeField] private Color _tickColor = new Color32(150, 165, 180, 255);
        [SerializeField] private Color _bandColor = new Color32(215, 70, 70, 255);
        [SerializeField] private Color _needleColor = new Color32(255, 210, 90, 255);
        [SerializeField] private Color _rampColor = new Color32(130, 145, 158, 255);

        private float _pitch = DialMin;
        private float _ramp = DialMin;

        /// <summary>The needle's value, and the uncapped ramp behind it.</summary>
        public void SetReading(float pitch, float ramp)
        {
            if (Mathf.Approximately(_pitch, pitch) && Mathf.Approximately(_ramp, ramp)) return;
            _pitch = pitch;
            _ramp = ramp;
            SetVerticesDirty();
        }

        /// <summary>The dial's angle for a value, in radians.</summary>
        private static float Angle(float value)
            => (SweepStartDegrees -
                SweepDegrees * ((value - DialMin) / (DialMax - DialMin))) * Mathf.Deg2Rad;

        private static Vector2 At(Vector2 centre, float angle, float radius)
            => new Vector2(
                centre.x + Mathf.Cos(angle) * radius,
                centre.y + Mathf.Sin(angle) * radius);

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            Rect rect = GetPixelAdjustedRect();
            var centre = new Vector2(rect.center.x, rect.center.y);

            Arc(helper, centre, DialMin, DialMax, Radius, _dialColor, 2f);
            Arc(helper, centre, BandStart, DialMax, Radius, _bandColor, 4f);

            for (int tick = 0; tick <= Ticks; ++tick)
            {
                float value = DialMin + (DialMax - DialMin) * tick / Ticks;
                float angle = Angle(value);
                AddLine(
                    helper, At(centre, angle, Radius - 9f), At(centre, angle, Radius - 2f),
                    _tickColor, 1f);
            }

            // Only when it is actually ahead of the needle, which is the one place
            // it says something: past the cap.
            if (_ramp > _pitch + 0.001f)
            {
                float angle = Angle(Mathf.Min(_ramp, DialMax));
                AddLine(
                    helper, At(centre, angle, Radius - 16f), At(centre, angle, Radius - 2f),
                    _rampColor, 1f);
            }

            float needle = Angle(Mathf.Clamp(_pitch, DialMin, DialMax));
            AddLine(helper, centre, At(centre, needle, Radius - 6f), _needleColor, 3f);
        }

        /// <summary>The dial arcs, as a short run of straight segments.</summary>
        private static void Arc(
            VertexHelper helper, Vector2 centre, float from, float to,
            float radius, Color color, float thickness)
        {
            const int steps = 48;

            Vector2 previous = At(centre, Angle(from), radius);
            for (int step = 1; step <= steps; ++step)
            {
                float value = from + (to - from) * step / steps;
                Vector2 point = At(centre, Angle(value), radius);
                AddLine(helper, previous, point, color, thickness);
                previous = point;
            }
        }

        private static void AddLine(
            VertexHelper helper, Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude <= 1e-6f) return;
            direction.Normalize();
            Vector2 across = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);

            int index = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = a - across; helper.AddVert(vertex);
            vertex.position = a + across; helper.AddVert(vertex);
            vertex.position = b + across; helper.AddVert(vertex);
            vertex.position = b - across; helper.AddVert(vertex);
            helper.AddTriangle(index + 0, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 3, index + 0);
        }
    }
}
