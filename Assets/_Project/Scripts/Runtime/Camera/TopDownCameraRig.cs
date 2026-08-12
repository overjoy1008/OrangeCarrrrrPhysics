using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The <c>F5</c> key's top-down projection, as an orthographic Unity camera.
    ///
    /// <c>world_to_screen</c> fits a 110 x 76 unit window inside the client rect
    /// less a 45 px margin, keeping the aspect by taking the smaller of the two
    /// scales, and centres it on the kart. Both axes are negated, so world +Y
    /// points up the screen and world +X points left — a rotation, not a mirror,
    /// which is why steering handedness is unchanged and why the view agrees with
    /// the original minimap artwork.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownCameraRig : MonoBehaviour, IKartCameraman
    {
        public const float ViewHalfWidth = 55.0f;
        public const float ViewHalfHeight = 38.0f;
        public const float ScreenMargin = 45.0f;

        /// <summary>How far above the ground plane the camera sits. Only affects clipping.</summary>
        [SerializeField] private float _height = 400f;

        private Camera _camera;

        public Camera Camera
        {
            get
            {
                if (_camera == null) _camera = GetComponent<Camera>();
                return _camera;
            }
        }

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.nearClipPlane = ScreenLineBatch.NearDepth;
        }

        public void Activate(KartSimulationState kart) => gameObject.SetActive(true);

        public void Deactivate() => gameObject.SetActive(false);

        /// <summary>
        /// The projection is a function of where the kart is and of nothing else,
        /// so the frame's length is not read.
        /// </summary>
        void IKartCameraman.Step(KartSimulationState kart, uint elapsedMs) => Step(kart);

        public void Step(KartSimulationState kart)
        {
            if (kart == null) return;

            // The view looks straight down engine -Z with world +Y up the screen.
            Vector3 forward = KartSpace.ToUnity(new KartVec3(0f, 0f, -1f));
            Vector3 up = KartSpace.ToUnity(new KartVec3(0f, 1f, 0f));

            var eye = new KartVec3(kart.Position.X, kart.Position.Y, kart.Position.Z + _height);
            transform.SetPositionAndRotation(
                KartSpace.ToUnity(eye),
                Quaternion.LookRotation(forward, up));

            Camera.orthographicSize = OrthographicSize(Camera.pixelWidth, Camera.pixelHeight);
            Camera.farClipPlane = Mathf.Max(Camera.farClipPlane, _height + 1000f);
        }

        /// <summary>
        /// Half the visible world height, derived from <c>world_to_screen</c>'s
        /// pixels-per-unit so the top-down view frames exactly what the original
        /// frames at the same window size.
        /// </summary>
        public static float OrthographicSize(float pixelWidth, float pixelHeight)
        {
            float usableWidth = pixelWidth - ScreenMargin * 2f;
            float usableHeight = pixelHeight - ScreenMargin * 2f;
            float scaleX = usableWidth / (ViewHalfWidth * 2f);
            float scaleY = usableHeight / (ViewHalfHeight * 2f);
            float scale = Mathf.Min(scaleX, scaleY);
            if (scale <= 0f) return ViewHalfHeight;
            return pixelHeight * 0.5f / scale;
        }
    }
}
