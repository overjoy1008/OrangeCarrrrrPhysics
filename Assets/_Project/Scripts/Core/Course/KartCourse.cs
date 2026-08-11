using System;
using System.Collections.Generic;

namespace OrangeCarrrrr.Core
{
    /// <summary>The gate the crossing test works on, ported from <c>KartCourseGate</c>.</summary>
    public struct KartCourseGate
    {
        public KartCourseTriangle First;
        public KartCourseTriangle Second;
        public KartVec3 Normal;
        public bool IsFinal;

        public KartCourseTriangle Face(int face) => face == 0 ? First : Second;
    }

    /// <summary>
    /// One entry of a node's forward or backward list: the gate to test and the
    /// node reached by crossing it.
    /// </summary>
    public struct KartCourseLink
    {
        public int Gate;
        public int Node;
        public int Next;
    }

    public struct KartCoursePoint
    {
        public KartVec3 Position;
        public KartVec3 Direction;
    }

    /// <summary>One node of the course graph, ported from <c>KartCourseNode</c>.</summary>
    public struct KartCourseNode
    {
        public uint Id;

        /// <summary>
        /// Heads of the two link lists. The names are the original's field names,
        /// and they read backwards: crossing a <see cref="Backward"/> link's gate
        /// the positive way moves the kart <em>forward</em> along the course.
        /// </summary>
        public int Forward;

        public int Backward;
        public int PointFirst;
        public int PointCount;

        /// <summary>The summed length of the point polyline.</summary>
        public float Length;

        /// <summary>Asset-authored discontinuity in a "warpnext" centreline.</summary>
        public bool WarpNext;

        public KartVec3 WarpSource;
        public KartVec3 WarpDestination;
        public KartVec3 WarpSourceDirection;
        public KartVec3 WarpDestinationDirection;
        public float WarpRadiusSquared;
    }

    /// <summary>
    /// The original's course graph, gate crossing and lap counter, ported from
    /// <c>kart_course.c</c>.
    ///
    /// A checkpoint here is not a radius around a point: it is the two triangles
    /// of a quad standing across the road, and a kart passes it when the segment
    /// between two consecutive positions pierces one of them. Three things look
    /// wrong until they are checked against the original, and are kept:
    ///
    /// <list type="bullet">
    /// <item>A node's <c>Forward</c> list holds the nodes <em>behind</em> it and
    /// <c>Backward</c> the nodes ahead, so the names read inverted throughout.</item>
    /// <item>The lap counter does not fire on the finish line. It fires when the
    /// number of times the kart crossed the <em>first</em> node equals the number
    /// of laps it has been credited with, which is why cutting the course
    /// silently stops laps from counting.</item>
    /// <item>Nothing has a radius or a tolerance. The only threshold in the whole
    /// mechanism is zero.</item>
    /// </list>
    /// </summary>
    public sealed class KartCourse
    {
        public const int NoIndex = -1;

        public KartCourseNode[] Nodes = Array.Empty<KartCourseNode>();
        public KartCourseGate[] Gates = Array.Empty<KartCourseGate>();
        public KartCourseLink[] Links = Array.Empty<KartCourseLink>();
        public KartCoursePoint[] Points = Array.Empty<KartCoursePoint>();

        /// <summary>
        /// The first and last node. Only crossing into the first one moves the
        /// lap-progress counter.
        /// </summary>
        public int FirstNode = NoIndex;

        public int LastNode = NoIndex;

        /// <summary>How many laps the race is, which the final gate is checked against.</summary>
        public uint LapCount;

        /// <summary>
        /// The start pose derived from the first node's first point. The basis is
        /// column-major, the original's layout, so column 1 is the negated
        /// direction of travel.
        /// </summary>
        public KartVec3 StartPosition;

        public readonly float[,] StartBasis = new float[3, 3];

        public int NodeCount => Nodes.Length;
        public int GateCount => Gates.Length;

        public void SetLapCount(uint laps) => LapCount = laps;

        // ------------------------------------------------------------- build

        /// <summary>
        /// Builds the graph. Returns null if the asset is empty or a section names
        /// an element list it cannot walk.
        /// </summary>
        public static KartCourse Build(KartCourseAsset asset)
        {
            if (asset == null || asset.SectionCount == 0) return null;

            var builder = new Builder();
            var emitted = new List<int>();
            builder.BuildSections(asset.Sections, 0u, out _, closeRing: true, emitted);
            if (builder.Failed || emitted.Count == 0) return null;

            var course = new KartCourse
            {
                Gates = builder.Gates.ToArray(),
                Links = builder.Links.ToArray(),
                Points = builder.Points.ToArray(),
            };

            // The builder always has one node in hand that it has not published
            // yet, so each section leaves a spare behind and a branch leaves one
            // per alternative. The original's node vector holds only what it
            // published, so the graph is compacted to that list and the leftovers
            // go away.
            var remap = new int[builder.Nodes.Count];
            for (int index = 0; index < remap.Length; ++index) remap[index] = NoIndex;

            var compact = new KartCourseNode[emitted.Count];
            for (int index = 0; index < emitted.Count; ++index)
            {
                remap[emitted[index]] = index;
                compact[index] = builder.Nodes[emitted[index]];
            }
            for (int index = 0; index < course.Links.Length; ++index)
            {
                if (course.Links[index].Node != NoIndex)
                {
                    course.Links[index].Node = remap[course.Links[index].Node];
                }
            }

            course.Nodes = compact;
            course.FirstNode = 0;
            course.LastNode = course.Nodes.Length - 1;

            // "warpnext" targets the first point of the following graph node: on
            // ice_R01 that is RoadObj01's named `liftup` element.
            for (int index = 0; index < course.Nodes.Length; ++index)
            {
                if (!course.Nodes[index].WarpNext) continue;

                int link = course.Nodes[index].Backward;
                if (link == NoIndex) continue;

                int target = course.Links[link].Node;
                if (target == NoIndex || target >= course.Nodes.Length ||
                    course.Nodes[target].PointCount == 0) continue;

                int first = course.Nodes[target].PointFirst;
                course.Nodes[index].WarpDestination = course.Points[first].Position;
                course.Nodes[index].WarpDestinationDirection = course.Points[first].Direction;
            }

            // The start pose. Column 1 is the negated direction of travel, which
            // is the kart's forward axis negated, so a kart placed with this basis
            // faces down the course.
            KartCourseNode firstNode = course.Nodes[course.FirstNode];
            if (firstNode.PointCount == 0) return null;

            KartVec3 backward = -course.Points[firstNode.PointFirst].Direction;
            KartVec3 right = Normalize(KartVec3.Cross(backward, KartVec3.UnitZ));
            KartVec3 lift = Normalize(KartVec3.Cross(right, backward));

            course.StartPosition = course.Points[firstNode.PointFirst].Position + backward * 0.5f;
            WriteBasis(course.StartBasis, right, backward, lift);
            return course;
        }

        private static void WriteBasis(float[,] basis, KartVec3 right, KartVec3 backward, KartVec3 lift)
        {
            basis[0, 0] = right.X; basis[1, 0] = right.Y; basis[2, 0] = right.Z;
            basis[0, 1] = backward.X; basis[1, 1] = backward.Y; basis[2, 1] = backward.Z;
            basis[0, 2] = lift.X; basis[1, 2] = lift.Y; basis[2, 2] = lift.Z;
        }

        /// <summary>
        /// The original divides by the length with no guard; a zero vector would
        /// give NaN there, so the guard here only affects inputs the original
        /// never produces.
        /// </summary>
        private static KartVec3 Normalize(KartVec3 value)
        {
            float length = value.Magnitude;
            return length > 0f ? value * (1f / length) : KartVec3.Zero;
        }

        /// <summary>
        /// The build state. The two tail arrays keep each node's link lists in the
        /// original's insertion order rather than reversing them.
        /// </summary>
        private sealed class Builder
        {
            public readonly List<KartCourseNode> Nodes = new List<KartCourseNode>();
            public readonly List<KartCourseGate> Gates = new List<KartCourseGate>();
            public readonly List<KartCourseLink> Links = new List<KartCourseLink>();
            public readonly List<KartCoursePoint> Points = new List<KartCoursePoint>();

            private readonly List<int> _forwardTail = new List<int>();
            private readonly List<int> _backwardTail = new List<int>();

            public bool Failed;

            public int NewNode(uint id)
            {
                int index = Nodes.Count;
                Nodes.Add(new KartCourseNode
                {
                    Id = id,
                    Forward = NoIndex,
                    Backward = NoIndex,
                    PointFirst = Points.Count,
                });
                _forwardTail.Add(NoIndex);
                _backwardTail.Add(NoIndex);
                return index;
            }

            private int NewLink(int gate, int node)
            {
                Links.Add(new KartCourseLink { Gate = gate, Node = node, Next = NoIndex });
                return Links.Count - 1;
            }

            public void AppendLink(int node, bool forward, int gate, int target)
            {
                int link = NewLink(gate, target);
                int tail = forward ? _forwardTail[node] : _backwardTail[node];
                if (tail == NoIndex)
                {
                    KartCourseNode entry = Nodes[node];
                    if (forward) entry.Forward = link; else entry.Backward = link;
                    Nodes[node] = entry;
                }
                else
                {
                    KartCourseLink previous = Links[tail];
                    previous.Next = link;
                    Links[tail] = previous;
                }
                if (forward) _forwardTail[node] = link; else _backwardTail[node] = link;
            }

            public void ClearForwardList(int node)
            {
                KartCourseNode entry = Nodes[node];
                entry.Forward = NoIndex;
                Nodes[node] = entry;
                _forwardTail[node] = NoIndex;
            }

            public void AppendPoint(int node, KartVec3 position, KartVec3 direction)
            {
                KartCourseNode entry = Nodes[node];

                // Claimed on the first point rather than when the node is made: a
                // branch's join node is created before the alternatives are built
                // and only gets its centreline afterwards, so anything fixed at
                // creation would point into the alternative's points instead.
                if (entry.PointCount == 0) entry.PointFirst = Points.Count;

                Points.Add(new KartCoursePoint { Position = position, Direction = direction });
                ++entry.PointCount;
                Nodes[node] = entry;
            }

            /// <summary>The polyline length a node carries.</summary>
            public void MeasureNode(int node)
            {
                KartCourseNode entry = Nodes[node];
                float total = 0f;
                for (int index = 1; index < entry.PointCount; ++index)
                {
                    KartVec3 previous = Points[entry.PointFirst + index - 1].Position;
                    KartVec3 current = Points[entry.PointFirst + index].Position;
                    total += (current - previous).Magnitude;
                }
                entry.Length = total;
                Nodes[node] = entry;
            }

            private static int FindElement(
                KartCourseElement[] elements, string name, int fallback)
            {
                if (name == null) return fallback;
                for (int index = 0; index < elements.Length; ++index)
                {
                    if (elements[index].Name == name) return index;
                }
                return fallback;
            }

            public void BuildSections(
                KartCourseSection[] sections,
                uint startId,
                out uint lastId,
                bool closeRing,
                List<int> emitted)
            {
                var previous = new List<int>();
                int current = NewNode(startId);
                lastId = startId;

                for (int index = 0; index < sections.Length && !Failed; ++index)
                {
                    if (sections[index].IsRoad)
                    {
                        BuildRoad(sections[index], ref current, previous, ref lastId, emitted);
                    }
                    else
                    {
                        BuildBranch(sections[index], ref current, previous, startId, ref lastId, emitted);
                    }
                }

                if (Failed || !closeRing || emitted.Count == 0 || previous.Count == 0) return;

                // The dangling forward link of the first node now points at the
                // last, and the last node gets a backward link into the first.
                // That is the only thing that makes the course a loop.
                int first = emitted[0];
                int last = previous[0];
                int entry = Nodes[first].Forward;
                if (entry == NoIndex) return;

                KartCourseLink link = Links[entry];
                link.Node = last;
                Links[entry] = link;
                AppendLink(last, forward: false, link.Gate, first);
            }

            /// <summary>One <c>road</c> tag.</summary>
            private void BuildRoad(
                KartCourseSection section,
                ref int current,
                List<int> previous,
                ref uint lastId,
                List<int> emitted)
            {
                KartCourseElement[] elements = section.Elements;
                int count = elements.Length;
                if (count == 0) { Failed = true; return; }

                bool reverse = section.Reverse;
                int start = FindElement(elements, section.Start, 0);
                int end = FindElement(elements, section.End, count - 1);
                int final = FindElement(elements, section.Final, count);
                int index = start;

                while (true)
                {
                    int next = reverse
                        ? (index == 0 ? count : index) - 1
                        : (index == count - 1 ? 0 : index + 1);

                    KartCourseElement element = elements[index];
                    KartCourseElement pointSource = elements[reverse ? next : index];
                    if (element.RecordCount == 0 || pointSource.RecordCount == 0)
                    {
                        Failed = true;
                        return;
                    }

                    // Corner 0, then the other two swapped when the road is walked
                    // backwards.
                    var gate = new KartCourseGate
                    {
                        First = Permute(element.Face(0), reverse),
                        Second = Permute(element.Face(1), reverse),
                        // Record 0's direction, flipped when reverse.
                        Normal = reverse
                            ? -element.Records[0].Direction
                            : element.Records[0].Direction,
                        IsFinal = final == index,
                    };
                    Gates.Add(gate);
                    int gateIndex = Gates.Count - 1;

                    if (previous.Count == 0)
                    {
                        // The dangling entry the ring closure or the enclosing
                        // branch fills in later.
                        AppendLink(current, forward: true, gateIndex, NoIndex);
                    }
                    else
                    {
                        foreach (int prior in previous)
                        {
                            AppendLink(prior, forward: false, gateIndex, current);
                            AppendLink(current, forward: true, gateIndex, prior);
                        }
                    }

                    // The centreline. Walking backwards takes the *next* element's
                    // records, in reverse order and with every direction negated.
                    if (!reverse)
                    {
                        for (int record = 0; record < element.RecordCount; ++record)
                        {
                            AppendPoint(current,
                                element.Records[record].Position,
                                element.Records[record].Direction);
                        }
                        if (element.Extra == "warpnext") ReadWarp(current, element, gate);
                    }
                    else
                    {
                        for (int record = pointSource.RecordCount - 1; record >= 0; --record)
                        {
                            AppendPoint(current,
                                pointSource.Records[record].Position,
                                -pointSource.Records[record].Direction);
                        }
                    }

                    MeasureNode(current);
                    if (Failed) return;

                    emitted.Add(current);
                    previous.Clear();
                    previous.Add(current);
                    current = NewNode(lastId + 1u);
                    lastId = Nodes[current].Id;
                    if (index == end) break;
                    index = next;
                }
            }

            private static KartCourseTriangle Permute(KartCourseTriangle face, bool reverse)
                => reverse
                    ? new KartCourseTriangle { A = face.A, B = face.C, C = face.B }
                    : face;

            /// <summary>
            /// The authored discontinuity: two consecutive records at the same
            /// position mark the plane, and the record after them the destination.
            /// The radius is the widest span across the gate quad's own corners.
            /// </summary>
            private void ReadWarp(int node, KartCourseElement element, KartCourseGate gate)
            {
                for (int record = 0; record + 2 < element.RecordCount; ++record)
                {
                    KartVec3 first = element.Records[record].Position;
                    KartVec3 duplicate = element.Records[record + 1].Position;
                    if (first.X != duplicate.X || first.Y != duplicate.Y ||
                        first.Z != duplicate.Z) continue;

                    KartCourseNode entry = Nodes[node];
                    entry.WarpNext = true;
                    entry.WarpSource = first;
                    entry.WarpDestination = element.Records[record + 2].Position;
                    entry.WarpSourceDirection = element.Records[record].Direction;

                    for (int a = 0; a < 2; ++a)
                    {
                        for (int b = 0; b < 3; ++b)
                        {
                            for (int c = 0; c < 2; ++c)
                            {
                                for (int d = 0; d < 3; ++d)
                                {
                                    KartVec3 span = gate.Face(a)[b] - gate.Face(c)[d];
                                    float squared = KartVec3.Dot(span, span);
                                    if (squared > entry.WarpRadiusSquared)
                                    {
                                        entry.WarpRadiusSquared = squared;
                                    }
                                }
                            }
                        }
                    }

                    Nodes[node] = entry;
                    return;
                }
            }

            /// <summary>
            /// One <c>branch</c> tag. Each alternative is built as its own course,
            /// then spliced between the node before the branch and the node after
            /// it.
            /// </summary>
            private void BuildBranch(
                KartCourseSection section,
                ref int current,
                List<int> previous,
                uint startId,
                ref uint lastId,
                List<int> emitted)
            {
                uint highest = startId;

                for (int alternative = 0; alternative < section.AlternativeCount; ++alternative)
                {
                    var sub = new List<int>();
                    BuildSections(section.Alternatives[alternative], lastId, out uint subLast,
                                  closeRing: false, sub);
                    if (Failed || sub.Count == 0) { Failed = true; return; }
                    if (subLast > highest) highest = subLast;

                    // The sub-course's first node has one dangling forward link.
                    // Its gate is the branch entry, and both ends of it get wired
                    // to the nodes on either side of the branch.
                    int entryGate = Links[Nodes[sub[0]].Forward].Gate;
                    ClearForwardList(sub[0]);
                    foreach (int prior in previous)
                    {
                        AppendLink(sub[0], forward: true, entryGate, prior);
                        AppendLink(prior, forward: false, entryGate, sub[0]);
                    }

                    // The sub-course's last node is not kept: the node after the
                    // branch takes its place, so every alternative rejoins the
                    // same node.
                    for (int link = Nodes[sub[sub.Count - 1]].Forward;
                         link != NoIndex;
                         link = Links[link].Next)
                    {
                        int behind = Links[link].Node;
                        if (behind == NoIndex) continue;

                        int target = Nodes[behind].Backward;
                        KartCourseLink entry = Links[target];
                        entry.Node = current;
                        Links[target] = entry;
                    }

                    int exit = Nodes[sub[sub.Count - 1]].Forward;
                    if (exit != NoIndex)
                    {
                        AppendLink(current, forward: true, Links[exit].Gate, Links[exit].Node);
                    }

                    for (int index = 0; index + 1 < sub.Count; ++index) emitted.Add(sub[index]);

                    // Only the first alternative's tail geometry is carried over,
                    // which is what makes the shared join node measurable at all.
                    if (alternative == 0)
                    {
                        KartCourseNode tail = Nodes[sub[sub.Count - 1]];
                        for (int index = 0; index < tail.PointCount; ++index)
                        {
                            KartCoursePoint point = Points[tail.PointFirst + index];
                            AppendPoint(current, point.Position, point.Direction);
                        }
                        MeasureNode(current);
                    }
                    if (Failed) return;
                }

                KartCourseNode join = Nodes[current];
                join.Id = highest - 1u;
                Nodes[current] = join;

                emitted.Add(current);
                previous.Clear();
                previous.Add(current);
                current = NewNode(highest);
                lastId = highest;
            }
        }

        // ------------------------------------------------------ gate crossing

        /// <summary>
        /// The gate test: +1 crossing with the normal, -1 against it, 0 for no
        /// crossing. Public because it is the whole checkpoint mechanism.
        /// </summary>
        public static int GateCrossing(in KartCourseGate gate, KartVec3 segmentStart, KartVec3 segmentEnd)
        {
            KartVec3 direction = segmentEnd - segmentStart;
            for (int face = 0; face < 2; ++face)
            {
                KartCourseTriangle triangle = gate.Face(face);
                if (!KartTrackCollision.SegmentTriangleHit(
                        segmentStart, direction, triangle.A, triangle.B, triangle.C))
                {
                    continue;
                }

                // The only threshold in the whole mechanism, and it is zero. A
                // grazing pass exactly along the gate plane therefore counts as
                // forward.
                return KartVec3.Dot(direction, gate.Normal) < 0f ? -1 : 1;
            }
            return 0;
        }

        /// <summary>
        /// The asset-authored warp destination and horizontal turn, when the
        /// active node's "warpnext" plane is crossed in the forward direction.
        /// </summary>
        public bool WarpNext(
            KartVec3 segmentStart, KartVec3 segmentEnd,
            out KartVec3 destination, out float yawRadians)
        {
            destination = default;
            yawRadians = 0f;

            for (int index = 0; index < Nodes.Length; ++index)
            {
                KartCourseNode node = Nodes[index];
                if (!node.WarpNext) continue;

                float before = KartVec3.Dot(segmentStart - node.WarpSource, node.WarpSourceDirection);
                float after = KartVec3.Dot(segmentEnd - node.WarpSource, node.WarpSourceDirection);
                if (before >= 0f || after < 0f) continue;

                float t = before / (before - after);
                KartVec3 crossing = segmentStart + (segmentEnd - segmentStart) * t;
                KartVec3 offset = crossing - node.WarpSource;
                if (KartVec3.Dot(offset, offset) > node.WarpRadiusSquared) continue;

                destination = node.WarpDestination;
                yawRadians =
                    MathF.Atan2(node.WarpDestinationDirection.Y, node.WarpDestinationDirection.X) -
                    MathF.Atan2(node.WarpSourceDirection.Y, node.WarpSourceDirection.X);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------- placement

        /// <summary>
        /// The start grid. <paramref name="gridIndex"/> is the kart's slot; slot 0
        /// sits on the course's own start pose and the rest spread sideways along
        /// it. The ground snap the original follows this with is the caller's job,
        /// since it needs the collision scene.
        /// </summary>
        public void StartPose(int gridIndex, out KartVec3 position, out KartQuat orientation)
        {
            position = StartPosition;
            orientation = QuaternionFromBasis(StartBasis);
            if (Nodes.Length == 0) return;

            // Even slots step out one way and odd slots the other, in steps of 2.
            float offset = gridIndex % 2 == 0
                ? gridIndex / 2 + gridIndex / 2
                : (gridIndex / 2 + 1) * -2f;

            var right = new KartVec3(StartBasis[0, 0], StartBasis[1, 0], StartBasis[2, 0]);
            position = StartPosition + right * offset;
        }

        /// <summary>
        /// What the original does 500 ms after a reset or a fall: the kart returns
        /// to the first point of the node it is currently in, half a unit back
        /// from that point and facing along the course.
        /// </summary>
        public bool RespawnPose(
            in KartCourseProgress progress, out KartVec3 position, out KartQuat orientation)
        {
            position = default;
            orientation = KartQuat.Identity;

            if (progress.Node == NoIndex || progress.Node >= Nodes.Length) return false;

            KartCourseNode node = Nodes[progress.Node];
            if (node.PointCount == 0) return false;

            // Point 0 of the node the kart is in, not the point it is nearest. The
            // original also does not normalise the two cross products, so a banked
            // road gives a basis that is not quite orthonormal; that is reproduced.
            KartCoursePoint point = Points[node.PointFirst];
            KartVec3 backward = -point.Direction;
            KartVec3 right = KartVec3.Cross(backward, KartVec3.UnitZ);
            KartVec3 lift = KartVec3.Cross(right, backward);

            var basis = new float[3, 3];
            WriteBasis(basis, right, backward, lift);

            position = point.Position + backward * 0.5f;
            orientation = QuaternionFromBasis(basis);
            return true;
        }

        /// <summary>
        /// The quaternion for a basis whose columns are the original's. The kart's
        /// forward axis is column 1 negated, so this and the body-axis reader stay
        /// each other's inverse.
        /// </summary>
        private static KartQuat QuaternionFromBasis(float[,] m)
        {
            float trace = m[0, 0] + m[1, 1] + m[2, 2];
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                return new KartQuat(
                    0.25f * s,
                    (m[2, 1] - m[1, 2]) / s,
                    (m[0, 2] - m[2, 0]) / s,
                    (m[1, 0] - m[0, 1]) / s);
            }
            if (m[0, 0] > m[1, 1] && m[0, 0] > m[2, 2])
            {
                float s = MathF.Sqrt(1f + m[0, 0] - m[1, 1] - m[2, 2]) * 2f;
                return new KartQuat(
                    (m[2, 1] - m[1, 2]) / s,
                    0.25f * s,
                    (m[0, 1] + m[1, 0]) / s,
                    (m[0, 2] + m[2, 0]) / s);
            }
            if (m[1, 1] > m[2, 2])
            {
                float s = MathF.Sqrt(1f + m[1, 1] - m[0, 0] - m[2, 2]) * 2f;
                return new KartQuat(
                    (m[0, 2] - m[2, 0]) / s,
                    (m[0, 1] + m[1, 0]) / s,
                    0.25f * s,
                    (m[1, 2] + m[2, 1]) / s);
            }
            {
                float s = MathF.Sqrt(1f + m[2, 2] - m[0, 0] - m[1, 1]) * 2f;
                return new KartQuat(
                    (m[1, 0] - m[0, 1]) / s,
                    (m[0, 2] + m[2, 0]) / s,
                    (m[1, 2] + m[2, 1]) / s,
                    0.25f * s);
            }
        }
    }
}
