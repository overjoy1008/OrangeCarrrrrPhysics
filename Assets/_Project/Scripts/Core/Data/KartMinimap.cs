namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Where a kart sits on a track's minimap, ported from
    /// <c>normalized_track_point</c> and <c>kart_demo_minimap_direction</c>.
    ///
    /// Both the position and the heading go through the same pair of flips, and
    /// they have to: the panel's rows grow downward while the track's +Y maps
    /// upward on the authored artwork, and every scene is mirrored in X. Flipping
    /// one and not the other puts the marker in the right place pointing the
    /// wrong way.
    /// </summary>
    public static class KartMinimap
    {
        /// <summary>The marker triangle the original draws, in its own units.</summary>
        public static readonly float[,] MarkerVertices =
        {
            { 16.587f, -16.396f },
            { 0.274f, 21.517f },
            { -16.587f, -16.396f },
        };

        public const float MarkerScale = 0.50f;

        /// <summary>
        /// The kart's position as a fraction of the panel, with 0,0 at the
        /// top-left. Clamped, so a kart outside the bounds pins to the edge
        /// rather than drawing outside the map.
        /// </summary>
        public static void NormalizedPoint(
            TrackSpec track, in KartVec3 position, out float x, out float y)
        {
            float width = track.Width;
            float length = track.Length;

            // Every scene is mirrored in X, so the map is too.
            x = 0.5f - position.X / width;
            y = 0.5f - position.Y / length;

            if (x < 0f) x = 0f;
            if (x > 1f) x = 1f;
            if (y < 0f) y = 0f;
            if (y > 1f) y = 1f;
        }

        /// <summary>
        /// A world direction in the minimap's frame. The caller negates X as well
        /// when it builds the marker, which is the mirror the position already
        /// carries.
        /// </summary>
        public static KartVec3 Direction(in KartVec3 worldDirection)
            => new KartVec3(worldDirection.X, -worldDirection.Y, worldDirection.Z);

        /// <summary>
        /// The marker's heading on the panel, as a unit vector in panel space
        /// where +x is right and +y is down.
        /// </summary>
        public static void MarkerHeading(
            in KartVec3 bodyForward, out float headingX, out float headingY)
        {
            KartVec3 forward = Direction(bodyForward);
            float x = -forward.X;
            float y = forward.Y;

            float length = System.MathF.Sqrt(x * x + y * y);
            if (length > 0f)
            {
                x /= length;
                y /= length;
            }
            headingX = x;
            headingY = y;
        }
    }
}
