using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Orientation quaternion in the original engine's frame, stored W first the
    /// way <c>KartQuat</c> does in C.
    /// </summary>
    [Serializable]
    public struct KartQuat : IEquatable<KartQuat>
    {
        public float W;
        public float X;
        public float Y;
        public float Z;

        public static readonly KartQuat Identity = new KartQuat(1f, 0f, 0f, 0f);

        public KartQuat(float w, float x, float y, float z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }

        public static float Dot(KartQuat a, KartQuat b)
            => a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public KartQuat Normalized
        {
            get
            {
                float length = MathF.Sqrt(W * W + X * X + Y * Y + Z * Z);
                if (length <= 0f) return Identity;
                float inverse = 1f / length;
                return new KartQuat(W * inverse, X * inverse, Y * inverse, Z * inverse);
            }
        }

        public static KartQuat operator -(KartQuat q) => new KartQuat(-q.W, -q.X, -q.Y, -q.Z);

        /// <summary>
        /// Columns of the rotation matrix <c>0x0042B640</c> builds, picked out by
        /// <c>0x0042B330</c>: column 0 is body right, column 2 is body up, and the
        /// engine's forward is the negated column 1.
        /// </summary>
        public void GetAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up)
        {
            float xx = X * X;
            float yy = Y * Y;
            float zz = Z * Z;
            float xy = X * Y;
            float xz = X * Z;
            float yz = Y * Z;
            float wx = W * X;
            float wy = W * Y;
            float wz = W * Z;

            right = new KartVec3(
                1f - 2f * (yy + zz),
                2f * (xy + wz),
                2f * (xz - wy));
            forward = new KartVec3(
                -2f * (xy - wz),
                -(1f - 2f * (xx + zz)),
                -2f * (yz + wx));
            up = new KartVec3(
                2f * (xz + wy),
                2f * (yz - wx),
                1f - 2f * (xx + yy));
        }

        /// <summary>Body up's Z component, which the tilt clamp watches.</summary>
        public float UpZ => 1f - 2f * (X * X + Y * Y);

        public bool Equals(KartQuat other)
            => W == other.W && X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is KartQuat other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(W, X, Y, Z);

        public override string ToString() => $"({W:F3}, {X:F3}, {Y:F3}, {Z:F3})";
    }
}
