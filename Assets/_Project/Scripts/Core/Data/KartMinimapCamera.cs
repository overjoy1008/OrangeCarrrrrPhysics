using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The original's rotating minimap camera, ported from
    /// <c>kart_demo_draw_original_minimap_camera</c>.
    ///
    /// The map is not drawn square-on. The original hangs a camera behind and
    /// above the kart, pointing along its heading, and projects the flat 256x256
    /// artwork on the ground plane through it — so the map tilts into the
    /// distance and swings round as the kart turns. Every pixel of the panel is a
    /// ray cast at the z = 0 plane and a clamped sample of the image, which is
    /// what this reproduces.
    ///
    /// The camera lags the kart: it slerps toward the kart's orientation with a
    /// 1500 ms time constant, so a flick of the wheel does not spin the map.
    /// Nothing here is smoothed apart from that — the marker uses the kart's own
    /// unsmoothed heading, which is why it leads the map through a corner.
    /// </summary>
    public sealed class KartMinimapCamera
    {
        /// <summary>How far behind and above the kart the camera sits, in map pixels.</summary>
        public const float Distance = 60f;

        /// <summary>The slerp time constant.</summary>
        public const float FollowMs = 1500f;

        /// <summary>
        /// The alpha the original's <c>AlphaBlend</c> uses — 77/255, the
        /// <c>minimap.1s</c> TexProperty alpha of 0.3.
        /// </summary>
        public const float Alpha = 77f / 255f;

        private KartQuat _orientation;
        private uint _timeMs;
        private bool _initialized;

        /// <summary>The camera basis, in the map image's own space.</summary>
        public KartVec3 Right { get; private set; }

        /// <summary>The axis the camera looks back along; the ray's base is its negation.</summary>
        public KartVec3 Back { get; private set; }

        public KartVec3 Up { get; private set; }

        /// <summary>The camera's position in map space, z above the image plane.</summary>
        public KartVec3 Position { get; private set; }

        /// <summary>Where the kart sits on the image, in pixels.</summary>
        public float MapX { get; private set; }

        public float MapY { get; private set; }

        /// <summary>The kart's own heading, unsmoothed — what the marker is drawn with.</summary>
        public KartQuat Target { get; private set; }

        public void Reset() => _initialized = false;

        /// <summary>
        /// Advances the camera one frame and recomputes the basis and position.
        /// </summary>
        public void Step(
            TrackSpec track,
            KartMinimapMapping mapping,
            in KartVec3 worldPosition,
            in KartQuat worldOrientation,
            uint frameTimeMs)
        {
            if (track == null || mapping == null) return;

            // Asset space to simulator space is S = diag(-1, 1, 1), so the
            // orientation comes back through R_asset = S * R_world * S.
            Target = new KartQuat(
                worldOrientation.W, worldOrientation.X,
                -worldOrientation.Y, -worldOrientation.Z);

            if (!_initialized)
            {
                _orientation = Target;
                _timeMs = frameTimeMs;
                _initialized = true;
            }
            else
            {
                uint elapsed = frameTimeMs - _timeMs;
                float t = elapsed / FollowMs;
                if (t > 1f) t = 1f;

                // The short way round, so a heading that crosses the branch cut
                // does not send the map the long way about.
                float dot = KartQuat.Dot(_orientation, Target);
                if (dot < 0f) _orientation = -_orientation;

                _orientation = KartChaseCamera.Interpolate(_orientation, Target, t);
                _timeMs = frameTimeMs;
            }

            KartVec3 forward = Flattened(_orientation);
            KartVec3 back = Normalize(new KartVec3(-forward.X, -forward.Y, 1f));
            KartVec3 right = Normalize(KartVec3.Cross(back, forward));
            KartVec3 up = Normalize(KartVec3.Cross(right, back));

            Back = back;
            Right = right;
            Up = up;

            // The simulator's centred X mirror is undone before the mapping is
            // applied, because ToMinimap was authored against the asset's axes.
            float centerX = (track.Minimum.X + track.Maximum.X) * 0.5f;
            float centerY = (track.Minimum.Y + track.Maximum.Y) * 0.5f;

            MapX = mapping.Width * 0.5f +
                   ((centerX - worldPosition.X) - mapping.OriginX) * mapping.Scale;
            MapY = mapping.Height * 0.5f +
                   ((centerY + worldPosition.Y) - mapping.OriginY) * mapping.Scale;

            Position = new KartVec3(
                MapX + back.X * Distance,
                MapY + back.Y * Distance,
                back.Z * Distance);
        }

        /// <summary>
        /// The kart marker's three corners, projected through the camera into
        /// panel space: 0,0 at the top-left, 1,1 at the bottom-right.
        ///
        /// The marker is built from the kart's unsmoothed heading and lifted a
        /// tenth of a unit off the image plane, both of which are the original's.
        /// </summary>
        public void ProjectMarker(float[,] corners)
        {
            KartVec3 forward = Flattened(Target);
            KartVec3 across = Normalize(KartVec3.Cross(forward, KartVec3.UnitZ));

            for (int corner = 0; corner < 3; ++corner)
            {
                float side = KartMinimap.MarkerVertices[corner, 0] * KartMinimap.MarkerScale;
                float along = KartMinimap.MarkerVertices[corner, 1] * KartMinimap.MarkerScale;

                var point = new KartVec3(
                    MapX + across.X * side + forward.X * along,
                    MapY + across.Y * side + forward.Y * along,
                    0.1f);

                KartVec3 delta = point - Position;
                float localX = KartVec3.Dot(delta, Right);
                float localY = KartVec3.Dot(delta, Back);
                float localZ = KartVec3.Dot(delta, Up);

                // The panel is drawn with its X reversed relative to the recovered
                // camera, so the marker is reversed with it — half of the same
                // mirror the background sampling applies.
                float depth = -localY;
                if (depth <= 0f) depth = 1e-4f;

                corners[corner, 0] = 0.5f - localX / depth * 0.5f;
                corners[corner, 1] = 0.5f - localZ / depth * 0.5f;
            }
        }

        /// <summary>The body's forward axis, flattened onto the map plane.</summary>
        private static KartVec3 Flattened(KartQuat q)
        {
            var forward = new KartVec3(
                -2f * (q.X * q.Y - q.W * q.Z),
                -(1f - 2f * (q.X * q.X + q.Z * q.Z)),
                0f);
            return Normalize(forward);
        }

        private static KartVec3 Normalize(KartVec3 value)
        {
            float length = MathF.Sqrt(KartVec3.Dot(value, value));
            return length > 0f ? value * (1f / length) : KartVec3.Zero;
        }
    }
}
