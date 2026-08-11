using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>kart_demo_draw_speedometer</c>: the large lower-right km/h read-out.
    ///
    /// The digits are the original's own conversion — <c>|v| * 3.6</c> rounded the
    /// way its x87 store rounds — printed as three digits with leading zeros, so a
    /// stationary kart reads 000 rather than 0.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Speedometer")]
    public sealed class Speedometer : HudWidget
    {
        public const float PanelWidth = 238f;
        public const float PanelHeight = 94f;
        public const float Margin = 18f;
        public const float BorderWidth = 3f;

        [SerializeField] private PanelBox _panel;
        [SerializeField] private TextMeshProUGUI _digits;
        [SerializeField] private TextMeshProUGUI _unit;
        [SerializeField] private HudFontSet _fonts;

        private int _shownKmh = -1;
        private bool _shownBoost;

        /// <summary>Whether either boost is running, which recolours the panel.</summary>
        public bool BoostActive { get; private set; }

        protected override void Refresh()
        {
            if (Simulator == null) return;
            KartSimulationState kart = Simulator.State;
            if (kart == null) return;

            BoostActive = KartDynamics.AnyBoostActive(kart.TimedBoost, kart.InstantBoost);

            int kmh = KartUnits.SpeedometerKmh(kart.LinearVelocity);
            if (kmh == _shownKmh && BoostActive == _shownBoost) return;

            _shownKmh = kmh;
            _shownBoost = BoostActive;

            if (_digits != null)
            {
                _digits.SetText("{0:000}", kmh);
                _digits.color = BoostActive
                    ? HudPalette.SpeedometerBoostDigits
                    : HudPalette.SpeedometerDigits;
            }
            if (_panel != null)
            {
                _panel.BorderColor = BoostActive
                    ? HudPalette.StatusBoost
                    : HudPalette.SpeedometerBorder;
            }
        }

        /// <summary>Applies the recovered panel and text-rect geometry.</summary>
        public void ApplyLayout()
        {
            var panelRect = (RectTransform)transform;
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-Margin, Margin);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            if (_panel != null)
            {
                var rect = (RectTransform)_panel.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _panel.color = HudPalette.PanelFill;
                _panel.BorderColor = HudPalette.SpeedometerBorder;
                _panel.BorderWidth = BorderWidth;
            }

            // digits_rect: panel.left + 8 .. panel.right - 65, panel.top + 4 .. panel.bottom - 5.
            PlaceFromPanelRect(_digits, 8f, 4f, PanelWidth - 65f, PanelHeight - 5f);
            if (_digits != null)
            {
                _digits.fontSize = 58f;

                // No bold style: the original asks GDI for Segoe UI at FW_HEAVY,
                // which picks the Black face, and that face is what UiHeavy
                // already is. Adding Bold on top of it makes TMP synthesise a
                // second weight by dilating the SDF, which thickens the strokes
                // and spaces the digits wider than the original's.
                _digits.fontStyle = FontStyles.Normal;
                _digits.alignment = TextAlignmentOptions.Right;
                _digits.textWrappingMode = TextWrappingModes.NoWrap;
                if (_fonts != null) _digits.font = _fonts.UiHeavy;
            }

            // unit_rect: panel.right - 68 .. panel.right - 10, panel.top + 42 .. panel.bottom - 8.
            PlaceFromPanelRect(_unit, PanelWidth - 68f, 42f, PanelWidth - 10f, PanelHeight - 8f);
            if (_unit != null)
            {
                _unit.SetText("KM/H");
                _unit.fontSize = 18f;

                // Likewise: FW_BOLD in the original is the Bold face, which Ui is.
                _unit.fontStyle = FontStyles.Normal;
                _unit.alignment = TextAlignmentOptions.Center;
                _unit.color = HudPalette.SpeedometerUnit;
                if (_fonts != null) _unit.font = _fonts.Ui;
            }
        }

        /// <summary>
        /// Places a child from a GDI-style rect in panel pixels, where y grows
        /// downward from the panel's top edge.
        /// </summary>
        private static void PlaceFromPanelRect(
            TMP_Text text, float left, float top, float right, float bottom)
        {
            if (text == null) return;
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(right - left, bottom - top);
        }
    }
}
