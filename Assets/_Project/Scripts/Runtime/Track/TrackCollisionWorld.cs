using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Binds a baked collision set to a track spec and hands the simulator the
    /// two queries it needs.
    ///
    /// It also owns the world transform: the scene is mirrored in X, centred on
    /// its own AABB and dropped so the start line's plane is z = 0. The render
    /// object has to be placed to match, which <see cref="ApplyToRenderRoot"/>
    /// does — a negative X scale plus the same offsets. Unity flips the winding
    /// for a negative-determinant transform, so the faces stay the right way out.
    /// </summary>
    [ExecuteAlways]
    public sealed class TrackCollisionWorld : MonoBehaviour
    {
        [SerializeField] private TrackSpecAsset _track;
        [SerializeField] private TrackCollisionAsset _collision;

        [Tooltip("The imported KTRK root. Placed to match the physics world transform.")]
        [SerializeField] private Transform _renderRoot;

        private KartTrackCollision _world;
        private TrackCollisionAsset _builtFrom;

        public TrackSpecAsset Track => _track;

        /// <summary>The ground and body queries, built on first use.</summary>
        public KartTrackCollision World
        {
            get
            {
                if (_world == null || _builtFrom != _collision) Rebuild();
                return _world;
            }
        }

        private void OnEnable()
        {
            Rebuild();
            ApplyToRenderRoot();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Rebuild();
            ApplyToRenderRoot();
        }

        private void Rebuild()
        {
            _world = null;
            _builtFrom = _collision;
            if (_collision == null || _track == null) return;

            _world = new KartTrackCollision(
                _collision.ToScene(), KartTrackTransform.FromSpec(_track.ToSpec()));
        }

        /// <summary>
        /// Places the render root so the drawn track sits exactly where the
        /// collision triangles are.
        ///
        /// The importer emits Unity-space vertices straight from asset space, so
        /// applying the engine's world transform here means negating X, adding
        /// the AABB centre back on X, dropping by the ground plane on Y, and
        /// shifting by the centre on Z — the axis the engine's Y became.
        /// </summary>
        public void ApplyToRenderRoot()
        {
            if (_renderRoot == null || _track == null) return;

            KartTrackTransform transform = KartTrackTransform.FromSpec(_track.ToSpec());

            _renderRoot.localScale = new Vector3(transform.MirrorX ? -1f : 1f, 1f, 1f);
            _renderRoot.localPosition = new Vector3(
                transform.MirrorX ? transform.CenterX : -transform.CenterX,
                -transform.GroundZ,
                transform.CenterY);
            _renderRoot.localRotation = Quaternion.identity;
        }
    }
}
