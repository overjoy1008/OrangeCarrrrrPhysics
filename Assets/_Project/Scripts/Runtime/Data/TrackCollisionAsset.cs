using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The collidable triangle set a KTRK export carries, baked at import time.
    ///
    /// Kept apart from the render meshes on purpose. The physics runs in the
    /// engine's own frame and only ever touches the meshes the asset tags
    /// collidable, so it wants a flat array of those triangles rather than 375
    /// Unity meshes it would have to convert back every query. No MeshCollider
    /// and no Rigidbody are involved anywhere.
    /// </summary>
    public sealed class TrackCollisionAsset : ScriptableObject
    {
        [SerializeField] private Vector3[] _vertices;
        [SerializeField] private int[] _indices;
        [SerializeField] private int[] _meshIndexStart;
        [SerializeField] private int[] _meshIndexCount;
        [SerializeField] private int[] _meshVertexStart;
        [SerializeField] private int[] _meshVertexCount;
        [SerializeField] private Vector3[] _meshMinimum;
        [SerializeField] private Vector3[] _meshMaximum;

        public int MeshCount => _meshIndexStart != null ? _meshIndexStart.Length : 0;

        public int TriangleCount
        {
            get
            {
                int total = 0;
                if (_meshIndexCount == null) return 0;
                foreach (int count in _meshIndexCount) total += count / 3;
                return total;
            }
        }

        /// <summary>
        /// Stores one scene. Vertices and bounds are in the exporter's asset
        /// space, exactly as the C loader keeps them; the world transform is
        /// applied per query by <see cref="KartTrackCollision"/>.
        /// </summary>
        public void Store(
            Vector3[] vertices,
            int[] indices,
            int[] meshVertexStart,
            int[] meshVertexCount,
            int[] meshIndexStart,
            int[] meshIndexCount,
            Vector3[] meshMinimum,
            Vector3[] meshMaximum)
        {
            _vertices = vertices;
            _indices = indices;
            _meshVertexStart = meshVertexStart;
            _meshVertexCount = meshVertexCount;
            _meshIndexStart = meshIndexStart;
            _meshIndexCount = meshIndexCount;
            _meshMinimum = meshMinimum;
            _meshMaximum = meshMaximum;
        }

        /// <summary>Builds the plain Core structure the queries run against.</summary>
        public KartCollisionScene ToScene()
        {
            var scene = new KartCollisionScene();
            if (_vertices == null || _indices == null || _meshIndexStart == null) return scene;

            scene.Vertices = new KartVec3[_vertices.Length];
            for (int i = 0; i < _vertices.Length; ++i)
            {
                Vector3 v = _vertices[i];
                scene.Vertices[i] = new KartVec3(v.x, v.y, v.z);
            }

            scene.Indices = (int[])_indices.Clone();

            scene.Meshes = new KartCollisionMesh[_meshIndexStart.Length];
            for (int i = 0; i < scene.Meshes.Length; ++i)
            {
                scene.Meshes[i] = new KartCollisionMesh
                {
                    VertexStart = _meshVertexStart[i],
                    VertexCount = _meshVertexCount[i],
                    IndexStart = _meshIndexStart[i],
                    IndexCount = _meshIndexCount[i],
                    Minimum = new KartVec3(
                        _meshMinimum[i].x, _meshMinimum[i].y, _meshMinimum[i].z),
                    Maximum = new KartVec3(
                        _meshMaximum[i].x, _meshMaximum[i].y, _meshMaximum[i].z),
                };
            }
            return scene;
        }
    }
}
