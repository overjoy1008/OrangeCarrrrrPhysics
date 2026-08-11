using System;
using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The collision queries and the race-start machinery, pinned against the
    /// thresholds the recovery established.
    /// </summary>
    public sealed class KartCollisionTests
    {
        /// <summary>
        /// A single flat quad in asset space, big enough to stand on. Built with
        /// the winding the exporter produces, so it exercises the mirror
        /// compensation rather than side-stepping it.
        /// </summary>
        private static KartCollisionScene FlatQuad(float z = 0f, float extent = 50f)
        {
            var scene = new KartCollisionScene
            {
                Vertices = new[]
                {
                    new KartVec3(-extent, -extent, z),
                    new KartVec3(extent, -extent, z),
                    new KartVec3(extent, extent, z),
                    new KartVec3(-extent, extent, z),
                },
                Indices = new[] { 0, 1, 2, 0, 2, 3 },
            };
            scene.Meshes = new[]
            {
                new KartCollisionMesh
                {
                    VertexStart = 0,
                    VertexCount = 4,
                    IndexStart = 0,
                    IndexCount = 6,
                    Minimum = new KartVec3(-extent, -extent, z),
                    Maximum = new KartVec3(extent, extent, z),
                },
            };
            return scene;
        }

        private static KartTrackTransform Identity => new KartTrackTransform
        {
            CenterX = 0f,
            CenterY = 0f,
            GroundZ = 0f,
            MirrorX = true,
        };

        [Test]
        public void GroundRay_HitsTheRoadAndReportsAnUpwardNormal()
        {
            var collision = new KartTrackCollision(FlatQuad(), Identity);

            bool hit = collision.QueryGround(
                new KartVec3(0f, 0f, 1f), new KartVec3(0f, 0f, -2f), out KartGroundHit ground);

            Assert.That(hit, Is.True);
            Assert.That(ground.Point.Z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(ground.Normal.Z, Is.GreaterThan(0.99f),
                "the mirror compensation must leave the road facing up");
            Assert.That(ground.SurfaceId, Is.EqualTo(3u));
        }

        [Test]
        public void GroundRay_MissesWhenTheSegmentStopsShort()
        {
            var collision = new KartTrackCollision(FlatQuad(), Identity);

            bool hit = collision.QueryGround(
                new KartVec3(0f, 0f, 5f), new KartVec3(0f, 0f, -1f), out _);

            Assert.That(hit, Is.False);
        }

        /// <summary>
        /// The ray query skips anything steeper than 0.65, so walls are invisible
        /// to the wheels — the body box is what finds them.
        /// </summary>
        [Test]
        public void GroundRay_IgnoresWalls()
        {
            var scene = new KartCollisionScene
            {
                // A vertical quad in the X=0 plane: its normal has zero Z.
                Vertices = new[]
                {
                    new KartVec3(0f, -10f, -10f),
                    new KartVec3(0f, 10f, -10f),
                    new KartVec3(0f, 10f, 10f),
                    new KartVec3(0f, -10f, 10f),
                },
                Indices = new[] { 0, 1, 2, 0, 2, 3 },
            };
            scene.Meshes = new[]
            {
                new KartCollisionMesh
                {
                    VertexStart = 0, VertexCount = 4, IndexStart = 0, IndexCount = 6,
                    Minimum = new KartVec3(0f, -10f, -10f),
                    Maximum = new KartVec3(0f, 10f, 10f),
                },
            };

            var collision = new KartTrackCollision(scene, Identity);
            bool hit = collision.QueryGround(
                new KartVec3(-1f, 0f, 0f), new KartVec3(2f, 0f, 0f), out _);

            Assert.That(hit, Is.False);
        }

        /// <summary>
        /// The body box is what finds walls. It is centred a unit up the chassis
        /// axis with a 0.7 half-height, so it spans 0.3 to 1.7 above the wheel
        /// contact and deliberately does not touch the road the kart is standing
        /// on — that is the wheel rays' job.
        /// </summary>
        [Test]
        public void BodyBox_FindsAWallBesideTheKart()
        {
            var scene = new KartCollisionScene
            {
                // A vertical quad inside the box's half-width, which is
                // cotten5's 0.875. A wall further out than that is simply not
                // touching the kart yet.
                Vertices = new[]
                {
                    new KartVec3(0.5f, -10f, -5f),
                    new KartVec3(0.5f, 10f, -5f),
                    new KartVec3(0.5f, 10f, 5f),
                    new KartVec3(0.5f, -10f, 5f),
                },
                Indices = new[] { 0, 1, 2, 0, 2, 3 },
            };
            scene.Meshes = new[]
            {
                new KartCollisionMesh
                {
                    VertexStart = 0, VertexCount = 4, IndexStart = 0, IndexCount = 6,
                    Minimum = new KartVec3(0.5f, -10f, -5f),
                    Maximum = new KartVec3(0.5f, 10f, 5f),
                },
            };

            var collision = new KartTrackCollision(scene, Identity);
            KartSpec spec = KartDemoData.Cotten5;

            var state = new KartSimulationState();
            KartSimulation.Init(state, spec.Dynamics, spec.Geometry);
            state.Position = KartVec3.Zero;

            var contacts = new KartBodyContact[KartSimulation.MaxBodyContacts];
            int count = collision.QueryBodyCollisions(
                state, contacts, KartSimulation.MaxBodyContacts);

            Assert.That(count, Is.GreaterThan(0), "the wall is inside the body box");
            Assert.That(MathF.Abs(contacts[0].Normal.Z), Is.LessThan(0.01f),
                "a vertical face has no vertical normal component");
        }

        /// <summary>
        /// The road the kart stands on is below the body box, so it produces no
        /// body contact. Confirming this matters: if it did, every frame on flat
        /// ground would run the collision resolver.
        /// </summary>
        [Test]
        public void BodyBox_DoesNotSeeTheRoadUnderneath()
        {
            var collision = new KartTrackCollision(FlatQuad(), Identity);
            KartSpec spec = KartDemoData.Cotten5;

            var state = new KartSimulationState();
            KartSimulation.Init(state, spec.Dynamics, spec.Geometry);
            state.Position = KartVec3.Zero;

            var contacts = new KartBodyContact[KartSimulation.MaxBodyContacts];
            Assert.That(
                collision.QueryBodyCollisions(state, contacts, KartSimulation.MaxBodyContacts),
                Is.EqualTo(0));
        }

        [Test]
        public void BodyBox_FindsNothingWhenClear()
        {
            var collision = new KartTrackCollision(FlatQuad(), Identity);
            KartSpec spec = KartDemoData.Cotten5;

            var state = new KartSimulationState();
            KartSimulation.Init(state, spec.Dynamics, spec.Geometry);
            state.Position = new KartVec3(0f, 0f, 50f);

            var contacts = new KartBodyContact[KartSimulation.MaxBodyContacts];
            Assert.That(
                collision.QueryBodyCollisions(state, contacts, KartSimulation.MaxBodyContacts),
                Is.EqualTo(0));
        }

        /// <summary>
        /// A steep normal takes the wall branch: the approach speed is removed
        /// with a 1.5x impulse and the vertical part of that correction is
        /// dropped, so a wall cannot launch a kart.
        /// </summary>
        [Test]
        public void CollisionResponse_WallBranchRemovesApproachSpeed()
        {
            var input = new KartCollisionInput
            {
                Velocity = new KartVec3(-10f, 0f, 0f),
                AngularVelocity = KartVec3.Zero,
                Normal = new KartVec3(1f, 0f, 0f),
                BodyRight = new KartVec3(1f, 0f, 0f),
                BodyForward = new KartVec3(0f, -1f, 0f),
                BodyUp = new KartVec3(0f, 0f, 1f),
            };

            KartCollisionOutput output = KartDynamics.ResolveLinearCollision(input);

            Assert.That(output.Incoming, Is.True);
            Assert.That(output.WallContact, Is.True);
            Assert.That(output.NormalSpeed, Is.EqualTo(10f).Within(0.001f));
            Assert.That(output.Velocity.X, Is.GreaterThan(0f), "it should be pushed back out");
            Assert.That(output.Velocity.Z, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void CollisionResponse_ShallowNormalTakesTheLandingBranch()
        {
            var input = new KartCollisionInput
            {
                Velocity = new KartVec3(0f, 0f, -10f),
                Normal = new KartVec3(0f, 0f, 1f),
                BodyRight = new KartVec3(1f, 0f, 0f),
                BodyForward = new KartVec3(0f, -1f, 0f),
                BodyUp = new KartVec3(0f, 0f, 1f),
            };

            KartCollisionOutput output = KartDynamics.ResolveLinearCollision(input);

            Assert.That(output.WallContact, Is.False);
            // Soft restitution of 0.2 against a 10-unit approach.
            Assert.That(output.Velocity.Z, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void CollisionResponse_IgnoresContactsItIsMovingAwayFrom()
        {
            var input = new KartCollisionInput
            {
                Velocity = new KartVec3(5f, 0f, 0f),
                Normal = new KartVec3(1f, 0f, 0f),
                BodyRight = new KartVec3(1f, 0f, 0f),
                BodyForward = new KartVec3(0f, -1f, 0f),
                BodyUp = new KartVec3(0f, 0f, 1f),
            };

            KartCollisionOutput output = KartDynamics.ResolveLinearCollision(input);

            Assert.That(output.Incoming, Is.False);
            Assert.That(output.Velocity.X, Is.EqualTo(5f).Within(0.001f));
        }

        // ------------------------------------------------------------ countdown

        [Test]
        public void Countdown_FiresThreeTwoOneGoAtTheRecoveredThresholds()
        {
            var countdown = new KartCountdown();
            countdown.Start(0u);

            Assert.That(countdown.DeadlineMs, Is.EqualTo(KartCountdown.TotalMs));

            Assert.That(countdown.Update(3999u).PlayThree, Is.False, "still too early");
            Assert.That(countdown.Update(4000u).PlayThree, Is.True, "deadline is now within 3000");
            Assert.That(countdown.Update(5000u).PlayTwo, Is.True);
            Assert.That(countdown.Update(6000u).PlayOne, Is.True);

            KartCountdownCues go = countdown.Update(7000u);
            Assert.That(go.PlayGo, Is.True);
            Assert.That(go.Released, Is.True);
            Assert.That(countdown.Stage, Is.EqualTo(KartCountdownStage.Running));

            Assert.That(countdown.Update(7016u).PlayGo, Is.False, "GO fires once");
        }

        [Test]
        public void Countdown_HoldsTheKartUntilGo()
        {
            var countdown = new KartCountdown();
            countdown.Start(0u);

            Assert.That(countdown.Update(100u).Released, Is.False);
            Assert.That(countdown.Update(6999u).Released, Is.False);
            Assert.That(countdown.Update(7000u).Released, Is.True);
        }

        [Test]
        public void StartBoost_IsGrantedOnlyInsideTheWindow()
        {
            var countdown = new KartCountdown();
            countdown.Start(0u);

            Assert.That(countdown.StartBoostGranted(6899u), Is.False);
            Assert.That(countdown.StartBoostGranted(6900u), Is.True);
            Assert.That(countdown.StartBoostGranted(7000u), Is.True);
            Assert.That(countdown.StartBoostGranted(7100u), Is.True);
            Assert.That(countdown.StartBoostGranted(7101u), Is.False);
        }

        // ----------------------------------------------------------- start pose

        /// <summary>
        /// The recovered spawn for village_R01, which is one of the two tracks
        /// whose racing direction was checked against the original game.
        /// </summary>
        [Test]
        public void VillageOverpass_StartsWhereTheOriginalStarts()
        {
            var village = new TrackSpec
            {
                AssetName = "village_R01",
                Minimum = new KartVec3(-36.535f, -111.3333f, 2.849871f),
                Maximum = new KartVec3(1239.292f, 1600.784f, 106.8285f),
                HasScene = true,
                StartKind = KartTrackStartKind.Confirmed,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(143.6366f, 523.2488f, 26.84984f),
            };

            Assert.That(KartTrackStart.Position(village, out KartVec3 position), Is.True);
            Assert.That(position.X, Is.EqualTo(457.7419f).Within(0.001f));
            Assert.That(position.Y, Is.EqualTo(-221.4766f).Within(0.001f));

            // AXIS_Y takes the fallback pose: a 180-degree Z rotation, which
            // turns forward from -Y to +Y and body-right from +X to -X together,
            // so steering and camera handedness are preserved. The same pose the
            // recovery's own test pins for forest_I01 (z = 1, w = 0).
            KartQuat orientation = KartTrackStart.Orientation(village);
            Assert.That(orientation.Z, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(orientation.W, Is.EqualTo(0f).Within(0.0001f));

            orientation.GetAxes(out KartVec3 right, out KartVec3 forward, out _);
            Assert.That(forward.Y, Is.EqualTo(1f).Within(0.001f), "driven along +Y");
            Assert.That(right.X, Is.EqualTo(-1f).Within(0.001f), "handedness preserved");
        }

        [Test]
        public void TrackTransform_PutsTheStartLineOnTheGroundPlane()
        {
            var village = new TrackSpec
            {
                Minimum = new KartVec3(-36.535f, -111.3333f, 2.849871f),
                Maximum = new KartVec3(1239.292f, 1600.784f, 106.8285f),
                StartKind = KartTrackStartKind.Confirmed,
                StartLine = new KartVec3(143.6366f, 523.2488f, 26.84984f),
            };

            KartTrackTransform transform = KartTrackTransform.FromSpec(village);
            KartVec3 world = transform.ToWorld(village.StartLine);

            Assert.That(transform.MirrorX, Is.True);
            Assert.That(world.Z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(world.X, Is.EqualTo(457.7419f).Within(0.001f));
        }
    }
}
