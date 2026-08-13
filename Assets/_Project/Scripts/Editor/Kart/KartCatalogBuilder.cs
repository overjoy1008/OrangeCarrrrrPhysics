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

                // The prefabs and materials the guest importer writes land in the
                // model directory too. Rebuilding on those would rebuild them
                // again, and the build would never settle.
                if (normalised.EndsWith(".prefab") || normalised.EndsWith(".mat")) continue;

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
        private const string GuestAudioDirectory = "Assets/_Project/Audio/Kart/Guest";
        private const string GuestMusicDirectory = "Assets/_Project/Audio/Music/Guest";
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

                // The demo's own models sit flat in the directory; anything from
                // the later client is kept in a folder of its own, so the two sets
                // never blur together on disk. Demo first, so a name in both wins
                // for the demo. The atlas is split the same way, and for the same
                // reason.
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDirectory}/{name}.ktrk");
                if (model == null)
                {
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{ModelDirectory}/TCGames/{name}.ktrk");
                }

                // The later client ships two images beside model.1s. The body
                // atlas is 1.png — it carries the same magenta filler and the same
                // 45x20 block of blue the painter keys off. 0.png is alpha 0 all
                // the way through on every one of them, so it is not brought over.
                string skinPath = $"{SkinDirectory}/{name}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(skinPath) == null)
                {
                    skinPath = $"{SkinDirectory}/TCGames/{name}.png";
                }
                MakeReadable(skinPath);
                var skin = AssetDatabase.LoadAssetAtPath<Texture2D>(skinPath);
                if (model != null) ++models;
                if (skin != null) ++skins;

                spec.SetContent(model, skin);
                EditorUtility.SetDirty(spec);
                specs.Add(spec);
            }

            int guests = 0;
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                GameObject prefab = KartGuestModelBuilder.Build(guest, out KartSpec measured);
                if (prefab == null || measured == null) continue;

                string guestPath = $"{SpecDirectory}/{guest.AssetName}.asset";
                var spec = AssetDatabase.LoadAssetAtPath<KartSpecAsset>(guestPath);
                if (spec == null)
                {
                    spec = ScriptableObject.CreateInstance<KartSpecAsset>();
                    AssetDatabase.CreateAsset(spec, guestPath);
                }

                // Measured, not transcribed: a guest's geometry comes back from
                // the model the importer just fitted, so the body box and the
                // thing on screen cannot drift apart. No skin either — the atlas
                // painter keys off blocks the recovered templates carry and a
                // guest's own texture has none, so it is left as its author
                // drew it.
                spec.ApplySpec(measured);
                spec.SetContent(
                    prefab, null, GuestBoosterSound(guest),
                    guest.BoosterSoundStart, guest.BoosterSoundSlowStart,
                    GuestClip(guest.AssetName, GuestMusicDirectory, guest.ThemeMusic));
                EditorUtility.SetDirty(spec);
                specs.Add(spec);
                ++guests;
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
                    $"{guests} guests, {shared}/{SharedImages.Length} shared stamps. " +
                    "Parameters come from the recovered KARTS[] table; a guest's " +
                    "are measured off its own model.");
            }
            finally { _building = false; }
        }

        /// <summary>
        /// A guest's own booster sample, from <c>Audio/Kart/Guest</c>.
        ///
        /// Kept out of the engine sets on purpose. Those are built one folder per
        /// generation by <c>KartSoundSetBuilder</c> and a guest is in none of
        /// them; dropping a sample into one would put a cat noise on every kart of
        /// that generation.
        /// </summary>
        private static AudioClip GuestBoosterSound(KartGuestSpec guest)
            => GuestClip(guest.AssetName, GuestAudioDirectory, guest.BoosterSound);

        private static AudioClip GuestClip(string assetName, string directory, string file)
        {
            if (string.IsNullOrEmpty(file)) return null;

            string path = $"{directory}/{file}";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"Guest kart '{assetName}': no clip at {path}.");
            return clip;
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
