using System;
using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// A camera path imported from a KCAM file — the baked animation of the
    /// original's <c>readyCamera.1s</c>.
    ///
    /// The values are kept in the engine's own frame (X and Y on the ground, Z up)
    /// and in the original's units. Converting to Unity's frame is the rig's job,
    /// the same way it is for every other camera here.
    /// </summary>
    public sealed class ReadyCameraAsset : ScriptableObject
    {
        [Serializable]
        public struct PositionKey
        {
            public uint TimeMs;
            public Vector3 Position;
        }

        [Serializable]
        public struct CubicKey
        {
            public uint TimeMs;
            public float Value;
            public float LeftSlope;
            public float RightSlope;
        }

        [Tooltip("The scene node the path was baked from.")]
        public string NodeName;

        [Tooltip("The controller's end time. 3333 ms in the demo's readyCamera.")]
        public uint DurationMs;

        [Tooltip("The child ReCamera's field of view, in degrees.")]
        public float FieldOfViewDegrees;

        public float NearPlane;
        public float FarPlane;

        [Tooltip("The node's own transform, used when the asset carries no curve.")]
        public Vector3 RestPosition;

        [Tooltip("The child ReCamera's local translation, in the animated node's frame.")]
        public Vector3 CameraPosition;

        /// <summary>
        /// The child ReCamera's local rotation, as three basis vectors in the
        /// file's own order — which is the engine's row-major 3x3.
        ///
        /// This is not decoration. The cameraman is handed the camera, not the
        /// node above it, and the transform it reads is the accumulated one, so
        /// the camera's own axes are part of where it looks.
        /// </summary>
        public Vector3[] CameraBasis = Array.Empty<Vector3>();

        public PositionKey[] PositionKeys = Array.Empty<PositionKey>();
        public CubicKey[] EulerX = Array.Empty<CubicKey>();
        public CubicKey[] EulerY = Array.Empty<CubicKey>();
        public CubicKey[] EulerZ = Array.Empty<CubicKey>();

        /// <summary>Builds the engine-side path this asset describes.</summary>
        public KartReadyCameraPath ToPath()
        {
            var position = new KartPathKey[PositionKeys.Length];
            for (int i = 0; i < PositionKeys.Length; ++i)
            {
                PositionKey key = PositionKeys[i];
                position[i] = new KartPathKey(
                    key.TimeMs, new KartVec3(key.Position.x, key.Position.y, key.Position.z));
            }

            // The three file vectors are the engine's rows; the path wants the
            // columns, which is how a basis is applied to a vector.
            KartVec3 cameraColumn0 = KartVec3.Zero;
            KartVec3 cameraColumn1 = KartVec3.Zero;
            KartVec3 cameraColumn2 = KartVec3.Zero;
            if (CameraBasis != null && CameraBasis.Length == 3)
            {
                Vector3 row0 = CameraBasis[0];
                Vector3 row1 = CameraBasis[1];
                Vector3 row2 = CameraBasis[2];
                cameraColumn0 = new KartVec3(row0.x, row1.x, row2.x);
                cameraColumn1 = new KartVec3(row0.y, row1.y, row2.y);
                cameraColumn2 = new KartVec3(row0.z, row1.z, row2.z);
            }

            return new KartReadyCameraPath(
                position,
                Convert(EulerX),
                Convert(EulerY),
                Convert(EulerZ),
                DurationMs,
                FieldOfViewDegrees,
                new KartVec3(RestPosition.x, RestPosition.y, RestPosition.z),
                new KartVec3(CameraPosition.x, CameraPosition.y, CameraPosition.z),
                cameraColumn0,
                cameraColumn1,
                cameraColumn2);
        }

        private static KartCubicKey[] Convert(CubicKey[] keys)
        {
            var converted = new KartCubicKey[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                CubicKey key = keys[i];
                converted[i] = new KartCubicKey(key.TimeMs, key.Value, key.LeftSlope, key.RightSlope);
            }
            return converted;
        }
    }
}
