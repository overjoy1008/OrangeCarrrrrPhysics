using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Creates a <see cref="KartSpecAsset"/> for every row of the recovered
    /// <c>KARTS[]</c> table, wires each to its imported model and atlas, and
    /// gathers them into the catalog the <c>K</c> key walks.
    ///
    /// The parameters are never typed in here: each asset takes its values
    /// straight from <see cref="KartDemoData"/>, which was transcribed from the
    /// demo's own <c>parameter.xml</c>. Re-running refreshes them, so an asset
    /// that was edited in the inspector goes back to the recovered numbers.
    ///
    /// Existing assets are updated in place, so the GUIDs prefabs and scenes
    /// already reference survive.
    /// </summary>
    public sealed class KartCatalogBuilder : AssetPostprocessor
    {
        private static bool _building;

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_building) return;

            foreach (string path in imported)
            {
                string normalised = path.Replace('\\', '/');
                if (!normalised.StartsWith(ModelDirectory + "/") &&
                    !normalised.StartsWith(SkinDirectory + "/") &&
                    !normalised.StartsWith(CommonDirectory + "/")) continue;

                // Deferred: creating assets from inside the import callback
                // re-enters the asset pipeline while it is still running.
                EditorApplication.delayCall += Build;
                return;
            }
        }

        private const string SpecDirectory = "Assets/_Project/Data/Karts";
        private const string ModelDirectory = "Assets/_Project/Art/Karts/Models";
        private const string SkinDirectory = "Assets/_Project/Art/Karts/Skins";
        private const string CommonDirectory = "Assets/_Project/Art/Karts/Common";
        private const string CatalogPath = SpecDirectory + "/KartCatalog.asset";

        /// <summary>
        /// The two images <c>0x00417160</c> stamps onto every skin. They are read
        /// on the CPU exactly like the templates are, so they need the same
        /// treatment — without it the painter finds them unreadable and silently
        /// skips both stamps, leaving the plate's 900-texel blue key block and the
        /// number's cyan anchors showing on the finished kart.
        /// </summary>
        private static readonly string[] SharedImages =
        {
            CommonDirectory + "/plate.png",
            CommonDirectory + "/number.png",
        };

        public static void Build()
        {
            if (_building) return;
            _building = true;
            try
            {
            Directory.CreateDirectory(Path.GetFullPath(SpecDirectory));
            AssetDatabase.Refresh();

            int shared = 0;
            foreach (string path in SharedImages)
            {
                MakeReadable(path);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) ++shared;
                else Debug.LogWarning($"Missing shared kart image: {path}");
            }

            var specs = new List<KartSpecAsset>(KartDemoData.Karts.Count);
            int models = 0;
            int skins = 0;

            foreach (KartSpec recovered in KartDemoData.Karts)
            {
                string name = recovered.AssetName;
                string specPath = $"{SpecDirectory}/{name}.asset";

                var spec = AssetDatabase.LoadAssetAtPath<KartSpecAsset>(specPath);
                if (spec == null)
                {
                    spec = ScriptableObject.CreateInstance<KartSpecAsset>();
                    AssetDatabase.CreateAsset(spec, specPath);
                }

                spec.ApplySpec(recovered);

                string skinPath = $"{SkinDirectory}/{name}.png";
                MakeReadable(skinPath);

                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDirectory}/{name}.ktrk");
                var skin = AssetDatabase.LoadAssetAtPath<Texture2D>(skinPath);
                if (model != null) ++models;
                if (skin != null) ++skins;

                spec.SetContent(model, skin);
                EditorUtility.SetDirty(spec);
                specs.Add(spec);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<KartCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<KartCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetKarts(specs.ToArray());
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();

                Debug.Log(
                    $"Kart catalog: {specs.Count} specs, {models} models, {skins} skins, " +
                    $"{shared}/{SharedImages.Length} shared stamps. " +
                    "Parameters come from the recovered KARTS[] table.");
            }
            finally { _building = false; }
        }

        /// <summary>
        /// The atlas is repainted on the CPU every time the kart or the colour
        /// changes, so it has to stay readable and must not be block-compressed —
        /// the key texels the paint keys off would not survive DXT.
        /// </summary>
        private static void MakeReadable(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            bool dirty = false;

            // Keep the exact pixel dimensions. Unity's default rescales a
            // non-power-of-two texture to the nearest one, which turns the 45x20
            // plate into 32x16 and the 100x17 number strip into 128x16. Both are
            // stamped at texel coordinates the atlas dictates, so a resize is not
            // a quality trade-off, it is wrong data: a 32x16 plate cannot cover
            // its 45x20 key block, the leftover key is found by the scan and
            // stamped again, and the kart ends up wearing four number plates.
            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                dirty = true;
            }
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }

            // Repeat, not clamp. Half the kart models carry steering-wheel UVs
            // outside 0..1 — burst2's sit at v 1.322..1.471, exactly one turn on
            // from burst1's 0.322..0.470 — so they only land on the right texels
            // if the sampler wraps. Clamping pins them to the atlas edge and the
            // part comes out a single flat colour.
            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                dirty = true;
            }

            if (dirty) importer.SaveAndReimport();
        }
    }
}
