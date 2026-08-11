using System.Collections.Generic;
using System.IO;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Builds a <see cref="KartSoundSet"/> for every engine preset from the
    /// samples as they import, gathers them into the catalog the <c>U</c> key
    /// walks, and gives the samples the import settings the originals need.
    ///
    /// The demo's samples are 16-bit mono 22050 Hz PCM. Unity's defaults would
    /// compress them to Vorbis and load them streaming, which adds latency to a
    /// one-shot and resamples a loop whose rate the engine note changes every
    /// 64 ms. Decompressed PCM, preloaded, is what keeps them behaving like the
    /// waveOut mixer's buffers.
    ///
    /// The thirteen presets are the original's own list and order. Each one is
    /// four samples — motor, booster, instant boost, booster idle — and the bike
    /// variants differ only in the first two, which is why they sit beside their
    /// car in the order rather than in a group of their own. The kart and
    /// countdown samples are shared: they are not part of the engine note.
    ///
    /// Wiring the assets here rather than leaving them to be dragged in is
    /// deliberate: an unattached sound set is a silent simulator with nothing to
    /// show for it.
    /// </summary>
    public sealed class KartSoundSetBuilder : AssetPostprocessor
    {
        private const string EngineRoot = "Assets/_Project/Audio/Engine";
        private const string KartDirectory = "Assets/_Project/Audio/Kart";
        private const string CountdownDirectory = "Assets/_Project/Audio/Countdown";
        private const string SetDirectory = "Assets/_Project/Data/Audio";
        private const string CatalogPath = SetDirectory + "/KartSoundCatalog.asset";

        /// <summary>
        /// The presets in the original's enum order: classic, then each engine
        /// with its bike variant after it.
        /// </summary>
        private static readonly string[] Presets =
        {
            "Classic",
            "Sr", "SrBike",
            "Z7", "Z7Bike",
            "Ht", "HtBike",
            "Jiu", "JiuBike",
            "X", "XBike",
            "V1", "V1Bike",
        };

        /// <summary>The four engine slots, in the order the original loads them.</summary>
        private static readonly string[] EngineSlots =
        {
            "engine.wav", "booster.wav", "boosterDrift.wav", "boosterPlay.wav",
        };

        private static readonly string[] SharedClips =
        {
            KartDirectory + "/drift.wav",
            KartDirectory + "/crash.wav",
            KartDirectory + "/shock.wav",
            CountdownDirectory + "/count_3.wav",
            CountdownDirectory + "/count_2.wav",
            CountdownDirectory + "/count_1.wav",
            CountdownDirectory + "/count_go.wav",
        };

        private static bool IsSample(string path)
        {
            string normalised = path.Replace('\\', '/');
            if (System.Array.Exists(SharedClips, clip => clip == normalised)) return true;
            return normalised.StartsWith(EngineRoot + "/") && normalised.EndsWith(".wav");
        }

        /// <summary>
        /// Import settings for the demo's own samples, applied as they arrive so
        /// the asset never exists in a compressed state.
        /// </summary>
        private void OnPreprocessAudio()
        {
            if (!IsSample(assetPath)) return;

            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;

            // Preloading moved onto the per-platform sample settings; the
            // importer-level property is obsolete and errors on this version.
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!IsSample(path)) continue;

                // Deferred: creating assets from inside the import callback
                // re-enters the asset pipeline while it is still running.
                EditorApplication.delayCall += Build;
                return;
            }
        }

        internal static void Build()
        {
            Directory.CreateDirectory(Path.GetFullPath(SetDirectory));
            AssetDatabase.Refresh();

            int found = 0;
            int wanted = 0;

            AudioClip Clip(string path)
            {
                ++wanted;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) ++found;
                return clip;
            }

            var sets = new List<KartSoundSet>(Presets.Length);
            foreach (string preset in Presets)
            {
                string setPath = $"{SetDirectory}/{preset}.asset";
                var set = AssetDatabase.LoadAssetAtPath<KartSoundSet>(setPath);
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<KartSoundSet>();
                    AssetDatabase.CreateAsset(set, setPath);
                }

                string engine = $"{EngineRoot}/{preset}";
                set.SetClips(
                    preset,
                    Clip($"{engine}/{EngineSlots[0]}"),
                    Clip($"{engine}/{EngineSlots[1]}"),
                    Clip($"{engine}/{EngineSlots[2]}"),
                    Clip($"{engine}/{EngineSlots[3]}"),
                    Clip(SharedClips[0]),
                    Clip(SharedClips[1]),
                    Clip(SharedClips[2]),
                    Clip(SharedClips[3]),
                    Clip(SharedClips[4]),
                    Clip(SharedClips[5]),
                    Clip(SharedClips[6]));

                EditorUtility.SetDirty(set);
                sets.Add(set);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<KartSoundCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<KartSoundCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetPresets(sets.ToArray());
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Engine sound presets: {Presets.Length} sets, " +
                $"{found} of {wanted} samples wired, catalog at {CatalogPath}.");
        }
    }
}
