using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>kart_demo_draw_wheel_load</c>: each suspension contact's compression
    /// over its tyre. Uncompressed is 0 and fully compressed is 1; the original
    /// rests near 0.5 on level ground.
    ///
    /// It sits directly above the speedometer, which is 94 tall on the same
    /// margin — the C code derives its own position from that, and so does this.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Wheel Load Panel")]
    public sealed class WheelLoadPanel : HudWidget
    {
        public const float PanelWidth = 238f;
        public const float PanelHeight = 118f;
        public const float Margin = 18f;

        /// <summary>The speedometer's height plus the 8 px gap between them.</summary>
        public const float SpeedometerClearance = Speedometer.PanelHeight + 8f;

        [SerializeField] private PanelBox _panel;
        [SerializeField] private WheelLoadFigure _figure;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private TextMeshProUGUI[] _wheelLabels = new TextMeshProUGUI[4];
        [SerializeField] private HudFontSet _fonts;

        protected override void Refresh()
        {
            if (Simulator == null) return;
            KartSimulationState kart = Simulator.State;
            if (kart == null) return;

            if (_figure != null)
            {
                _figure.Grounded = kart.Grounded;
                _figure.SetCompression(
                    kart.Wheels.Compression0,
                    kart.Wheels.Compression1,
                    kart.Wheels.Compression2,
                    kart.Wheels.Compression3);
            }

            if (_label != null)
            {
                _label.color = kart.Grounded
                    ? HudPalette.WheelPanelLabel
                    : HudPalette.TelemetryAlert;
            }

            for (int wheel = 0; wheel < _wheelLabels.Length && wheel < 4; ++wheel)
            {
                TextMeshProUGUI text = _wheelLabels[wheel];
                if (text == null) continue;

                float value = kart.Wheels[wheel];
                text.SetText("{0:2}", value);
                text.color = value > 0.001f
                    ? HudPalette.WheelLoadedText
                    : HudPalette.WheelIdleText;
            }
        }

        /// <summary>Applies the recovered panel geometry.</summary>
        public void ApplyLayout()
        {
            var panelRect = (RectTransform)transform;
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-Margin, Margin + SpeedometerClearance);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            if (_panel != null)
            {
                Stretch((RectTransform)_panel.transform);
                _panel.color = HudPalette.PanelFill;
                _panel.BorderColor = HudPalette.PanelBorder;
                _panel.BorderWidth = 1f;
            }

            if (_figure != null) Stretch((RectTransform)_figure.transform);

            if (_label != null)
            {
                var rect = (RectTransform)_label.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(9f, -6f);
                rect.sizeDelta = new Vector2(140f, 16f);
                _label.SetText("WHEEL LOAD");
                _label.fontSize = 12f;
                _label.fontStyle = FontStyles.Bold;
                _label.alignment = TextAlignmentOptions.TopLeft;
                if (_fonts != null) _label.font = _fonts.Ui;
            }

            for (int wheel = 0; wheel < _wheelLabels.Length && wheel < 4; ++wheel)
            {
                TextMeshProUGUI text = _wheelLabels[wheel];
                if (text == null) continue;

                Rect box = WheelLoadFigure.LabelRect(wheel, PanelWidth, PanelHeight);
                var rect = (RectTransform)text.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(box.xMin, -box.yMin);
                rect.sizeDelta = new Vector2(box.width, box.height);

                text.fontSize = 12f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = WheelLoadFigure.LabelIsRightAligned(wheel)
                    ? TextAlignmentOptions.Right
                    : TextAlignmentOptions.Left;
                if (_fonts != null) text.font = _fonts.Ui;
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
