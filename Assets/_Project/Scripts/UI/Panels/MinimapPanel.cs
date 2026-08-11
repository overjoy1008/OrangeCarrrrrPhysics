using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>draw_track_minimap</c>: the top-right TRACK MAP panel.
    ///
    /// The map area is the track's own <c>xt_minimap.png</c> where the archive
    /// has one, and a quarter grid where it does not — which is the case for the
    /// synthetic flat track. Either way the kart is an oriented triangle at its
    /// normalized position inside the cyan boundary.
    ///
    /// The original also runs a rotating camera over the artwork on tracks that
    /// have a per-track mapping for it. That is not ported: this draws the map
    /// square-on, which is what the fallback path does and what makes the marker
    /// readable.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Minimap Panel")]
    public sealed class MinimapPanel : HudWidget
    {
        public const float PanelWidth = 220f;
        public const float PanelHeight = 248f;
        public const float Margin = 16f;

        /// <summary>The image rect the C code insets inside the panel.</summary>
        public const float ImageInsetX = 15f;
        public const float ImageTop = 42f;
        public const float ImageBottom = 16f;

        [SerializeField] private PanelBox _panel;
        [SerializeField] private RawImage _image;
        [SerializeField] private MinimapMarker _marker;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private TextMeshProUGUI _kartLabel;
        [SerializeField] private HudFontSet _fonts;

        private string _shownKart;

        /// <summary>
        /// The artwork currently on the panel. Held so the texture is only pushed
        /// when the track actually changes, the way <c>_shownKart</c> is.
        /// </summary>
        private Texture2D _shownMinimap;

        private bool _mapApplied;

        protected override void Refresh()
        {
            if (Simulator == null || _marker == null) return;

            KartSimulationState kart = Simulator.State;
            TrackSpecAsset trackAsset = Simulator.Track;
            if (kart == null || trackAsset == null) return;

            ShowMap(trackAsset.Minimap);

            TrackSpec track = trackAsset.ToSpec();
            KartMinimap.NormalizedPoint(track, kart.Position, out float x, out float y);

            kart.GetBodyAxes(out _, out KartVec3 forward, out _);
            KartMinimap.MarkerHeading(forward, out float headingX, out float headingY);

            _marker.SetKart(new Vector2(x, y), new Vector2(headingX, headingY));

            KartSpecAsset kartSpec = Simulator.Kart;
            if (_kartLabel != null && kartSpec != null && _shownKart != kartSpec.AssetName)
            {
                _shownKart = kartSpec.AssetName;
                _kartLabel.SetText(
                    $"KART {kartSpec.AssetName}  {kartSpec.Width:F3} x {kartSpec.Length:F3}");
            }
        }

        /// <summary>Applies the recovered panel geometry.</summary>
        public void ApplyLayout()
        {
            var panelRect = (RectTransform)transform;
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-Margin, -Margin);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            if (_panel != null)
            {
                Stretch((RectTransform)_panel.transform);
                _panel.color = HudPalette.TelemetryPanelFill;
                _panel.BorderColor = HudPalette.TelemetryPanelBorder;
                _panel.BorderWidth = 1f;
            }

            // image_rect: panel.left + 15 .. panel.right - 15,
            //             panel.top + 42 .. panel.bottom - 16.
            PlaceImageRect(_image != null ? (RectTransform)_image.transform : null);
            PlaceImageRect(_marker != null ? (RectTransform)_marker.transform : null);

            if (_image != null)
            {
                _image.color = Color.white;
                _image.raycastTarget = false;
            }

            // The artwork itself is the track's, not the layout's, so re-applying
            // the layout must not decide what the panel shows.
            _mapApplied = false;
            ShowMap(Simulator != null && Simulator.Track != null ? Simulator.Track.Minimap : null);

            PlaceLabel(_label, 6f, "TRACK MAP");
            PlaceLabel(_kartLabel, 22f, null);
        }

        /// <summary>
        /// Puts the track's own map on the panel, falling back to the marker's
        /// quarter grid where the archive has no artwork — which is the case for
        /// the synthetic flat track.
        /// </summary>
        private void ShowMap(Texture2D minimap)
        {
            if (_mapApplied && _shownMinimap == minimap) return;
            _shownMinimap = minimap;
            _mapApplied = true;

            bool hasArtwork = minimap != null;
            if (_image != null)
            {
                _image.texture = minimap;
                _image.enabled = hasArtwork;
            }
            if (_marker != null) _marker.ShowGrid = !hasArtwork;
        }

        private static void PlaceImageRect(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(ImageInsetX, -ImageTop);
            rect.sizeDelta = new Vector2(
                PanelWidth - ImageInsetX * 2f,
                PanelHeight - ImageTop - ImageBottom);
        }

        private void PlaceLabel(TextMeshProUGUI text, float top, string content)
        {
            if (text == null) return;

            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -top);
            rect.sizeDelta = new Vector2(PanelWidth - 16f, 16f);

            text.fontSize = 12f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = HudPalette.WheelPanelLabel;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            if (content != null) text.SetText(content);
            if (_fonts != null && _fonts.Mono != null) text.font = _fonts.Mono;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
