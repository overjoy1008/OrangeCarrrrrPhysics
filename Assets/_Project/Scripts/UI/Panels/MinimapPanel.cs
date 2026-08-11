using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>draw_track_minimap</c>: the top-right map panel.
    ///
    /// A track with artwork gets the original's rotating camera over its own
    /// <c>xt_minimap.png</c> — see <see cref="KartMinimapCamera"/> — with the
    /// kart projected through the same camera and no box behind it. A track
    /// without artwork, which is only the synthetic flat one, falls back to the
    /// quarter grid inside a boxed cyan boundary.
    ///
    /// The kart's triangle is drawn either way; only the box comes and goes.
    ///
    /// The original's "TRACK MAP" heading and kart read-out are dropped: they are
    /// text the rest of the HUD already carries.
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

        /// <summary>
        /// The artwork currently on the panel. Held so the texture is only pushed
        /// when the track actually changes.
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

            // image_rect: panel.left + 15 .. panel.right - 15,
            //             panel.top + 42 .. panel.bottom - 16.
            PlaceImageRect(_image != null ? (RectTransform)_image.transform : null);
            PlaceImageRect(_marker != null ? (RectTransform)_marker.transform : null);

            // The box sits on the map area rather than the whole panel: it only
            // ever shows behind the fallback grid, and a box larger than the grid
            // it frames would be framing nothing.
            if (_panel != null)
            {
                PlaceImageRect((RectTransform)_panel.transform);
                _panel.color = HudPalette.TelemetryPanelFill;
                _panel.BorderColor = HudPalette.TelemetryPanelBorder;
                _panel.BorderWidth = 1f;
            }

            if (_image != null)
            {
                _image.color = Color.white;
                _image.raycastTarget = false;
            }

            // The artwork itself is the track's, not the layout's, so re-applying
            // the layout must not decide what the panel shows.
            _mapApplied = false;
            ShowMap(Simulator != null && Simulator.Track != null ? Simulator.Track.Minimap : null);

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

            // The box belongs to the fallback map only. The original will not put
            // an opaque panel behind the artwork — the map carries its own 0.3
            // blend, and a box under it would hide the scene that blend is there
            // to show. The marker stays on either way: it is the kart.
            if (_panel != null) _panel.enabled = !hasArtwork;
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


    }
}
