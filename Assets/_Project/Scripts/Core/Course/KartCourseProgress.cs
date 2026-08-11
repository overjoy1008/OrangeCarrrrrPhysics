namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// One kart's place on the course, ported from <c>KartCourseProgress</c>.
    ///
    /// The field names are the original's record slots. <see cref="Advance"/> is
    /// the one that matters: +1 for each crossing of the start node, -1 for each
    /// crossing back over it. Every other gate only moves <see cref="Node"/>,
    /// which is why cutting the course stops laps from counting.
    /// </summary>
    public struct KartCourseProgress
    {
        public int Node;
        public int Advance;
        public uint NodeId;

        /// <summary>How far along the current node's point polyline the kart is.</summary>
        public float NodeDistance;

        /// <summary>Which point of that polyline it is past.</summary>
        public int Point;

        public uint LapStartMs;
        public uint Lap;
        public bool LapCompleted;

        /// <summary>Zero until a lap has been timed.</summary>
        public uint BestLapMs;

        /// <summary>
        /// True only when the kart both points and moves against the course
        /// direction; either one agreeing with it clears the flag.
        /// </summary>
        public bool WrongWay;

        /// <summary>
        /// Puts the kart at the last node with no progress, which is where the
        /// original starts every kart: behind the first node's gate.
        /// </summary>
        public static KartCourseProgress Init(KartCourse course, KartVec3 position)
        {
            var progress = new KartCourseProgress { Node = KartCourse.NoIndex };
            if (course == null || course.NodeCount == 0) return progress;

            progress.Node = course.LastNode;
            progress.NodeId = course.Nodes[course.LastNode].Id;
            Measure(course, ref progress, position);
            return progress;
        }

        /// <summary>
        /// One trail segment, the unit the original works in. <paramref name="timeMs"/>
        /// is the race clock lap times are stamped with. Returns the advance the
        /// segment produced, which is -1, 0 or +1.
        /// </summary>
        public static int Step(
            KartCourse course,
            ref KartCourseProgress progress,
            KartVec3 previousPosition,
            KartVec3 position,
            KartQuat orientation,
            KartVec3 velocity,
            uint timeMs)
        {
            int advance = 0;
            bool moved = false;

            if (course == null || progress.Node == KartCourse.NoIndex) return 0;

            // The original walks every segment of the kart's position trail; one
            // simulation step is one segment of that trail.
            for (int link = course.Nodes[progress.Node].Backward;
                 link != KartCourse.NoIndex;
                 link = course.Links[link].Next)
            {
                int target = course.Links[link].Node;
                if (target == KartCourse.NoIndex) continue;
                if (KartCourse.GateCrossing(
                        course.Gates[course.Links[link].Gate], previousPosition, position) <= 0)
                {
                    continue;
                }

                if (target == course.FirstNode)
                {
                    advance = 1;
                }
                else if (course.Gates[course.Links[link].Gate].IsFinal &&
                         unchecked((uint)progress.Advance) == course.LapCount)
                {
                    // The final gate closes the race for a course that does not
                    // end where it starts; only the last lap counts it.
                    advance = 1;
                }

                progress.Node = target;
                moved = true;
                break;
            }

            if (!moved)
            {
                for (int link = course.Nodes[progress.Node].Forward;
                     link != KartCourse.NoIndex;
                     link = course.Links[link].Next)
                {
                    int target = course.Links[link].Node;
                    if (target == KartCourse.NoIndex) continue;
                    if (KartCourse.GateCrossing(
                            course.Gates[course.Links[link].Gate], previousPosition, position) >= 0)
                    {
                        continue;
                    }

                    if (progress.Node == course.FirstNode) advance = -1;
                    progress.Node = target;
                    break;
                }
            }

            progress.LapCompleted = false;
            if (advance == 1 && progress.Advance == (int)progress.Lap)
            {
                if (progress.Lap == 0)
                {
                    progress.LapStartMs = timeMs;
                    progress.Lap = 1;
                }
                else
                {
                    uint lapMs = timeMs - progress.LapStartMs;
                    progress.BestLapMs = progress.BestLapMs != 0 && progress.BestLapMs < lapMs
                        ? progress.BestLapMs
                        : lapMs;
                    progress.LapStartMs = timeMs;
                    ++progress.Lap;
                    progress.LapCompleted = true;
                }
            }

            progress.Advance += advance;
            progress.NodeId = course.Nodes[progress.Node].Id;
            Measure(course, ref progress, position);
            progress.WrongWay = IsWrongWay(course, progress.Node, orientation, velocity);
            return advance;
        }

        /// <summary>
        /// How far into the current node the kart is, and which point of the
        /// node's polyline it is past.
        /// </summary>
        private static void Measure(KartCourse course, ref KartCourseProgress progress, KartVec3 position)
        {
            KartCourseNode node = course.Nodes[progress.Node];

            int index = 0;
            while (index < node.PointCount)
            {
                KartCoursePoint point = course.Points[node.PointFirst + index];
                if (KartVec3.Dot(position - point.Position, point.Direction) < 0f) break;
                ++index;
            }

            progress.NodeDistance = 0f;
            if (index != 0)
            {
                float travelled = 0f;
                for (int step = 0; step + 1 < index; ++step)
                {
                    KartVec3 a = course.Points[node.PointFirst + step].Position;
                    KartVec3 b = course.Points[node.PointFirst + step + 1].Position;
                    travelled += (b - a).Magnitude;
                }

                KartCoursePoint last = course.Points[node.PointFirst + index - 1];
                travelled += KartVec3.Dot(position - last.Position, last.Direction);
                progress.NodeDistance = travelled;
            }

            progress.Point = index != 0 ? index - 1 : 0;
            if (progress.NodeDistance > node.Length) progress.NodeDistance = node.Length;
        }

        /// <summary>
        /// The kart's forward axis: column 1 of the kart's basis, negated, which
        /// is the same axis the collision code reports.
        /// </summary>
        private static KartVec3 OrientationForward(KartQuat q) => new KartVec3(
            -2f * (q.X * q.Y - q.W * q.Z),
            -(1f - 2f * (q.X * q.X + q.Z * q.Z)),
            -2f * (q.Y * q.Z + q.W * q.X));

        /// <summary>
        /// The kart is going the wrong way when the course direction at its node —
        /// the node ahead minus the node behind — opposes both where it points and
        /// where it is moving. Either one agreeing with the course clears the flag,
        /// so spinning out does not raise it.
        /// </summary>
        private static bool IsWrongWay(
            KartCourse course, int node, KartQuat orientation, KartVec3 velocity)
        {
            const float limit = -0.5f;

            KartVec3 facing = OrientationForward(orientation);
            KartVec3 motion = velocity.Normalized;

            for (int ahead = course.Nodes[node].Backward;
                 ahead != KartCourse.NoIndex;
                 ahead = course.Links[ahead].Next)
            {
                int aheadNode = course.Links[ahead].Node;
                if (aheadNode == KartCourse.NoIndex || course.Nodes[aheadNode].PointCount == 0)
                {
                    continue;
                }

                for (int behind = course.Nodes[node].Forward;
                     behind != KartCourse.NoIndex;
                     behind = course.Links[behind].Next)
                {
                    int behindNode = course.Links[behind].Node;
                    if (behindNode == KartCourse.NoIndex ||
                        course.Nodes[behindNode].PointCount == 0) continue;

                    KartVec3 travel = (
                        course.Points[course.Nodes[aheadNode].PointFirst].Position -
                        course.Points[course.Nodes[behindNode].PointFirst].Position).Normalized;

                    if (KartVec3.Dot(travel, facing) > limit) return false;
                    if (KartVec3.Dot(travel, motion) > limit) return false;
                }
            }
            return true;
        }
    }
}
