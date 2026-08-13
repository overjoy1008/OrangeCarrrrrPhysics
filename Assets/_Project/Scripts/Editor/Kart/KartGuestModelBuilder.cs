using System;
using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Import settings for the guest models.
    ///
    /// Only two matter. The meshes are read here, submesh by submesh, to find out
    /// how big the model is and which end its face is on, so they have to stay
    /// readable. And a kart is a body, not a scene: an FBX that brings its
    /// author's camera and lights along would light the track from inside the cat.
    /// </summary>
    public sealed class KartGuestModelImportSettings : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(KartGuestModelBuilder.ModelDirectory + "/")) return;

            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.importCameras = false;
            importer.importLights = false;

            // The shapes are kept and the clip that drives them is not. A guest's
            // second look is switched by the kart's own state — see
            // <c>KartGuestSpin</c> — so importing the author's timeline would only
            // add a second thing animating the same weight.
            importer.importBlendShapes = true;
            importer.importAnimation = false;
        }
    }

    /// <summary>
    /// Turns a guest model — an FBX from outside either client — into a kart
    /// prefab the rest of the port can treat like an imported KTRK one.
    ///
    /// A recovered kart needs none of this. Its model arrives already in the
    /// engine's frame, already the right size, and its AABB is a transcribed
    /// number. A guest arrives in whatever frame and whatever units it was
    /// modelled in, so the three things a kart prefab has to guarantee are
    /// derived here from the mesh itself rather than typed in:
    ///
    /// <list type="bullet">
    /// <item><b>Facing.</b> The guest's own rotation when it states one;
    /// otherwise the long horizontal axis becomes Z — which is where
    /// <c>KartSpace</c> puts forward — and the face submesh decides which of the
    /// two ways round that is.</item>
    /// <item><b>Size.</b> One uniform scale, chosen so the model's length matches
    /// the mean of the line named in <see cref="KartGuestSpec.SizeReference"/>.
    /// Uniform because a per-axis fit to a kart's proportions would flatten a cat
    /// into a rug.</item>
    /// <item><b>Origin.</b> Centred on X and Z with its lowest point at y = 0,
    /// because <c>KartView</c> puts the model at the simulation's position with no
    /// offset and that position is the wheel contact point.</item>
    /// </list>
    ///
    /// The measured box is then handed back as the kart's geometry, so the physics
    /// body is the model rather than a guess at it — the same relationship the
    /// recovered karts have, whose AABBs came out of their models too.
    /// </summary>
    internal static class KartGuestModelBuilder
    {
        public const string ModelDirectory = "Assets/_Project/Art/Karts/Models/Guest";

        /// <summary>
        /// Builds the prefab and the spec for one guest, or returns null and
        /// leaves <paramref name="spec"/> null when its model is missing.
        /// </summary>
        public static GameObject Build(KartGuestSpec guest, out KartSpec spec)
        {
            spec = null;
            if (guest == null) return null;

            string modelPath = ModelDirectory + "/" + guest.ModelFile;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null)
            {
                Debug.LogWarning($"Guest kart '{guest.AssetName}': no model at {modelPath}.");
                return null;
            }

            bool byHeight = guest.SizeHeightMeters > 0f;
            float targetLength = byHeight ? 0f : KartGuestData.ReferenceLength(guest.SizeReference);
            if (!byHeight && targetLength <= 0f)
            {
                Debug.LogWarning(
                    $"Guest kart '{guest.AssetName}': no kart matches size reference " +
                    $"'{guest.SizeReference}', so there is nothing to scale it against.");
                return null;
            }

            var root = new GameObject(guest.AssetName);
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(source);
                instance.name = Path.GetFileNameWithoutExtension(guest.ModelFile);
                instance.transform.SetParent(root.transform, worldPositionStays: false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                List<Part> parts = Parts(instance);
                if (parts.Count == 0)
                {
                    Debug.LogWarning($"Guest kart '{guest.AssetName}': the model has no meshes.");
                    return null;
                }

                Bounds raw = Measure(parts, Matrix4x4.identity);
                Quaternion facing = Facing(guest, parts);
                Bounds facingBounds = Measure(parts, Matrix4x4.Rotate(facing));

                // The extent the fit divides by: the model's length, or its
                // height for one that stands.
                float fitExtent = byHeight ? facingBounds.size.y : facingBounds.size.z;
                if (fitExtent <= Mathf.Epsilon)
                {
                    Debug.LogWarning(
                        $"Guest kart '{guest.AssetName}': the model has no " +
                        (byHeight ? "height." : "length."));
                    return null;
                }

                float scale = (byHeight ? guest.SizeHeightMeters : targetLength) / fitExtent;
                Matrix4x4 fitted = Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Rotate(facing);
                Bounds box = Measure(parts, fitted);

                // Wheel contact point at the origin, and centred across and along
                // it: the simulation drives the box, not the model's own pivot.
                instance.transform.localRotation = facing;
                instance.transform.localScale = Vector3.one * scale;
                instance.transform.localPosition =
                    new Vector3(-box.center.x, -box.min.y, -box.center.z);

                Paint(guest, parts);

                if (guest.Spins)
                {
                    // On the root, so the turn is about the kart's own origin and
                    // carries the fitted child with it.
                    var spin = root.AddComponent<KartGuestSpin>();
                    spin.Configure(
                        guest.SpinBlendShape, guest.SpinDegreesPerSecond,
                        guest.SpinRampSeconds, guest.SlowSpinScale);
                }

                string prefabPath = ModelDirectory + "/" + guest.AssetName + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                spec = new KartSpec
                {
                    AssetName = guest.AssetName,
                    Source = KartAssetSource.Guest,
                    Dynamics = GuestDynamics(guest),
                    Geometry = new KartSimulationGeometry
                    {
                        HalfWidth = box.size.x * 0.5f,
                        HalfLength = box.size.z * 0.5f,
                        SuspensionRange = 0.5f,
                        GroundedDragScale = 1.0f,
                    },
                    ModelHeight = box.size.y,
                    MaxBoosters = KartDemoData.DefaultMaxBoosters,
                };

                // The model's own extents are logged beside the fitted ones
                // because that is what says whether it arrived upright. A model
                // lying down has its length on Y as imported, and no amount of
                // reading the fitted numbers alone would show it.
                Debug.Log(
                    $"Guest kart '{guest.AssetName}': {parts.Count} parts, imported " +
                    $"{raw.size.x:0.000} x {raw.size.y:0.000} x {raw.size.z:0.000} (xyz), " +
                    $"turned by {facing.eulerAngles} and scaled x{scale:0.0000} to " +
                    $"{box.size.x:0.000} wide, {box.size.z:0.000} long, {box.size.y:0.000} high, " +
                    (byHeight
                        ? $"fitted to {guest.SizeHeightMeters:0.000} tall. "
                        : $"against the {guest.SizeReference} line's mean {targetLength:0.000}. ") +
                    guest.Credit);

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// What a guest drives on: the standard kart, with its own drift lean if
        /// it states one.
        ///
        /// Everything else is left alone. A guest is not a recovered kart and its
        /// numbers are nobody's transcription, but starting from the standard row
        /// and changing one thing keeps it comparable with the karts around it.
        /// </summary>
        private static KartDynamicsConfig GuestDynamics(KartGuestSpec guest)
        {
            KartDynamicsConfig dynamics = KartDynamicsConfig.Standard();
            if (guest.DriftLean != 0f) dynamics.DriftLeanFactor = guest.DriftLean;
            return dynamics;
        }

        /// <summary>
        /// One submesh of the model, with its box and its place inside it.
        ///
        /// A submesh rather than a whole renderer because that is the grain the
        /// model actually has: Maxwell is one mesh with two materials, so his body
        /// and his whiskers are not separate objects and nothing would tell them
        /// apart at renderer level.
        /// </summary>
        private readonly struct Part
        {
            public readonly string Name;
            public readonly Bounds Local;
            public readonly Matrix4x4 ToModel;
            public readonly Renderer Renderer;
            public readonly int SubMesh;

            public Part(
                string name, Bounds local, Matrix4x4 toModel, Renderer renderer, int subMesh)
            {
                Name = name;
                Local = local;
                ToModel = toModel;
                Renderer = renderer;
                SubMesh = subMesh;
            }
        }

        /// <summary>
        /// Every submesh of the model, whatever kind of renderer draws it.
        ///
        /// Skinned renderers are here because a model with a blend shape is a
        /// skinned model whether or not it has a single bone — Unity gives the
        /// OIIA cat a <see cref="SkinnedMeshRenderer"/> purely for its two shapes.
        /// Its mesh is read at rest, which is the shape the kart drives around as
        /// and therefore the one its body box should match.
        /// </summary>
        private static List<Part> Parts(GameObject instance)
        {
            var parts = new List<Part>();
            Matrix4x4 toModel = instance.transform.worldToLocalMatrix;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = MeshOf(renderer);
                if (mesh == null) continue;

                Matrix4x4 place = toModel * renderer.transform.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;
                Vector3[] vertices = mesh.vertices;

                for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                {
                    Material material =
                        materials != null && sub < materials.Length ? materials[sub] : null;

                    // The material's name is what carries the meaning here: it is
                    // the FBX's own, and on a one-mesh model it is the only thing
                    // that says which submesh is which.
                    string name = string.Join(
                        " ",
                        renderer.gameObject.name,
                        mesh.name,
                        material != null ? material.name : string.Empty);

                    parts.Add(new Part(
                        name, SubMeshBounds(mesh, vertices, sub), place, renderer, sub));
                }
            }
            return parts;
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// A submesh's own box.
        ///
        /// From its triangles' vertices, so the two halves of a single mesh have
        /// different boxes — which is the whole point, since that is what tells
        /// the face from the rest of the body. Falls back to the mesh's box if the
        /// vertices cannot be read, which costs the facing check rather than the
        /// import.
        /// </summary>
        private static Bounds SubMeshBounds(Mesh mesh, Vector3[] vertices, int subMesh)
        {
            if (vertices == null || vertices.Length == 0) return mesh.bounds;

            int[] indices = mesh.GetIndices(subMesh);
            if (indices.Length == 0) return mesh.bounds;

            var box = new Bounds(vertices[indices[0]], Vector3.zero);
            for (int i = 1; i < indices.Length; ++i) box.Encapsulate(vertices[indices[i]]);
            return box;
        }

        /// <summary>
        /// The rotation that stands the model up, puts its length on Z and its
        /// face on +Z.
        ///
        /// The guest's own if it gives one, because a rotation someone read off
        /// the inspector with the model facing front is not a thing to second-
        /// guess. Otherwise derived, and then only in quarter turns: anything
        /// finer would be tilting the model rather than turning it.
        /// </summary>
        private static Quaternion Facing(KartGuestSpec guest, IReadOnlyList<Part> parts)
        {
            float[] given = guest.ModelRotationDeg;
            if (given != null)
            {
                if (given.Length != 3)
                {
                    Debug.LogWarning(
                        $"Guest kart '{guest.AssetName}': ModelRotationDeg has " +
                        $"{given.Length} numbers rather than three, so it is ignored.");
                }
                else
                {
                    // Taken whole, not as a starting point. Half-honouring it —
                    // keeping the roll and re-deriving the yaw — would quietly
                    // turn a model someone had already checked on screen.
                    return Quaternion.Euler(given[0], given[1], given[2]);
                }
            }

            Bounds flat = Measure(parts, Matrix4x4.identity);
            float yaw = flat.size.x > flat.size.z ? 90f : 0f;

            if (!string.IsNullOrEmpty(guest.FaceMeshHint))
            {
                Matrix4x4 turned = Matrix4x4.Rotate(Quaternion.Euler(0f, yaw, 0f));
                if (Face(parts, guest.FaceMeshHint, turned, out Bounds face))
                {
                    Bounds whole = Measure(parts, turned);

                    // The face came out at the back, so the model is tail-first.
                    if (face.center.z < whole.center.z) yaw += 180f;
                }
                else
                {
                    Debug.LogWarning(
                        $"Guest kart '{guest.AssetName}': nothing in the model is named " +
                        $"'{guest.FaceMeshHint}', so which way it faces is a guess and it " +
                        "may drive backwards.");
                }
            }

            return Quaternion.Euler(0f, yaw, 0f);
        }

        private static bool Face(
            IReadOnlyList<Part> parts, string hint, Matrix4x4 transform, out Bounds face)
        {
            face = default;
            bool found = false;

            foreach (Part part in parts)
            {
                if (!Matches(part, hint)) continue;

                Bounds one = Measure(part, transform);
                if (!found) { face = one; found = true; }
                else face.Encapsulate(one);
            }
            return found;
        }

        private static bool Matches(in Part part, string hint)
            => !string.IsNullOrEmpty(hint) &&
               part.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The model's box under a transform.</summary>
        private static Bounds Measure(IReadOnlyList<Part> parts, Matrix4x4 transform)
        {
            Bounds box = default;
            bool started = false;

            foreach (Part part in parts)
            {
                Bounds one = Measure(part, transform);
                if (!started) { box = one; started = true; }
                else box.Encapsulate(one);
            }
            return box;
        }

        /// <summary>
        /// One part's box under a transform, from the eight corners of its own.
        /// For the quarter turns and the uniform scale used here that is exact.
        /// </summary>
        private static Bounds Measure(in Part part, Matrix4x4 transform)
        {
            Matrix4x4 matrix = transform * part.ToModel;
            Vector3 min = part.Local.min;
            Vector3 max = part.Local.max;

            Bounds box = default;
            for (int corner = 0; corner < 8; ++corner)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);

                Vector3 moved = matrix.MultiplyPoint3x4(point);
                if (corner == 0) box = new Bounds(moved, Vector3.zero);
                else box.Encapsulate(moved);
            }
            return box;
        }

        /// <summary>
        /// Gives the model materials of this project's own making rather than
        /// whatever its FBX import produced.
        ///
        /// The reason is the one <see cref="KtrkImporter"/> gives for never
        /// falling back to Standard: a material built on a shader this pipeline
        /// does not use draws magenta. Building them here also settles the
        /// whiskers, which are crossed cards and need their alpha cut out and
        /// their back faces kept, neither of which an opaque import gives them.
        /// </summary>
        private static void Paint(KartGuestSpec guest, IReadOnlyList<Part> parts)
        {
            Shader lit = KtrkImporter.LitShader();
            if (lit == null)
            {
                Debug.LogWarning(
                    $"Guest kart '{guest.AssetName}': URP's Lit shader was not found, " +
                    "so the model keeps the materials its own import made.");
                return;
            }

            Material body = BuildMaterial(
                lit, guest.AssetName, "body", guest.BodyTexture, guest.BodyColorHtml, false);
            ApplyNormalMap(body, guest);
            Material cutout = BuildMaterial(
                lit, guest.AssetName, "cutout", guest.CutoutTexture, null, true);
            if (body == null && cutout == null) return;

            // Collected per renderer first: a material array has to be assigned
            // whole, and one renderer holds every submesh of its mesh.
            var slots = new Dictionary<Renderer, Material[]>();
            foreach (Part part in parts)
            {
                if (part.Renderer == null) continue;

                if (!slots.TryGetValue(part.Renderer, out Material[] materials))
                {
                    materials = part.Renderer.sharedMaterials;
                    slots[part.Renderer] = materials;
                }
                if (part.SubMesh >= materials.Length) continue;

                Material chosen = cutout != null && Matches(part, guest.FaceMeshHint)
                    ? cutout
                    : body;
                if (chosen != null) materials[part.SubMesh] = chosen;
            }

            foreach (KeyValuePair<Renderer, Material[]> slot in slots)
            {
                slot.Key.sharedMaterials = slot.Value;
            }
        }

        /// <summary>
        /// Puts the guest's normal map on its body material, and makes sure the
        /// image is imported as one.
        ///
        /// A normal map read as a colour texture is not merely wrong, it lights
        /// the model as though every dent were painted on, so the import type is
        /// set here rather than left to whoever dropped the file in.
        /// </summary>
        private static void ApplyNormalMap(Material body, KartGuestSpec guest)
        {
            if (body == null || string.IsNullOrEmpty(guest.NormalTexture)) return;

            string path = ModelDirectory + "/" + guest.NormalTexture;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (normal == null)
            {
                Debug.LogWarning(
                    $"Guest kart '{guest.AssetName}': no normal map at {guest.NormalTexture}.");
                return;
            }

            body.SetTexture(BumpMap, normal);
            body.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(body);
        }

        /// <summary>
        /// One material, from a texture or — for a model that ships none — from a
        /// flat colour.
        /// </summary>
        private static Material BuildMaterial(
            Shader lit,
            string assetName,
            string suffix,
            string textureFile,
            string colorHtml,
            bool cutout)
        {
            Texture2D texture = null;
            if (!string.IsNullOrEmpty(textureFile))
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    ModelDirectory + "/" + textureFile);
                if (texture == null)
                {
                    Debug.LogWarning($"Guest kart '{assetName}': no texture at {textureFile}.");
                    return null;
                }
            }

            var color = Color.white;
            if (texture == null)
            {
                if (string.IsNullOrEmpty(colorHtml)) return null;
                if (!ColorUtility.TryParseHtmlString(colorHtml, out color))
                {
                    Debug.LogWarning(
                        $"Guest kart '{assetName}': '{colorHtml}' is not a colour.");
                    return null;
                }
            }

            string path = $"{ModelDirectory}/{assetName}_{suffix}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit) { name = $"{assetName}_{suffix}" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = lit;
            material.SetTexture(BaseMap, texture);
            material.SetColor(BaseColor, color);
            material.SetFloat(AlphaClip, cutout ? 1f : 0f);
            material.SetFloat(Cutoff, 0.5f);
            material.SetFloat(Cull, cutout ? (float)CullMode.Off : (float)CullMode.Back);
            material.doubleSidedGI = cutout;
            if (cutout) material.EnableKeyword("_ALPHATEST_ON");
            else material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
        private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
    }
}
