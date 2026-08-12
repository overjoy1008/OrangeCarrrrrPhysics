using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Drives a Unity <see cref="Camera"/> from the recovered
    /// <see cref="KartReadyCamera"/> — the sweep over the grid before the countdown.
    ///
    /// The original installs <c>KartReCameraman</c> when the race is set up and
    /// replaces it with the chase camera on <c>count_3</c>, which is the first of
    /// the seven-second countdown's three digits. The path itself is 3333 ms long
    /// and the window it plays in is 4000 ms, so it holds its last pose for the
    /// remaining two thirds of a second before the swap.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class ReadyCameraRig : MonoBehaviour, IKartCameraman
    {
        [Tooltip("The imported readyCamera.kcam. Left empty, it is resolved from the project.")]
        [SerializeField] private ReadyCameraAsset _path;

        [SerializeField] private bool _applyVerticalCenterShift = true;

        private Camera _camera;
        private KartReadyCamera _sweep;
        private ReadyCameraAsset _builtFrom;

        public ReadyCameraAsset Path
        {
            get => _path;
            set
            {
                _path = value;
                _sweep = null;
            }
        }

        /// <summary>True once the baked path has run out, for anything watching.</summary>
        public bool Finished => Sweep == null || Sweep.Finished;

        public Camera Camera
        {
            get
            {
                if (_camera == null) _camera = GetComponent<Camera>();
                return _camera;
            }
        }

        private KartReadyCamera Sweep
        {
            get
            {
                ReadyCameraAsset asset = Resolve();
                if (asset == null) return null;

                if (_sweep == null || _builtFrom != asset)
                {
                    _builtFrom = asset;
                    _sweep = new KartReadyCamera(asset.ToPath());
                }
                return _sweep;
            }
        }

        private ReadyCameraAsset Resolve()
        {
#if UNITY_EDITOR
            if (_path == null)
            {
                _path = UnityEditor.AssetDatabase.LoadAssetAtPath<ReadyCameraAsset>(
                    "Assets/_Project/Art/Cameras/readyCamera.kcam");
            }
#endif
            return _path;
        }

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _camera.nearClipPlane = ScreenLineBatch.NearDepth;
        }

        /// <summary>
        /// Rewinds the sweep without reinstalling it. Needed because a reset while
        /// the sweep is already the live cameraman does not change the slot, so
        /// nothing would otherwise put the path back to its first key.
        /// </summary>
        public void Restart() => Sweep?.Start();

        /// <summary>Installed: the path plays from its first key.</summary>
        public void Activate(KartSimulationState kart)
        {
            Sweep?.Start();
            gameObject.SetActive(true);
            if (kart != null) Step(kart, 0u);
        }

        public void Deactivate() => gameObject.SetActive(false);

        public void Step(KartSimulationState kart, uint elapsedMs)
        {
            KartReadyCamera sweep = Sweep;
            if (kart == null || sweep == null) return;

            KartChaseCameraPose pose = sweep.Update(kart.Position, kart.Orientation, elapsedMs);

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

            // Unlike the other two cameramen this one does carry a field of view:
            // it is the ReCamera's own, read at 0x004476F0.
            if (pose.FieldOfViewDegrees > 0f) Camera.fieldOfView = pose.FieldOfViewDegrees;
            CameraProjection.ApplyVerticalCenterShift(Camera, _applyVerticalCenterShift);
        }
    }
}
