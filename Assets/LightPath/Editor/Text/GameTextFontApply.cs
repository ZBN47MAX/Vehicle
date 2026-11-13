using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 将统一字体应用到项目全部游戏场景及预制体，包含未激活文字。 */
    public static class GameTextFontApply
    {
        /** @brief 游戏资源范围，不处理插件示例或编辑器自身文字。 */
        private static readonly string[] _roots = { "Assets/LightPath" };
        /** @brief 替换全游戏字体；打开场景支持撤销，原有未保存改动不会被强行保存。@param catalog 唯一文案配置 */
        public static void Apply(GameTextCatalog catalog)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("请退出运行模式和 Prefab 编辑模式，再应用全游戏字体。");
            if (!catalog || !catalog.Font)
                throw new InvalidOperationException("请先在游戏文案 SO 的统一 TMP 字体中选择 TMP Font Asset。");
            GameText.Bind(catalog);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("统一游戏 TMP 字体");
            int count = 0;
            Scene active = SceneManager.GetActiveScene();
            Dictionary<string, byte[]> originals = new Dictionary<string, byte[]>();
            try
            {
                TMP_Settings settings = TMP_Settings.instance;
                if (!settings) throw new InvalidOperationException("项目缺少 TMP Settings，请先导入 TMP Essential Resources。");
                string settingsPath = AssetDatabase.GetAssetPath(settings);
                originals[settingsPath] = File.ReadAllBytes(settingsPath);
                Undo.RecordObject(settings, "统一 TMP 默认字体");
                var serializedSettings = new SerializedObject(settings);
                serializedSettings.FindProperty("m_defaultFontAsset").objectReferenceValue = catalog.Font;
                serializedSettings.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(settings);
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", _roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    originals[path] = File.ReadAllBytes(path);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        int changed = ApplyRoot(root, catalog, false);
                        if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                        count += changed;
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", _roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    originals[path] = File.ReadAllBytes(path);
                    Scene scene = SceneManager.GetSceneByPath(path);
                    bool loaded = scene.IsValid() && scene.isLoaded;
                    if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    bool wasDirty = scene.isDirty;
                    try
                    {
                        int changed = 0;
                        foreach (GameObject root in scene.GetRootGameObjects()) changed += ApplyRoot(root, catalog, loaded);
                        if (changed > 0)
                        {
                            EditorSceneManager.MarkSceneDirty(scene);
                            if (!wasDirty) EditorSceneManager.SaveScene(scene);
                        }
                        count += changed;
                    }
                    finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
                }
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                Debug.Log($"GAME_TEXT_FONT_APPLIED count={count} font={catalog.Font.name}；原先已有未保存改动的场景请自行保存。");
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                foreach (KeyValuePair<string, byte[]> original in originals)
                    File.WriteAllBytes(original.Key, original.Value);
                AssetDatabase.Refresh();
                throw;
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Undo.CollapseUndoOperations(group);
            }
        }
        /** @brief 保留布局和文案，只更新字体、材质及运行时字体绑定。@param root 对象根 @param catalog 文案表 @param undo 是否记录场景撤销 @return 文字数 */
        private static int ApplyRoot(GameObject root, GameTextCatalog catalog, bool undo)
        {
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in labels)
            {
                if (undo) Undo.RecordObject(text, "统一 TMP 字体");
                text.font = catalog.Font;
                text.fontSharedMaterial = catalog.Font.material;
                EditorUtility.SetDirty(text);
                if (!text.GetComponent<GameTextLabel>())
                {
                    if (undo) Undo.AddComponent<GameTextLabel>(text.gameObject);
                    else text.gameObject.AddComponent<GameTextLabel>();
                }
                if (PrefabUtility.IsPartOfPrefabInstance(text)) PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            return labels.Length;
        }
    }
}
