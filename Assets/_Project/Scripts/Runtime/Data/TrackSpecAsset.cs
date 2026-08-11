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

        [Tooltip(
            "Scene that holds this track's geometry and collision. The T key " +
            "loads it. Each track is its own scene because a KTRK scene is 375 " +
            "objects and a collision set of its own, not something to keep loaded " +
            "beside every other track.")]
        [SerializeField] private string _sceneName = "Simulator";
        [SerializeField] private string _displayName = "테스트 평지";
        [SerializeField] private string _raceMode = "테스트";
        [SerializeField, Range(0, 5)] private int _difficulty;

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

        public string AssetName => _assetName;
        public string SceneName => _sceneName;
        public string DisplayName => _displayName;
        public string RaceMode => _raceMode;
        public bool HasScene => _hasScene;

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
            Difficulty = (uint)_difficulty,
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
            _difficulty = (int)spec.Difficulty;
            _minimum = new Vector3(spec.Minimum.X, spec.Minimum.Y, spec.Minimum.Z);
            _maximum = new Vector3(spec.Maximum.X, spec.Maximum.Y, spec.Maximum.Z);
            _hasScene = spec.HasScene;
            _startKind = spec.StartKind;
            _startAxis = spec.StartAxis;
            _startLine = new Vector3(spec.StartLine.X, spec.StartLine.Y, spec.StartLine.Z);
        }
    }
}
