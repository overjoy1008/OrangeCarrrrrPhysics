using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// One row of the recovered <c>KARTS[]</c> table, as an editable asset.
    ///
    /// The C build compiles the table in; here each kart is its own asset so the
    /// parameter editor, the K menu and the prefab reference all hang off one
    /// object. The values still come from the demo's <c>parameter.xml</c> — use
    /// <see cref="ResetToRecoveredDefaults"/> to put an edited asset back.
    /// </summary>
    [CreateAssetMenu(
        fileName = "KartSpec",
        menuName = "OrangeCarrrrr/Kart Spec",
        order = 0)]
    public sealed class KartSpecAsset : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Asset name inside the original kart.rho. The archive spells Cotton as \"cotten\".")]
        [SerializeField] private string _assetName = "cotten5";

        [Tooltip("Which client the body and its numbers came from.")]
        [SerializeField] private KartAssetSource _source;

        [Header("Model")]
        [Tooltip("Imported KTRK model root. Instantiated by KartView.")]
        [SerializeField] private GameObject _modelPrefab;

        [Tooltip(
            "The kart's atlas as it ships beside model.1s — a template, not a " +
            "finished skin. KartView paints it for the chosen colour.")]
        [SerializeField] private Texture2D _skinTemplate;

        [Header("Recovered geometry (kart.rho model-root AABB)")]
        [SerializeField] private float _halfWidth = 0.87533175f;
        [SerializeField] private float _halfLength = 1.13917575f;
        [SerializeField] private float _modelHeight = 0.9822382f;
        [SerializeField] private float _suspensionRange = 0.5f;
        [SerializeField] private float _groundedDragScale = 1.0f;

        [Header("Recovered dynamics (parameter.xml)")]
        [SerializeField] private KartDynamicsConfig _dynamics = KartDynamicsConfig.Standard();

        [Header("Booster storage")]
        [SerializeField] private uint _maxBoosters = KartDemoData.DefaultMaxBoosters;

        public string AssetName => _assetName;

        /// <summary>
        /// What the kart is called — 버스트 SR, 뉴 코튼, 골든 파라곤 9.
        ///
        /// Derived from the asset name rather than stored beside it, the way
        /// <see cref="TrackSpecAsset.SceneName"/> is: there is one right answer
        /// per kart and a second editable copy could only drift out of step.
        /// </summary>
        public string DisplayName => KartDisplayName.For(_assetName);

        /// <summary>
        /// Which client this came out of. Serialised as Demo on an asset that
        /// predates the field, so the shipped table is asked instead — the same
        /// fallback the lap count uses.
        /// </summary>
        public KartAssetSource Source => _source != KartAssetSource.Demo
            ? _source
            : KartDemoData.FindKart(_assetName)?.Source ?? KartAssetSource.Demo;
        public GameObject ModelPrefab => _modelPrefab;

        /// <summary>
        /// The kart's unpainted atlas. It hangs off the spec rather than off the
        /// prefab so that selecting a kart brings its skin with it, the same way
        /// it already brings its geometry and its dynamics.
        /// </summary>
        public Texture2D SkinTemplate => _skinTemplate;
        public float ModelHeight => _modelHeight;
        public uint MaxBoosters => _maxBoosters;
        public KartDynamicsConfig Dynamics => _dynamics;

        public KartSimulationGeometry Geometry => new KartSimulationGeometry
        {
            HalfWidth = _halfWidth,
            HalfLength = _halfLength,
            SuspensionRange = _suspensionRange,
            GroundedDragScale = _groundedDragScale,
        };

        public float Width => _halfWidth * 2f;
        public float Length => _halfLength * 2f;

        public KartSpec ToSpec() => new KartSpec
        {
            AssetName = _assetName,
            Source = Source,
            Dynamics = _dynamics,
            Geometry = Geometry,
            ModelHeight = _modelHeight,
            MaxBoosters = _maxBoosters,
        };

        /// <summary>Copies a recovered row back over the editable fields.</summary>
        public void ApplySpec(KartSpec spec)
        {
            _assetName = spec.AssetName;
            _source = spec.Source;
            _dynamics = spec.Dynamics;
            _halfWidth = spec.Geometry.HalfWidth;
            _halfLength = spec.Geometry.HalfLength;
            _suspensionRange = spec.Geometry.SuspensionRange;
            _groundedDragScale = spec.Geometry.GroundedDragScale;
            _modelHeight = spec.ModelHeight;
            _maxBoosters = spec.MaxBoosters;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only, for the asset builder to wire what it imported.</summary>
        internal void SetContent(GameObject modelPrefab, Texture2D skinTemplate)
        {
            _modelPrefab = modelPrefab;
            _skinTemplate = skinTemplate;
        }
#endif

        [ContextMenu("Reset to recovered defaults")]
        public void ResetToRecoveredDefaults()
        {
            KartSpec recovered = KartDemoData.FindKart(_assetName);
            if (recovered == null)
            {
                Debug.LogWarning($"No recovered row for kart '{_assetName}'.", this);
                return;
            }
            ApplySpec(recovered);
        }
    }
}
