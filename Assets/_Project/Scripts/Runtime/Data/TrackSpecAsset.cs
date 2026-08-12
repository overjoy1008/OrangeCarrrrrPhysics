using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// One row of the recovered <c>TRACKS[]</c> table, as an editable asset.
    ///
    /// The bounds are the KTRK header's own vertex AABB, so they are data rather
    /// than level design. <c>flat_test</c> is the one synthetic row: it has no
    /// mesh at all, which is what makes it the clean bench for the physics.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TrackSpec",
        menuName = "OrangeCarrrrr/Track Spec",
        order = 1)]
    public sealed class TrackSpecAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _assetName = "flat_test";

        [SerializeField] private string _displayName = "테스트 평지";
        [SerializeField] private string _raceMode = "테스트";
        [SerializeField, Range(0, 5)] private int _difficulty;

        [Tooltip("Which client the mesh and its numbers came from.")]
        [SerializeField] private KartAssetSource _source;

        [Tooltip("Laps, from the theme archive's track.xml. Zero falls back to the shipped table.")]
        [SerializeField, Range(0, 9)] private int _laps;

        [Header("Bounds, engine frame (X/Y ground, Z up)")]
        [SerializeField] private Vector3 _minimum = new Vector3(-448.1955f, -403.5915f, 0f);
        [SerializeField] private Vector3 _maximum = new Vector3(448.1955f, 403.5915f, 120f);

        [Header("Scene")]
        [Tooltip("False for the synthetic flat track, which keeps the flat ground plane and the AABB walls.")]
        [SerializeField] private bool _hasScene;

        [Header("Start pose")]
        [SerializeField] private KartTrackStartKind _startKind = KartTrackStartKind.None;
        [SerializeField] private KartTrackStartAxis _startAxis = KartTrackStartAxis.None;
        [SerializeField] private Vector3 _startLine = Vector3.zero;

        [Header("Minimap")]
        [Tooltip("The archive's own xt_minimap.png. Empty falls back to the quarter grid.")]
        [SerializeField] private Texture2D _minimap;

        [Header("Course")]
        [Tooltip("The checkpoint gates from the track's own course tag. Empty means no checkpoints.")]
        [SerializeField] private TrackCourseAsset _course;

        public string AssetName => _assetName;

        /// <summary>
        /// Which client this came out of. Serialised as Demo on an asset that
        /// predates the field, so the shipped table is asked instead — the same
        /// fallback the lap count uses.
        /// </summary>
        public KartAssetSource Source => _source != KartAssetSource.Demo
            ? _source
            : KartDemoData.FindTrack(_assetName)?.Source ?? KartAssetSource.Demo;

        /// <summary>
        /// How many laps this track is raced over.
        ///
        /// The original keeps it in the theme archive's <c>track.xml</c>, one line
        /// per track, and it is not the same for all of them: the R courses run 2
        /// — <c>village_R03</c> only 1 — while most I courses run 3.
        ///
        /// A zero here means the asset predates the field, so the shipped table is
        /// used instead. That keeps the thirteen authored assets right without
        /// re-running their builder.
        /// </summary>
        public uint Laps => _laps > 0
            ? (uint)_laps
            : KartDemoData.FindTrack(_assetName)?.Laps ?? 0u;

        /// <summary>
        /// The scene that holds this track's geometry and collision, which is the
        /// track id: <c>village_R01</c>'s scene is <c>village_R01.unity</c>.
        ///
        /// Derived rather than stored. A track already has exactly one name — the
        /// id the archive, the KTRK, the art folder and the spec asset all use —
        /// and a second, editable copy of it could only ever drift out of step.
        /// Each track is its own scene because a decoded track.1s is 375 objects
        /// and a collision set of its own, not something to keep loaded beside the
        /// other twelve.
        /// </summary>
        public string SceneName => _assetName;
        public string DisplayName => _displayName;
        public string RaceMode => _raceMode;
        public bool HasScene => _hasScene;

        /// <summary>
        /// The artwork the HUD's TRACK MAP panel draws, which the archive ships
        /// per track as <c>&lt;id&gt;_minimap.png</c>.
        ///
        /// It lives on the track rather than on the HUD prefab because the prefab
        /// is shared by all thirteen scenes: one texture wired in there is one
        /// track's map shown under every other track's kart.
        /// </summary>
        public Texture2D Minimap => _minimap;

        /// <summary>
        /// The track's checkpoint course, or null where there is none — which is
        /// only the synthetic flat track, since all thirteen real tracks carry a
        /// <c>course</c> tag.
        ///
        /// The course, not the start-line quad, is what actually knows which way
        /// round a lap is driven. Where a track has one, it supersedes
        /// <see cref="KartTrackStartKind"/>'s assumed direction entirely.
        /// </summary>
        public TrackCourseAsset Course => _course;

        /// <summary>Track footprint, the way the HUD prints it.</summary>
        public float Width => _maximum.x - _minimum.x;

        public float Length => _maximum.y - _minimum.y;

        public KartVec3 Minimum => new KartVec3(_minimum.x, _minimum.y, _minimum.z);

        public KartVec3 Maximum => new KartVec3(_maximum.x, _maximum.y, _maximum.z);

        public TrackSpec ToSpec() => new TrackSpec
        {
            AssetName = _assetName,
            DisplayName = _displayName,
            RaceMode = _raceMode,
            Source = Source,
            Difficulty = (uint)_difficulty,
            Laps = Laps,
            Minimum = Minimum,
            Maximum = Maximum,
            HasScene = _hasScene,
            StartKind = _startKind,
            StartAxis = _startAxis,
            StartLine = new KartVec3(_startLine.x, _startLine.y, _startLine.z),
        };

        public void ApplySpec(TrackSpec spec)
        {
            _assetName = spec.AssetName;
            _displayName = spec.DisplayName;
            _raceMode = spec.RaceMode;
            _source = spec.Source;
            _difficulty = (int)spec.Difficulty;
            _laps = (int)spec.Laps;
            _minimum = new Vector3(spec.Minimum.X, spec.Minimum.Y, spec.Minimum.Z);
            _maximum = new Vector3(spec.Maximum.X, spec.Maximum.Y, spec.Maximum.Z);
            _hasScene = spec.HasScene;
            _startKind = spec.StartKind;
            _startAxis = spec.StartAxis;
            _startLine = new Vector3(spec.StartLine.X, spec.StartLine.Y, spec.StartLine.Z);
        }
    }
}
