using System.Collections.Generic;
using NUnit.Framework;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The checkpoint graph, checked against the thirteen real courses.
    ///
    /// These are not unit tests of arithmetic: the build is a graph walk over
    /// asset data, and the things that go wrong with it are structural — a road
    /// walked the wrong way round, a branch spliced to the wrong node, a ring that
    /// does not close. So the assertions are about shape, and every track is run
    /// through them rather than one sample.
    /// </summary>
    public sealed class KartCourseTests
    {
        private const string CourseDirectory = "Assets/_Project/Data/Courses";

        private static IEnumerable<string> TrackNames()
        {
            foreach (TrackSpec track in KartDemoData.Tracks)
            {
                if (track.HasScene) yield return track.AssetName;
            }
        }

        private static KartCourse Load(string track)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TrackCourseAsset>(
                $"{CourseDirectory}/{track}.asset");
            Assert.That(asset, Is.Not.Null, $"{track} has no baked course asset.");

            KartCourse course = KartCourse.Build(asset.ToAsset());
            Assert.That(course, Is.Not.Null, $"{track}'s course did not build.");
            return course;
        }

        [Test]
        public void EveryTrackBuildsAGraph([ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);

            Assert.That(course.NodeCount, Is.GreaterThan(1), "a course is more than one node");
            Assert.That(course.GateCount, Is.GreaterThanOrEqualTo(course.NodeCount));
            Assert.That(course.FirstNode, Is.EqualTo(0));
            Assert.That(course.LastNode, Is.EqualTo(course.NodeCount - 1));
        }

        /// <summary>
        /// Every published node carries centreline points. A node without them is
        /// the signature of a road walked past its end: the respawn has nothing to
        /// put the kart on, and the wrong-way test skips it silently.
        /// </summary>
        [Test]
        public void EveryNodeHasACentreline([ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);

            for (int index = 0; index < course.NodeCount; ++index)
            {
                KartCourseNode node = course.Nodes[index];
                Assert.That(node.PointCount, Is.GreaterThan(0), $"node {index} has no points");
                Assert.That(node.PointFirst + node.PointCount,
                            Is.LessThanOrEqualTo(course.Points.Length),
                            $"node {index} points past the end of the point table");
                Assert.That(node.Length, Is.GreaterThanOrEqualTo(0f));
            }
        }

        /// <summary>
        /// Links resolve, and the ring closes: the first node is reachable from
        /// the last. That closure is the one thing that makes a lap possible, and
        /// it is applied at the very end of the build where it is easiest to lose.
        /// </summary>
        [Test]
        public void LinksResolveAndTheRingCloses([ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);

            foreach (KartCourseLink link in course.Links)
            {
                Assert.That(link.Gate, Is.InRange(0, course.GateCount - 1));
                if (link.Node != KartCourse.NoIndex)
                {
                    Assert.That(link.Node, Is.InRange(0, course.NodeCount - 1));
                }
            }

            bool reachesFirst = false;
            for (int link = course.Nodes[course.LastNode].Backward;
                 link != KartCourse.NoIndex;
                 link = course.Links[link].Next)
            {
                if (course.Links[link].Node == course.FirstNode) reachesFirst = true;
            }
            Assert.That(reachesFirst, Is.True, "the last node does not lead back to the first");
        }

        /// <summary>
        /// The start pose faces down the course. Column 1 of the basis is the
        /// negated direction of travel, so the kart's own forward axis has to agree
        /// with the first point's direction — this is what the eleven tracks with
        /// only an assumed start direction now get from the asset instead.
        /// </summary>
        [Test]
        public void TheStartPoseFacesDownTheCourse([ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);
            course.StartPose(0, out KartVec3 position, out KartQuat orientation);

            KartCourseNode first = course.Nodes[course.FirstNode];
            KartVec3 direction = course.Points[first.PointFirst].Direction;

            var forward = new KartVec3(
                -2f * (orientation.X * orientation.Y - orientation.W * orientation.Z),
                -(1f - 2f * (orientation.X * orientation.X + orientation.Z * orientation.Z)),
                -2f * (orientation.Y * orientation.Z + orientation.W * orientation.X));

            Assert.That(KartVec3.Dot(forward, direction), Is.GreaterThan(0.99f));

            // Half a unit behind the first point, so the first gate is still ahead.
            Assert.That(
                KartVec3.Dot(position - course.Points[first.PointFirst].Position, direction),
                Is.LessThan(0f));
        }

        /// <summary>
        /// A kart driven from the start pose along the centreline crosses the
        /// gates in order and comes back to node 0 with one lap credited.
        ///
        /// This is the whole mechanism end to end: the gate quads, the link lists,
        /// the advance counter and the lap rule. Walking the centreline is the one
        /// path that is guaranteed to be on the road, so a course that fails this
        /// is wrong rather than merely hard to drive.
        /// </summary>
        [Test]
        public void DrivingTheCentrelineCompletesALap([ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);
            course.SetLapCount(1);

            course.StartPose(0, out KartVec3 position, out KartQuat orientation);
            KartCourseProgress progress = KartCourseProgress.Init(course, position);

            KartVec3 previous = position;
            uint clock = 0;

            // Every node's points in order, twice round, so the lap that starts on
            // the first crossing of node 0 also finishes.
            for (int lap = 0; lap < 2; ++lap)
            {
                for (int index = 0; index < course.NodeCount; ++index)
                {
                    KartCourseNode node = course.Nodes[index];
                    for (int point = 0; point < node.PointCount; ++point)
                    {
                        KartVec3 next = course.Points[node.PointFirst + point].Position;
                        clock += 16u;
                        KartCourseProgress.Step(
                            course, ref progress, previous, next, orientation,
                            next - previous, clock);
                        previous = next;
                    }
                }
            }

            Assert.That(progress.Lap, Is.GreaterThanOrEqualTo(2u),
                        "two laps of the centreline did not credit two laps");
            Assert.That(progress.WrongWay, Is.False, "driving the centreline read as wrong way");
        }

        /// <summary>
        /// The respawn puts the kart on the node it is in, facing along it, and
        /// never off the end of the point table.
        /// </summary>
        [Test]
        public void RespawnLandsOnTheCurrentNode([ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);

            for (int index = 0; index < course.NodeCount; ++index)
            {
                var progress = new KartCourseProgress { Node = index };
                Assert.That(
                    course.RespawnPose(progress, out KartVec3 position, out KartQuat orientation),
                    Is.True, $"node {index} has no respawn pose");

                KartCourseNode node = course.Nodes[index];
                KartCoursePoint point = course.Points[node.PointFirst];

                // Half a unit back along the road from the node's first point.
                Assert.That((position - point.Position).Magnitude, Is.EqualTo(0.5f).Within(0.05f));
                Assert.That(orientation.W, Is.Not.NaN);
            }
        }

        /// <summary>
        /// A segment that pierces a gate reads as a crossing, and its sign follows
        /// the direction of travel. The gate test has no tolerance at all, so both
        /// directions are checked rather than assumed symmetric.
        /// </summary>
        [Test]
        public void GateCrossingSignsFollowTheDirectionOfTravel(
            [ValueSource(nameof(TrackNames))] string track)
        {
            KartCourse course = Load(track);

            int crossed = 0;
            for (int index = 0; index < course.GateCount; ++index)
            {
                KartCourseGate gate = course.Gates[index];

                // Through the middle of the first triangle, along the normal.
                KartVec3 centre =
                    (gate.First.A + gate.First.B + gate.First.C) * (1f / 3f);
                KartVec3 step = gate.Normal * 2f;

                if (KartCourse.GateCrossing(gate, centre - step, centre + step) != 1) continue;
                Assert.That(KartCourse.GateCrossing(gate, centre + step, centre - step),
                            Is.EqualTo(-1), $"gate {index} is not signed symmetrically");
                ++crossed;
            }

            Assert.That(crossed, Is.GreaterThan(course.GateCount / 2),
                        "most gates should be crossable through their own centre");
        }
    }
}
