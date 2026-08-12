using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The <c>F4</c> key: the kart model's own bounding boxes, drawn as wireframe.
    ///
    /// Ported from <c>draw_kart_model_bounds</c>. Three volumes, the same three
    /// the model catalogue tabulates, in the original's colours — full yellow,
    /// wheels magenta, body green — drawn together so the gap where the wheels
    /// stand wider than the body is visible.
    ///
    /// The parts are found the way <c>kart_model_parts_build</c> finds them: the
    /// body is mesh 0, and the wheels are the one group of exactly four meshes
    /// from index 1 onward that share a vertex count. Ties are impossible in the
    /// demo's models, and preferring the larger group keeps the rule
    /// deterministic if a future asset has one.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ScreenLineRenderer))]
    public sealed class KartModelBoundsView : MonoBehaviour
    {
        /// <summary>The four wheels the original looks for.</summary>
        public const int WheelCount = 4;

        [Header("Appearance (RGB values from draw_kart_model_bounds)")]
        [SerializeField] private Color _fullColor = new Color32(235, 215, 90, 255);
        [SerializeField] private Color _wheelsColor = new Color32(225, 105, 220, 255);
        [SerializeField] private Color _bodyColor = new Color32(110, 235, 140, 255);
        [SerializeField, Min(0.5f)] private float _widthPixels = 1f;

        [SerializeField] private bool _show;

        /// <summary>The kart whose model is measured. Set by the simulator.</summary>
        public KartView Kart { get; set; }

        public bool Show
        {
            get => _show;
            set => _show = value;
        }

        private ScreenLineRenderer _lines;

        // The three boxes, in the kart's own local space, and what they were
        // measured from. Measuring walks every mesh, so it is done when the model
        // changes rather than per frame.
        private Bounds _full;
        private Bounds _body;
        private Bounds _wheels;
        private bool _hasWheels;
        private bool _measured;
        private Transform _measuredFrom;
        private int _measuredChildren;

        private readonly List<Vector3> _loop = new List<Vector3>(4);

        private void OnEnable()
        {
            _lines = GetComponent<ScreenLineRenderer>();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
            => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            => Render(camera);

        public void Render(Camera camera)
        {
            if (_lines == null) _lines = GetComponent<ScreenLineRenderer>();
            if (_lines == null || camera == null) return;

            ScreenLineBatch batch = _lines.Batch;
            batch.Clear();

            if (_show && Kart != null)
            {
                Measure();
                if (_measured)
                {
                    AddBox(batch, _full, _fullColor);
                    if (_hasWheels) AddBox(batch, _wheels, _wheelsColor);
                    AddBox(batch, _body, _bodyColor);
                }
            }

            _lines.Rebuild(camera);
        }

        /// <summary>
        /// Measures the three boxes in the kart's local space, when the model has
        /// changed under it.
        /// </summary>
        private void Measure()
        {
            Transform root = Kart.ModelRoot;
            if (root == null) { _measured = false; return; }

            // Child count stands in for "the model was rebuilt": KartView tears
            // the old one down and makes a new one, so the count and the root
            // together are enough to notice.
            if (_measured && _measuredFrom == root && _measuredChildren == root.childCount) return;

            _measuredFrom = root;
            _measuredChildren = root.childCount;
            _measured = false;
            _hasWheels = false;

            var meshes = new List<MeshFilter>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (filter.sharedMesh != null) meshes.Add(filter);
            }
            if (meshes.Count == 0) return;

            Transform frame = Kart.transform;

            bool started = false;
            for (int index = 0; index < meshes.Count; ++index)
            {
                Bounds box = LocalBounds(meshes[index], frame);
                if (!started) { _full = box; started = true; }
                else _full.Encapsulate(box);
            }

            _body = LocalBounds(meshes[0], frame);

            int[] wheels = FindWheels(meshes);
            if (wheels != null)
            {
                _wheels = LocalBounds(meshes[wheels[0]], frame);
                for (int index = 1; index < wheels.Length; ++index)
                {
                    _wheels.Encapsulate(LocalBounds(meshes[wheels[index]], frame));
                }
                _hasWheels = true;
            }

            _measured = true;
        }

        /// <summary>
        /// The one group of exactly four meshes, from index 1 onward, sharing a
        /// vertex count. Null when the model has no such group.
        /// </summary>
        private static int[] FindWheels(List<MeshFilter> meshes)
        {
            int[] best = null;
            int bestVertices = -1;

            for (int candidate = 1; candidate < meshes.Count; ++candidate)
            {
                int vertices = meshes[candidate].sharedMesh.vertexCount;

                var matches = new List<int>(WheelCount + 1);
                for (int other = 1; other < meshes.Count; ++other)
                {
                    if (meshes[other].sharedMesh.vertexCount != vertices) continue;
                    matches.Add(other);
                    if (matches.Count > WheelCount) break;
                }

                if (matches.Count != WheelCount) continue;
                if (best != null && vertices <= bestVertices) continue;

                best = matches.ToArray();
                bestVertices = vertices;
            }
            return best;
        }

        /// <summary>One mesh's box, brought into the kart's own frame.</summary>
        private static Bounds LocalBounds(MeshFilter filter, Transform frame)
        {
            Bounds local = filter.sharedMesh.bounds;
            Vector3 centre = local.center;
            Vector3 extents = local.extents;

            var result = new Bounds();
            for (int corner = 0; corner < 8; ++corner)
            {
                var offset = new Vector3(
                    (corner & 1) != 0 ? extents.x : -extents.x,
                    (corner & 2) != 0 ? extents.y : -extents.y,
                    (corner & 4) != 0 ? extents.z : -extents.z);

                Vector3 point = frame.InverseTransformPoint(
                    filter.transform.TransformPoint(centre + offset));

                if (corner == 0) result = new Bounds(point, Vector3.zero);
                else result.Encapsulate(point);
            }
            return result;
        }

        /// <summary>
        /// The box's twelve edges. 0-3 walk the bottom face in order and 4-7 the
        /// top, which is what the edge table expects — deriving the corners from
        /// the bits of the index instead would put 1 and 2 diagonally opposite and
        /// draw an X across each face.
        /// </summary>
        private static readonly int[,] Corners =
        {
            {0,0,0}, {1,0,0}, {1,1,0}, {0,1,0},
            {0,0,1}, {1,0,1}, {1,1,1}, {0,1,1},
        };

        private void AddBox(ScreenLineBatch batch, in Bounds box, Color color)
        {
            Transform frame = Kart.transform;
            Vector3 min = box.min;
            Vector3 max = box.max;

            var points = new Vector3[8];
            for (int corner = 0; corner < 8; ++corner)
            {
                points[corner] = frame.TransformPoint(new Vector3(
                    Corners[corner, 0] != 0 ? max.x : min.x,
                    Corners[corner, 1] != 0 ? max.y : min.y,
                    Corners[corner, 2] != 0 ? max.z : min.z));
            }

            AddFace(batch, points, 0, 1, 2, 3, color);
            AddFace(batch, points, 4, 5, 6, 7, color);
            for (int corner = 0; corner < 4; ++corner)
            {
                batch.AddSegment(points[corner], points[corner + 4], color, _widthPixels);
            }
        }

        private void AddFace(
            ScreenLineBatch batch, Vector3[] points, int a, int b, int c, int d, Color color)
        {
            _loop.Clear();
            _loop.Add(points[a]);
            _loop.Add(points[b]);
            _loop.Add(points[c]);
            _loop.Add(points[d]);
            batch.AddLoop(_loop, color, _widthPixels);
        }
    }
}
