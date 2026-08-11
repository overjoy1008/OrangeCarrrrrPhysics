using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The world X/Y/Z triad under the debug lines.
    ///
    /// The arrows always show the world frame, never the kart's body axes, so the
    /// widget tells you how the world is oriented from the current viewpoint — in
    /// the chase view it turns as the camera yaws, and in the top-down view Z is
    /// always the "out of the page" ring.
    ///
    /// The projection is <c>draw_scene</c>'s: each world axis is dotted against
    /// the camera basis, with screen Y and the camera's forward negated because
    /// screen Y grows downward and forward points into the screen.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/World Axis Gizmo")]
    public sealed class WorldAxisGizmo : HudWidget
    {
        /// <summary>
        /// Unity's world axes, not the engine's. The simulation still works in a
        /// Z-up frame, but the widget is there to tell you which way the scene is
        /// facing while you look at it in Unity, so it reads X right, Y up,
        /// Z forward like every other gizmo in the editor.
        /// </summary>
        private static readonly Vector3[] WorldAxes =
        {
            Vector3.right,
            Vector3.up,
            Vector3.forward,
        };

        [SerializeField] private AxisTriadGraphic _triad;
        [SerializeField] private TextMeshProUGUI[] _axisLabels = new TextMeshProUGUI[3];
        [SerializeField] private TextMeshProUGUI _worldLabel;
        [SerializeField] private HudFontSet _fonts;

        protected override void Refresh()
        {
            if (Simulator == null || _triad == null) return;

            Camera camera = Simulator.ActiveCamera;
            if (camera == null) return;

            Transform view = camera.transform;

            Vector3 screenX = default;
            Vector3 screenY = default;
            Vector3 toward = default;
            for (int axis = 0; axis < 3; ++axis)
            {
                // Screen Y grows downward and the camera's forward points into
                // the screen, so both are negated to get screen directions.
                screenX[axis] = Vector3.Dot(WorldAxes[axis], view.right);
                screenY[axis] = -Vector3.Dot(WorldAxes[axis], view.up);
                toward[axis] = -Vector3.Dot(WorldAxes[axis], view.forward);
            }

            _triad.SetAxes(screenX, screenY, toward);

            for (int axis = 0; axis < _axisLabels.Length && axis < 3; ++axis)
            {
                TextMeshProUGUI label = _axisLabels[axis];
                if (label == null) continue;
                ((RectTransform)label.transform).anchoredPosition = _triad.LabelPosition(axis);
            }
        }

        /// <summary>Applies the recovered widget geometry.</summary>
        public void ApplyLayout()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(AxisTriadGraphic.Margin, -AxisTriadGraphic.Top);
            rect.sizeDelta = new Vector2(AxisTriadGraphic.Span * 2f, AxisTriadGraphic.Span * 2f);

            if (_triad != null)
            {
                var triadRect = (RectTransform)_triad.transform;
                triadRect.anchorMin = Vector2.zero;
                triadRect.anchorMax = Vector2.one;
                triadRect.offsetMin = Vector2.zero;
                triadRect.offsetMax = Vector2.zero;
            }

            for (int axis = 0; axis < _axisLabels.Length && axis < 3; ++axis)
            {
                TextMeshProUGUI label = _axisLabels[axis];
                if (label == null) continue;

                // Anchored to the triad's centre, which is where AxisTriadGraphic
                // draws from, so LabelPosition can be used verbatim.
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = new Vector2(0.5f, 0.5f);
                labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.sizeDelta = new Vector2(16f, 16f);

                label.SetText(AxisTriadGraphic.AxisLabels[axis]);
                label.color = AxisTriadGraphic.AxisColors[axis];
                label.fontSize = 12f;
                label.alignment = TextAlignmentOptions.Center;
                if (_fonts != null) label.font = _fonts.Mono;
            }

            if (_worldLabel != null)
            {
                // Above the triad: the downward arrow would otherwise run into it.
                var worldRect = (RectTransform)_worldLabel.transform;
                worldRect.anchorMin = new Vector2(0f, 1f);
                worldRect.anchorMax = new Vector2(0f, 1f);
                worldRect.pivot = new Vector2(0f, 1f);
                // center.y - span - 4 in client pixels, which is 4 px above the
                // widget's own top edge.
                worldRect.anchoredPosition = new Vector2(0f, 4f);
                worldRect.sizeDelta = new Vector2(80f, 16f);

                _worldLabel.SetText("WORLD");
                _worldLabel.color = HudPalette.AxisLabel;
                _worldLabel.fontSize = 12f;
                _worldLabel.alignment = TextAlignmentOptions.TopLeft;
                if (_fonts != null) _worldLabel.font = _fonts.Mono;
            }
        }
    }
}
