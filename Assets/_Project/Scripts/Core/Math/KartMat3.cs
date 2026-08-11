using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>A 3x3 matrix, used only for the inverse inertia tensor.</summary>
    [Serializable]
    public struct KartMat3
    {
        public float M00, M01, M02;
        public float M10, M11, M12;
        public float M20, M21, M22;

        public static KartMat3 Diagonal(float value) => new KartMat3
        {
            M00 = value,
            M11 = value,
            M22 = value,
        };

        public KartVec3 Multiply(in KartVec3 v) => new KartVec3(
            M00 * v.X + M01 * v.Y + M02 * v.Z,
            M10 * v.X + M11 * v.Y + M12 * v.Z,
            M20 * v.X + M21 * v.Y + M22 * v.Z);

        /// <summary>
        /// Recovered from 0x0042e5d5-0x0042e623: an isotropic tensor whose
        /// diagonal is 12 / mass.
        /// </summary>
        public static KartMat3 DefaultInverseInertia(float mass)
            => mass > 0f ? Diagonal(12f / mass) : default;
    }
}
