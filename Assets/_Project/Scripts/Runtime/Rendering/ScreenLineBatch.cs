using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Builds one mesh out of constant-pixel-width 3D line segments, the way the
    /// original's GDI pens behave.
    ///
    /// Segments are clipped against the near plane here rather than in the
    /// shader, matching <c>draw_line_3d</c>: a segment with both endpoints behind
    /// the plane is dropped, and one endpoint behind is pulled forward onto it.
    /// Doing it on the CPU keeps w positive in the vertex stage, which is what
    /// lets the screen-space expansion be a plain normalize.
    /// </summary>
    public sealed class ScreenLineBatch
    {
        /// <summary>
        /// <c>draw_line_3d</c>'s <c>near_depth</c>. The camera's own near plane is
        /// set to match so nothing is clipped twice at different distances.
        /// </summary>
        public const float NearDepth = 0.15f;

        private struct Segment
        {
            public Vector3 A;
            public Vector3 B;
            public Color32 Color;
            public float WidthPixels;
        }

        private readonly List<Segment> _segments = new List<Segment>(256);

        private readonly List<Vector3> _positions = new List<Vector3>(1024);
        private readonly List<Vector4> _others = new List<Vector4>(1024);
        private readonly List<Vector2> _sideWidth = new List<Vector2>(1024);
        private readonly List<Color32> _colors = new List<Color32>(1024);
        private readonly List<int> _indices = new List<int>(1536);

        public int SegmentCount => _segments.Count;

        public void Clear() => _segments.Clear();

        public void AddSegment(Vector3 a, Vector3 b, Color color, float widthPixels)
        {
            _segments.Add(new Segment
            {
                A = a,
                B = b,
                // The palette holds the original's sRGB pen colours. Mesh vertex
                // colours reach the shader untouched, so in a linear project they
                // have to be converted here or every line renders washed out.
                Color = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? color.linear
                    : color,
                WidthPixels = widthPixels,
            });
        }

        /// <summary>Adds a closed loop through the given points.</summary>
        public void AddLoop(IReadOnlyList<Vector3> points, Color color, float widthPixels)
        {
            if (points == null || points.Count < 2) return;
            for (int i = 0; i < points.Count; ++i)
            {
                AddSegment(points[i], points[(i + 1) % points.Count], color, widthPixels);
            }
        }

        /// <summary>
        /// Rewrites <paramref name="mesh"/> as the clipped, camera-facing quad
        /// strip for everything added since the last <see cref="Clear"/>.
        /// Vertices are in world space, so the renderer's transform must be
        /// identity.
        /// </summary>
        public void BuildMesh(Mesh mesh, Camera camera)
        {
            _positions.Clear();
            _others.Clear();
            _sideWidth.Clear();
            _colors.Clear();
            _indices.Clear();

            if (camera != null)
            {
                Transform cameraTransform = camera.transform;
                Vector3 eye = cameraTransform.position;
                Vector3 forward = cameraTransform.forward;

                foreach (Segment segment in _segments)
                {
                    Vector3 a = segment.A;
                    Vector3 b = segment.B;
                    if (!ClipToNearPlane(eye, forward, ref a, ref b)) continue;
                    Emit(a, b, segment.Color, segment.WidthPixels);
                }
            }

            mesh.Clear();
            if (_positions.Count == 0)
            {
                return;
            }

            mesh.indexFormat = _positions.Count > 65000
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(_positions);
            mesh.SetTangents(_others);
            mesh.SetUVs(0, _sideWidth);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_indices, 0, calculateBounds: false);

            // The expansion happens after projection, so the real screen coverage
            // is wider than the segment itself. A generous bounds keeps culling
            // from popping lines that are only just off-frustum.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        /// <summary>
        /// <c>draw_line_3d</c>'s near-plane handling, in camera-forward depth.
        /// Returns false when the whole segment is behind the plane.
        /// </summary>
        private static bool ClipToNearPlane(
            Vector3 eye,
            Vector3 forward,
            ref Vector3 a,
            ref Vector3 b)
        {
            float depthA = Vector3.Dot(a - eye, forward);
            float depthB = Vector3.Dot(b - eye, forward);

            if (depthA <= NearDepth && depthB <= NearDepth) return false;

            if (depthA <= NearDepth)
            {
                float t = (NearDepth - depthA) / (depthB - depthA);
                a += (b - a) * t;
            }
            else if (depthB <= NearDepth)
            {
                float t = (NearDepth - depthB) / (depthA - depthB);
                b += (a - b) * t;
            }
            return true;
        }

        private void Emit(Vector3 a, Vector3 b, Color32 color, float widthPixels)
        {
            int baseIndex = _positions.Count;
            var otherB = new Vector4(b.x, b.y, b.z, 0f);
            var otherA = new Vector4(a.x, a.y, a.z, 0f);

            // Both corners at A know where B is, and the reverse, so the vertex
            // stage can work out the screen direction from either end. The two
            // ends see opposite directions and therefore opposite perpendiculars,
            // so B's sides are swapped to keep the quad from folding into a
            // bowtie: corners come out A-p, A+p, B+p, B-p.
            _positions.Add(a); _others.Add(otherB); _sideWidth.Add(new Vector2(-1f, widthPixels)); _colors.Add(color);
            _positions.Add(a); _others.Add(otherB); _sideWidth.Add(new Vector2(1f, widthPixels)); _colors.Add(color);
            _positions.Add(b); _others.Add(otherA); _sideWidth.Add(new Vector2(-1f, widthPixels)); _colors.Add(color);
            _positions.Add(b); _others.Add(otherA); _sideWidth.Add(new Vector2(1f, widthPixels)); _colors.Add(color);

            _indices.Add(baseIndex + 0);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex + 0);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex + 3);
        }
    }
}
