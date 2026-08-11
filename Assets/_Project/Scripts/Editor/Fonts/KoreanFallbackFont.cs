using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Makes the HUD's Korean text render, once, when the source font imports.
    ///
    /// The track names are the demo's own — <c>빌리지 고가의 질주</c> — and the HUD
    /// prints them straight. The hand-made font assets in this project are static
    /// atlases baked from whatever happened to be on screen at the time: Malgun
    /// Gothic SDF holds exactly the five glyphs of <c>테스트 평지</c>, so every other
    /// track came out as boxes.
    ///
    /// The fix is a <em>dynamic</em> font asset — it pulls glyphs from the TTF on
    /// demand, so it covers all thirteen tracks and anything added later without
    /// being rebaked — registered as TMP's global fallback so every text object
    /// picks it up rather than each one needing its own fallback list.
    ///
    /// Noto Sans KR is SIL OFL, which is why it can live in the repository;
    /// Malgun Gothic could not.
    /// </summary>
    public sealed class KoreanFallbackFont : AssetPostprocessor
    {
        private const string SourceFont = "Assets/_Project/Art/UI/Fonts/NotoSansKR-Regular.ttf";
        private const string FontAsset = "Assets/_Project/Art/UI/Fonts/NotoSansKR SDF.asset";
        private const string SettingsAsset = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        // 1024x1024 at 90 pt holds a few hundred Hangul syllables; multi-atlas
        // support adds pages if the project ever needs more.
        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 1024;

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (path.Replace('\\', '/') != SourceFont) continue;

                // Deferred: creating assets from inside the import callback
                // re-enters the asset pipeline while it is still running.
                EditorApplication.delayCall += Ensure;
                return;
            }
        }

        private static void Ensure()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFont);
            if (source == null)
            {
                Debug.LogWarning($"No source font at {SourceFont}; Korean text will stay boxed.");
                return;
            }

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAsset);
            if (font == null)
            {
                font = Create(source);
                if (font == null) return;
            }

            RegisterAsGlobalFallback(font);
        }

        private static TMP_FontAsset Create(Font source)
        {
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
                source,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            if (font == null)
            {
                Debug.LogError($"TextMeshPro could not build a font asset from {SourceFont}.");
                return null;
            }

            font.name = Path.GetFileNameWithoutExtension(FontAsset);
            AssetDatabase.CreateAsset(font, FontAsset);

            // The atlas texture and the material are created alongside the asset
            // and have to be folded into it, or they are lost on the next reload
            // and the font renders blank.
            if (font.material != null)
            {
                font.material.name = font.name + " Material";
                AssetDatabase.AddObjectToAsset(font.material, font);
            }
            if (font.atlasTextures != null)
            {
                foreach (Texture2D atlas in font.atlasTextures)
                {
                    if (atlas == null) continue;
                    atlas.name = font.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, font);
                }
            }

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            Debug.Log($"Built the dynamic Korean fallback font at {FontAsset}.");
            return font;
        }

        /// <summary>
        /// Appends the font to TMP's global fallback list.
        ///
        /// Global rather than per-asset: the HUD uses three faces and none of them
        /// carry Hangul, so a per-asset fallback would have to be maintained three
        /// times over and again for every face added later.
        /// </summary>
        private static void RegisterAsGlobalFallback(TMP_FontAsset font)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsAsset);
            if (settings == null)
            {
                Debug.LogWarning(
                    $"No TMP settings at {SettingsAsset}; add '{font.name}' to the " +
                    "fallback list by hand.");
                return;
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty list = serialized.FindProperty("m_fallbackFontAssets");
            if (list == null) return;

            for (int i = 0; i < list.arraySize; ++i)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == font) return;
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = font;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log($"Registered '{font.name}' as a TextMeshPro global fallback.");
        }
    }
}
