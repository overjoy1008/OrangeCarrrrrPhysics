using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Reimports any KTRK whose baked materials came out on the wrong shader.
    ///
    /// The import artifact is cached per machine and is not in the repository, so
    /// a fresh clone builds its own. If that build happens before the render
    /// pipeline's shaders are in the asset database — which is what a first open
    /// with no <c>Library</c> can do — the materials are baked against whatever
    /// was resolvable at that moment, and every track draws magenta from then on.
    /// Nothing about the checkout is wrong, so nothing ever triggers a rebuild,
    /// and re-cloning does not help.
    ///
    /// This is the recovery: on every editor load, look at what the artifacts
    /// actually hold and reimport the ones that are wrong. It costs one material
    /// check per track when everything is fine, which is the normal case.
    /// </summary>
    [InitializeOnLoad]
    internal static class KtrkMaterialRepair
    {
        static KtrkMaterialRepair() => EditorApplication.delayCall += Repair;

        /// <summary>
        /// Reimports every KTRK whether or not it looks wrong.
        ///
        /// The automatic pass only touches what it can prove is broken, which is
        /// right for something that runs on every load. This is the hand-operated
        /// version for when the materials are wrong in a way the check does not
        /// recognise — the shader is right and the textures still are not there.
        /// </summary>
        internal static void ReimportAll()
        {
            string[] paths = KtrkPaths();
            foreach (string path in paths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            Debug.Log($"Track materials: reimported all {paths.Length} KTRK asset(s).");
        }

        private static void Repair() => RepairBroken(out _);

        /// <summary>
        /// Reimports the KTRK assets whose materials are on the wrong shader.
        ///
        /// Returns false when the shader itself could not be resolved, which is
        /// the one case where nothing can be judged and trying would only reimport
        /// everything onto the same wrong answer.
        /// </summary>
        internal static bool RepairBroken(out int repaired)
        {
            repaired = 0;

            Shader expected = KtrkImporter.LitShader();
            if (expected == null)
            {
                Debug.LogWarning(
                    $"'{KtrkImporter.LitShaderName}' is not available; track " +
                    "materials cannot be checked this session.");
                return false;
            }

            var broken = new List<string>();
            foreach (string path in KtrkPaths())
            {
                if (IsBroken(path, expected)) broken.Add(path);
            }

            if (broken.Count == 0) return true;

            foreach (string path in broken)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            repaired = broken.Count;

            Debug.Log(
                $"Track materials: reimported {broken.Count} KTRK asset(s) whose " +
                $"materials were not on {expected.name} ({string.Join(", ", broken)}).");
            return true;
        }

        /// <summary>
        /// Every KTRK in the project, as asset paths.
        ///
        /// Walked off disk rather than queried through the asset database, so it
        /// does not depend on the search index being built — which is exactly
        /// what is unreliable at the moment this is guarding against.
        /// </summary>
        private static string[] KtrkPaths()
        {
            string[] full = Directory.GetFiles(
                Application.dataPath, "*.ktrk", SearchOption.AllDirectories);

            var paths = new string[full.Length];
            for (int index = 0; index < full.Length; ++index)
            {
                paths[index] = "Assets" + full[index]
                    .Substring(Application.dataPath.Length)
                    .Replace('\\', '/');
            }
            return paths;
        }

        /// <summary>
        /// True when the asset holds at least one material and any of them is on
        /// the wrong shader.
        ///
        /// An asset with no materials at all is left alone: that is what a track
        /// whose textures are genuinely missing looks like, and reimporting it
        /// every load would never fix it.
        /// </summary>
        private static bool IsBroken(string path, Shader expected)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is not Material material) continue;
                if (material.shader != expected) return true;
            }
            return false;
        }
    }
}
