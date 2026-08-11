using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The single boundary between the engine frame and Unity's.
    ///
    /// The original puts X and Y on the ground with Z up, and at identity a kart's
    /// body axes are right (1,0,0), forward (0,-1,0), up (0,0,1):
    ///
    ///     engine (x, y, z)  ->  Unity (x, z, -y)
    ///
    /// which sends engine right to Unity right, engine up to Unity up, and engine
    /// forward (-Y) to Unity forward (+Z).
    ///
    /// This mapping is the one the track renders correctly under, and the track is
    /// the thing with a fixed reference to check against: the minimap artwork ships
    /// with the archives and is not derived from anything here. It was briefly
    /// swapped for (x, z, y) to explain the kart lettering coming out backwards,
    /// which fixed the karts and mirrored the track with them. That is the evidence
    /// that the two disagree in the data rather than in this mapping — kart models
    /// come out of a different exporter path from track scenes, and the original
    /// carries its own placement for them in kart_model_vertex. Any correction for
    /// the karts belongs on the kart import, not here.
    ///
    ///   * positions, velocities, forces, angular velocities and torques all use
    ///     the same mapping;
    ///   * index buffers are copied verbatim, and the track renders the right way
    ///     out under that.
    ///
    /// Nothing upstream of this class sees a <c>Vector3</c>, and nothing downstream
    /// sees a <c>KartVec3</c>. Simulation state stays in the engine frame end to
    /// end, so the recovered constants and the oracle tests keep their meaning.
    /// </summary>
    public static class KartSpace
    {
        /// <summary>
        /// The original's world gravity, <c>-58.8</c> rather than -9.8. The tyre
        /// forces separately use 9.8, so the two are not interchangeable and the
        /// world is not metres: rescaling to metres would break their ratio.
        /// Never fed to <c>Physics.gravity</c> — the port does not use Unity's
        /// rigid-body solver at all.
        /// </summary>
        public const float WorldGravityZ = -58.79999923706055f;

        /// <summary>
        /// Any vector quantity: position, velocity, force, angular velocity,
        /// torque, or a surface normal.
        /// </summary>
        public static Vector3 ToUnity(in KartVec3 v) => new Vector3(v.X, v.Z, -v.Y);

        /// <summary>The inverse of <see cref="ToUnity(in KartVec3)"/>.</summary>
        public static KartVec3 ToKart(in Vector3 v) => new KartVec3(v.x, -v.z, v.y);

        /// <summary>
        /// Orientation. The quaternion's scalar part is unchanged and its axis is
        /// carried across by the same mapping the vectors use.
        /// </summary>
        public static Quaternion ToUnity(in KartQuat q) => new Quaternion(q.X, q.Z, -q.Y, q.W);

        public static KartQuat ToKart(in Quaternion q) => new KartQuat(q.w, q.x, -q.z, q.y);

        /// <summary>
        /// An axis-aligned box given by engine-frame corners, as a Unity Bounds.
        /// The rotation moves which corner is which, so both are re-minimised.
        /// </summary>
        public static Bounds ToUnityBounds(in KartVec3 minimum, in KartVec3 maximum)
        {
            Vector3 a = ToUnity(minimum);
            Vector3 b = ToUnity(maximum);
            var bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b));
            return bounds;
        }
    }
}
