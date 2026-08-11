using System.Collections.Generic;
using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The <c>flat_test</c> reference track, drawn the way <c>draw_track</c> draws
    /// it: a camera-local patch of the 10-unit reference grid on the z=0 plane,
    /// plus the cyan AABB safety wall as two stacked rectangles.
    ///
    /// There is deliberately no ground surface. <c>flat_test</c> has no mesh at
    /// all — that is the whole point of it — so the grid is line art over the sky
    /// colour and the physics gets its ground from a plane query instead of from
    /// geometry.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ScreenLineRenderer))]
    public sealed class TestTrackView : MonoBehaviour
    {
        /// <summary>Half-extent of the patch <c>draw_track</c> draws around the camera.</summary>
        public const float GridRadius = 140f;

        /// <summary>The demo's 10-unit reference grid.</summary>
        public const float GridStep = 10f;

        /// <summary>The height of the upper bounds rectangle.</summary>
        public const float WallTopZ = 0.8f;

        [Header("Track")]
        [SerializeField] private TrackSpecAsset _track;

        [Header("Appearance (RGB values from draw_track)")]
        [SerializeField] private Color _gridColor = new Color32(86, 98, 108, 255);
        [SerializeField] private Color _boundsColor = new Color32(75, 220, 255, 255);
        [SerializeField, Min(0.5f)] private float _gridWidthPixels = 1f;
        [SerializeField, Min(0.5f)] private float _boundsWidthPixels = 4f;

        [Header("Toggles")]
        [Tooltip("B in the original: hides the AABB safety wall.")]
        [SerializeField] private bool _showBounds = true;

        private ScreenLineRenderer _lines;
        private readonly List<Vector3> _loop = new List<Vector3>(4);

        public TrackSpecAsset Track
        {
            get => _track;
            set => _track = value;
        }

        public bool ShowBounds
        {
            get => _showBounds;
            set => _showBounds = value;
        }

        private void OnEnable()
        {
            _lines = GetComponent<ScreenLineRenderer>();
            // Rebuilding per camera rather than once per frame is what makes the
            // track show up in the scene view as well as the game view: the patch
            // is centred on whichever camera is about to draw it, and the
            // near-plane clip uses that camera's eye.
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
            => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            => Render(camera);

        /// <summary>
        /// Builds the grid patch and the bounds for one camera. Safe to call more
        /// than once a frame; each call replaces the mesh.
        /// </summary>
        public void Render(Camera camera)
        {
            if (_lines == null) _lines = GetComponent<ScreenLineRenderer>();
            if (_lines == null || _track == null || camera == null) return;

            ScreenLineBatch batch = _lines.Batch;
            batch.Clear();

            KartVec3 minimum = _track.Minimum;
            KartVec3 maximum = _track.Maximum;
            KartVec3 eye = KartSpace.ToKart(camera.transform.position);

            AddGrid(batch, minimum, maximum, eye);
            if (_showBounds) AddBounds(batch, minimum, maximum);

            _lines.Rebuild(camera);
        }

        /// <summary>
        /// A camera-local patch instead of full-AABB lines, whose endpoints can
        /// both fall outside the view on the demo's very large tracks.
        /// </summary>
        private void AddGrid(
            ScreenLineBatch batch,
            in KartVec3 minimum,
            in KartVec3 maximum,
            in KartVec3 eye)
        {
            float minX = Mathf.Max(minimum.X, Mathf.Floor((eye.X - GridRadius) / GridStep) * GridStep);
            float maxX = Mathf.Min(maximum.X, Mathf.Ceil((eye.X + GridRadius) / GridStep) * GridStep);
            float minY = Mathf.Max(minimum.Y, Mathf.Floor((eye.Y - GridRadius) / GridStep) * GridStep);
            float maxY = Mathf.Min(maximum.Y, Mathf.Ceil((eye.Y + GridRadius) / GridStep) * GridStep);

            float groundZ = minimum.Z;

            for (float x = minX; x <= maxX; x += GridStep)
            {
                batch.AddSegment(
                    KartSpace.ToUnity(new KartVec3(x, minY, groundZ)),
                    KartSpace.ToUnity(new KartVec3(x, maxY, groundZ)),
                    _gridColor,
                    _gridWidthPixels);
            }
            for (float y = minY; y <= maxY; y += GridStep)
            {
                batch.AddSegment(
                    KartSpace.ToUnity(new KartVec3(minX, y, groundZ)),
                    KartSpace.ToUnity(new KartVec3(maxX, y, groundZ)),
                    _gridColor,
                    _gridWidthPixels);
            }
        }

        private void AddBounds(ScreenLineBatch batch, in KartVec3 minimum, in KartVec3 maximum)
        {
            AddRectangle(batch, minimum, maximum, minimum.Z);
            AddRectangle(batch, minimum, maximum, minimum.Z + WallTopZ);
        }

        private void AddRectangle(
            ScreenLineBatch batch,
            in KartVec3 minimum,
            in KartVec3 maximum,
            float z)
        {
            _loop.Clear();
            _loop.Add(KartSpace.ToUnity(new KartVec3(minimum.X, minimum.Y, z)));
            _loop.Add(KartSpace.ToUnity(new KartVec3(maximum.X, minimum.Y, z)));
            _loop.Add(KartSpace.ToUnity(new KartVec3(maximum.X, maximum.Y, z)));
            _loop.Add(KartSpace.ToUnity(new KartVec3(minimum.X, maximum.Y, z)));
            batch.AddLoop(_loop, _boundsColor, _boundsWidthPixels);
        }
    }
}
