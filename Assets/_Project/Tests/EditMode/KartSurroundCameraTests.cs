using System;
using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The finish camera, against the constants and the arithmetic recovered from
    /// <c>SurroundCameraman</c>.
    ///
    /// The curves are checked at their keys, where the binary's own numbers are
    /// the expected values, and the placement is checked at the two yaw angles
    /// whose geometry is known without redoing the matrix product: zero, which is
    /// the chase arrangement, and π, which is the mirror of it.
    /// </summary>
    public sealed class KartSurroundCameraTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>Facing along engine +Y with Z up, which is identity here.</summary>
        private static readonly KartQuat Level = KartQuat.Identity;

        private static void AssertVector(KartVec3 actual, KartVec3 expected, string what)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tolerance), what + " x");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tolerance), what + " y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tolerance), what + " z");
        }

        // --- the keyframe track ---------------------------------------------

        [Test]
        public void SingleKeyTrackIsConstant()
        {
            var track = new KartCameraTrack(
                new[] { new KartCameraKey(0u, 8f) }, 3000u, KartTrackPlayMode.Loop);

            Assert.That(track.Sample(0u), Is.EqualTo(8f));
            Assert.That(track.Sample(1500u), Is.EqualTo(8f));
            Assert.That(track.Sample(999999u), Is.EqualTo(8f));
        }

        [Test]
        public void TrackInterpolatesLinearlyBetweenKeys()
        {
            var track = new KartCameraTrack(
                new[]
                {
                    new KartCameraKey(0u, 0f),
                    new KartCameraKey(1000u, 10f),
                },
                1000u,
                KartTrackPlayMode.Once);

            Assert.That(track.Sample(0u), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(track.Sample(250u), Is.EqualTo(2.5f).Within(Tolerance));
            Assert.That(track.Sample(500u), Is.EqualTo(5f).Within(Tolerance));
            Assert.That(track.Sample(1000u), Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void LoopingTrackWrapsAndRewindsItsCursor()
        {
            var track = new KartCameraTrack(
                new[]
                {
                    new KartCameraKey(0u, 0f),
                    new KartCameraKey(1000u, 10f),
                    new KartCameraKey(2000u, 0f),
                },
                2000u,
                KartTrackPlayMode.Loop);

            Assert.That(track.Sample(500u), Is.EqualTo(5f).Within(Tolerance));
            Assert.That(track.Sample(1500u), Is.EqualTo(5f).Within(Tolerance));

            // Past the duration the timer wraps, which sends the time backwards and
            // has to rewind the cursor rather than hold the last key.
            Assert.That(track.Sample(2500u), Is.EqualTo(5f).Within(Tolerance));
            Assert.That(track.Sample(4500u), Is.EqualTo(5f).Within(Tolerance));
            Assert.That(track.Running, Is.True, "A looping track never stops.");
        }

        [Test]
        public void PlayOnceTrackClampsAndStops()
        {
            var track = new KartCameraTrack(
                new[]
                {
                    new KartCameraKey(0u, 21f),
                    new KartCameraKey(500u, 13f),
                    new KartCameraKey(2000u, 9f),
                },
                2000u,
                KartTrackPlayMode.Once);

            Assert.That(track.Sample(0u), Is.EqualTo(21f).Within(Tolerance));
            Assert.That(track.Sample(500u), Is.EqualTo(13f).Within(Tolerance));
            Assert.That(track.Sample(1250u), Is.EqualTo(11f).Within(Tolerance));
            Assert.That(track.Sample(2000u), Is.EqualTo(9f).Within(Tolerance));

            Assert.That(track.Sample(60000u), Is.EqualTo(9f).Within(Tolerance),
                "Play-once holds the last value instead of wrapping.");
            Assert.That(track.Running, Is.False);
        }

        // --- the orbit's curves ---------------------------------------------

        [Test]
        public void OrbitCurvesMatchTheRecoveredKeys()
        {
            var camera = new KartSurroundCamera(KartSurroundMode.Orbit);
            var position = new KartVec3(0f, 0f, 0f);

            camera.Update(position, Level, 0u);
            Assert.That(camera.Distance, Is.EqualTo(8f).Within(Tolerance));
            Assert.That(camera.Yaw, Is.EqualTo(KartSurroundCamera.Pi - 1.1f).Within(Tolerance));
            Assert.That(camera.Pitch, Is.EqualTo(0.5f).Within(Tolerance));

            camera.Update(position, Level, 2500u);
            Assert.That(camera.Pitch, Is.EqualTo(0.25f).Within(Tolerance),
                "The pitch bottoms out at 2500 ms.");

            camera.Update(position, Level, 500u);
            Assert.That(camera.Yaw, Is.EqualTo(KartSurroundCamera.Pi + 1f).Within(Tolerance),
                "The yaw peaks at 3000 ms.");

            // 6000 and 5000 do not divide each other, so at 15 s the yaw is at its
            // start again and the pitch is not.
            camera.Start(KartSurroundMode.Orbit);
            camera.Update(position, Level, 12000u);
            Assert.That(camera.Yaw, Is.EqualTo(KartSurroundCamera.Pi - 1.1f).Within(Tolerance));
            Assert.That(camera.Pitch, Is.Not.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void RushInModeRunsOnceAndHolds()
        {
            var camera = new KartSurroundCamera(KartSurroundMode.RushIn);
            var position = new KartVec3(0f, 0f, 0f);

            camera.Update(position, Level, 0u);
            Assert.That(camera.Distance, Is.EqualTo(21f).Within(Tolerance));
            Assert.That(camera.Yaw, Is.EqualTo(KartSurroundCamera.Pi).Within(Tolerance));
            Assert.That(camera.Pitch, Is.EqualTo(0f).Within(Tolerance));

            camera.Update(position, Level, 500u);
            Assert.That(camera.Distance, Is.EqualTo(13f).Within(Tolerance));

            camera.Update(position, Level, 30000u);
            Assert.That(camera.Distance, Is.EqualTo(9f).Within(Tolerance),
                "The rush-in holds nine rather than looping back out to 21.");
        }

        // --- the placement ---------------------------------------------------

        [Test]
        public void ZeroYawAndPitchIsTheChaseArrangement()
        {
            // Identity orientation: right is +X, forward is -Y, up is +Z.
            Level.GetAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up);

            var kart = new KartVec3(10f, 20f, 3f);
            KartChaseCameraPose pose = KartSurroundCamera.Place(kart, Level, 8f, 0f, 0f);

            AssertVector(pose.Right, right, "right");
            AssertVector(pose.Forward, forward, "forward");
            AssertVector(pose.Up, up, "up");

            // Behind the kart along its own forward, one unit up.
            AssertVector(
                pose.Position,
                new KartVec3(
                    kart.X - forward.X * 8f,
                    kart.Y - forward.Y * 8f,
                    kart.Z + KartSurroundCamera.HeightAboveKart - forward.Z * 8f),
                "position");
        }

        [Test]
        public void HalfTurnPutsTheCameraInFrontLookingBack()
        {
            Level.GetAxes(out _, out KartVec3 forward, out _);

            var kart = new KartVec3(0f, 0f, 0f);
            KartChaseCameraPose pose = KartSurroundCamera.Place(kart, Level, 8f, MathF.PI, 0f);

            AssertVector(pose.Forward, -forward, "forward points back at the kart");
            AssertVector(
                pose.Position,
                new KartVec3(
                    forward.X * 8f,
                    forward.Y * 8f,
                    KartSurroundCamera.HeightAboveKart + forward.Z * 8f),
                "position is ahead of the kart");
        }

        [Test]
        public void ThePoseStaysOrthonormalUnderBothAngles()
        {
            KartChaseCameraPose pose = KartSurroundCamera.Place(
                new KartVec3(4f, -7f, 2f), Level, 8f, KartSurroundCamera.Pi - 1.1f, 0.5f);

            Assert.That(pose.Right.Magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(pose.Forward.Magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(pose.Up.Magnitude, Is.EqualTo(1f).Within(Tolerance));

            Assert.That(KartVec3.Dot(pose.Right, pose.Forward), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(KartVec3.Dot(pose.Right, pose.Up), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(KartVec3.Dot(pose.Forward, pose.Up), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void TheCameraAlwaysLooksAtTheKart()
        {
            var kart = new KartVec3(12f, -5f, 1.5f);
            var camera = new KartSurroundCamera(KartSurroundMode.Orbit);

            // Walked across a whole yaw period, since the swing is what could aim
            // it away from the kart.
            for (uint step = 0; step < 40; ++step)
            {
                KartChaseCameraPose pose = camera.Update(kart, Level, 160u);

                KartVec3 toKart = (new KartVec3(
                    kart.X, kart.Y, kart.Z + KartSurroundCamera.HeightAboveKart) - pose.Position)
                    .Normalized;

                Assert.That(KartVec3.Dot(toKart, pose.Forward), Is.EqualTo(1f).Within(Tolerance),
                    $"step {step}: the view axis is the line to the kart");
            }
        }

        [Test]
        public void TheClassWritesNoFieldOfView()
        {
            KartChaseCameraPose pose = KartSurroundCamera.Place(
                KartVec3.Zero, Level, 8f, 1f, 0.5f);

            Assert.That(pose.FieldOfViewDegrees, Is.EqualTo(0f),
                "SurroundCameraman sets none, so the rig's own must be used.");
        }
    }
}
