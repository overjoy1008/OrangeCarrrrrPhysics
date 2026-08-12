using System;
using System.IO;
using System.Text;
using OrangeCarrrrr.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Reads the KCAM camera-path container that
    /// <c>DeveloperTools/AssetImporters/track-mesh-exporter export-camera</c> writes
    /// from a stage scene's animated camera node.
    ///
    /// <code>
    /// header:
    ///   char   magic[4] = "KCAM"
    ///   uint32 version = 1
    ///   char   node_utf8[64]
    ///   uint32 duration_ms
    ///   float  field_of_view_degrees
    ///   float  near_plane, far_plane
    ///   float  rest_position[3]
    ///   float  rest_basis[9]
    ///   float  camera_position[3]         the child ReCamera's local translation
    ///   float  camera_basis[9]            its local rotation, row-major in file order
    ///   uint32 position_key_count
    ///     key[]: uint32 time_ms; float x, y, z
    ///   uint32 euler_curve_count = 3          X, Y, Z; applied as Rz * Ry * Rx
    ///     curve[]: uint32 key_count
    ///       key[]: uint32 time_ms; float value, left_slope, right_slope
    /// </code>
    ///
    /// Coordinates are the engine's — X and Y on the ground, Z up — and are stored
    /// that way, because the path is consumed by the recovered cameraman rather
    /// than by a Unity transform.
    /// </summary>
    [ScriptedImporter(Version, "kcam")]
    public sealed class KcamImporter : ScriptedImporter
    {
        public const int Version = 2;

        /// <summary>
        /// Version 2 adds the child camera's own transform. A version-1 export is
        /// refused rather than read: without that transform the camera points at
        /// the ground, and silently importing one would put the bug back.
        /// </summary>
        public const uint FormatVersion = 2u;
        public const int NameBytes = 64;

        public override void OnImportAsset(AssetImportContext context)
        {
            using FileStream stream = File.OpenRead(context.assetPath);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            var asset = ScriptableObject.CreateInstance<ReadyCameraAsset>();
            asset.name = Path.GetFileNameWithoutExtension(context.assetPath);

            if (new string(reader.ReadChars(4)) != "KCAM")
            {
                throw new InvalidDataException($"{context.assetPath} is not a KCAM file.");
            }

            uint version = reader.ReadUInt32();
            if (version != FormatVersion)
            {
                throw new InvalidDataException(
                    $"{context.assetPath} is KCAM version {version}; this importer reads {FormatVersion}.");
            }

            asset.NodeName = ReadFixedUtf8(reader, NameBytes);
            asset.DurationMs = reader.ReadUInt32();
            asset.FieldOfViewDegrees = reader.ReadSingle();
            asset.NearPlane = reader.ReadSingle();
            asset.FarPlane = reader.ReadSingle();
            asset.RestPosition = ReadVector(reader);

            // The rest basis is read past rather than kept: the rotation curves are
            // the authority whenever there are any, and the demo's camera has them.
            for (int i = 0; i < 9; ++i) reader.ReadSingle();

            asset.CameraPosition = ReadVector(reader);
            asset.CameraBasis = new[] { ReadVector(reader), ReadVector(reader), ReadVector(reader) };

            uint positionCount = reader.ReadUInt32();
            var positions = new ReadyCameraAsset.PositionKey[positionCount];
            for (uint i = 0; i < positionCount; ++i)
            {
                positions[i] = new ReadyCameraAsset.PositionKey
                {
                    TimeMs = reader.ReadUInt32(),
                    Position = ReadVector(reader),
                };
            }
            asset.PositionKeys = positions;

            uint curveCount = reader.ReadUInt32();
            var curves = new ReadyCameraAsset.CubicKey[3][];
            for (uint curve = 0; curve < curveCount; ++curve)
            {
                uint keyCount = reader.ReadUInt32();
                var keys = new ReadyCameraAsset.CubicKey[keyCount];
                for (uint i = 0; i < keyCount; ++i)
                {
                    keys[i] = new ReadyCameraAsset.CubicKey
                    {
                        TimeMs = reader.ReadUInt32(),
                        Value = reader.ReadSingle(),
                        LeftSlope = reader.ReadSingle(),
                        RightSlope = reader.ReadSingle(),
                    };
                }
                if (curve < 3) curves[curve] = keys;
            }

            asset.EulerX = curves[0] ?? Array.Empty<ReadyCameraAsset.CubicKey>();
            asset.EulerY = curves[1] ?? Array.Empty<ReadyCameraAsset.CubicKey>();
            asset.EulerZ = curves[2] ?? Array.Empty<ReadyCameraAsset.CubicKey>();

            context.AddObjectToAsset("path", asset);
            context.SetMainObject(asset);
        }

        private static Vector3 ReadVector(BinaryReader reader)
            => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static string ReadFixedUtf8(BinaryReader reader, int bytes)
        {
            byte[] raw = reader.ReadBytes(bytes);
            int length = Array.IndexOf(raw, (byte)0);
            if (length < 0) length = raw.Length;
            return Encoding.UTF8.GetString(raw, 0, length);
        }
    }
}
