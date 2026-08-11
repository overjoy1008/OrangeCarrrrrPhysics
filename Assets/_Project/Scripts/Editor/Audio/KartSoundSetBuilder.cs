using System.IO;
using OrangeCarrrrr.Runtime;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Builds the classic engine preset's <see cref="KartSoundSet"/> from the
    /// samples as they import, and gives them the import settings the originals
    /// need.
    ///
    /// The demo's samples are 16-bit mono 22050 Hz PCM. Unity's defaults would
    /// compress them to Vorbis and load them streaming, which adds latency to a
    /// one-shot and resamples a loop whose rate the engine note changes every
    /// 64 ms. Decompressed PCM, preloaded, is what keeps them behaving like the
    /// waveOut mixer's buffers.
    ///
    /// Wiring the asset here rather than leaving it to be dragged in is
    /// deliberate: an unattached sound set is a silent simulator with nothing to
    /// show for it.
    /// </summary>
    public sealed class KartSoundSetBuilder : AssetPostprocessor
    {
        private const string EngineDirectory = "Assets/_Project/Audio/Engine/Classic";
        private const string KartDirectory = "Assets/_Project/Audio/Kart";
        private const string CountdownDirectory = "Assets/_Project/Audio/Countdown";
        private const string SetPath = "Assets/_Project/Data/Audio/Classic.asset";

        private static readonly string[] Clips =
        {
            EngineDirectory + "/engine.wav",
            EngineDirectory + "/booster.wav",
            EngineDirectory + "/boosterDrift.wav",
            EngineDirectory + "/boosterPlay.wav",
            KartDirectory + "/drift.wav",
            KartDirectory + "/crash.wav",
            KartDirectory + "/shock.wav",
            CountdownDirectory + "/count_3.wav",
            CountdownDirectory + "/count_2.wav",
            CountdownDirectory + "/count_1.wav",
            CountdownDirectory + "/count_go.wav",
        };

        /// <summary>
        /// Import settings for the demo's own samples, applied as they arrive so
        /// the asset never exists in a compressed state.
        /// </summary>
        private void OnPreprocessAudio()
        {
            if (!System.Array.Exists(Clips, path => path == assetPath.Replace('\\', '/'))) return;

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
                if (!System.Array.Exists(Clips, clip => clip == path.Replace('\\', '/'))) continue;

                // Deferred: creating assets from inside the import callback
                // re-enters the asset pipeline while it is still running.
                EditorApplication.delayCall += Build;
                return;
            }
        }

        private static void Build()
        {
            var set = AssetDatabase.LoadAssetAtPath<KartSoundSet>(SetPath);
            if (set == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SetPath)));
                AssetDatabase.Refresh();

                set = ScriptableObject.CreateInstance<KartSoundSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            int found = 0;
            AudioClip Clip(string path)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) ++found;
                return clip;
            }

            set.SetClips(
                "classic",
                Clip(EngineDirectory + "/engine.wav"),
                Clip(EngineDirectory + "/booster.wav"),
                Clip(EngineDirectory + "/boosterDrift.wav"),
                Clip(EngineDirectory + "/boosterPlay.wav"),
                Clip(KartDirectory + "/drift.wav"),
                Clip(KartDirectory + "/crash.wav"),
                Clip(KartDirectory + "/shock.wav"),
                Clip(CountdownDirectory + "/count_3.wav"),
                Clip(CountdownDirectory + "/count_2.wav"),
                Clip(CountdownDirectory + "/count_1.wav"),
                Clip(CountdownDirectory + "/count_go.wav"));

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            Debug.Log($"Classic sound set: {found} of {Clips.Length} samples wired into {SetPath}.");
        }
    }
}
