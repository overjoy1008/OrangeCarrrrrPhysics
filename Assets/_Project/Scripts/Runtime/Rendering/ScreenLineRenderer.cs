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

            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

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
