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
    public sealed class ChaseCameraRig : MonoBehaviour
    {
        /// <summary>
        /// <c>project_point</c> puts the vanishing point at 0.52 of the client
        /// height rather than 0.50, so the horizon sits slightly below centre.
        /// Reproduced as a projection-matrix shift.
        /// </summary>
        public const float VerticalCenterFraction = 0.52f;

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

        /// <summary>
        /// The original's principal point sits 0.52 of the way down the client
        /// rect. Moving it down by 0.02 of the height moves the image down by
        /// 0.04 in NDC, which is a constant added to the projection's y row.
        /// </summary>
        private void ApplyVerticalCenterShift()
        {
            if (!_applyVerticalCenterShift)
            {
                Camera.ResetProjectionMatrix();
                return;
            }

            Camera.ResetProjectionMatrix();
            Matrix4x4 projection = Camera.projectionMatrix;
            projection[1, 2] += (VerticalCenterFraction - 0.5f) * 2f;
            Camera.projectionMatrix = projection;
        }
    }
}
