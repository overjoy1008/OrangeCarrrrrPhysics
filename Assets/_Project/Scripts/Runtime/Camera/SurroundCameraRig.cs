using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Drives a Unity <see cref="Camera"/> from the recovered
    /// <see cref="KartSurroundCamera"/> — the cameraman the finish installs.
    ///
    /// The orbit's clock starts when the cameraman is installed, which is what
    /// <see cref="Activate"/> is for: the original captures its base timestamp on
    /// the first update after being created, and it is created at the finish.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class SurroundCameraRig : MonoBehaviour, IKartCameraman
    {
        /// <summary>
        /// The field of view the camera renders the orbit at.
        ///
        /// <b>Not recovered.</b> <c>SurroundCameraman</c> writes no field of view at
        /// all, so the original shows the finish at whatever the previous cameraman
        /// left behind. 75 degrees is the chase camera's resting value — the one it
        /// holds whenever no boost is running — so a kart that crosses the line off
        /// the booster is framed the same way it was a moment earlier.
        /// </summary>
        [SerializeField] private float _fieldOfViewDegrees = KartChaseCamera.FovNarrowDegrees;

        [SerializeField] private bool _applyVerticalCenterShift = true;

        private Camera _camera;
        private readonly KartSurroundCamera _orbit = new KartSurroundCamera();

        public KartSurroundCamera Orbit => _orbit;

        public Camera Camera
        {
            get
            {
                if (_camera == null) _camera = GetComponent<Camera>();
                return _camera;
            }
        }

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _camera.nearClipPlane = ScreenLineBatch.NearDepth;
        }

        /// <summary>Installed: the orbit starts from its first key.</summary>
        public void Activate(KartSimulationState kart)
        {
            _orbit.Start(KartSurroundMode.Orbit);
            gameObject.SetActive(true);

            // Placed once on the way in, so the first rendered frame is already the
            // orbit's opening pose rather than wherever the object was left.
            if (kart != null) Step(kart, 0u);
        }

        public void Deactivate() => gameObject.SetActive(false);

        public void Step(KartSimulationState kart, uint elapsedMs)
        {
            if (kart == null) return;

            KartChaseCameraPose pose = _orbit.Update(kart.Position, kart.Orientation, elapsedMs);

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

            // The pose's own field of view is deliberately zero — the recovered
            // class writes none — so the rig's setting is used instead.
            Camera.fieldOfView = _fieldOfViewDegrees;
            CameraProjection.ApplyVerticalCenterShift(Camera, _applyVerticalCenterShift);
        }
    }
}
