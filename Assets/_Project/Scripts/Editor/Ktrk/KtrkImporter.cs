using System;
using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Imports a <c>.ktrk</c> export as a native Unity model.
    ///
    /// Going through a ScriptedImporter rather than a round trip through FBX or
    /// glTF keeps the exporter as the single source of truth: re-export and the
    /// asset reimports, with no intermediate format to lose UVs, mesh names or the
    /// collidable flag along the way.
    ///
    /// Each KTRK mesh becomes its own child GameObject so parts stay addressable
    /// (the wheels are separate meshes in the kart exports). The exporter has
    /// already applied the hierarchy transforms to the vertices, so each part is
    /// re-centred here and the offset moved onto its Transform: the rendered
    /// result is identical, but a wheel now has a pivot it can actually turn
    /// around later.
    ///
    /// Materials are built here as sub-assets, the way Unity's own model
    /// importers do it. A mesh already names the texture <c>track.1s</c> gave its
    /// material, so the import can resolve that against the PNGs
    /// <c>Tools/AssetPipeline/import_track_textures.py</c> wrote and hand each
    /// part a real material. Assigning them afterwards is not an option: the whole
    /// hierarchy is regenerated on every reimport, so anything set by hand on the
    /// imported objects is discarded the next time the importer runs.
    /// </summary>
    [ScriptedImporter(Version, "ktrk")]
    public sealed class KtrkImporter : ScriptedImporter
    {
        // Bumped whenever KartSpace changes: mesh vertices are mapped into Unity's
        // frame here and baked into the artifact, so a mapping change that does
        // not force a reimport leaves the meshes in the old frame while every
        // transform around them has moved to the new one.
        private const int Version = 19;

        /// <summary>
        /// The tracks whose sign boards are mapped upside down. See
        /// <see cref="TurnSignFacesUpright"/>.
        ///
        /// ice_R01 is the 2004 demo's only one, and that was established from the
        /// data across all thirteen. The two later-client tracks are here because
        /// the same defect is visible on them on screen; nobody has counted their
        /// faces the way the demo's were, so this is an observation rather than a
        /// measurement, and a track added later should be checked before it is
        /// added to the list.
        /// </summary>
        private static readonly string[] UpsideDownSignTracks =
        {
            "track_ice_R01",
            "track_northeu_R01",
            "track_castle_R01",
        };

        /// <summary>Written beside the PNGs by the texture converter.</summary>
        private const string ManifestFile = "textures.json";

        private const string DefaultTextureDirectory = "Textures";

        /// <summary>
        /// The texture name a track gives a face that is meant to be invisible.
        ///
        /// It names no image and none is shipped: it is a marker, and the faces
        /// carrying it are blockers and filler the original never draws. Left to
        /// the ordinary path they take the untextured fallback and come out as
        /// solid panels standing in the middle of the course, which is what
        /// castle_R01's yellow walls were.
        ///
        /// Matched by name rather than by a missing file, because a texture that
        /// is merely absent — the ad boards are, in the demo's tracks as well as
        /// these — is a different thing: that face is meant to be drawn and only
        /// its image is gone.
        /// </summary>
        private const string InvisibleTexture = "transparency";

        [Tooltip("Material for parts with no texture of their own. Leave empty for the import-time default.")]
        public Material material;

        [Tooltip(
            "Folder holding the track's converted textures, relative to this file. " +
            "The kart exports name no texture at all and ignore this.")]
        public string textureDirectory = DefaultTextureDirectory;

        [Tooltip(
            "The engine's faces come out clockwise on screen in Unity, which is " +
            "Unity's own front-face convention, so this should stay off. Turn it " +
            "on only if an asset genuinely renders inside out.")]
        public bool reverseWinding;

        [Tooltip("Moves each part's vertices to its own centre and puts the offset on the Transform.")]
        public bool recenterParts = true;

        [Tooltip(
            "Bakes the meshes tagged collidable into a TrackCollisionAsset sub-asset. " +
            "Off for kart models, which carry no collision set.")]
        public bool bakeCollision = true;

        public override void OnImportAsset(AssetImportContext context)
        {
            KtrkFile.Scene scene;
            try
            {
                scene = KtrkFile.Load(context.assetPath);
            }
            catch (System.Exception exception)
            {
                context.LogImportError($"Could not read KTRK: {exception.Message}");
                return;
            }

            string assetName = System.IO.Path.GetFileNameWithoutExtension(context.assetPath);
            var root = new GameObject(assetName);

            // Keyed on the asset rather than exposed as a setting: this is one
            // track's authoring defect, not a choice, and a checkbox would invite
            // someone to turn it on somewhere it does not belong.
            _fixUpsideDownSigns = System.Array.IndexOf(UpsideDownSignTracks, assetName) >= 0;

            Material sharedMaterial = material != null
                ? material
                : BuildFallback(context, LitShader());
            Dictionary<string, Material> materials = BuildMaterials(context, scene);

            int textured = 0;
            var partNames = new HashSet<string>();
            for (int index = 0; index < scene.Meshes.Length; ++index)
            {
                KtrkFile.Mesh source = scene.Meshes[index];
                if (source.Vertices.Length == 0 || source.Indices.Length == 0) continue;

                string partName = UniquePartName(source.Name, index, partNames);
                Mesh mesh = BuildMesh(source, partName, out Vector3 pivot);

                var part = new GameObject(partName);
                part.transform.SetParent(root.transform, worldPositionStays: false);
                part.transform.localPosition = pivot;

                var filter = part.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                Material matched = Lookup(materials, source.Texture);
                if (matched != null) ++textured;

                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = matched ?? sharedMaterial;

                context.AddObjectToAsset(partName, mesh);
            }

            if (materials.Count > 0)
            {
                Debug.Log(
                    $"KTRK materials '{assetName}': {materials.Count} built, " +
                    $"{textured} of {scene.Meshes.Length} parts textured.");
            }

            if (bakeCollision) BakeCollision(context, scene, assetName);

            context.AddObjectToAsset("root", root);
            context.SetMainObject(root);

            Bounds bounds = KartSpace.ToUnityBounds(
                new KartVec3(scene.Minimum[0], scene.Minimum[1], scene.Minimum[2]),
                new KartVec3(scene.Maximum[0], scene.Maximum[1], scene.Maximum[2]));

            // Printed so an import can be checked against the recovered geometry
            // without opening the asset: cotten5 must come out 1.751 x 2.278.
            Debug.Log(
                $"KTRK v{scene.Version} '{assetName}': {scene.Meshes.Length} parts, " +
                $"{scene.TotalVertexCount} vertices, {scene.TotalTriangleCount} triangles, " +
                $"Unity bounds size {bounds.size.ToString("F4")} (x width, y height, z length).");
        }

        /// <summary>
        /// One material per distinct texture the scene names, as sub-assets.
        ///
        /// Names that resolve to no PNG are simply absent from the result and
        /// their parts fall back to <see cref="material"/>. On village_R01 that is
        /// the six <c>ad_*</c> advertising slots: the demo archives carry no
        /// pixels for them, and the C build draws them plain too.
        /// </summary>
        private Dictionary<string, Material> BuildMaterials(
            AssetImportContext context, KtrkFile.Scene scene)
        {
            var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

            string assetDirectory = Path.GetDirectoryName(context.assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(assetDirectory)) return materials;

            // A meta written before this field existed deserialises it as blank.
            // Falling back to the default rather than to "no textures" keeps an
            // older asset from silently importing untextured.
            string folder = string.IsNullOrWhiteSpace(textureDirectory)
                ? DefaultTextureDirectory
                : textureDirectory.Trim().Trim('/');

            string root = $"{assetDirectory}/{folder}";
            HashSet<string> cutouts = ReadCutoutNames(context, root);

            Shader shader = LitShader();
            if (shader == null)
            {
                // Never fall back to the built-in Standard shader. Standard is not
                // a URP shader, so a material built on it draws magenta, and the
                // artifact is cached — the track then stays pink on that machine
                // until something forces a reimport, with nothing on screen to say
                // why. An import error is recoverable and says what happened;
                // KtrkMaterialRepair also clears it on the next editor load.
                context.LogImportError(
                    $"'{LitShaderName}' was not available when this imported, so no " +
                    "materials were built. Reimport the asset once the render " +
                    "pipeline has loaded.");
                return materials;
            }

            // So the artifact is rebuilt if the shader itself changes or moves.
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (!string.IsNullOrEmpty(shaderPath)) context.DependsOnSourceAsset(shaderPath);

            foreach (KtrkFile.Mesh mesh in scene.Meshes)
            {
                string name = mesh.Texture?.Trim();
                if (string.IsNullOrEmpty(name) || materials.ContainsKey(name)) continue;

                Texture2D texture = LoadTexture(context, root, name);

                // The invisible marker gets a material of its own rather than a
                // texture: fully transparent, and left out of the depth buffer so
                // it cannot hide what is behind it.
                if (IsInvisible(name))
                {
                    materials[name] = BuildInvisible(context, shader, name);
                    continue;
                }

                if (texture == null) continue;

                bool cutout = cutouts.Contains(name);
                var built = new Material(shader) { name = name };
                built.SetTexture(BaseMapId, texture);
                if (built.HasProperty(MainTexId)) built.SetTexture(MainTexId, texture);
                built.SetColor(BaseColorId, Color.white);
                built.SetFloat(SurfaceId, 0f);
                built.SetFloat(SmoothnessId, 0f);
                built.SetFloat(MetallicId, 0f);

                // Drawn from both sides. The 2004 renderer does not cull, and the
                // tracks are authored on that: fences, banners and the desert jump
                // ramps are single quads with no back face of their own, so
                // culling makes them vanish from one approach. URP's Lit shader
                // needs both the property and the render-face enum, since the
                // inspector reads one and the pass reads the other.
                built.SetFloat(CullId, (float)CullMode.Off);
                built.SetFloat(RenderFaceId, RenderFaceBoth);
                built.doubleSidedGI = true;

                // Cut out rather than blended: the fences, foliage and flags are
                // hard-edged cutouts, and clipping keeps them out of the
                // transparent queue where they would sort against each other.
                built.SetFloat(AlphaClipId, cutout ? 1f : 0f);
                built.SetFloat(CutoffId, 0.5f);
                if (cutout)
                {
                    built.EnableKeyword("_ALPHATEST_ON");
                    built.renderQueue = (int)RenderQueue.AlphaTest;
                }
                else
                {
                    built.DisableKeyword("_ALPHATEST_ON");
                    built.renderQueue = (int)RenderQueue.Geometry;
                }

                context.AddObjectToAsset($"material_{name}", built);
                materials[name] = built;
            }

            return materials;
        }

        /// <summary>
        /// A texture by the name the mesh gives it.
        ///
        /// Almost all of them are PNGs the converter wrote. Three are not: the
        /// desert ramp and railing textures have Korean file names, which the
        /// converter dropped, and they are carried as the archive's own DDS
        /// instead — Unity reads DDS directly, so the pixels stay the originals
        /// rather than going through a re-encode to recover them.
        ///
        /// Both candidates are declared as dependencies whether or not the file is
        /// there, so a texture that appears later brings the scene back through
        /// here.
        /// </summary>
        /// <summary>
        /// The material an invisible face gets: clear, and lit by nothing.
        ///
        /// Transparent alone is not enough. URP's Lit shader still runs its
        /// specular and reflection terms on a surface whose alpha is zero, so a
        /// clear pane picks up a highlight and reads as glass — which is what the
        /// mirrored sheen on castle_R01 was. Turning both terms off, and the
        /// smoothness with them, leaves nothing for a light to catch.
        /// </summary>
        private static Material BuildInvisible(
            AssetImportContext context, Shader shader, string name)
        {
            var blank = new Material(shader) { name = name };
            blank.SetColor(BaseColorId, new Color(1f, 1f, 1f, 0f));
            blank.SetFloat(SurfaceId, 1f);
            blank.SetFloat(AlphaClipId, 0f);
            blank.SetFloat(SmoothnessId, 0f);
            blank.SetFloat(MetallicId, 0f);
            blank.SetInt("_ZWrite", 0);

            // The keyword is what the pass reads; the float is what the inspector
            // shows. URP needs both set or the term stays on.
            blank.SetFloat("_SpecularHighlights", 0f);
            blank.SetFloat("_EnvironmentReflections", 0f);
            blank.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            blank.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            blank.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            context.AddObjectToAsset($"material_{name}", blank);
            return blank;
        }

        /// <summary>Whether a texture name is the invisible marker.</summary>
        private static bool IsInvisible(string name)
            => string.Equals(name, InvisibleTexture, StringComparison.OrdinalIgnoreCase);

        private static Texture2D LoadTexture(AssetImportContext context, string root, string name)
        {
            Texture2D found = null;
            foreach (string extension in TextureExtensions)
            {
                string path = $"{root}/{name}{extension}";
                context.DependsOnSourceAsset(path);
                if (found == null) found = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return found;
        }

        private static readonly string[] TextureExtensions = { ".png", ".dds" };

        /// <summary>
        /// The texture names that actually carry see-through texels, from the
        /// manifest the converter writes. Absent manifest means every material
        /// comes out opaque, which is the safe half of the guess.
        /// </summary>
        private static HashSet<string> ReadCutoutNames(AssetImportContext context, string root)
        {
            var cutouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string manifestPath = $"{root}/{ManifestFile}";
            context.DependsOnSourceAsset(manifestPath);

            string full = Path.GetFullPath(manifestPath);
            if (!File.Exists(full)) return cutouts;

            try
            {
                var manifest = JsonUtility.FromJson<TextureManifest>(File.ReadAllText(full));
                if (manifest?.textures == null) return cutouts;

                foreach (TextureManifestEntry entry in manifest.textures)
                {
                    if (entry.transparent && !string.IsNullOrWhiteSpace(entry.name))
                    {
                        cutouts.Add(entry.name.Trim());
                    }
                }
            }
            catch (System.Exception exception)
            {
                context.LogImportWarning(
                    $"Could not read {ManifestFile}; every material will be opaque. {exception.Message}");
            }

            return cutouts;
        }

        private static Material Lookup(Dictionary<string, Material> materials, string textureName)
        {
            if (materials.Count == 0 || string.IsNullOrWhiteSpace(textureName)) return null;
            return materials.TryGetValue(textureName.Trim(), out Material found) ? found : null;
        }

        /// <summary>The shader every track material is built on.</summary>
        internal const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// URP's Lit, or null.
        /// </summary>
        internal static Shader LitShader()
        {
            // The pipeline asset is asked first. Shader.Find only sees shaders the
            // asset database has already imported, and on a fresh clone — a
            // machine that has just pulled the repository and has no Library —
            // this importer can run before the URP package's shaders are in it.
            // The pipeline asset holds a direct reference, so it answers whether
            // or not the search index is ready yet.
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultShader != null) return pipeline.defaultShader;

            return Shader.Find(LitShaderName);
        }

        // JsonUtility needs concrete serializable types whose field names match
        // the converter's manifest exactly.
        [System.Serializable]
        private sealed class TextureManifest
        {
            public TextureManifestEntry[] textures;
        }

        [System.Serializable]
        private struct TextureManifestEntry
        {
            public string name;
            public bool transparent;
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int RenderFaceId = Shader.PropertyToID("_RenderFace");

        /// <summary>
        /// URP's <c>RenderFace.Both</c>. Written as the number rather than the
        /// enum so this file does not take a dependency on the URP namespace for
        /// one constant.
        /// </summary>
        private const float RenderFaceBoth = 0f;

        /// <summary>
        /// Collects the meshes the asset tags collidable into one flat triangle
        /// set, in the exporter's own asset space.
        ///
        /// The physics never reads the render meshes: it works in the engine
        /// frame, and only against these. On village_R01 that is 55 meshes out of
        /// 375.
        /// </summary>
        private static void BakeCollision(
            AssetImportContext context, KtrkFile.Scene scene, string assetName)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var meshVertexStart = new List<int>();
            var meshVertexCount = new List<int>();
            var meshIndexStart = new List<int>();
            var meshIndexCount = new List<int>();
            var meshMinimum = new List<Vector3>();
            var meshMaximum = new List<Vector3>();

            foreach (KtrkFile.Mesh mesh in scene.Meshes)
            {
                if (!mesh.IsCollidable) continue;
                if (mesh.Vertices.Length == 0 || mesh.Indices.Length < 3) continue;

                int vertexStart = vertices.Count;
                int indexStart = indices.Count;

                var minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                foreach (KtrkFile.Vertex vertex in mesh.Vertices)
                {
                    // Asset space, not Unity space: the queries run in the
                    // engine's frame where X/Y are the ground and Z is height.
                    var position = new Vector3(vertex.X, vertex.Y, vertex.Z);
                    vertices.Add(position);
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
                foreach (int index in mesh.Indices) indices.Add(vertexStart + index);

                meshVertexStart.Add(vertexStart);
                meshVertexCount.Add(mesh.Vertices.Length);
                meshIndexStart.Add(indexStart);
                meshIndexCount.Add(mesh.Indices.Length);
                meshMinimum.Add(minimum);
                meshMaximum.Add(maximum);
            }

            if (meshIndexStart.Count == 0) return;

            var asset = ScriptableObject.CreateInstance<TrackCollisionAsset>();
            asset.name = assetName + " collision";
            asset.Store(
                vertices.ToArray(),
                indices.ToArray(),
                meshVertexStart.ToArray(),
                meshVertexCount.ToArray(),
                meshIndexStart.ToArray(),
                meshIndexCount.ToArray(),
                meshMinimum.ToArray(),
                meshMaximum.ToArray());

            context.AddObjectToAsset("collision", asset);

            Debug.Log(
                $"KTRK collision '{assetName}': {meshIndexStart.Count} meshes, " +
                $"{asset.TriangleCount} triangles.");
        }

        /// <summary>
        /// Set for the one track whose sign faces are authored upside down. See
        /// <see cref="TurnSignFacesUpright"/>.
        /// </summary>
        private bool _fixUpsideDownSigns;

        /// <summary>
        /// Turns ice_R01's sign faces the right way up, by mirroring the texture
        /// on them and nothing else.
        ///
        /// The 2004 asset has every sign board on 아이스 설산 다운힐 mapped upside
        /// down, and it is the only track with the defect. It is visible in the
        /// data, not just on screen: v = 0 is the top of the image, so a sign the
        /// right way up has v falling as the geometry rises, which holds for 2987
        /// of the 3287 height-mapped faces across the thirteen tracks. On these
        /// boards it runs the other way.
        ///
        /// The correction is per UV island, because a sign is a board and a post
        /// in one mesh sharing one atlas page: on 유턴+ the board island spans a
        /// whole atlas cell and runs backwards, while the post islands sit in a
        /// narrow strip and are already right. Mirroring v inside each island's
        /// own range therefore turns the board over and leaves the post alone, and
        /// keeps both inside the atlas cell they were mapped into — which
        /// <c>1 - v</c> would not.
        ///
        /// Nothing here moves a vertex. The boards are scenery the original drives
        /// straight through, so this cannot reach the physics even in principle;
        /// it is the smallest edit that fixes what is actually wrong.
        /// </summary>
        private static void TurnSignFacesUpright(
            KtrkFile.Mesh source, Vector3[] positions, Vector2[] uvs)
        {
            // Signs only. The road meshes on this track are full of faces whose v
            // rises with height, because a tiling road texture has no up.
            string texture = source.Texture != null ? source.Texture.Trim() : string.Empty;
            if (source.IsCollidable) return;
            if (texture.IndexOf("sign", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                !texture.StartsWith("ad_", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (List<int> island in UvIslands(source, positions.Length))
            {
                float minV = float.PositiveInfinity;
                float maxV = float.NegativeInfinity;
                float meanHeight = 0f;
                float meanV = 0f;

                foreach (int vertex in island)
                {
                    minV = Mathf.Min(minV, uvs[vertex].y);
                    maxV = Mathf.Max(maxV, uvs[vertex].y);
                    meanHeight += positions[vertex].y;
                    meanV += uvs[vertex].y;
                }
                meanHeight /= island.Count;
                meanV /= island.Count;

                float covariance = 0f;
                foreach (int vertex in island)
                {
                    covariance += (positions[vertex].y - meanHeight) * (uvs[vertex].y - meanV);
                }

                // After the v flip above, Unity's v = 0 is the bottom of the image,
                // so an upright face has v rising with height. Only the ones that
                // fall are turned over; a face with no height span at all has
                // nothing to be upside down about.
                if (covariance >= 0f) continue;

                foreach (int vertex in island)
                {
                    uvs[vertex] = new Vector2(uvs[vertex].x, minV + maxV - uvs[vertex].y);
                }
            }
        }

        /// <summary>
        /// The vertex groups joined by shared triangles. A sign's board and its
        /// post are separate groups even though they are one mesh, which is what
        /// lets the board be corrected without disturbing the post.
        /// </summary>
        private static List<List<int>> UvIslands(KtrkFile.Mesh source, int vertexCount)
        {
            var parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; ++i) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            int[] indices = source.Indices;
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = Find(indices[t]);
                int b = Find(indices[t + 1]);
                int c = Find(indices[t + 2]);
                if (a != b) parent[b] = a;
                if (Find(c) != Find(a)) parent[Find(c)] = Find(a);
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < vertexCount; ++i)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out List<int> island))
                {
                    island = new List<int>();
                    groups[root] = island;
                }
                island.Add(i);
            }
            return new List<List<int>>(groups.Values);
        }

        /// <summary>
        /// Builds one part. Positions and UVs come straight from the file; normals
        /// are generated because KTRK stores none, and the physics never reads
        /// these — collision uses the per-face cross product instead.
        /// </summary>
        private Mesh BuildMesh(KtrkFile.Mesh source, string name, out Vector3 pivot)
        {
            int vertexCount = source.Vertices.Length;
            var positions = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (int i = 0; i < vertexCount; ++i)
            {
                KtrkFile.Vertex vertex = source.Vertices[i];
                positions[i] = KartSpace.ToUnity(new KartVec3(vertex.X, vertex.Y, vertex.Z));

                // The original samples with ty = floor(v * height) into an image
                // whose row 0 is its top, so v = 0 is the top of the texture.
                // Unity's v = 0 is the bottom, so the coordinate is flipped here
                // rather than by flipping the imported image. U needs no such
                // correction: a column is a column in both, and mirroring it was
                // tried against the karts' reversed lettering and scrambled every
                // model, so the lettering is not a U-axis problem.
                uvs[i] = new Vector2(vertex.U, 1f - vertex.V);
            }

            if (_fixUpsideDownSigns) TurnSignFacesUpright(source, positions, uvs);

            pivot = Vector3.zero;
            if (recenterParts && vertexCount > 0)
            {
                var min = positions[0];
                var max = positions[0];
                for (int i = 1; i < vertexCount; ++i)
                {
                    min = Vector3.Min(min, positions[i]);
                    max = Vector3.Max(max, positions[i]);
                }
                pivot = (min + max) * 0.5f;
                for (int i = 0; i < vertexCount; ++i) positions[i] -= pivot;
            }

            int[] indices = source.Indices;
            if (reverseWinding)
            {
                indices = (int[])indices.Clone();
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                }
            }

            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, calculateBounds: true);
            // KTRK stores no normals. The exporter emits one vertex per triangle
            // corner (cotten5: 645 triangles, 1935 vertices) because the source
            // indexes positions and texture coordinates separately, so nothing is
            // shared and this comes out flat-shaded — which is how the original
            // draws these models.
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static string UniquePartName(string sourceName, int index, HashSet<string> used)
        {
            // The kart exports leave every mesh name empty, so the index is the
            // only stable handle they have.
            string name = string.IsNullOrWhiteSpace(sourceName)
                ? $"part{index:00}"
                : sourceName.Trim();

            string candidate = name;
            int suffix = 1;
            while (!used.Add(candidate))
            {
                candidate = $"{name}_{suffix++}";
            }
            return candidate;
        }

        /// <summary>
        /// The pipeline's own default material asset. Creating one here instead
        /// would leak an object the import never takes ownership of.
        /// </summary>
        /// <summary>
        /// The material a face gets when its texture is not in the archives - the
        /// ad boards, and a dozen more on the later client's courses.
        ///
        /// Built here rather than taken from the pipeline, which is what it used to
        /// do: that default is one shared asset used all over the editor, so it
        /// could not be given a colour of its own without changing everything else
        /// that draws with it. Plain white, unlit by any highlight, so an
        /// untextured face reads as blank rather than as a grey panel that looks
        /// deliberate.
        /// </summary>
        private static Material BuildFallback(AssetImportContext context, Shader shader)
        {
            var fallback = new Material(shader) { name = "Untextured" };
            fallback.SetColor(BaseColorId, Color.white);
            fallback.SetFloat(SurfaceId, 0f);
            fallback.SetFloat(SmoothnessId, 0f);
            fallback.SetFloat(MetallicId, 0f);
            fallback.SetFloat(CullId, (float)CullMode.Off);
            fallback.SetFloat(RenderFaceId, RenderFaceBoth);
            fallback.doubleSidedGI = true;

            context.AddObjectToAsset("material_untextured", fallback);
            return fallback;
        }

        private static Material DefaultMaterial()
            => GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
    }
}
