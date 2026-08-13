using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Places the kart model at the simulation state's pose, and paints it.
    ///
    /// The imported model's origin is its wheel-contact point — the KTRK AABB runs
    /// from z = 0.00097 up to z = 0.982 on cotten5 — so the engine's kart position
    /// maps onto the transform with no offset.
    ///
    /// The paint is done here, at run time, because that is what the original
    /// does: <c>0x00417160</c> repaints the atlas in memory when a kart is built.
    /// The atlas beside <c>model.1s</c> is a template, not a finished skin — its
    /// paint areas are transparent black, so a model drawn with the raw asset
    /// comes out with black bodywork and no number plate.
    ///
    /// Painting rather than baking is what keeps twenty-six karts affordable.
    /// Ten colours baked per kart would be 260 textures on disk; this is
    /// twenty-six templates and one 256x128 texture live at a time, rebuilt in
    /// well under a millisecond whenever the kart or the colour changes.
    /// </summary>
    [ExecuteAlways]
    public sealed class KartView : MonoBehaviour
    {
        private const string PlatePath = "Assets/_Project/Art/Karts/Common/plate.png";
        private const string NumberPath = "Assets/_Project/Art/Karts/Common/number.png";

        [Header("Kart")]
        [SerializeField] private KartSpecAsset _kart;

        [Tooltip("Where the model is instantiated. Left empty, a child is made for it.")]
        [SerializeField] private Transform _modelRoot;

        [Header("Paint")]
        [Tooltip(
            "Row of colortable.xml. The simulator opens on 0 (red); riderData.1s " +
            "itself ships 8 (pink), which KartColorTable.DefaultIndex still holds.")]
        [SerializeField] private int _colourIndex = KartColorTable.SimulatorIndex;

        [Tooltip(
            "The NEXON plate, stamped over its key block the way 0x00417160 does. " +
            "Off covers the key block with the body colour instead, which is the " +
            "plain-bodywork look rather than a blue marker showing through.")]
        [SerializeField] private bool _stampNumberPlate = true;

        [Tooltip("common/plate.png — the NEXON number plate. Resolved automatically.")]
        [SerializeField] private Texture2D _plate;

        [Tooltip(
            "On: the racing number is the kart's grade — practice 0, burst1 a 1, " +
            "solid5 a 5. Off restores the demo's own behaviour, which stamps 0 on " +
            "every kart it builds.")]
        [SerializeField] private bool _racingNumberFromGrade = true;

        [Tooltip("common/number.png — the ten-digit racing number strip. Resolved automatically.")]
        [SerializeField] private Texture2D _number;

        [Tooltip(
            "Mirrors the plate and the racing number as they are stamped. On: the " +
            "two stamps are the only thing on the atlas that arrives the wrong way " +
            "round, and mirroring them is what makes the lettering read. Turning " +
            "this off is what produced a gallery full of backwards NEXON plates " +
            "while every other scene, which had it on, read correctly.")]
        [SerializeField] private bool _mirrorStamps = true;

        [Header("Debug")]
        [Tooltip("B in the original: the body box the physics uses, as wireframe annotation.")]
        [SerializeField] private bool _showModelBounds;

        private MeshRenderer[] _renderers;
        private KartGuestSpin[] _spinners;
        private Texture2D _painted;
        private Material _material;

        private KartSpecAsset _builtKart;
        private int _paintedColour = -1;
        private int _paintedDigit = -1;
        private bool _paintedMirror;
        private Texture2D _paintedTemplate;

        public KartSpecAsset Kart
        {
            get => _kart;
            set
            {
                if (_kart == value) return;
                _kart = value;
                RebuildModel();
                ApplySkin();
            }
        }

        public Transform ModelRoot => _modelRoot != null ? _modelRoot : transform;

        /// <summary>
        /// True when this component belongs to the prefab asset on disk rather
        /// than to something in a scene.
        ///
        /// <see cref="ExecuteAlways"/> runs the callbacks there too, and a prefab
        /// asset's transform is persistent: nothing can be instantiated under it,
        /// and anything written to it would be written into the asset. The model
        /// and the paint are both views of the selected spec, so on the asset
        /// there is simply nothing to do.
        /// </summary>
        private bool IsPersistentAsset => !gameObject.scene.IsValid();

        /// <summary>The active row of the colour table.</summary>
        public int ColourIndex
        {
            get => _colourIndex;
            set
            {
                int wrapped = value % KartColorTable.Count;
                if (wrapped < 0) wrapped += KartColorTable.Count;
                if (_colourIndex == wrapped) return;
                _colourIndex = wrapped;
                ApplySkin();
            }
        }

        /// <summary>The HUD's <c>[0 red]</c>.</summary>
        public string ColourName => KartColorTable.NameAt(_colourIndex);

        /// <summary>The <c>C</c> key.</summary>
        public void NextColour() => ColourIndex = KartColorTable.Next(_colourIndex);

        public bool ShowModelBounds
        {
            get => _showModelBounds;
            set => _showModelBounds = value;
        }

        /// <summary>
        /// Throws the built model away and builds it again.
        ///
        /// The ordinary rebuild skips when the kart has not changed, which is what
        /// keeps it cheap — but a reimport changes the source asset underneath an
        /// instance that still looks current, and nothing about this object says
        /// so. The importer's post-processor calls this.
        /// </summary>
        public void ForceRebuild()
        {
            _builtKart = null;
            RebuildModel();
            ApplySkin(force: true);
        }

        /// <summary>
        /// Tells the model's effects which take the booster picked, so a kart
        /// that turns while boosting can turn at the speed it is singing at.
        /// </summary>
        public void SetSpinSlow(bool slow)
        {
            if (_spinners == null) return;
            for (int i = 0; i < _spinners.Length; ++i)
            {
                if (_spinners[i] != null) _spinners[i].Slow = slow;
            }
        }

        /// <summary>Applies one frame of simulation state.</summary>
        public void Apply(KartSimulationState kart)
        {
            if (kart == null) return;
            transform.SetPositionAndRotation(
                KartSpace.ToUnity(kart.Position),
                KartSpace.ToUnity(kart.Orientation));

            // A guest model may have a second look that follows the simulation.
            // Driven from here rather than from the effect's own Update so it
            // runs on the frames the kart is posed on and holds still on the
            // ones it is not.
            if (_spinners == null) return;
            for (int i = 0; i < _spinners.Length; ++i)
            {
                if (_spinners[i] != null) _spinners[i].Step(kart, Time.deltaTime);
            }
        }

        private void OnEnable()
        {
            ResolveSharedImages();
            RebuildModel();
            ApplySkin(force: true);
        }

        private void OnValidate()
        {
            ResolveSharedImages();

            // Deferred: OnValidate runs during deserialisation, where creating or
            // destroying objects is not allowed.
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null) return;
                    RebuildModel();
                    ApplySkin(force: true);
                };
#endif
                return;
            }

            RebuildModel();
            ApplySkin();
        }

        private void OnDestroy()
        {
            Discard(_painted);
            Discard(_material);
        }

        private static void Discard(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }

        /// <summary>
        /// Swaps the model for the selected kart's.
        ///
        /// The model is built rather than authored because there are twenty-six of
        /// them and the K key picks one at run time; keeping an instance of each in
        /// the prefab would be twenty-five hidden karts.
        ///
        /// It is built in edit mode too, so the scene view still shows a kart, but
        /// marked <see cref="HideFlags.DontSave"/> there: it is a view of the
        /// selected spec, not scene content, and it must not be written into the
        /// scene file.
        /// </summary>
        private void RebuildModel()
        {
            if (IsPersistentAsset) return;
            if (_kart == null || _kart.ModelPrefab == null) return;
            if (_builtKart == _kart && _modelRoot != null && _modelRoot.childCount > 0) return;

            Transform root = EnsureModelRoot();
            for (int i = root.childCount - 1; i >= 0; --i)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            GameObject model = Instantiate(_kart.ModelPrefab, root);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Instantiate appends "(Clone)", which reads in the hierarchy as
            // leftover debris rather than as the kart. It is the kart.
            model.name = _kart.AssetName;

            if (!Application.isPlaying) model.hideFlags = HideFlags.DontSave;

            _builtKart = _kart;
            _renderers = null;

            // Collected on the rebuild rather than looked up per frame: the model
            // only changes when the kart does.
            _spinners = model.GetComponentsInChildren<KartGuestSpin>(includeInactive: true);
            foreach (KartGuestSpin spinner in _spinners)
            {
                if (spinner != null) spinner.Reset();
            }
        }

        /// <summary>
        /// A dedicated child to hold the model, so rebuilding it cannot take the
        /// boost flame or anything else parented to the kart with it.
        /// </summary>
        private Transform EnsureModelRoot()
        {
            if (_modelRoot != null) return _modelRoot;

            Transform existing = transform.Find("Model");
            if (existing == null)
            {
                var holder = new GameObject("Model");
                holder.transform.SetParent(transform, worldPositionStays: false);
                existing = holder.transform;
            }
            _modelRoot = existing;
            return _modelRoot;
        }

        /// <summary>
        /// Repaints the atlas and puts it on the model.
        ///
        /// Skipped entirely when nothing that feeds the paint has changed, so a
        /// held frame costs a reference comparison.
        /// </summary>
        public void ApplySkin(bool force = false)
        {
            if (IsPersistentAsset) return;

            Texture2D template = _kart != null ? _kart.SkinTemplate : null;
            if (template == null) return;

            int digit = _racingNumberFromGrade
                ? KartSkinPainter.RacingNumberFor(_kart.AssetName)
                : KartSkinPainter.RacingNumberDigit;

            if (!force && _paintedColour == _colourIndex &&
                _paintedDigit == digit && _paintedMirror == _mirrorStamps &&
                _paintedTemplate == template)
            {
                return;
            }

            if (!Repaint(template, digit)) return;

            _paintedColour = _colourIndex;
            _paintedDigit = digit;
            _paintedMirror = _mirrorStamps;
            _paintedTemplate = template;

            if (force || _renderers == null)
            {
                _renderers = ModelRoot.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            }
            foreach (MeshRenderer renderer in _renderers)
            {
                if (renderer != null) renderer.sharedMaterial = _material;
            }
        }

        /// <summary>
        /// Runs <see cref="KartSkinPainter"/> over a copy of the template.
        ///
        /// Unity textures run bottom-up while the atlas, its stamp offsets and the
        /// original's own sampling all run top-down, so the rows are flipped on
        /// the way in and back on the way out. <c>GetPixels32</c> rather than the
        /// raw buffer because the raw layout follows whatever format the importer
        /// chose, which is not necessarily RGBA.
        /// </summary>
        private bool Repaint(Texture2D template, int digit)
        {
            if (!template.isReadable)
            {
                Debug.LogWarning(
                    $"'{template.name}' is not readable, so the kart cannot be painted. " +
                    "Turn Read/Write on for the kart skin templates.", this);
                return false;
            }

            int width = template.width;
            int height = template.height;
            Color32[] source = template.GetPixels32();

            var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; ++y)
            {
                int from = (height - 1 - y) * width;
                int to = y * width * 4;
                for (int x = 0; x < width; ++x)
                {
                    Color32 texel = source[from + x];
                    int o = to + x * 4;
                    bytes[o] = texel.r;
                    bytes[o + 1] = texel.g;
                    bytes[o + 2] = texel.b;
                    bytes[o + 3] = texel.a;
                }
            }

            // With the plate off the painter covers its key block with the body
            // colour, so the kart comes out plain there rather than showing the
            // 900-texel blue marker.
            KartSkinPainter.Image plate = _stampNumberPlate
                ? ToPainterImage(_plate, "plate.png")
                : default;
            KartSkinPainter.Image number = ToPainterImage(_number, "number.png");

            // The stamps are placed at texel coordinates the atlas fixes, so their
            // size is data rather than a quality setting. A plate that does not
            // cover its whole key block leaves key texels for the scan to find and
            // stamp again — four plates instead of one.
            WarnOnResize(plate, "plate.png",
                KartSkinPainter.PlateWidth, KartSkinPainter.PlateHeight);
            WarnOnResize(number, "number.png",
                KartSkinPainter.DigitWidth * 10, KartSkinPainter.DigitHeight);

            KartSkinPainter.Paint(
                new KartSkinPainter.Image(bytes, width, height),
                KartColorTable.At(_colourIndex),
                plate,
                number,
                digit,
                _mirrorStamps);

            if (_painted == null || _painted.width != width || _painted.height != height)
            {
                Discard(_painted);
                _painted = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "Kart skin (painted)",
                    // The atlas is pixel art and the original samples it with a
                    // floor, so point filtering is what keeps the plate readable.
                    filterMode = FilterMode.Point,
                    // Repeat because half the models put the steering wheel's UVs
                    // a whole turn outside 0..1; clamping flattens that part to
                    // the atlas edge colour.
                    wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.DontSave,
                };
            }

            var painted = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            {
                int from = y * width * 4;
                int to = (height - 1 - y) * width;
                for (int x = 0; x < width; ++x)
                {
                    int o = from + x * 4;
                    painted[to + x] = new Color32(bytes[o], bytes[o + 1], bytes[o + 2], bytes[o + 3]);
                }
            }

            _painted.SetPixels32(painted);
            _painted.Apply(updateMipmaps: false);

            EnsureMaterial();
            return true;
        }

        /// <summary>
        /// Reports a stamp whose imported size no longer matches the atlas.
        ///
        /// Both shared images are non-power-of-two, which Unity rescales by
        /// default. There is no filtering that makes that acceptable here.
        /// </summary>
        private void WarnOnResize(
            KartSkinPainter.Image image, string label, int expectedWidth, int expectedHeight)
        {
            if (!image.IsValid) return;
            if (image.Width == expectedWidth && image.Height == expectedHeight) return;

            Debug.LogWarning(
                $"{label} imported at {image.Width}x{image.Height} but the atlas " +
                $"expects {expectedWidth}x{expectedHeight}. Set its Non-Power of 2 " +
                "import option to None.", this);
        }

        /// <summary>
        /// One of the shared stamps as the painter wants it.
        ///
        /// A stamp that cannot be read is not a quiet no-op: skipping it leaves
        /// the key texels it was supposed to cover on the finished kart — the
        /// plate's 900-texel blue block and the number's cyan anchors — which
        /// looks like a paint bug rather than a missing import setting.
        /// </summary>
        private KartSkinPainter.Image ToPainterImage(Texture2D texture, string label)
        {
            if (texture == null)
            {
                Debug.LogWarning(
                    $"No {label} assigned, so that stamp is skipped and its key " +
                    "texels stay on the kart.", this);
                return default;
            }
            if (!texture.isReadable)
            {
                Debug.LogWarning(
                    $"'{texture.name}' ({label}) is not readable, so that stamp is " +
                    "skipped and its key texels stay on the kart. Turn Read/Write on.",
                    this);
                return default;
            }

            int width = texture.width;
            int height = texture.height;
            Color32[] source = texture.GetPixels32();
            var bytes = new byte[width * height * 4];

            for (int y = 0; y < height; ++y)
            {
                int from = (height - 1 - y) * width;
                int to = y * width * 4;
                for (int x = 0; x < width; ++x)
                {
                    Color32 texel = source[from + x];
                    int o = to + x * 4;
                    bytes[o] = texel.r;
                    bytes[o + 1] = texel.g;
                    bytes[o + 2] = texel.b;
                    bytes[o + 3] = texel.a;
                }
            }
            return new KartSkinPainter.Image(bytes, width, height);
        }

        /// <summary>
        /// Alpha-clipped rather than blended: the painter leaves the atlas filler
        /// at alpha 0, and it has to be discarded, not composited, or the kart
        /// sorts against itself.
        /// </summary>
        private void EnsureMaterial()
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) return;

                _material = new Material(shader)
                {
                    name = "Kart skin (runtime)",
                    hideFlags = HideFlags.DontSave,
                };
                _material.SetFloat("_Surface", 0f);
                _material.SetFloat("_AlphaClip", 1f);
                _material.SetFloat("_Cutoff", 0.5f);
                _material.SetFloat("_Smoothness", 0f);
                _material.SetFloat("_Metallic", 0f);
                _material.EnableKeyword("_ALPHATEST_ON");
                _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

            _material.SetTexture("_BaseMap", _painted);
            if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", _painted);
            _material.SetColor("_BaseColor", Color.white);
        }

        /// <summary>
        /// The plate and the number strip are shared by every kart, so they are
        /// resolved once by path rather than carried on all twenty-six specs.
        /// </summary>
        private void ResolveSharedImages()
        {
#if UNITY_EDITOR
            if (_plate == null) _plate = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(PlatePath);
            if (_number == null) _number = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(NumberPath);
#endif
        }

        private void OnDrawGizmosSelected()
        {
            if (!_showModelBounds || _kart == null) return;

            var size = new Vector3(_kart.Width, _kart.ModelHeight, _kart.Length);
            Gizmos.color = new Color32(120, 200, 255, 255);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
