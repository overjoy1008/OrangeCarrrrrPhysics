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
            if (!StepRotatingMap(track, kart))
            {
                KartMinimap.NormalizedPoint(track, kart.Position, out float x, out float y);

                kart.GetBodyAxes(out _, out KartVec3 forward, out _);
                KartMinimap.MarkerHeading(forward, out float headingX, out float headingY);

                _marker.SetKart(new Vector2(x, y), new Vector2(headingX, headingY));
            }

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
        /// Draws the map the way the original does: as the ground plane under a
        /// camera that hangs behind the kart and swings round with it.
        ///
        /// Returns false for a track with no <c>ToMinimap</c> mapping — only the
        /// synthetic flat one — which leaves the caller on the square-on path.
        ///
        /// The projection itself is per-pixel and lives in the shader; what is
        /// computed here is the camera it needs, and the marker, which has to go
        /// through the same camera or it would sit somewhere the map is not.
        /// </summary>
        private bool StepRotatingMap(TrackSpec track, KartSimulationState kart)
        {
            if (_image == null || _shownMinimap == null) return false;

            KartMinimapMapping mapping = KartDemoData.FindMinimapMapping(track.AssetName);
            if (mapping == null) return false;

            Material material = RotatingMaterial();
            if (material == null) return false;

            _camera.Step(track, mapping, kart.Position, kart.Orientation, Simulator.RaceClockMs);

            material.SetVector(MapSizeId, new Vector4(mapping.Width, mapping.Height, 0f, 0f));
            material.SetVector(CameraPositionId, ToVector(_camera.Position));
            material.SetVector(CameraRightId, ToVector(_camera.Right));
            material.SetVector(CameraBackId, ToVector(_camera.Back));
            material.SetVector(CameraUpId, ToVector(_camera.Up));

            if (_image.material != material) _image.material = material;

            // The original alpha-blends the whole projected map at 0.3, which is
            // the map object's own TexProperty alpha rather than a HUD choice.
            _image.color = new Color(1f, 1f, 1f, KartMinimapCamera.Alpha);

            _camera.ProjectMarker(_markerCorners);
            _marker.SetKartCorners(
                new Vector2(_markerCorners[0, 0], _markerCorners[0, 1]),
                new Vector2(_markerCorners[1, 0], _markerCorners[1, 1]),
                new Vector2(_markerCorners[2, 0], _markerCorners[2, 1]));
            return true;
        }

        private readonly KartMinimapCamera _camera = new KartMinimapCamera();
        private readonly float[,] _markerCorners = new float[3, 2];
        private Material _rotatingMaterial;

        private static readonly int MapSizeId = Shader.PropertyToID("_MapSize");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int CameraRightId = Shader.PropertyToID("_CameraRight");
        private static readonly int CameraBackId = Shader.PropertyToID("_CameraBack");
        private static readonly int CameraUpId = Shader.PropertyToID("_CameraUp");

        private static Vector4 ToVector(in KartVec3 value)
            => new Vector4(value.X, value.Y, value.Z, 0f);

        /// <summary>
        /// The projection material, made on first use. One per panel rather than
        /// shared, because the camera uniforms are this panel's own.
        /// </summary>
        private Material RotatingMaterial()
        {
            if (_rotatingMaterial != null) return _rotatingMaterial;

            var shader = Shader.Find(RotatingShader);
            if (shader == null)
            {
                Debug.LogWarning($"No {RotatingShader}; the map stays square-on.", this);
                return null;
            }

            _rotatingMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            return _rotatingMaterial;
        }

        private const string RotatingShader = "OrangeCarrrrr/Minimap3D";

        protected override void OnDisable()
        {
            base.OnDisable();
            _camera.Reset();
        }

        private void OnDestroy()
        {
            if (_rotatingMaterial == null) return;
            if (Application.isPlaying) Destroy(_rotatingMaterial);
            else DestroyImmediate(_rotatingMaterial);
            _rotatingMaterial = null;
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

            // With artwork the original fills only the header strip and leaves
            // the map area unfilled — "do not put an opaque simulator panel
            // behind it", because the map already carries its own 0.3 blend and
            // a solid box under it would hide the scene the blend is meant to
            // show. Only the outer outline is kept. Without artwork the panel is
            // solid, since the fallback grid has nothing to see through to.
            if (_panel != null) _panel.FillHeight = hasArtwork ? ImageTop : 0f;
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
