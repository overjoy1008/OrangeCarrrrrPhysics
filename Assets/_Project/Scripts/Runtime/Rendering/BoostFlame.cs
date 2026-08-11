using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The boost flame, ported from <c>raster_boost_effect</c>.
    ///
    /// It is one flat triangle behind the kart, not a particle system. The
    /// original draws exactly that — three points and a solid fill — and building
    /// something richer here would be inventing an effect the demo never had.
    ///
    /// The shape hangs off the kart's own geometry, so a wider kart gets a wider
    /// flame:
    ///
    ///   rear = position - forward * halfLength + up * 0.35
    ///   base = rear ± right * halfWidth * 0.42
    ///   tip  = rear - forward * 2.8 - up * 0.08
    ///
    /// The mesh is built once in local space and only toggled thereafter, since
    /// the triangle never changes shape while a kart is selected.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BoostFlame : MonoBehaviour
    {
        /// <summary>0x00FFC623, the rasterized pass's fill.</summary>
        public static readonly Color32 FlameColor = new Color32(255, 198, 35, 255);

        private const float RearUp = 0.35f;
        private const float BaseHalfWidthScale = 0.42f;
        private const float TipBack = 2.8f;
        private const float TipDown = 0.08f;

        [Tooltip("Read for the kart's half width and half length. Left empty, the parent's is used.")]
        [SerializeField] private KartSpecAsset _kart;

        [Tooltip("Left empty, an unlit material in the flame's own colour is built at run time.")]
        [SerializeField] private Material _material;

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _runtimeMaterial;
        private float _builtWidth = float.NaN;
        private float _builtLength = float.NaN;

        public KartSpecAsset Kart
        {
            get => _kart;
            set { _kart = value; Build(); }
        }

        private void OnEnable()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();

            Material resolved = ResolveMaterial();
            if (_renderer != null)
            {
                if (resolved != null) _renderer.sharedMaterial = resolved;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }

            Build();
            SetVisible(false);
        }

        private void OnValidate() => Build();

        private void OnDestroy()
        {
            Discard(_mesh);
            Discard(_runtimeMaterial);
        }

        private static void Discard(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }

        /// <summary>
        /// Unlit and in the flame's own colour: the original fills the triangle
        /// with a constant, so shading it would change what the effect is.
        /// </summary>
        private Material ResolveMaterial()
        {
            if (_material != null) return _material;
            if (_runtimeMaterial != null) return _runtimeMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            _runtimeMaterial = new Material(shader)
            {
                name = "Boost flame (runtime)",
                hideFlags = HideFlags.DontSave,
            };
            _runtimeMaterial.SetColor("_BaseColor", FlameColor);
            if (_runtimeMaterial.HasProperty("_Color")) _runtimeMaterial.SetColor("_Color", FlameColor);
            return _runtimeMaterial;
        }

        /// <summary>Shown exactly while any boost is running.</summary>
        public void Step(KartSimulationState kart)
        {
            if (kart == null) { SetVisible(false); return; }
            SetVisible(KartDynamics.AnyBoostActive(kart.TimedBoost, kart.InstantBoost));
        }

        public void SetVisible(bool visible)
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (_renderer != null && _renderer.enabled != visible) _renderer.enabled = visible;
        }

        private void Build()
        {
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_filter == null) return;

            KartSpecAsset spec = _kart != null
                ? _kart
                : GetComponentInParent<KartView>()?.Kart;

            float halfWidth = spec != null ? spec.Width * 0.5f : KartSimulationGeometry.Default.HalfWidth;
            float halfLength = spec != null ? spec.Length * 0.5f : KartSimulationGeometry.Default.HalfLength;

            if (_mesh != null && halfWidth == _builtWidth && halfLength == _builtLength) return;

            // Built in the engine's own body frame — right (1,0,0), forward
            // (0,-1,0), up (0,0,1) — and carried across by KartSpace, rather than
            // written straight into Unity axes. Which Unity axis a kart's nose
            // points along is KartSpace's business, and hard-coding an answer here
            // put the flame on the front of the kart the moment that changed.
            float side = halfWidth * BaseHalfWidthScale;

            var rear = new KartVec3(0f, halfLength, RearUp);
            Vector3 left = KartSpace.ToUnity(rear + new KartVec3(side, 0f, 0f));
            Vector3 right = KartSpace.ToUnity(rear + new KartVec3(-side, 0f, 0f));
            Vector3 tip = KartSpace.ToUnity(rear + new KartVec3(0f, TipBack, -TipDown));

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Boost flame" };
                _mesh.MarkDynamic();
            }

            _mesh.Clear();
            _mesh.SetVertices(new[] { left, right, tip });
            // Both windings, so the flame reads the same from either side without
            // needing a two-sided shader.
            _mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0, calculateBounds: true);
            _mesh.RecalculateNormals();

            _filter.sharedMesh = _mesh;
            _builtWidth = halfWidth;
            _builtLength = halfLength;
        }
    }
}
