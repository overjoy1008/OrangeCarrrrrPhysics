using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The two queries the original runs against its triangle set, ported from
    /// <c>kart_track_collision.c</c>.
    ///
    /// Ground is a per-wheel ray (0x00432fc0) and the body is an oriented box
    /// (0x00433310 -> 0x00434d40). They are separate mechanisms in the original
    /// and they are separate here.
    ///
    /// Both report the face normal as it is, rather than turning it toward the
    /// kart or toward the sky. Getting that sign wrong would flip every floor
    /// into a ceiling — and a face wound the wrong way genuinely does push the
    /// suspension down in the original, which is the bug that used to drop karts
    /// through the village overpass.
    /// </summary>
    public sealed class KartTrackCollision : IKartGroundQuery, IKartBodyCollisionQuery
    {
        /// <summary>
        /// _DAT_00571d48. The ray query skips any triangle steeper than this, so
        /// walls are invisible to the wheels; the response uses the same number
        /// to tell a wall contact from a landing.
        /// </summary>
        public const float RoadMinNormalZ = 0.6499999761581421f;

        /// <summary>
        /// 0x3f333333, the z half-extent 0x00430830 builds the body box with.
        /// Unlike the other two extents it is not per-kart.
        /// </summary>
        public const float BodyHalfHeight = 0.699999988079071f;

        /// <summary>
        /// Two normals this close are the same surface. The original has no such
        /// test — it iterates every overlapping triangle — but after the first
        /// contact the resolver's approach guard makes repeats of one normal
        /// no-ops, so folding them together changes nothing and stops a road from
        /// crowding a wall out of a fixed-size buffer.
        /// </summary>
        public const float ContactNormalEpsilon = 0.999f;

        private readonly KartCollisionScene _scene;
        private KartTrackTransform _transform;

        public KartTrackCollision(KartCollisionScene scene, in KartTrackTransform transform)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _transform = transform;
        }

        public KartTrackTransform Transform
        {
            get => _transform;
            set => _transform = value;
        }

        /// <summary>
        /// One triangle in world space, with the scene mirror compensated.
        ///
        /// Mirroring a single axis reverses winding, so a normal taken straight
        /// from the mirrored corners would point the opposite way to the one the
        /// original computed at 0x00432390. Swapping two corners puts it back.
        /// </summary>
        private void ReadTriangle(int indexStart, out KartVec3 a, out KartVec3 b, out KartVec3 c)
        {
            int[] indices = _scene.Indices;
            KartVec3[] vertices = _scene.Vertices;

            int offsetB = _transform.MirrorX ? 2 : 1;
            int offsetC = _transform.MirrorX ? 1 : 2;

            a = _transform.ToWorld(vertices[indices[indexStart]]);
            b = _transform.ToWorld(vertices[indices[indexStart + offsetB]]);
            c = _transform.ToWorld(vertices[indices[indexStart + offsetC]]);
        }

        /// <summary>
        /// The mesh's asset-space bounds in world space. Y and Z keep their
        /// ordering; the scene's X mirror swaps that pair of corners.
        /// </summary>
        private void MeshWorldBounds(
            in KartCollisionMesh mesh, out KartVec3 minimum, out KartVec3 maximum)
        {
            KartVec3 low = _transform.ToWorld(mesh.Minimum);
            KartVec3 high = _transform.ToWorld(mesh.Maximum);

            minimum = new KartVec3(MathF.Min(low.X, high.X), low.Y, low.Z);
            maximum = new KartVec3(MathF.Max(low.X, high.X), high.Y, high.Z);
        }

        private static bool BoxesOverlap(
            in KartVec3 aMin, in KartVec3 aMax, in KartVec3 bMin, in KartVec3 bMax)
            => aMin.X <= bMax.X && aMax.X >= bMin.X &&
               aMin.Y <= bMax.Y && aMax.Y >= bMin.Y &&
               aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;

        private bool MeshCanBeSkipped(
            in KartCollisionMesh mesh, in KartVec3 queryMin, in KartVec3 queryMax)
        {
            if (mesh.VertexCount == 0 || mesh.IndexCount < 3) return true;
            MeshWorldBounds(mesh, out KartVec3 meshMin, out KartVec3 meshMax);
            return !BoxesOverlap(meshMin, meshMax, queryMin, queryMax);
        }

        // ------------------------------------------------------------- ray

        private static bool SegmentTriangleIntersection(
            in KartVec3 start, in KartVec3 delta,
            in KartVec3 a, in KartVec3 b, in KartVec3 c,
            out float fraction, out KartVec3 normal)
        {
            const float epsilon = 1.0e-6f;
            fraction = 0f;
            normal = default;

            KartVec3 edgeAb = b - a;
            KartVec3 edgeAc = c - a;
            KartVec3 p = KartVec3.Cross(delta, edgeAc);
            float determinant = KartVec3.Dot(edgeAb, p);
            if (MathF.Abs(determinant) < epsilon) return false;

            float inverse = 1f / determinant;
            KartVec3 fromA = start - a;
            float u = KartVec3.Dot(fromA, p) * inverse;
            if (u < -epsilon || u > 1f + epsilon) return false;

            KartVec3 q = KartVec3.Cross(fromA, edgeAb);
            float v = KartVec3.Dot(delta, q) * inverse;
            if (v < -epsilon || u + v > 1f + epsilon) return false;

            float t = KartVec3.Dot(edgeAc, q) * inverse;
            if (t < -epsilon || t > 1f + epsilon) return false;

            fraction = MathF.Max(0f, MathF.Min(t, 1f));
            normal = KartVec3.Cross(edgeAb, edgeAc).Normalized;
            return normal.SqrMagnitude > 0f;
        }

        /// <summary>
        /// 0x00432fc0. Rejects on <c>fabs(normal.z)</c> and reports the face
        /// normal as it is — it does not turn a downward-facing face upward.
        /// </summary>
        public bool QueryGround(in KartVec3 rayStart, in KartVec3 rayDelta, out KartGroundHit hit)
        {
            hit = default;

            float nearest = 2f;
            var nearestNormal = KartVec3.Zero;
            KartVec3 rayEnd = rayStart + rayDelta;

            var queryMin = new KartVec3(
                MathF.Min(rayStart.X, rayEnd.X),
                MathF.Min(rayStart.Y, rayEnd.Y),
                MathF.Min(rayStart.Z, rayEnd.Z));
            var queryMax = new KartVec3(
                MathF.Max(rayStart.X, rayEnd.X),
                MathF.Max(rayStart.Y, rayEnd.Y),
                MathF.Max(rayStart.Z, rayEnd.Z));

            foreach (KartCollisionMesh mesh in _scene.Meshes)
            {
                if (MeshCanBeSkipped(mesh, queryMin, queryMax)) continue;

                int end = mesh.IndexStart + mesh.IndexCount;
                for (int index = mesh.IndexStart; index + 2 < end; index += 3)
                {
                    ReadTriangle(index, out KartVec3 a, out KartVec3 b, out KartVec3 c);
                    if (!SegmentTriangleIntersection(
                            rayStart, rayDelta, a, b, c, out float fraction, out KartVec3 normal))
                    {
                        continue;
                    }
                    if (MathF.Abs(normal.Z) < RoadMinNormalZ || fraction >= nearest) continue;

                    nearest = fraction;
                    nearestNormal = normal;
                }
            }

            if (nearest > 1f) return false;

            hit.Point = rayStart + rayDelta * nearest;
            hit.Normal = nearestNormal;
            hit.SurfaceId = 3u;
            return true;
        }

        // -------------------------------------------------------- body box

        /// <summary>
        /// One separating-axis test. <paramref name="p0"/>/<paramref name="p1"/>
        /// are the two distinct projections of the triangle's vertices onto the
        /// axis and <paramref name="radius"/> is the box's extent along it.
        /// </summary>
        private static bool AxisSeparates(float p0, float p1, float radius)
        {
            float low = p0 < p1 ? p0 : p1;
            float high = p0 < p1 ? p1 : p0;
            return low > radius || high < -radius;
        }

        /// <summary>
        /// Which two of the three corners each test compares. The third always
        /// projects onto one of them, and which two those are changes with the
        /// edge: 0x00434d40 reads v0/v2 for the first two edges on X and Y but
        /// v0/v1 for the third, and shifts the Z axis the other way. Using one
        /// fixed pair for all nine reports false separations.
        /// </summary>
        private static readonly int[,] FirstCorner = { { 0, 0, 1 }, { 0, 0, 0 }, { 0, 0, 1 } };
        private static readonly int[,] SecondCorner = { { 2, 2, 2 }, { 2, 2, 1 }, { 1, 1, 2 } };

        /// <summary>
        /// The test at 0x00434d40: nine box-axis-cross-edge axes, then the three
        /// box axes, then the triangle's plane. The box is axis-aligned because
        /// the caller has already rotated the triangle into the box's frame,
        /// which is how the original gets an oriented box out of an axis-aligned
        /// test.
        /// </summary>
        public static bool BoxTriangleOverlap(
            in KartVec3 half, in KartVec3 v0, in KartVec3 v1, in KartVec3 v2)
        {
            KartVec3 e0 = v1 - v0;
            KartVec3 e1 = v2 - v1;
            KartVec3 e2 = v0 - v2;

            for (int i = 0; i < 3; ++i)
            {
                KartVec3 e = i == 0 ? e0 : (i == 1 ? e1 : e2);
                float ax = MathF.Abs(e.X);
                float ay = MathF.Abs(e.Y);
                float az = MathF.Abs(e.Z);

                KartVec3 px = Corner(v0, v1, v2, FirstCorner[i, 0]);
                KartVec3 qx = Corner(v0, v1, v2, SecondCorner[i, 0]);
                KartVec3 py = Corner(v0, v1, v2, FirstCorner[i, 1]);
                KartVec3 qy = Corner(v0, v1, v2, SecondCorner[i, 1]);
                KartVec3 pz = Corner(v0, v1, v2, FirstCorner[i, 2]);
                KartVec3 qz = Corner(v0, v1, v2, SecondCorner[i, 2]);

                // axis = X cross e
                if (AxisSeparates(
                        e.Z * px.Y - e.Y * px.Z,
                        e.Z * qx.Y - e.Y * qx.Z,
                        az * half.Y + ay * half.Z)) return false;
                // axis = Y cross e
                if (AxisSeparates(
                        -e.Z * py.X + e.X * py.Z,
                        -e.Z * qy.X + e.X * qy.Z,
                        az * half.X + ax * half.Z)) return false;
                // axis = Z cross e
                if (AxisSeparates(
                        e.Y * pz.X - e.X * pz.Y,
                        e.Y * qz.X - e.X * qz.Y,
                        ay * half.X + ax * half.Y)) return false;
            }

            // The three box axes: a plain AABB overlap against the triangle.
            float lowX = MathF.Min(v0.X, MathF.Min(v1.X, v2.X));
            float highX = MathF.Max(v0.X, MathF.Max(v1.X, v2.X));
            if (lowX > half.X || highX < -half.X) return false;

            float lowY = MathF.Min(v0.Y, MathF.Min(v1.Y, v2.Y));
            float highY = MathF.Max(v0.Y, MathF.Max(v1.Y, v2.Y));
            if (lowY > half.Y || highY < -half.Y) return false;

            float lowZ = MathF.Min(v0.Z, MathF.Min(v1.Z, v2.Z));
            float highZ = MathF.Max(v0.Z, MathF.Max(v1.Z, v2.Z));
            if (lowZ > half.Z || highZ < -half.Z) return false;

            // The triangle's plane against the box.
            KartVec3 normal = KartVec3.Cross(e0, e1);
            float planeOffset = -KartVec3.Dot(normal, v0);
            float planeRadius =
                MathF.Abs(normal.X) * half.X +
                MathF.Abs(normal.Y) * half.Y +
                MathF.Abs(normal.Z) * half.Z;
            return MathF.Abs(planeOffset) <= planeRadius;
        }

        private static KartVec3 Corner(in KartVec3 v0, in KartVec3 v1, in KartVec3 v2, int index)
            => index == 0 ? v0 : (index == 1 ? v1 : v2);

        private static bool ContactIsDuplicate(
            KartBodyContact[] contacts, int count, in KartVec3 normal)
        {
            for (int i = 0; i < count; ++i)
            {
                if (KartVec3.Dot(contacts[i].Normal, normal) > ContactNormalEpsilon) return true;
            }
            return false;
        }

        /// <summary>
        /// 0x00433310. The box is built from the kart's two plan-view half
        /// extents and a fixed half-height, centred one unit up the chassis axis
        /// from the origin the wheels hang off.
        /// </summary>
        public int QueryBodyCollisions(
            KartSimulationState state, KartBodyContact[] contacts, int capacity)
        {
            if (state == null || contacts == null || capacity == 0) return 0;

            state.GetBodyAxes(
                out KartVec3 bodyRight, out KartVec3 bodyForward, out KartVec3 bodyUp);

            var half = new KartVec3(
                state.Geometry.HalfWidth, state.Geometry.HalfLength, BodyHalfHeight);
            KartVec3 centre = state.Position + bodyUp;

            float reach = MathF.Sqrt(KartVec3.Dot(half, half));
            var queryMin = new KartVec3(centre.X - reach, centre.Y - reach, centre.Z - reach);
            var queryMax = new KartVec3(centre.X + reach, centre.Y + reach, centre.Z + reach);

            int count = 0;
            foreach (KartCollisionMesh mesh in _scene.Meshes)
            {
                if (count >= capacity) break;
                if (MeshCanBeSkipped(mesh, queryMin, queryMax)) continue;

                int end = mesh.IndexStart + mesh.IndexCount;
                for (int index = mesh.IndexStart; index + 2 < end && count < capacity; index += 3)
                {
                    ReadTriangle(index, out KartVec3 a, out KartVec3 b, out KartVec3 c);

                    // Into the box's frame: the rows of the rotation are the body axes.
                    KartVec3 offsetA = a - centre;
                    KartVec3 offsetB = b - centre;
                    KartVec3 offsetC = c - centre;
                    var localA = new KartVec3(
                        KartVec3.Dot(offsetA, bodyRight),
                        KartVec3.Dot(offsetA, bodyForward),
                        KartVec3.Dot(offsetA, bodyUp));
                    var localB = new KartVec3(
                        KartVec3.Dot(offsetB, bodyRight),
                        KartVec3.Dot(offsetB, bodyForward),
                        KartVec3.Dot(offsetB, bodyUp));
                    var localC = new KartVec3(
                        KartVec3.Dot(offsetC, bodyRight),
                        KartVec3.Dot(offsetC, bodyForward),
                        KartVec3.Dot(offsetC, bodyUp));

                    if (!BoxTriangleOverlap(half, localA, localB, localC)) continue;

                    // The true face normal, with no flattening and no steepness
                    // filter: a shallow face reaches the resolver's landing branch
                    // and a steep one reaches its wall branch.
                    KartVec3 normal = KartVec3.Cross(b - a, c - a).Normalized;
                    if (normal.SqrMagnitude == 0f ||
                        ContactIsDuplicate(contacts, count, normal))
                    {
                        continue;
                    }

                    contacts[count] = new KartBodyContact
                    {
                        Normal = normal,
                        Point = (a + b + c) * (1f / 3f),
                        SweepFraction = 0.5f,
                        SurfaceId = 4u,
                    };
                    ++count;
                }
            }
            return count;
        }
    }
}
