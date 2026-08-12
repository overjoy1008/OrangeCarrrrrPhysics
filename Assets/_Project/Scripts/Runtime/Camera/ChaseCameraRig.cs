using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Drives a Unity <see cref="Camera"/> from the recovered
    /// <see cref="KartChaseCamera"/>. Cinemachine is deliberately not used: the
    /// original's follow is a specific quaternion approach with its own
    /// interpolation curve, and any generic damping would change how the kart
    /// reads on screen.
    ///
    /// The original's stored FOV is a full vertical angle in degrees, which is
    /// exactly what <see cref="Camera.fieldOfView"/> means, so it is copied over
    /// rather than converted.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCameraRig : MonoBehaviour, IKartCameraman
    {
        /// <summary>
        /// The renderer's off-centre vanishing point. Kept here as the name older
        /// scenes and notes refer to; <see cref="CameraProjection"/> owns it now,
        /// since every cameraman renders through the same projection.
        /// </summary>
        public const float VerticalCenterFraction = CameraProjection.VerticalCenterFraction;

        [Header("Follow")]
        [Tooltip("400 ms is the Overhead Chase Cameraman; 100 ms is the Front one.")]
        [SerializeField] private float _followMs = KartChaseCamera.FollowOverheadMs;

        [Tooltip("The kart's booster-like runtime flag. Widens the FOV to 110 degrees.")]
        [SerializeField] private bool _wideView;

        [SerializeField] private bool _applyVerticalCenterShift = true;

        private Camera _camera;
        private readonly KartChaseCamera _follow = new KartChaseCamera();

        public KartChaseCamera Follow => _follow;

        public Camera Camera
        {
            get
            {
                if (_camera == null) _camera = GetComponent<Camera>();
                return _camera;
            }
        }

        public float FollowMs
        {
            get => _followMs;
            set => _followMs = value;
        }

        public bool WideView
        {
            get => _wideView;
            set => _wideView = value;
        }

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            // Matches ScreenLineBatch's clip depth, so the grid is not clipped
            // twice at two different distances.
            _camera.nearClipPlane = ScreenLineBatch.NearDepth;
            _follow.Reset();
        }

        public void ResetFollow() => _follow.Reset();

        /// <summary>
        /// Installed as the live cameraman. The follow is not reset here: enabling
        /// the object runs <see cref="OnEnable"/>, which already snaps it, and the
        /// race's own reset snaps it again.
        /// </summary>
        public void Activate(KartSimulationState kart) => gameObject.SetActive(true);

        public void Deactivate() => gameObject.SetActive(false);

        /// <summary>Places the camera for one frame of the demo's fixed 16 ms tick.</summary>
        public void Step(KartSimulationState kart, uint elapsedMs)
        {
            if (kart == null) return;

            KartChaseCameraPose pose = _follow.Update(
                kart.Position,
                kart.Orientation,
                kart.Speed,
                _wideView,
                elapsedMs,
                _followMs);

            Apply(pose);
        }

        public void Apply(in KartChaseCameraPose pose)
        {
            Vector3 position = KartSpace.ToUnity(pose.Position);
            Vector3 forward = KartSpace.ToUnity(pose.Forward);
            Vector3 up = KartSpace.ToUnity(pose.Up);

            if (forward.sqrMagnitude > 0f)
            {
                transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            }
            else
            {
                transform.position = position;
            }

            Camera.fieldOfView = pose.FieldOfViewDegrees;
            ApplyVerticalCenterShift();
        }

        private void ApplyVerticalCenterShift()
            => CameraProjection.ApplyVerticalCenterShift(Camera, _applyVerticalCenterShift);
    }
}
