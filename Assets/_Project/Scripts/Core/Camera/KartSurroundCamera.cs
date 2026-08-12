using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The two curve sets <c>SurroundCameraman::Start</c> (<c>0x004460D0</c>) builds,
    /// selected by its one argument.
    /// </summary>
    public enum KartSurroundMode
    {
        /// <summary>
        /// The orbit. This is what the time challenge's finish installs — the
        /// argument pushed at <c>0x00457239</c> is zero.
        /// </summary>
        Orbit = 0,

        /// <summary>
        /// The 2 s rush in from 21 units to 9, held afterwards. Built by the same
        /// function, but no traced call site reaches it.
        /// </summary>
        RushIn = 1,
    }

    /// <summary>
    /// The camera the finished kart is shown in, ported from <c>SurroundCameraman</c>
    /// without behavioural change.
    ///
    /// RTTI type descriptor <c>0x00599940</c>, vftable <c>0x00572834</c>:
    /// slot 7 <c>0x004460D0</c> builds the curves, slot 8 <c>0x004468D0</c> places the
    /// camera, slot 9 <c>0x00446B90</c> returns <c>L"Surround Cameraman"</c>.
    ///
    /// Three animated scalars — a distance and two angles — turn the kart's own
    /// basis and push the camera out along it:
    ///
    /// <code>
    /// basis    = kart basis * RotZ(yaw) * RotX(pitch)   0x0047EF60, 0x0047EE40, 0x0042B370
    /// position = kart + (0,0,1) + column1(basis) * distance
    /// </code>
    ///
    /// Column 1 is the port's negated forward, so the camera stands one unit above
    /// the kart, <c>distance</c> along its own backward axis, looking at it. With the
    /// yaw at zero that is the chase arrangement exactly; the orbit's keys hold it
    /// near π instead, which puts the camera in front of the kart looking back.
    ///
    /// The class writes no field of view — <see cref="KartChaseCameraPose.FieldOfViewDegrees"/>
    /// is left at zero and the caller is expected to leave the camera's own alone.
    ///
    /// See <c>analysis/SURROUND_CAMERA_RECOVERY.md</c> in the reversing tree for the
    /// full trace.
    /// </summary>
    public sealed class KartSurroundCamera
    {
        /// <summary>The height the camera is lifted above the kart, from the <c>(0,0,1)</c> add.</summary>
        public const float HeightAboveKart = 1f;

        /// <summary>
        /// The binary's own π, <c>0x40490FD8</c>. Written out rather than taken from
        /// <see cref="MathF"/> because the orbit's two yaw keys are this value minus
        /// 1.1 and plus 1, and they only land on the stored constants when they are
        /// derived from this one.
        /// </summary>
        public const float Pi = 3.141592f;

        // --- mode 0, the finish orbit ---------------------------------------
        //
        // Distance is a single key; the yaw sweeps 1.1 rad to one side of Pi and
        // 1.0 to the other on a 6 s loop, and the pitch breathes between 0.5 and
        // 0.25 rad on a 5 s loop. The periods do not divide each other, so the
        // orbit does not repeat for 30 s.

        public const float OrbitDistance = 8f;
        public const uint OrbitDistanceDurationMs = 3000u;
        public const uint OrbitYawDurationMs = 6000u;
        public const uint OrbitPitchDurationMs = 5000u;

        private static KartCameraKey[] OrbitDistanceKeys() => new[]
        {
            new KartCameraKey(0u, OrbitDistance),
        };

        private static KartCameraKey[] OrbitYawKeys() => new[]
        {
            new KartCameraKey(0u, Pi - 1.1f),
            new KartCameraKey(3000u, Pi + 1f),
            new KartCameraKey(6000u, Pi - 1.1f),
        };

        private static KartCameraKey[] OrbitPitchKeys() => new[]
        {
            new KartCameraKey(0u, 0.5f),
            new KartCameraKey(2500u, 0.25f),
            new KartCameraKey(5000u, 0.5f),
        };

        // --- mode 1, built but not reached ----------------------------------

        public const uint RushInDistanceDurationMs = 2000u;
        public const uint RushInAngleDurationMs = 3000u;

        private static KartCameraKey[] RushInDistanceKeys() => new[]
        {
            new KartCameraKey(0u, 21f),
            new KartCameraKey(500u, 13f),
            new KartCameraKey(2000u, 9f),
        };

        private static KartCameraKey[] RushInYawKeys() => new[]
        {
            new KartCameraKey(0u, Pi),
        };

        private static KartCameraKey[] RushInPitchKeys() => new[]
        {
            new KartCameraKey(0u, 0f),
        };

        private KartCameraTrack _distance;
        private KartCameraTrack _yaw;
        private KartCameraTrack _pitch;

        /// <summary>
        /// Milliseconds since the camera was installed. The original captures a
        /// base timestamp on its first update and measures against the OS clock
        /// from there; accumulating the frame lengths is the same measurement.
        /// </summary>
        private uint _elapsedMs;

        public KartSurroundMode Mode { get; private set; }

        public float Distance => _distance.Value;
        public float Yaw => _yaw.Value;
        public float Pitch => _pitch.Value;
        public uint ElapsedMs => _elapsedMs;

        public KartSurroundCamera(KartSurroundMode mode = KartSurroundMode.Orbit) => Start(mode);

        /// <summary>Slot 7. Builds the mode's three curves and rewinds the clock.</summary>
        public void Start(KartSurroundMode mode)
        {
            Mode = mode;
            _elapsedMs = 0u;

            if (mode == KartSurroundMode.RushIn)
            {
                // The distance is the one curve in either mode that does not loop:
                // it runs once and holds nine.
                _distance = new KartCameraTrack(
                    RushInDistanceKeys(), RushInDistanceDurationMs, KartTrackPlayMode.Once);
                _yaw = new KartCameraTrack(
                    RushInYawKeys(), RushInAngleDurationMs, KartTrackPlayMode.Loop);
                _pitch = new KartCameraTrack(
                    RushInPitchKeys(), RushInAngleDurationMs, KartTrackPlayMode.Loop);
                return;
            }

            _distance = new KartCameraTrack(
                OrbitDistanceKeys(), OrbitDistanceDurationMs, KartTrackPlayMode.Loop);
            _yaw = new KartCameraTrack(
                OrbitYawKeys(), OrbitYawDurationMs, KartTrackPlayMode.Loop);
            _pitch = new KartCameraTrack(
                OrbitPitchKeys(), OrbitPitchDurationMs, KartTrackPlayMode.Loop);
        }

        /// <summary>Slot 8. Places the camera for one frame.</summary>
        public KartChaseCameraPose Update(
            KartVec3 kartPosition, KartQuat kartOrientation, uint elapsedMs)
        {
            _elapsedMs += elapsedMs;

            float distance = _distance.Sample(_elapsedMs);
            float yaw = _yaw.Sample(_elapsedMs);
            float pitch = _pitch.Sample(_elapsedMs);

            return Place(kartPosition, kartOrientation, distance, yaw, pitch);
        }

        /// <summary>
        /// The placement on its own, with the curves already sampled.
        ///
        /// <c>0x0047EF60</c> is a rotation about Z and <c>0x0047EE40</c> one about X, so
        /// the product's columns are the kart's axes turned first in the ground
        /// plane and then out of it. Written out per column rather than as a matrix
        /// multiply, which is the same arithmetic with the kart's basis already in
        /// the port's right / forward / up form.
        /// </summary>
        public static KartChaseCameraPose Place(
            KartVec3 kartPosition,
            KartQuat kartOrientation,
            float distance,
            float yaw,
            float pitch)
        {
            kartOrientation.GetAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up);

            float cosYaw = MathF.Cos(yaw);
            float sinYaw = MathF.Sin(yaw);
            float cosPitch = MathF.Cos(pitch);
            float sinPitch = MathF.Sin(pitch);

            // The engine's column 1 is the negated forward, which is why every
            // term carrying it changes sign here.
            KartVec3 column0 = right * cosYaw - forward * sinYaw;

            KartVec3 column1 =
                right * (-sinYaw * cosPitch) -
                forward * (cosYaw * cosPitch) +
                up * sinPitch;

            KartVec3 column2 =
                right * (sinYaw * sinPitch) +
                forward * (cosYaw * sinPitch) +
                up * cosPitch;

            return new KartChaseCameraPose
            {
                Position = new KartVec3(
                    kartPosition.X + column1.X * distance,
                    kartPosition.Y + column1.Y * distance,
                    kartPosition.Z + HeightAboveKart + column1.Z * distance),
                Right = column0,
                Forward = new KartVec3(-column1.X, -column1.Y, -column1.Z),
                Up = column2,

                // Written here for the record: the class sets no field of view.
                FieldOfViewDegrees = 0f,
            };
        }
    }
}
