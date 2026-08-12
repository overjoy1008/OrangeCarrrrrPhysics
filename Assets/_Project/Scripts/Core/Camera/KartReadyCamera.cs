using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>A linear position key of the ready camera's baked path.</summary>
    public readonly struct KartPathKey
    {
        public readonly uint TimeMs;
        public readonly KartVec3 Position;

        public KartPathKey(uint timeMs, KartVec3 position)
        {
            TimeMs = timeMs;
            Position = position;
        }
    }

    /// <summary>
    /// A cubic key of one Euler axis: a value and the two tangents the asset
    /// carries either side of it.
    /// </summary>
    public readonly struct KartCubicKey
    {
        public readonly uint TimeMs;
        public readonly float Value;
        public readonly float LeftSlope;
        public readonly float RightSlope;

        public KartCubicKey(uint timeMs, float value, float leftSlope, float rightSlope)
        {
            TimeMs = timeMs;
            Value = value;
            LeftSlope = leftSlope;
            RightSlope = rightSlope;
        }
    }

    /// <summary>
    /// The camera path baked into <c>readyCamera.1s</c>: the node <c>Camera02</c>'s
    /// animated translation and rotation, and the field of view of the
    /// <c>ReCamera</c> under it.
    ///
    /// The translation is a dense linear track — 201 keys, one every 33 ms, which
    /// is a 30 fps bake — and the rotation is three cubic curves of Euler angles
    /// with three keys each. Both are the asset's own data; only the evaluation
    /// below is code.
    ///
    /// <b>Provenance.</b> The keys come from the shipped asset, read with the
    /// vendored KartRider reader. The cubic form and the <c>Rz * Ry * Rx</c> order
    /// are that reader's reading of the format, not something traced in
    /// <c>KartRider.exe</c> — but they check out against the asset itself: the
    /// node's own static transform is a rotation about X of 50 degrees, and the X
    /// curve's first key is 0.8726647 rad, which is 50 degrees exactly.
    /// </summary>
    public sealed class KartReadyCameraPath
    {
        private readonly KartPathKey[] _position;
        private readonly KartCubicKey[] _eulerX;
        private readonly KartCubicKey[] _eulerY;
        private readonly KartCubicKey[] _eulerZ;

        /// <summary>The controller's end time. <c>readyCamera.1s</c> ends at 3333 ms.</summary>
        public uint DurationMs { get; }

        public float FieldOfViewDegrees { get; }

        /// <summary>The node's own transform, used when the asset carries no curve.</summary>
        public KartVec3 RestPosition { get; }

        /// <summary>The child camera's local translation, in the animated node's frame.</summary>
        public KartVec3 CameraPosition { get; }

        /// <summary>
        /// The child camera's local basis, by column.
        ///
        /// The cameraman is handed the <c>ReCamera</c> rather than the node that
        /// carries the animation — it reads the field of view off the same object
        /// at <c>+0x178</c> — and the transform it reads is that object's
        /// <em>world</em> matrix at <c>+0xF8</c>, which <c>0x00498B10</c> builds as
        /// <c>parent.world * local</c>. So the camera's own local rotation is part
        /// of where it points, and leaving it out aims the camera at the ground.
        /// </summary>
        public KartVec3 CameraColumn0 { get; }

        public KartVec3 CameraColumn1 { get; }

        public KartVec3 CameraColumn2 { get; }

        /// <summary>True when the asset carried the child camera's own transform.</summary>
        public bool HasCameraBasis { get; }

        public int PositionKeyCount => _position.Length;

        public KartReadyCameraPath(
            KartPathKey[] position,
            KartCubicKey[] eulerX,
            KartCubicKey[] eulerY,
            KartCubicKey[] eulerZ,
            uint durationMs,
            float fieldOfViewDegrees,
            KartVec3 restPosition,
            KartVec3 cameraPosition = default,
            KartVec3 cameraColumn0 = default,
            KartVec3 cameraColumn1 = default,
            KartVec3 cameraColumn2 = default)
        {
            CameraPosition = cameraPosition;
            CameraColumn0 = cameraColumn0;
            CameraColumn1 = cameraColumn1;
            CameraColumn2 = cameraColumn2;

            // A zero basis is an asset from before the child transform was
            // exported; the identity keeps it behaving as it did rather than
            // collapsing the camera onto the kart.
            HasCameraBasis = cameraColumn0.SqrMagnitude > 0f ||
                             cameraColumn1.SqrMagnitude > 0f ||
                             cameraColumn2.SqrMagnitude > 0f;

            _position = position ?? Array.Empty<KartPathKey>();
            _eulerX = eulerX ?? Array.Empty<KartCubicKey>();
            _eulerY = eulerY ?? Array.Empty<KartCubicKey>();
            _eulerZ = eulerZ ?? Array.Empty<KartCubicKey>();
            DurationMs = durationMs;
            FieldOfViewDegrees = fieldOfViewDegrees;
            RestPosition = restPosition;
        }

        /// <summary>
        /// The path's position at a time, in the node's own frame.
        ///
        /// Before the first key and after the last one the reader holds the end
        /// value rather than extrapolating, and this does the same. The asset's own
        /// tail is already flat, so the hold is only ever reached by a path that
        /// runs longer than it was baked for.
        /// </summary>
        public KartVec3 SamplePosition(uint timeMs)
        {
            if (_position.Length == 0) return RestPosition;
            if (timeMs <= _position[0].TimeMs) return _position[0].Position;

            KartPathKey last = _position[_position.Length - 1];
            if (timeMs >= last.TimeMs) return last.Position;

            int index = 0;
            while (index + 1 < _position.Length && _position[index + 1].TimeMs <= timeMs) ++index;

            KartPathKey from = _position[index];
            KartPathKey to = _position[index + 1];
            float span = to.TimeMs - from.TimeMs;
            float alpha = span > 0f ? (timeMs - from.TimeMs) / span : 0f;

            return from.Position * (1f - alpha) + to.Position * alpha;
        }

        /// <summary>The three Euler angles at a time, in radians.</summary>
        public void SampleEuler(uint timeMs, out float x, out float y, out float z)
        {
            x = SampleCubic(_eulerX, timeMs);
            y = SampleCubic(_eulerY, timeMs);
            z = SampleCubic(_eulerZ, timeMs);
        }

        /// <summary>
        /// One cubic curve.
        ///
        /// <code>
        /// delta = next - value
        /// a = rightSlope + nextLeftSlope - 2 * delta
        /// b = 3 * delta - nextLeftSlope - 2 * rightSlope
        /// c = rightSlope
        /// value(t) = ((a * t + b) * t + c) * t + value
        /// </code>
        ///
        /// which is the Hermite form written with the tangents the asset stores.
        /// </summary>
        public static float SampleCubic(KartCubicKey[] keys, uint timeMs)
        {
            if (keys == null || keys.Length == 0) return 0f;
            if (timeMs <= keys[0].TimeMs) return keys[0].Value;

            KartCubicKey last = keys[keys.Length - 1];
            if (timeMs >= last.TimeMs) return last.Value;

            int index = 0;
            while (index + 1 < keys.Length && keys[index + 1].TimeMs <= timeMs) ++index;

            KartCubicKey from = keys[index];
            KartCubicKey to = keys[index + 1];
            float span = to.TimeMs - from.TimeMs;
            float t = span > 0f ? (timeMs - from.TimeMs) / span : 0f;

            float delta = to.Value - from.Value;
            float a = from.RightSlope + to.LeftSlope - 2f * delta;
            float b = 3f * delta - to.LeftSlope - 2f * from.RightSlope;
            float c = from.RightSlope;
            return ((a * t + b) * t + c) * t + from.Value;
        }
    }

    /// <summary>
    /// The camera that plays over the grid before the countdown, ported from
    /// <c>KartReCameraman</c>.
    ///
    /// RTTI type descriptor <c>0x005998C8</c>, vftable <c>0x005726F4</c>: slot 7
    /// <c>0x00445430</c> clears the start time, slot 8 <c>0x00445450</c> places the
    /// camera, slot 9 <c>0x004455E0</c> returns <c>L"ReCamera Kart Cameraman"</c>.
    ///
    /// Unlike the chase and surround cameras this one computes nothing of its own:
    /// it seeks an animation to the time since it was installed and reads the
    /// animated node straight off it, expressed in the kart's frame.
    ///
    /// <code>
    /// position = kart position + kart basis * node position     0x0042D980, 0x00418710
    /// basis    = kart basis * node rotation                     0x0042B370
    /// fov      = node + 0x178                                   0x004476F0
    /// </code>
    ///
    /// <c>0x0042D980</c> is a row-major matrix times a column vector, so the node's
    /// translation is read out along the kart's own axes — which is why the sweep
    /// works on every track without the track knowing about it.
    /// </summary>
    public sealed class KartReadyCamera
    {
        private readonly KartReadyCameraPath _path;
        private uint _elapsedMs;

        /// <summary>Milliseconds since the cameraman was installed.</summary>
        public uint ElapsedMs => _elapsedMs;

        /// <summary>True once the baked path has run out.</summary>
        public bool Finished => _path == null || _elapsedMs >= _path.DurationMs;

        public KartReadyCameraPath Path => _path;

        public KartReadyCamera(KartReadyCameraPath path)
        {
            _path = path;
            Start();
        }

        /// <summary>Slot 7: the start time is cleared, so the next update is the first.</summary>
        public void Start() => _elapsedMs = 0u;

        /// <summary>Slot 8.</summary>
        public KartChaseCameraPose Update(
            KartVec3 kartPosition, KartQuat kartOrientation, uint elapsedMs)
        {
            _elapsedMs += elapsedMs;
            return Place(_path, kartPosition, kartOrientation, _elapsedMs);
        }

        /// <summary>The placement on its own, at an absolute time into the path.</summary>
        public static KartChaseCameraPose Place(
            KartReadyCameraPath path, KartVec3 kartPosition, KartQuat kartOrientation, uint timeMs)
        {
            kartOrientation.GetAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up);

            // The engine's column 1 is the negated forward; the kart's basis has to
            // be used in the layout the original stores it in.
            KartVec3 column1 = new KartVec3(-forward.X, -forward.Y, -forward.Z);

            if (path == null)
            {
                return new KartChaseCameraPose
                {
                    Position = kartPosition,
                    Right = right,
                    Forward = forward,
                    Up = up,
                    FieldOfViewDegrees = 0f,
                };
            }

            KartVec3 nodePosition = path.SamplePosition(timeMs);
            path.SampleEuler(timeMs, out float ex, out float ey, out float ez);

            // Rz * Ry * Rx: 0x0048F300 turns the three sampled angles into
            // quaternions about (1,0,0), (0,1,0) and (0,0,1) and folds them left
            // to right, which leaves Z outermost.
            RotationColumns(ex, ey, ez,
                out KartVec3 nodeColumn0, out KartVec3 nodeColumn1, out KartVec3 nodeColumn2);

            // world = animated node * the child camera's own local transform, the
            // way 0x00498B10 accumulates a node's matrix.
            if (path.HasCameraBasis)
            {
                KartVec3 childColumn0 = path.CameraColumn0;
                KartVec3 childColumn1 = path.CameraColumn1;
                KartVec3 childColumn2 = path.CameraColumn2;

                KartVec3 composed0 = Apply(nodeColumn0, nodeColumn1, nodeColumn2, childColumn0);
                KartVec3 composed1 = Apply(nodeColumn0, nodeColumn1, nodeColumn2, childColumn1);
                KartVec3 composed2 = Apply(nodeColumn0, nodeColumn1, nodeColumn2, childColumn2);

                nodePosition += Apply(nodeColumn0, nodeColumn1, nodeColumn2, path.CameraPosition);
                nodeColumn0 = composed0;
                nodeColumn1 = composed1;
                nodeColumn2 = composed2;
            }

            KartVec3 cameraColumn0 = Apply(right, column1, up, nodeColumn0);
            KartVec3 cameraColumn1 = Apply(right, column1, up, nodeColumn1);
            KartVec3 cameraColumn2 = Apply(right, column1, up, nodeColumn2);

            KartVec3 offset = Apply(right, column1, up, nodePosition);

            return new KartChaseCameraPose
            {
                Position = kartPosition + offset,
                Right = cameraColumn0,
                Forward = new KartVec3(-cameraColumn1.X, -cameraColumn1.Y, -cameraColumn1.Z),
                Up = cameraColumn2,
                FieldOfViewDegrees = path.FieldOfViewDegrees,
            };
        }

        /// <summary>
        /// The kart's basis times a vector, which <c>0x0042D980</c> computes as a
        /// row-major matrix against a column vector — so the vector's components
        /// weigh the basis's columns.
        /// </summary>
        private static KartVec3 Apply(
            KartVec3 column0, KartVec3 column1, KartVec3 column2, KartVec3 value)
            => column0 * value.X + column1 * value.Y + column2 * value.Z;

        /// <summary>
        /// The columns of <c>Rz(z) * Ry(y) * Rx(x)</c>, written out rather than built
        /// as three matrices and multiplied.
        /// </summary>
        public static void RotationColumns(
            float x, float y, float z,
            out KartVec3 column0, out KartVec3 column1, out KartVec3 column2)
        {
            float cx = MathF.Cos(x), sx = MathF.Sin(x);
            float cy = MathF.Cos(y), sy = MathF.Sin(y);
            float cz = MathF.Cos(z), sz = MathF.Sin(z);

            column0 = new KartVec3(cz * cy, sz * cy, -sy);
            column1 = new KartVec3(
                cz * sy * sx - sz * cx,
                sz * sy * sx + cz * cx,
                cy * sx);
            column2 = new KartVec3(
                cz * sy * cx + sz * sx,
                sz * sy * cx - cz * sx,
                cy * cx);
        }
    }
}
