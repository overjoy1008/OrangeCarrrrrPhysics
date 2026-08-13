using System.Collections.Generic;
using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The twin rear-wheel skid marks, ported from <c>update_skid_marks</c>.
    ///
    /// Each side builds a ribbon of cross-sections while the kart is drifting and
    /// both rear wheels are on the ground. A strip closes when the drift ends or
    /// when it fills, and the pool of fifty per side is recycled round-robin, so
    /// the marks are bounded without ever being faded out — that is what the
    /// original does, and the oldest strip simply gets overwritten.
    ///
    /// The cross-section's lateral axis is the load-bearing part. <c>0x00428D30</c>
    /// builds a second frame from <c>-linearVelocity</c> and the chassis' third
    /// column; <c>0x00470110</c> then transforms (-1, 0, 0) by that frame, dots it
    /// against the chassis' own axis, clamps the dot at 0.7 and applies half the
    /// width. Taking the ribbon straight from the chassis instead would let the
    /// steering angle turn every cross-section, which is not how the original
    /// lays them down.
    ///
    /// One mesh carries every strip. It is rebuilt only on the frames that append
    /// a section, which is at most once per frame and only while drifting.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SkidMarkTrail : MonoBehaviour
    {
        public const int SideCount = 2;
        public const int PoolSize = 50;
        public const int MaxCrossSections = 44;

        public const float Width = 0.28f;
        public const float SurfaceBias = 0.02f;
        public const float LocalX = 0.61f;
        public const float LocalY = 0.5f;

        /// <summary>Where the original samples the ground: wheel contacts 2 and 3.</summary>
        private const float RearWheelX = 0.8f;
        private const float RearWheelY = 0.8f;

        /// <summary>The clamp <c>0x00470470</c> puts on the velocity frame's dot.</summary>
        private const float MinimumFrameDot = 0.7f;

        /// <summary>The ribbon uses the left half of the 64x32 source.</summary>
        private const float TextureU = 0.499999f;

        /// <summary>
        /// One cross-section per 16 ms, which is the window timer the original
        /// lays them down on.
        ///
        /// Tying this to the render frame instead makes the marks frame-rate
        /// dependent: a strip holds a fixed 44 sections, so at 133 fps it spans a
        /// third of a second rather than three quarters, and the seam between
        /// strips comes round twice as often. The marks are supposed to measure
        /// distance travelled, not frames drawn.
        /// </summary>
        private const uint EmitIntervalMs = 16u;

        private struct CrossSection
        {
            public Vector3 Left;
            public Vector3 Right;
            public float TextureV;
        }

        private sealed class Strip
        {
            public readonly CrossSection[] Sections = new CrossSection[MaxCrossSections];
            public int Count;

            public void Clear() => Count = 0;
        }

        private readonly Strip[,] _strips = new Strip[SideCount, PoolSize];
        private readonly int[] _next = new int[SideCount];
        private readonly int[] _building = { -1, -1 };
        private readonly KartGroundHit[] _hits = new KartGroundHit[SideCount];

        private readonly List<Vector3> _vertices = new List<Vector3>(SideCount * PoolSize * MaxCrossSections * 2);
        private readonly List<Vector2> _uvs = new List<Vector2>(SideCount * PoolSize * MaxCrossSections * 2);
        private readonly List<int> _indices = new List<int>(SideCount * PoolSize * MaxCrossSections * 6);

        [Tooltip("effect.rho/skidmark/skidmark.tga. Left empty, the marks draw flat, which is the C build's own fallback.")]
        [SerializeField] private Texture2D _texture;

        [Tooltip("Left empty, an unlit transparent material is built at run time.")]
        [SerializeField] private Material _material;

        /// <summary>The flat colour <c>raster_skid_marks</c> falls back to.</summary>
        private static readonly Color32 UntexturedColor = new Color32(0x14, 0x14, 0x14, 0xFF);

        private const string DefaultTexturePath = "Assets/_Project/Art/Effects/skidmark.png";

        /// <summary>
        /// The skid faces the port can lay, in cycle order.
        ///
        /// The first is the demo's own <c>effect.rho/skidmark/skidmark.tga</c>. The
        /// rest are not the demo's — <c>rainbow</c> comes from the later client's
        /// <c>stuff.rho/skidMark</c>, which is why it lives under a <c>TCGames</c>
        /// folder of its own — and the 2004 game has no way to change the mark at
        /// all, so the cycle itself is the port's.
        /// </summary>
        private static readonly (string Name, string Path)[] Styles =
        {
            ("demo", DefaultTexturePath),
            ("rainbow", "Assets/_Project/Art/Effects/TCGames/rainbow.png"),
        };

        [Tooltip(
            "Which of the skid faces is being laid. Cycled by the simulator's key. " +
            "Opens on the demo's own mark; I cycles to rainbow.")]
        [SerializeField] private int _style = DemoStyle;

        /// <summary>
        /// The face the simulator opens on: the demo's own, which is
        /// <see cref="Styles"/>[0].
        ///
        /// It opened on rainbow for a while, which put the later client's mark
        /// under a 2004 kart before anyone had pressed a key. The recovered one is
        /// the better default for the same reason the recovered parameters are.
        /// </summary>
        private const int DemoStyle = 0;

        /// <summary>The face now being laid, for the HUD.</summary>
        public string StyleName => Styles[Wrap(_style)].Name;

        public int StyleCount => Styles.Length;

        private static int Wrap(int style)
            => ((style % Styles.Length) + Styles.Length) % Styles.Length;

        /// <summary>
        /// Moves to the next face. The marks already on the ground keep the one
        /// they were laid with only until the next clear — they share a material —
        /// so this changes the whole trail at once.
        /// </summary>
        public void NextStyle()
        {
            _style = Wrap(_style + 1);
            _texture = null;
            ApplyStyle();
        }

        /// <summary>Puts the current face on the material.</summary>
        private void ApplyStyle()
        {
            ResolveDefaultTexture();

            Material material = ResolveMaterial();
            if (material == null) return;

            material.mainTexture = _texture;
            material.color = _texture != null ? Color.white : (Color)UntexturedColor;
        }

        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Material _runtimeMaterial;
        private bool _dirty;
        private uint _emitClockMs;

        /// <summary>How many quads are currently laid down, for the HUD.</summary>
        public int SegmentCount { get; private set; }

        private void Awake() => EnsureBuffers();

        private void OnEnable()
        {
            // The ribbon carries world-space positions, so anything but identity
            // here would move the marks with the object holding them.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;

            EnsureBuffers();
            Clear();
        }

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

#if UNITY_EDITOR
        /// <summary>
        /// Resolves the source texture the first time the component is seen in the
        /// editor, so the reference is serialised into the scene and the marks are
        /// textured in a build without anyone having had to drag it in.
        /// </summary>
        private void OnValidate()
        {
            if (_texture != null) return;
            ResolveDefaultTexture();
            if (_texture != null) UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// Finds the source texture for an instance created at run time, which
        /// never went through the editor and so has nothing serialised. Without
        /// this the marks fall back to a flat colour even though the asset is
        /// sitting in the project.
        /// </summary>
        private void ResolveDefaultTexture()
        {
#if UNITY_EDITOR
            if (_texture == null)
            {
                _texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Styles[Wrap(_style)].Path);
            }
#endif
        }

        /// <summary>
        /// The material the ribbon draws with: whatever was assigned, else an
        /// unlit transparent one built here. Unlit because the original composites
        /// the mark straight onto the surface with no lighting term at all.
        /// </summary>
        private Material ResolveMaterial()
        {
            if (_material != null) return _material;
            if (_runtimeMaterial != null) return _runtimeMaterial;

            ResolveDefaultTexture();

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                Debug.LogWarning("No unlit shader found; skid marks will not draw.", this);
                return null;
            }

            _runtimeMaterial = new Material(shader)
            {
                name = "Skid mark (runtime)",
                hideFlags = HideFlags.DontSave,
            };

            if (_texture != null)
            {
                _runtimeMaterial.SetTexture("_BaseMap", _texture);
                if (_runtimeMaterial.HasProperty("_MainTex")) _runtimeMaterial.SetTexture("_MainTex", _texture);
                _runtimeMaterial.SetColor("_BaseColor", Color.white);
            }
            else
            {
                _runtimeMaterial.SetColor("_BaseColor", UntexturedColor);
            }

            // Two-sided. The ribbon's own winding puts its geometric normal
            // downward, and `raster_world_triangle` never culled anything — it
            // clips against the near plane and fills, which is why the original
            // draws these regardless of facing. URP's default Cull Back would
            // discard every mark when seen from above.
            _runtimeMaterial.SetFloat("_Cull", (float)CullMode.Off);
            _runtimeMaterial.doubleSidedGI = true;

            // Alpha blended and depth-write off: the marks lie flat on the road
            // and have to sort over it without fighting it.
            _runtimeMaterial.SetFloat("_Surface", 1f);
            _runtimeMaterial.SetFloat("_Blend", 0f);
            _runtimeMaterial.SetFloat("_ZWrite", 0f);
            _runtimeMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _runtimeMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _runtimeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _runtimeMaterial.DisableKeyword("_ALPHATEST_ON");
            _runtimeMaterial.renderQueue = (int)RenderQueue.Transparent;

            return _runtimeMaterial;
        }

        private void EnsureBuffers()
        {
            for (int side = 0; side < SideCount; ++side)
            {
                for (int i = 0; i < PoolSize; ++i)
                {
                    _strips[side, i] ??= new Strip();
                }
            }

            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Skid marks", indexFormat = IndexFormat.UInt32 };
                _mesh.MarkDynamic();
            }
            if (_filter != null && _filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;

            if (_renderer != null)
            {
                Material resolved = ResolveMaterial();
                if (resolved != null && _renderer.sharedMaterial != resolved)
                {
                    _renderer.sharedMaterial = resolved;
                }
                // The ribbon is written in world space, so the transform must not
                // move it and it must never be culled by its own tiny bounds.
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }
        }

        /// <summary>Drops every mark. The <c>R</c> key.</summary>
        public void Clear()
        {
            for (int side = 0; side < SideCount; ++side)
            {
                for (int i = 0; i < PoolSize; ++i) _strips[side, i]?.Clear();
                _next[side] = 0;
                _building[side] = -1;
            }
            _emitClockMs = 0u;
            _dirty = true;
            Rebuild();
        }

        /// <summary>
        /// One frame of emission. The ground query is the same one the wheels use,
        /// so a mark only appears where the physics agrees there is ground.
        /// </summary>
        public void Step(KartSimulationState kart, IKartGroundQuery ground, uint elapsedMs)
        {
            if (kart == null || ground == null) return;

            _emitClockMs += elapsedMs;
            if (_emitClockMs < EmitIntervalMs) return;

            // One section per tick however far behind the clock has fallen: a
            // second section at the same pose would be a zero-length segment.
            // Anything beyond one interval is dropped rather than banked.
            _emitClockMs = 0u;

            kart.Orientation.GetAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up);

            bool bothContact = true;

            for (int side = 0; side < SideCount; ++side)
            {
                float sign = side == 0 ? -RearWheelX : RearWheelX;

                KartVec3 start = kart.Position
                    + right * (kart.Geometry.HalfWidth * sign)
                    + forward * (-kart.Geometry.HalfLength * RearWheelY)
                    + up * kart.Geometry.SuspensionRange;
                KartVec3 delta = up * (-2f * kart.Geometry.SuspensionRange);

                if (!ground.QueryGround(start, delta, out _hits[side])) bothContact = false;
            }

            if (!DriftVisualActive(kart) || !bothContact)
            {
                // Closing the open strips rather than clearing them is what keeps
                // the marks on the ground after the drift ends.
                for (int side = 0; side < SideCount; ++side) _building[side] = -1;
                return;
            }

            for (int side = 0; side < SideCount; ++side)
            {
                Append(side, kart, right, forward, up, _hits[side]);
            }

            _dirty = true;
        }

        /// <summary>
        /// The HUD, the sound, the gauge and the marks all treat the linger as
        /// drifting. Kept as one definition in <see cref="KartGauge"/> rather than
        /// a copy here: four callers agreeing by coincidence is three chances for
        /// one of them to drift.
        /// </summary>
        public static bool DriftVisualActive(KartSimulationState kart)
            => KartGauge.DriftVisualActive(kart);

        private void Append(
            int side,
            KartSimulationState kart,
            in KartVec3 right,
            in KartVec3 forward,
            in KartVec3 up,
            in KartGroundHit hit)
        {
            Strip strip = OpenStrip(side);

            KartVec3 centre = kart.Position
                + right * (side == 0 ? -LocalX : LocalX)
                + forward * -LocalY;

            // Flattened onto the surface it was found on, so the ribbon follows a
            // bank instead of cutting through it.
            centre -= hit.Normal * KartVec3.Dot(centre - hit.Point, hit.Normal);

            KartVec3 lateral;
            if (KartVec3.Dot(kart.LinearVelocity, kart.LinearVelocity) == 0f)
            {
                lateral = right * -1f;
            }
            else
            {
                KartVec3 backward = (kart.LinearVelocity * -1f).Normalized;
                lateral = KartVec3.Cross(up, backward);
            }

            float frameDot = KartVec3.Dot(lateral, right * -1f);
            if (frameDot < MinimumFrameDot) frameDot = MinimumFrameDot;
            lateral *= frameDot * Width * 0.5f;

            KartVec3 bias = hit.Normal * SurfaceBias;
            var section = new CrossSection
            {
                Left = KartSpace.ToUnity(centre - lateral + bias),
                Right = KartSpace.ToUnity(centre + lateral + bias),
            };

            if (strip.Count != 0)
            {
                CrossSection previous = strip.Sections[strip.Count - 1];
                Vector3 before = (previous.Left + previous.Right) * 0.5f;
                Vector3 now = (section.Left + section.Right) * 0.5f;
                // The texture runs along the ribbon at one unit per unit of
                // travel, so a slow crawl does not stretch the tread pattern.
                section.TextureV = previous.TextureV + Vector3.Distance(now, before);
            }

            strip.Sections[strip.Count++] = section;
        }

        /// <summary>
        /// The strip this side is currently laying down, starting a new one when
        /// there is none or the current one is full.
        /// </summary>
        private Strip OpenStrip(int side)
        {
            Strip full = null;
            if (_building[side] >= 0)
            {
                Strip current = _strips[side, _building[side]];
                if (current.Count < MaxCrossSections) return current;
                full = current;
            }

            int index = _next[side];
            Strip strip = _strips[side, index];
            strip.Clear();
            _building[side] = index;
            _next[side] = (index + 1) % PoolSize;

            // Carry the full strip's last cross-section over as the new one's
            // first. The C build starts the replacement empty, so the quad that
            // would have joined them is never emitted and an unbroken drift shows
            // a seam every 44 sections. That is an artifact of how the pool rolls
            // over, not something the recovered geometry asks for: the sections
            // themselves are unchanged, this only stops one of them being
            // dropped. A strip that closed because the drift ended is not
            // continued — the kart has moved on by the time the next one opens.
            if (full != null && full.Count > 0)
            {
                strip.Sections[strip.Count++] = full.Sections[full.Count - 1];
            }

            return strip;
        }

        private void LateUpdate()
        {
            if (_dirty) Rebuild();
        }

        /// <summary>
        /// Rewrites the mesh from the strips.
        ///
        /// The winding matches the original's two triangles per segment —
        /// (a.left, b.left, b.right) and (a.left, b.right, a.right) — so the
        /// ribbon faces the same way it does in the C build.
        /// </summary>
        private void Rebuild()
        {
            _dirty = false;
            if (_mesh == null) EnsureBuffers();

            _vertices.Clear();
            _uvs.Clear();
            _indices.Clear();
            SegmentCount = 0;

            for (int side = 0; side < SideCount; ++side)
            {
                for (int i = 0; i < PoolSize; ++i)
                {
                    Strip strip = _strips[side, i];
                    if (strip == null || strip.Count < 2) continue;

                    int first = _vertices.Count;
                    for (int s = 0; s < strip.Count; ++s)
                    {
                        CrossSection section = strip.Sections[s];
                        _vertices.Add(section.Left);
                        _vertices.Add(section.Right);
                        _uvs.Add(new Vector2(0f, section.TextureV));
                        _uvs.Add(new Vector2(TextureU, section.TextureV));
                    }

                    for (int s = 1; s < strip.Count; ++s)
                    {
                        int a = first + (s - 1) * 2;
                        int b = first + s * 2;
                        _indices.Add(a); _indices.Add(b); _indices.Add(b + 1);
                        _indices.Add(a); _indices.Add(b + 1); _indices.Add(a + 1);
                        ++SegmentCount;
                    }
                }
            }

            _mesh.Clear();
            if (_vertices.Count == 0) return;

            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_indices, 0, calculateBounds: true);
            // The ribbon is two-sided, so these only feed lighting the unlit
            // material never reads; they are here so a lit material can be
            // dropped in without the mesh needing rebuilding.
            _mesh.RecalculateNormals();
        }
    }
}
