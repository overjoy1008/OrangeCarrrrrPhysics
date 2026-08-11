using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Draws a <see cref="ScreenLineBatch"/> through a MeshFilter/MeshRenderer
    /// pair. Sources (the ground grid, the track bounds) fill the batch in
    /// <c>LateUpdate</c> and call <see cref="Rebuild"/>.
    ///
    /// The mesh is built in world space, so this object's transform is forced to
    /// identity.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ScreenLineRenderer : MonoBehaviour
    {
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        public ScreenLineBatch Batch { get; } = new ScreenLineBatch();

        private void Awake() => EnsureMesh();

        private void OnEnable() => EnsureMesh();

        private void EnsureMesh()
        {
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = $"{name} lines" };
                _mesh.MarkDynamic();
            }
            if (_filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;
            EnsureMaterial();

            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// The batch's colours are vertex colours, so a renderer with no material
        /// draws nothing at all rather than drawing wrongly.
        ///
        /// An authored renderer already has one. This is for the ones created at
        /// runtime — the course gate view is added to a scene that never had it —
        /// where forgetting the material would look exactly like the feature being
        /// broken.
        /// </summary>
        private void EnsureMaterial()
        {
            if (_renderer.sharedMaterial != null) return;

            var material = Resources.Load<Material>(LineMaterialResource);
            if (material == null)
            {
                Debug.LogWarning(
                    $"No line material at Resources/{LineMaterialResource}; " +
                    $"{name} will not draw.", this);
                return;
            }
            _renderer.sharedMaterial = material;
        }

        /// <summary>The vertex-colour material every line source shares.</summary>
        private const string LineMaterialResource = "ScreenLine";

        /// <summary>Uploads whatever is currently in <see cref="Batch"/>.</summary>
        public void Rebuild(Camera camera)
        {
            EnsureMesh();
            Batch.BuildMesh(_mesh, camera);
            _renderer.enabled = _mesh.vertexCount > 0;
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}
