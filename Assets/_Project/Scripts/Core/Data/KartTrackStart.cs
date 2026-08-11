namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Where a track puts a kart, ported from <c>kart_demo_data.c</c>.
    ///
    /// The start line's position is known from the mesh — it is the centroid of
    /// the road-flagged quad whose texture names it — but nothing in
    /// <c>track.1s</c> says which way round the lap is driven. Everything below
    /// <see cref="KartTrackStartKind.Confirmed"/> therefore assumes the +axis
    /// direction. village_R01 is one of only two tracks whose direction was
    /// checked against the original game.
    /// </summary>
    public static class KartTrackStart
    {
        /// <summary>
        /// The spawn point in world space: the start quad's centroid, mirrored and
        /// re-centred the same way the scene is, with z at the ground plane.
        /// </summary>
        public static bool Position(TrackSpec track, out KartVec3 position)
        {
            position = default;
            if (track == null || track.StartKind == KartTrackStartKind.None) return false;

            float centerX = (track.Minimum.X + track.Maximum.X) * 0.5f;
            float centerY = (track.Minimum.Y + track.Maximum.Y) * 0.5f;

            position = new KartVec3(
                // Every scene is mirrored in X, so the start point is too.
                centerX - track.StartLine.X,
                track.StartLine.Y - centerY,
                0f);
            return true;
        }

        /// <summary>
        /// The asset-space height the scene is dropped by, so that the plane the
        /// start line sits on ends up at world z 0.
        /// </summary>
        public static float SceneGroundZ(TrackSpec track)
        {
            if (track == null) return 0f;
            return track.StartKind == KartTrackStartKind.None
                ? track.Minimum.Z
                : track.StartLine.Z;
        }

        /// <summary>
        /// Below this the kart has left the world, ported from
        /// <c>kart_demo_track_fall_limit</c>.
        ///
        /// A scene only carries road triangles where the original track had road,
        /// so going off an edge drops the kart into a void it can never land in.
        /// Forty units of clearance under the lowest geometry is wide enough that
        /// a hard landing on the lowest road is never mistaken for a fall.
        ///
        /// This matters more here than it does in the C build: eleven of the
        /// thirteen tracks have an assumed driving direction, so a kart that is
        /// pointing the wrong way leaves the road almost immediately.
        /// </summary>
        public static float FallLimit(TrackSpec track)
        {
            const float margin = 40f;
            if (track == null) return -margin;

            float floor = track.Minimum.Z - SceneGroundZ(track);
            if (floor > 0f) floor = 0f;
            return floor - margin;
        }

        /// <summary>
        /// The facing. A track with no start line still gets the same facing as
        /// every other track — only its spawn position falls back to the bounds
        /// centre. Leaving the raw default would point it at -Y, opposite to all
        /// thirteen real tracks.
        ///
        /// The default body basis has forward at -Y and body-right at +X. A
        /// stripe running along X is driven along Y, assumed +Y: a 180-degree Z
        /// rotation turns forward from -Y to +Y and body-right from +X to -X
        /// together, so steering and camera handedness are preserved. A stripe
        /// running along Y is driven along X, assumed +X — the same pose turned a
        /// further -90 degrees about Z.
        /// </summary>
        public static KartQuat Orientation(TrackSpec track)
        {
            if (track != null && track.StartAxis == KartTrackStartAxis.X)
            {
                const float halfRootTwo = 0.70710678f;
                return new KartQuat(halfRootTwo, 0f, 0f, halfRootTwo);
            }
            return new KartQuat(0f, 0f, 0f, 1f);
        }
    }
}
