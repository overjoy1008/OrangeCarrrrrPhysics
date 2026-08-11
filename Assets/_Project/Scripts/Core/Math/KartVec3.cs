using System;
using System.Runtime.CompilerServices;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// A vector in the original engine's frame: X and Y are the ground plane and
    /// Z is height. Deliberately not <c>UnityEngine.Vector3</c> — the two frames
    /// differ in both up-axis and handedness, and keeping them as distinct types
    /// makes every conversion an explicit call to <c>KartSpace</c>.
    /// </summary>
    [Serializable]
    public struct KartVec3 : IEquatable<KartVec3>
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly KartVec3 Zero = default;
        public static readonly KartVec3 UnitX = new KartVec3(1f, 0f, 0f);
        public static readonly KartVec3 UnitY = new KartVec3(0f, 1f, 0f);
        public static readonly KartVec3 UnitZ = new KartVec3(0f, 0f, 1f);

        public KartVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    default: throw new IndexOutOfRangeException(nameof(index));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KartVec3 operator +(KartVec3 a, KartVec3 b)
            => new KartVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KartVec3 operator -(KartVec3 a, KartVec3 b)
            => new KartVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KartVec3 operator -(KartVec3 a)
            => new KartVec3(-a.X, -a.Y, -a.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KartVec3 operator *(KartVec3 a, float s)
            => new KartVec3(a.X * s, a.Y * s, a.Z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KartVec3 operator *(float s, KartVec3 a) => a * s;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(KartVec3 a, KartVec3 b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KartVec3 Cross(KartVec3 a, KartVec3 b)
            => new KartVec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;

        public float Magnitude => MathF.Sqrt(X * X + Y * Y + Z * Z);

        public KartVec3 Normalized
        {
            get
            {
                float length = Magnitude;
                if (length <= 0f) return Zero;
                float inverse = 1f / length;
                return new KartVec3(X * inverse, Y * inverse, Z * inverse);
            }
        }

        public bool Equals(KartVec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is KartVec3 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }
}
