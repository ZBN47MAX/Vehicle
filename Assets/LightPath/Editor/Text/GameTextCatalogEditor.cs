using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace DelayNoMore.LightPath.Editor
{
    /** @brief 按界面、正常游玩与组件用途三级分类编辑统一文案。 */
    [CustomEditor(typeof(GameTextCatalog))]
    public sealed class GameTextCatalogEditor : UnityEditor.Editor
    {
        /** @brief 当前搜索词。 */
        private string _search = "";
        /** @brief 新增文字的唯一绑定键。 */
        private string _newKey = "";
        /** @brief 折叠状态只影响当前编辑器，不写入共享配置。 */
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
        /** @brief 新分组默认展开状态。 */
        private bool _expand;
        /** @brief 界面固定阅读顺序，未知分类排在其后。 */
        private static readonly string[] _screens = { "主界面", "2D世界", "3D总览", "投影调整", "投影仪维修", "换色轮盘", "暂停菜单", "结算界面", "全局HUD", "运行与配置检查", "待分类" };
        /** @brief 打开唯一文案资产。 */
        [MenuItem("Tools/Light Path/游戏文案表")]
        public static void Open() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameTextCatalog>("Assets/LightPath/Resources/GameTextCatalog.asset");
        /** @brief 绘制统一字体入口和三级文案树；编辑支持Unity撤销。 */
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty font = serializedObject.FindProperty("Font");
            EditorGUILayout.PropertyField(font, new GUIContent("统一 TMP 字体"));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || !font.objectReferenceValue))
            {
                if (GUILayout.Button("一键应用到全部游戏 TMP 文字"))
                {
                    serializedObject.ApplyModifiedProperties();
                    GameTextFontApply.Apply((GameTextCatalog)target);
                    serializedObject.Update();
                }
            }
            EditorGUILayout.HelpBox("界面 → 正常 / 不正常 → 组件类型。断光、未解锁、操作受阻属于正常玩法提示；报错、预览及初始化占位归入不正常。修改正文即可；分类和技术信息位于每项详情。", MessageType.Info);
            _search = EditorGUILayout.TextField("搜索位置 / 用途 / 文案", _search);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("展开全部")) { _foldouts.Clear(); _expand = true; }
                if (GUILayout.Button("折叠全部")) { _foldouts.Clear(); _expand = false; }
            }
            SerializedProperty entries = serializedObject.FindProperty("Entries");
            using (new EditorGUILayout.HorizontalScope())
            {
                _newKey = EditorGUILayout.TextField("新文案唯一Key", _newKey);
                bool exists = ((GameTextCatalog)target).Entries.Any(entry => entry.Key == _newKey.Trim());
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newKey) || exists || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("新增文案", GUILayout.Width(80)))
                    {
                        int index = entries.arraySize++;
                        SerializedProperty added = entries.GetArrayElementAtIndex(index);
                        foreach (string field in new[] { "Screen", "Component", "Title", "Purpose", "Location", "Parameters", "Value" }) added.FindPropertyRelative(field).stringValue = "";
                        added.FindPropertyRelative("Key").stringValue = _newKey.Trim();
                        added.FindPropertyRelative("NormalPlay").boolValue = true;
                        added.FindPropertyRelative("Screen").stringValue = "待分类";
                        _search = _newKey.Trim(); _foldouts["detail/" + _search] = true; _newKey = "";
                    }
                }
            }
            var visible = new List<SerializedProperty>();
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                string haystack = string.Join(" ", new[] { "Screen", "Component", "Title", "Purpose", "Location", "Key", "Value" }.Select(field => Text(entry, field)));
                if (string.IsNullOrWhiteSpace(_search) || haystack.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) visible.Add(entry);
            }
            EditorGUILayout.LabelField($"显示 {visible.Count} / {entries.arraySize} 条");
            foreach (var screen in visible.GroupBy(entry => Group(entry, "Screen")).OrderBy(group => ScreenOrder(group.Key)).ThenBy(group => group.Key))
            {
                if (!Fold("screen/" + screen.Key, $"{screen.Key}（{screen.Count()}）")) continue;
                EditorGUI.indentLevel++;
                foreach (bool normal in new[] { true, false })
                {
                    var items = screen.Where(entry => entry.FindPropertyRelative("NormalPlay").boolValue == normal).ToList();
                    string path = "screen/" + screen.Key + "/" + normal;
                    if (!Fold(path, (normal ? "正常" : "不正常（报错 / 预览 / 占位）") + $"（{items.Count}）")) continue;
                    EditorGUI.indentLevel++;
                    foreach (var component in items.GroupBy(entry => Group(entry, "Component")).OrderBy(group => group.Key))
                    {
                        if (!Fold(path + "/" + component.Key, $"{component.Key}（{component.Count()}）")) continue;
                        foreach (SerializedProperty entry in component.OrderBy(entry => Text(entry, "Title"))) DrawEntry(entry);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            if (serializedObject.ApplyModifiedProperties()) GameText.Bind((GameTextCatalog)target);
        }
        /** @brief 绘制友好名称、用途及正文，技术字段默认折叠。@param entry 条目 */
        private void DrawEntry(SerializedProperty entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string key = Text(entry, "Key");
                EditorGUILayout.LabelField(Group(entry, "Title"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Text(entry, "Purpose"), EditorStyles.wordWrappedLabel);
                string source = key.StartsWith("prefab/") ? "UI预制体" : key.StartsWith("scene/b909") ? "独立 LightUI 场景" : key.StartsWith("scene/") ? "LightGame 场景" : "运行时文案 / 内容配置";
                EditorGUILayout.LabelField("来源：" + source, EditorStyles.miniLabel);
                string parameters = Text(entry, "Parameters");
                if (!string.IsNullOrEmpty(parameters)) EditorGUILayout.LabelField("动态参数：" + parameters, EditorStyles.wordWrappedLabel);
                SerializedProperty value = entry.FindPropertyRelative("Value");
                value.stringValue = EditorGUILayout.TextArea(value.stringValue, GUILayout.MinHeight(44));
                if (Fold("detail/" + key, "分类与绑定详情", false))
                {
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Screen"), new GUIContent("出现界面"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("NormalPlay"), new GUIContent("正常游玩会出现"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Component"), new GUIContent("组件类型 / 用途"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Title"), new GUIContent("文字位置名称"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Purpose"), new GUIContent("用途和触发时机"));
                    EditorGUILayout.LabelField("绑定位置：" + Text(entry, "Location"), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("稳定标识（只读）");
                    EditorGUILayout.SelectableLabel(key, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(36));
                }
            }
        }
        /** @brief 读取文本属性。@param entry 条目 @param name 字段 @return 文本 */
        private static string Text(SerializedProperty entry, string name) => entry.FindPropertyRelative(name).stringValue;
        /** @brief 空分类明确标为待分类。@param entry 条目 @param name 字段 @return 分类 */
        private static string Group(SerializedProperty entry, string name) => string.IsNullOrWhiteSpace(Text(entry, name)) ? "待分类" : Text(entry, name);
        /** @brief 获取界面排序位置。@param screen 界面 @return 序号 */
        private static int ScreenOrder(string screen) => Array.IndexOf(_screens, screen) is int index && index >= 0 ? index : _screens.Length;
        /** @brief 搜索自动展开匹配分组，详情保持手动控制。@param key 唯一折叠键 @param label 标题 @param group 是否为分组 @return 是否展开 */
        private bool Fold(string key, string label, bool group = true)
        {
            bool expanded = _foldouts.TryGetValue(key, out bool saved) ? saved : group && _expand;
            if (group && !string.IsNullOrWhiteSpace(_search)) expanded = true;
            expanded = EditorGUILayout.Foldout(expanded, label, true);
            _foldouts[key] = expanded;
            return expanded;
        }
    }
    /** @brief 编辑器加载时只绑定唯一文案资产，不创建或修改资产。 */
    [InitializeOnLoad]
    internal static class GameTextEditorBridge
    {
        /** @brief 延迟到资源系统就绪后读取文案。 */
        static GameTextEditorBridge() => EditorApplication.delayCall += () => GameText.Bind(AssetDatabase.LoadAssetAtPath<GameTextCatalog>("Assets/LightPath/Resources/GameTextCatalog.asset"));
    }
}
