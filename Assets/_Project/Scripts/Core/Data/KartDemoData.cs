using System;
using System.Collections.Generic;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// How much of a track's start pose is actually supported by evidence.
    /// </summary>
    public enum KartTrackStartKind
    {
        /// <summary>No road-flagged start quad in the scene; spawn at the bounds centre.</summary>
        None = 0,
        /// <summary>Quad found but nearly square, so even the axis is a guess.</summary>
        AxisWeak,
        /// <summary>Quad clearly elongated; axis read from geometry, sign assumed.</summary>
        AxisClear,
        /// <summary>Direction compared against the original game.</summary>
        Confirmed,
    }

    /// <summary>Which horizontal axis the start line runs along.</summary>
    public enum KartTrackStartAxis
    {
        None = 0,
        X,
        Y,
    }

    /// <summary><c>KartDemoKartSpec</c> from <c>kart_demo_data.h</c>.</summary>
    public sealed class KartSpec
    {
        public string AssetName;
        public KartDynamicsConfig Dynamics;
        public KartSimulationGeometry Geometry;
        public float ModelHeight;
        public uint MaxBoosters;

        public float Width => Geometry.HalfWidth * 2f;
        public float Length => Geometry.HalfLength * 2f;
    }

    /// <summary><c>KartDemoTrackSpec</c> from <c>kart_demo_data.h</c>.</summary>
    public sealed class TrackSpec
    {
        public string AssetName;
        public string DisplayName;
        public string RaceMode;
        public uint Difficulty;
        public KartVec3 Minimum;
        public KartVec3 Maximum;

        /// <summary>
        /// False for the synthetic flat track, which has no decoded track.1s mesh
        /// and deliberately keeps the flat ground plus AABB walls.
        /// </summary>
        public bool HasScene;

        public KartTrackStartKind StartKind;
        public KartTrackStartAxis StartAxis;
        public KartVec3 StartLine;

        public float Width => Maximum.X - Minimum.X;
        public float Length => Maximum.Y - Minimum.Y;

        public KartVec3 Center => new KartVec3(
            (Minimum.X + Maximum.X) * 0.5f,
            (Minimum.Y + Maximum.Y) * 0.5f,
            Minimum.Z);
    }

    /// <summary>
    /// The recovered tables, transcribed from <c>kart_demo_data.c</c>. All
    /// twenty-six karts are carried; the thirteen real tracks land with their
    /// own phase.
    ///
    /// Every value here came out of the 2004 demo's own archives via
    /// <c>DeveloperTools/AssetPipeline</c>, so it is transcribed rather than tuned.
    /// </summary>
    public static class KartDemoData
    {
        public const uint DefaultMaxBoosters = 2u;

        /// <summary>
        /// The <c>KART(...)</c> macro: geometry's suspension range is 0.5 and its
        /// grounded drag scale is 1.0 for every row.
        /// </summary>
        private static KartSpec Kart(
            string assetName,
            KartDynamicsConfig dynamics,
            float halfWidth,
            float halfLength,
            float height) => new KartSpec
            {
                AssetName = assetName,
                Dynamics = dynamics,
                Geometry = new KartSimulationGeometry
                {
                    HalfWidth = halfWidth,
                    HalfLength = halfLength,
                    SuspensionRange = 0.5f,
                    GroundedDragScale = 1.0f,
                },
                ModelHeight = height,
                MaxBoosters = DefaultMaxBoosters,
            };

        /// <summary>
        /// Exact model-root AABB and parameter.xml values extracted from the 2004
        /// demo's kart.rho. The archive spells Cotton as "cotten".
        /// </summary>
        /// <summary>
        /// <c>KARTS[]</c> in the file's own order, which is the order the K menu
        /// walks. Five dynamics presets cover the five families; the geometry is
        /// per kart because each model root has its own AABB.
        /// </summary>
        private static readonly KartSpec[] KartTable =
        {
            Kart("practice1", KartDynamicsConfig.Practice(), 0.7644821f, 0.81728435f, 0.81284374f),
            Kart("burst1", KartDynamicsConfig.Practice(), 0.7863699f, 0.86079025f, 0.81284374f),
            Kart("burst2", KartDynamicsConfig.Standard(), 0.7489265f, 0.9987979f, 0.81284374f),
            Kart("burst3", KartDynamicsConfig.Standard(), 0.8080695f, 1.06789325f, 0.81284374f),
            Kart("burst4", KartDynamicsConfig.Standard(), 0.8378915f, 1.0619645f, 0.83439654f),
            Kart("burst5", KartDynamicsConfig.Standard(), 0.8338487f, 1.1471490f, 0.9270710f),
            Kart("cotten1", KartDynamicsConfig.Standard(), 0.7802979f, 0.8544110f, 0.81284374f),
            Kart("cotten2", KartDynamicsConfig.Standard(), 0.7802979f, 0.98605945f, 0.81284374f),
            Kart("cotten3", KartDynamicsConfig.Standard(), 0.8656463f, 1.09538875f, 0.81284374f),
            Kart("cotten4", KartDynamicsConfig.Standard(), 0.8792389f, 1.0857897f, 0.87846375f),
            Kart("cotten5", KartDynamicsConfig.Standard(), 0.87533175f, 1.13917575f, 0.9822382f),
            Kart("marathon1", KartDynamicsConfig.Marathon(), 0.74652725f, 0.8395150f, 0.81284374f),
            Kart("marathon2", KartDynamicsConfig.Marathon(), 0.78619005f, 0.9645016f, 0.81284374f),
            Kart("marathon3", KartDynamicsConfig.Marathon(), 0.90422895f, 1.0765728f, 0.81284374f),
            Kart("marathon4", KartDynamicsConfig.Marathon(), 0.84413935f, 1.1062925f, 0.81284374f),
            Kart("marathon5", KartDynamicsConfig.Marathon(), 0.8707624f, 1.24406455f, 0.9171259f),
            Kart("saber1", KartDynamicsConfig.Saber(), 0.7815945f, 0.91753305f, 0.8148401f),
            Kart("saber2", KartDynamicsConfig.Saber(), 0.7266946f, 1.04699875f, 0.81284374f),
            Kart("saber3", KartDynamicsConfig.Saber(), 0.9616292f, 1.07968415f, 0.81284374f),
            Kart("saber4", KartDynamicsConfig.Saber(), 0.91966365f, 1.1252806f, 0.83305377f),
            Kart("saber5", KartDynamicsConfig.Saber(), 0.8918933f, 1.3687856f, 0.99534184f),
            Kart("solid1", KartDynamicsConfig.Solid(), 0.7267921f, 0.83080195f, 0.81284374f),
            Kart("solid2", KartDynamicsConfig.Solid(), 0.7744493f, 0.97958995f, 0.81284374f),
            Kart("solid3", KartDynamicsConfig.Solid(), 0.8325483f, 1.08305655f, 0.81284374f),
            Kart("solid4", KartDynamicsConfig.Solid(), 0.79691135f, 1.0517733f, 0.81284374f),
            Kart("solid5", KartDynamicsConfig.Solid(), 0.93279995f, 1.1436093f, 0.9673969f),
        };

        /// <summary>
        /// The kart the simulator opens on. The demo's own kartlist.xml offers
        /// only burst3; cotten5 is a simulator-side choice.
        /// </summary>
        public static readonly KartSpec Cotten5 = FindKart("cotten5");

        /// <summary>
        /// The synthetic reference track: no mesh, so the simulator falls back to
        /// the flat ground plane and the cyan AABB walls. Its footprint matches
        /// Forest Log (896.391 x 807.183) and it is centred on the world origin,
        /// which makes it the clearest scene for reading the world axis gizmo.
        /// </summary>
        public static readonly TrackSpec FlatTest = new TrackSpec
        {
            AssetName = "flat_test",
            DisplayName = "테스트 평지",
            RaceMode = "테스트",
            Difficulty = 0,
            Minimum = new KartVec3(-448.1955f, -403.5915f, 0.0f),
            Maximum = new KartVec3(448.1955f, 403.5915f, 120.0f),
            HasScene = false,
            StartKind = KartTrackStartKind.None,
            StartAxis = KartTrackStartAxis.None,
            StartLine = KartVec3.Zero,
        };

        public static IReadOnlyList<KartSpec> Karts { get; } = KartTable;

        public static IReadOnlyList<TrackSpec> Tracks { get; } = new[] { FlatTest };

        public static KartSpec DefaultKart => Cotten5;

        public static TrackSpec DefaultTrack => FlatTest;

        public static KartSpec FindKart(string assetName)
        {
            // Reads the table rather than the Karts property: Cotten5 is resolved
            // through here during static initialisation, and the property's own
            // initialiser has not necessarily run by then.
            foreach (KartSpec kart in KartTable)
            {
                if (string.Equals(kart.AssetName, assetName, StringComparison.Ordinal)) return kart;
            }
            return null;
        }

        public static TrackSpec FindTrack(string assetName)
        {
            foreach (TrackSpec track in Tracks)
            {
                if (string.Equals(track.AssetName, assetName, StringComparison.Ordinal)) return track;
            }
            return null;
        }
    }
}
