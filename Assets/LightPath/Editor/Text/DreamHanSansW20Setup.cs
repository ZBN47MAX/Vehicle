using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 本次字体切换使用的一次性编辑器任务，完成后移除。 */
    [InitializeOnLoad]
    public static class DreamHanSansW20Setup
    {
        /** @brief 本次任务完成标识。 */
        private const string ResultPath = "Temp/DreamHanSansW20Setup.result";
        /** @brief 新 TMP 资产位置。 */
        private const string FontPath = "Assets/LightPath/Art/Fonts/DreamHanSans-W20 TMP.asset";

        /** @brief 等待编辑器完成资源加载后执行字体切换。 */
        static DreamHanSansW20Setup()
        {
            EditorApplication.delayCall += Run;
        }

        /** @brief 创建并验证动态字体，接入文案表与全部游戏文字。 */
        public static void Run()
        {
            if (File.Exists(ResultPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorApplication.delayCall += Run;
                return;
            }
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<GameTextCatalog>("Assets/LightPath/Resources/GameTextCatalog.asset");
                if (!catalog) throw new InvalidOperationException("找不到唯一游戏文案表。");
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                if (!font)
                {
                    var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/LightPath/Art/Fonts/(ttc)DreamHanSans/DreamHanSans-W20.ttc");
                    if (!source) throw new InvalidOperationException("W20 字体尚未正确导入。");
                    font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                    if (!font) throw new InvalidOperationException("W20 字体创建失败。");
                    font.name = "DreamHanSans-W20 TMP";
                    AssetDatabase.CreateAsset(font, FontPath);
                    AssetDatabase.AddObjectToAsset(font.material, font);
                    AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
                }
                string characters = string.Concat(catalog.Entries.Select(entry => entry.Value)) +
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 []{}<>+-=*/%.,:;!?()\\|_\"'\n";
                characters = new string(characters.Where(character => !char.IsControl(character)).Distinct().ToArray());
                font.TryAddCharacters(characters, out string missing);
                var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/LightPath/Art/Fonts/NotoSansCJKsc TMP.asset");
                if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (fallback && !font.fallbackFontAssetTable.Contains(fallback)) font.fallbackFontAssetTable.Add(fallback);
                foreach (Texture2D texture in font.atlasTextures)
                {
                    if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
                    EditorUtility.SetDirty(texture);
                }
                EditorUtility.SetDirty(font.material);
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssetIfDirty(font);
                Undo.RecordObject(catalog, "切换 DreamHanSans W20 字体");
                catalog.Font = font;
                GameTextFontApply.Apply(catalog);
                if (catalog.Font != font || TMP_Settings.defaultFontAsset != font)
                    throw new InvalidOperationException("全局字体引用核验失败。");
                string result = $"SUCCESS font={font.name} family={font.faceInfo.familyName} style={font.faceInfo.styleName} characters={font.characterTable.Count} atlases={font.atlasTextures.Length} missing={missing}";
                File.WriteAllText(ResultPath, result);
                Debug.Log(result);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ResultPath, "FAILED " + exception);
                Debug.LogException(exception);
            }
        }
    }
}
