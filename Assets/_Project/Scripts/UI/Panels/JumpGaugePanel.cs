using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>kart_demo_draw_jump_gauge</c>: the jump's timing bar, shown only while
    /// the key is held.
    ///
    /// A marker sweeps left to right and back while the spring winds up, and the
    /// green block at the right end is where releasing is worth full power. The
    /// measurements are the original's: 300 x 16, centred, 72 px up from the
    /// bottom, a 26-wide target inset 2 px, and the label sitting 17 px above the
    /// bar.
    ///
    /// It is hidden outside the crouch rather than left empty, which is what the
    /// original does — a bar on screen while nothing is charging would read as a
    /// gauge stuck at zero.
    ///
    /// Unlike <see cref="GaugePanel"/> this one is not a bench readout: the jump
    /// and its gauge are both recovered, so what is drawn here is the 2004
    /// mechanic rather than a hypothesis about it.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Jump Gauge Panel")]
    public sealed class JumpGaugePanel : HudWidget
    {
        public const float BarWidth = 300f;
        public const float BarHeight = 16f;

        /// <summary>Up from the bottom of the client rect to the bar's top edge.</summary>
        public const float BottomOffset = 72f;

        public const float Inset = 2f;

        /// <summary>The full-power block at the right end.</summary>
        public const float TargetWidth = 26f;

        public const float MarkerWidth = 2f;

        /// <summary>How far the marker line stands out above and below the bar.</summary>
        public const float MarkerOverhang = 3f;

        public const float LabelOffset = 17f;

        private PanelBox _bar;
        private PanelBox _target;
        private PanelBox _marker;
        private RectTransform _markerRect;
        private TextMeshProUGUI _label;

        private float _shownPosition = -1f;
        private float _shownStrength = -1f;
        private bool _shownActive;

        protected override void Refresh()
        {
            if (Simulator == null) return;

            KartSimulationState kart = Simulator.State;
            if (kart == null) return;

            Build();

            bool active = kart.Jump.Phase == KartJumpPhase.Crouch;
            if (active != _shownActive)
            {
                _shownActive = active;
                _bar.enabled = active;
                _target.enabled = active;
                _marker.enabled = active;
                _label.enabled = active;
            }
            if (!active) return;

            float position = Mathf.Clamp01(kart.Jump.GaugePosition);
            if (!Mathf.Approximately(position, _shownPosition))
            {
                _shownPosition = position;
                _markerRect.anchoredPosition = new Vector2(
                    Inset + (BarWidth - Inset * 2f) * position, 0f);
            }

            float strength = kart.Jump.JumpStrength;
            if (!Mathf.Approximately(strength, _shownStrength))
            {
                _shownStrength = strength;
                _label.SetText($"CAT JUMP  release at end  {strength * 100f:0}%");
            }
        }

        private void Build()
        {
            if (_bar != null) return;

            var self = (RectTransform)transform;
            self.anchorMin = Vector2.zero;
            self.anchorMax = Vector2.one;
            self.offsetMin = Vector2.zero;
            self.offsetMax = Vector2.zero;

            _bar = AddBox("Bar", HudPalette.JumpGaugeBack, HudPalette.JumpGaugeEdge, 1f);
            var barRect = (RectTransform)_bar.transform;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, BottomOffset);
            barRect.sizeDelta = new Vector2(BarWidth, BarHeight);

            // Pinned to the right edge inside the bar: this is the window the
            // gauge is swept towards, so it belongs to the bar and not the screen.
            _target = AddBox("Target", HudPalette.JumpGaugeTarget, Color.clear, 0f);
            var targetRect = (RectTransform)_target.transform;
            targetRect.SetParent(barRect, worldPositionStays: false);
            targetRect.anchorMin = new Vector2(1f, 0f);
            targetRect.anchorMax = new Vector2(1f, 1f);
            targetRect.pivot = new Vector2(1f, 0.5f);
            targetRect.anchoredPosition = new Vector2(-Inset, 0f);
            targetRect.sizeDelta = new Vector2(TargetWidth, -Inset * 2f);

            // Anchored to the bar's left edge and moved by its X, so the sweep is
            // one number changing and the overhang comes from the height.
            _marker = AddBox("Marker", HudPalette.JumpGaugeMarker, Color.clear, 0f);
            _markerRect = (RectTransform)_marker.transform;
            _markerRect.SetParent(barRect, worldPositionStays: false);
            _markerRect.anchorMin = new Vector2(0f, 0f);
            _markerRect.anchorMax = new Vector2(0f, 1f);
            _markerRect.pivot = new Vector2(0.5f, 0.5f);
            _markerRect.anchoredPosition = new Vector2(Inset, 0f);
            _markerRect.sizeDelta = new Vector2(MarkerWidth, MarkerOverhang * 2f);

            _label = AddLabel("Label", HudPalette.JumpGaugeLabel);
            var labelRect = (RectTransform)_label.transform;
            labelRect.SetParent(barRect, worldPositionStays: false);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, LabelOffset - BarHeight);
            labelRect.sizeDelta = new Vector2(BarWidth, LabelOffset);
        }

        private PanelBox AddBox(string name, Color fill, Color border, float borderWidth)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(transform, worldPositionStays: false);

            var box = holder.AddComponent<PanelBox>();
            box.color = fill;
            box.BorderColor = border;
            box.BorderWidth = borderWidth;
            box.raycastTarget = false;
            return box;
        }

        private TextMeshProUGUI AddLabel(string name, Color color)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(transform, worldPositionStays: false);

            var label = holder.AddComponent<TextMeshProUGUI>();

            // The HUD's own face, taken from a label already on the canvas so the
            // Korean fallback and the look come along with it.
            Canvas canvas = GetComponentInParent<Canvas>();
            TextMeshProUGUI sample = canvas != null
                ? canvas.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            if (sample != null) label.font = sample.font;

            label.fontSize = 12f;
            label.color = color;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.raycastTarget = false;
            return label;
        }
    }
}
