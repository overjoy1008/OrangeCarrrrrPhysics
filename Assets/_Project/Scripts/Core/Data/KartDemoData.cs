using System;
using System.Collections.Generic;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// How much of a track's start pose is actually supported by evidence.
    /// </summary>
    /// <summary>
    /// Which client an asset came out of.
    ///
    /// The recovery only ever argues from the 2004 demo, and the later TC Games
    /// client is explicitly not evidence for it. Content from that client is kept
    /// anyway — it is the only way to drive a course the demo never shipped — so
    /// the two are marked apart here as well as on disk. Anything asking "is this
    /// number the original's?" reads this first.
    /// </summary>
    public enum KartAssetSource
    {
        /// <summary>The 2004 demo. Everything the recovery argues from.</summary>
        Demo = 0,

        /// <summary>The later TC Games client. Never evidence for the demo.</summary>
        TCGames = 1,
    }

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

        /// <summary>Which client the body and its numbers came from.</summary>
        public KartAssetSource Source;

        public KartDynamicsConfig Dynamics;
        public KartSimulationGeometry Geometry;
        public float ModelHeight;
        public uint MaxBoosters;

        public float Width => Geometry.HalfWidth * 2f;
        public float Length => Geometry.HalfLength * 2f;
    }

    /// <summary>
    /// A track's serialized <c>the::ToMinimap</c> values, from its own
    /// <c>track.1s</c>.
    ///
    /// This is what turns the flat artwork into the original's rotating map: it
    /// says where the track's world origin sits on the 256x256 image and how many
    /// image pixels a world unit is worth. A track without one — only the
    /// synthetic <c>flat_test</c> — falls back to the square-on bounds map.
    /// </summary>
    public sealed class KartMinimapMapping
    {
        public string AssetName;
        public float OriginX;
        public float OriginY;
        public float Scale;
        public uint Width;
        public uint Height;
    }

    /// <summary><c>KartDemoTrackSpec</c> from <c>kart_demo_data.h</c>.</summary>
    public sealed class TrackSpec
    {
        public string AssetName;

        /// <summary>Which client the mesh and its numbers came from.</summary>
        public KartAssetSource Source;

        public string DisplayName;
        public string RaceMode;
        public uint Difficulty;

        /// <summary>
        /// How many laps the track is raced over, from the theme archive's own
        /// <c>track.xml</c> — <c>&lt;Track name='village_R01' folder='village_R01' laps='2'/&gt;</c>.
        /// The demo's four themes carry one each, and this is that number.
        ///
        /// A challenge can override it: <c>track.rho</c>'s <c>challenge.xml</c> gives
        /// its two time challenges <c>lap='1'</c>. Only the track's own value is
        /// held here, because the challenges name two courses the demo does not
        /// ship the geometry for.
        /// </summary>
        public uint Laps;
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
    /// The recovered tables, transcribed from <c>kart_demo_data.c</c>: all
    /// twenty-six karts and all fourteen tracks.
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
            float height,
            KartAssetSource source = KartAssetSource.Demo) => new KartSpec
            {
                AssetName = assetName,
                Source = source,
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
        /// <c>KARTS[]</c> in the file's own order, which is the order the K menu
        /// walks. Exact model-root AABBs and parameter.xml values extracted from
        /// the 2004 demo's kart.rho; the archive spells Cotton as "cotten". Five dynamics presets cover the five families; the geometry is
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

            // --- not the demo's ------------------------------------------------
            //
            // Paragon bodies from the later TC Games client, kept so the kart list
            // can show them. The geometry is theirs — half width, half length and
            // model height measured off their own exported meshes — but the
            // dynamics are NOT: their param.xml is a different physics model
            // altogether, naming only six of the sixteen values the recovered
            // engine needs and giving those on another scale entirely
            // (ForwardAccelForce 147 against the demo's 3300, DragFactor -0.0768
            // against +0.725, no Mass, no grip or brake forces at all). Feeding
            // those numbers to this engine would not be that kart's handling, it
            // would be noise, so these run the demo's Standard bench instead and
            // what is driven here is the body and nothing more.
            Kart("paragonV1", KartDynamicsConfig.Standard(), 0.8316438f, 1.1356342f, 0.7178360f, KartAssetSource.TCGames),
            Kart("paragonV1_gold", KartDynamicsConfig.Standard(), 0.7299818f, 1.1196411f, 0.7898212f, KartAssetSource.TCGames),
            Kart("paragonX", KartDynamicsConfig.Standard(), 0.7698725f, 1.1795354f, 0.7005107f, KartAssetSource.TCGames),
            Kart("paragonX_gold", KartDynamicsConfig.Standard(), 0.7709029f, 1.2039901f, 0.7005107f, KartAssetSource.TCGames),
            Kart("paragon_9th", KartDynamicsConfig.Standard(), 0.7847314f, 1.1000245f, 0.8232949f, KartAssetSource.TCGames),
            Kart("paragon_9th_golden", KartDynamicsConfig.Standard(), 0.7847314f, 1.1683116f, 0.9376051f, KartAssetSource.TCGames),
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

        /// <summary>
        /// <c>TRACKS[]</c> in the file's own order: the synthetic flat track, then
        /// the thirteen real ones. Bounds, start line and the evidence level of
        /// each start pose all came out of the decoded <c>track.1s</c>.
        ///
        /// Only forest_I01 and village_R01 have their start *direction* checked
        /// against the original game; everything below Confirmed assumes the
        /// +axis direction.
        /// </summary>
        private static readonly TrackSpec[] TrackTable =
        {
            FlatTest,
            new TrackSpec
            {
                AssetName = "desert_I01",
                Laps = 3u,
                DisplayName = "사막 지옥의 모래구덩이",
                RaceMode = "아이템",
                Difficulty = 2,
                Minimum = new KartVec3(27.22131f, 17.29889f, 6.201988f),
                Maximum = new KartVec3(976.4961f, 1240.95f, 131.7757f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(535.2981f, 643.5748f, 30.19893f),
            },
            new TrackSpec
            {
                AssetName = "desert_I02",
                Laps = 3u,
                DisplayName = "사막 피라미드 탐험",
                RaceMode = "아이템",
                Difficulty = 2,
                Minimum = new KartVec3(15.52185f, 21.88702f, 15.50548f),
                Maximum = new KartVec3(716.0608f, 1257.209f, 131.0995f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(270.9646f, 765.0216f, 21.74277f),
            },
            new TrackSpec
            {
                AssetName = "desert_R01",
                Laps = 2u,
                DisplayName = "사막 빙글빙글 공사장",
                RaceMode = "스피드",
                Difficulty = 4,
                Minimum = new KartVec3(-75.36597f, 49.39597f, 5.913539f),
                Maximum = new KartVec3(1131.802f, 986.7229f, 152.137f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(264.6601f, 490.8936f, 37.98141f),
            },
            new TrackSpec
            {
                AssetName = "forest_I01",
                Laps = 3u,
                DisplayName = "포레스트 통나무",
                RaceMode = "아이템",
                Difficulty = 1,
                Minimum = new KartVec3(13.48803f, 27.24757f, 1.752445f),
                Maximum = new KartVec3(909.879f, 834.4304f, 118.1231f),
                HasScene = true,
                StartKind = KartTrackStartKind.Confirmed,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(758.6121f, 484.8074f, 27.07603f),
            },
            new TrackSpec
            {
                AssetName = "forest_I02",
                Laps = 2u,
                DisplayName = "포레스트 버섯동굴",
                RaceMode = "아이템",
                Difficulty = 2,
                Minimum = new KartVec3(82.21716f, -140.4142f, -1.271019f),
                Maximum = new KartVec3(754.2902f, 1395.292f, 113.3375f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(224.0634f, 553.9555f, 31.909f),
            },
            new TrackSpec
            {
                AssetName = "forest_R02",
                Laps = 2u,
                DisplayName = "포레스트 지그재그",
                RaceMode = "스피드",
                Difficulty = 4,
                Minimum = new KartVec3(-7.913147f, -134.8193f, -0.718708f),
                Maximum = new KartVec3(1084.896f, 1392.051f, 172.8659f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(738.7255f, 341.9955f, 60.39529f),
            },
            new TrackSpec
            {
                AssetName = "ice_I01",
                Laps = 3u,
                DisplayName = "아이스 갈라진 빙산",
                RaceMode = "아이템→현재 스피드",
                Difficulty = 4,
                Minimum = new KartVec3(24.31265f, 60.53888f, 1.654024f),
                Maximum = new KartVec3(1107.896f, 1165.535f, 318.2377f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisWeak,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(405.2543f, 737.3219f, 15.57826f),
            },
            new TrackSpec
            {
                AssetName = "ice_I02",
                Laps = 3u,
                DisplayName = "아이스 상어의 무덤",
                RaceMode = "아이템",
                Difficulty = 1,
                Minimum = new KartVec3(93.76453f, 55.66373f, -5.161366f),
                Maximum = new KartVec3(870.5864f, 896.9417f, 313.5863f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisWeak,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(381.7374f, 456.2767f, 13.87031f),
            },
            new TrackSpec
            {
                AssetName = "ice_R01",
                Laps = 2u,
                DisplayName = "아이스 설산 다운힐",
                RaceMode = "스피드",
                Difficulty = 5,
                Minimum = new KartVec3(40.15965f, -209.4469f, 11.96741f),
                Maximum = new KartVec3(1979.582f, 1983.356f, 796.1914f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisWeak,
                StartAxis = KartTrackStartAxis.X,
                StartLine = new KartVec3(1088.797f, 1561.169f, 530.375f),
            },
            new TrackSpec
            {
                AssetName = "village_I01",
                Laps = 3u,
                DisplayName = "빌리지 시계탑",
                RaceMode = "아이템",
                Difficulty = 2,
                Minimum = new KartVec3(6.550034f, 13.51373f, 0.5811663f),
                Maximum = new KartVec3(723.9319f, 1013.374f, 87.23308f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.X,
                StartLine = new KartVec3(314.5892f, 152.8071f, 13.29302f),
            },
            new TrackSpec
            {
                AssetName = "village_I02",
                Laps = 3u,
                DisplayName = "빌리지 손가락",
                RaceMode = "아이템→현재 스피드",
                Difficulty = 3,
                Minimum = new KartVec3(-2.671448f, 11.83069f, -1.29806f),
                Maximum = new KartVec3(561.3079f, 682.4102f, 49.30659f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(119.2628f, 337.893f, 8.578018f),
            },
            new TrackSpec
            {
                AssetName = "village_R01",
                Laps = 2u,
                DisplayName = "빌리지 고가의 질주",
                RaceMode = "스피드",
                Difficulty = 2,
                Minimum = new KartVec3(-36.535f, -111.3333f, 2.849871f),
                Maximum = new KartVec3(1239.292f, 1600.784f, 106.8285f),
                HasScene = true,
                StartKind = KartTrackStartKind.Confirmed,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(143.6366f, 523.2488f, 26.84984f),
            },
            new TrackSpec
            {
                AssetName = "village_R03",
                Laps = 1u,
                DisplayName = "빌리지 붐힐터널",
                RaceMode = "스피드",
                Difficulty = 4,
                Minimum = new KartVec3(23.36246f, 46.18066f, 8.646021f),
                Maximum = new KartVec3(1213.144f, 1447.473f, 90.51943f),
                HasScene = true,
                StartKind = KartTrackStartKind.AxisClear,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(215.792f, 600.6079f, 27.30944f),
            },

            // --- not the demo's ------------------------------------------------
            //
            // Two courses from the later TC Games client, kept so they can be
            // driven. The bounds are measured off their own exported meshes and are
            // theirs; nothing about the start is.
            //
            // Both carry a course in the same format the demo's tracks use - the
            // same `the::ToRoad` objects and the same `course` tag of `road`
            // children - so their checkpoints and their start pose are read out of
            // the asset exactly as the thirteen above are. The tables are generated
            // by the same tool into kart_course_data_tcgames.c, kept apart from the
            // demo's so that reading one is never reading the other.
            //
            // StartKind is None on both, and that is not a shrug: neither track
            // paints the flat "start"-textured stripe the demo's do - every
            // start-textured mesh in them is a vertical banner - so there is no
            // start quad, which is exactly what None says.
            //
            // It also has to be None, because the height the gates were baked
            // against follows from it. derive_course_gates.py drops every gate by
            // the start stripe's Z where there is one and by the mesh AABB's
            // minimum Z where there is not, and SceneGroundZ has to make the same
            // choice or the two end up in different spaces - which is precisely
            // what happened when this said AxisClear: the scene was dropped by the
            // start gate's Z and the checkpoints floated 433 units over northeu and
            // 95 over castle.
            //
            // StartLine is kept as the course's own start gate for the record. It
            // is not read while the course is loaded, since the kart is placed by
            // KartCourse.StartPose.
            new TrackSpec
            {
                AssetName = "northeu_R01",
                Source = KartAssetSource.TCGames,
                DisplayName = "노르테유 익스프레스",
                RaceMode = "스피드",
                Difficulty = 0,
                Minimum = new KartVec3(-25.168457f, -32.016785f, -49.353912f),
                Maximum = new KartVec3(1689.1127f, 1952.6205f, 480.42487f),
                HasScene = true,
                StartKind = KartTrackStartKind.None,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(795.53918f, 1421.47546f, 383.58554f),
                Laps = 1u,
            },
            new TrackSpec
            {
                AssetName = "castle_R01",
                Source = KartAssetSource.TCGames,
                DisplayName = "대저택 은밀한 지하실",
                RaceMode = "스피드",
                Difficulty = 0,
                Minimum = new KartVec3(54.815975f, 12.568909f, 43.814198f),
                Maximum = new KartVec3(1131.1954f, 1477.7528f, 226.89317f),
                HasScene = true,
                StartKind = KartTrackStartKind.None,
                StartAxis = KartTrackStartAxis.Y,
                StartLine = new KartVec3(486.71613f, 1025.87616f, 138.92685f),

                Laps = 2u,
            },
        };

        /// <summary>
        /// <c>ORIGINAL_MINIMAP_MAPPINGS[]</c>, the serialized <c>the::ToMinimap</c>
        /// values from each track.1s. <c>flat_test</c> has none, which is why it
        /// is absent rather than zeroed.
        /// </summary>
        private static KartMinimapMapping Minimap(
            string assetName, float originX, float originY, float scale)
            => new KartMinimapMapping
            {
                AssetName = assetName,
                OriginX = originX,
                OriginY = originY,
                Scale = scale,
                Width = 256u,
                Height = 256u,
            };

        private static readonly KartMinimapMapping[] MinimapTable =
        {
            Minimap("desert_I01", 529.7672f, 598.147949f, 0.3f),
            Minimap("desert_I02", 390.977051f, 595.9359f, 0.3f),
            Minimap("desert_R01", 614.902832f, 461.68692f, 0.3f),
            Minimap("forest_I01", 468.612122f, 408.32428f, 0.3f),
            Minimap("forest_I02", 481.403046f, 618.0281f, 0.3f),
            Minimap("forest_R02", 571.3153f, 723.649048f, 0.211053431f),
            Minimap("ice_I01", 562.5881f, 751.542236f, 0.3f),
            Minimap("ice_I02", 566.9641f, 487.197235f, 0.3f),
            Minimap("ice_R01", 863.445557f, 897.8673f, 0.160318315f),
            Minimap("village_I01", 326.083923f, 484.866364f, 0.3f),
            Minimap("village_I02", 259.2857f, 356.454651f, 0.3f),
            Minimap("village_R01", 631.49884f, 744.7253f, 0.144852176f),
            Minimap("village_R03", 573.462f, 769.314758f, 0.1878337f),

            // The later client's two, read out of their own track.1s by the same
            // layout: a the::ToMinimap payload of origin, scale, width and height.
            // The reader was checked against four of the thirteen above before it
            // was pointed at these, and it reproduced their recovered values to the
            // float; 256x256 here as well.
            Minimap("northeu_R01", 823.046692f, 913.042908f, 0.13309437f),
            Minimap("castle_R01", 696.28717f, 945.925293f, 0.246547982f),
        };

        public static IReadOnlyList<TrackSpec> Tracks { get; } = TrackTable;

        public static IReadOnlyList<KartMinimapMapping> MinimapMappings { get; } = MinimapTable;

        /// <summary>The track's map mapping, or null where it has none.</summary>
        public static KartMinimapMapping FindMinimapMapping(string assetName)
        {
            foreach (KartMinimapMapping mapping in MinimapTable)
            {
                if (string.Equals(mapping.AssetName, assetName, StringComparison.Ordinal))
                {
                    return mapping;
                }
            }
            return null;
        }

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
            foreach (TrackSpec track in TrackTable)
            {
                if (string.Equals(track.AssetName, assetName, StringComparison.Ordinal)) return track;
            }
            return null;
        }
    }
}
