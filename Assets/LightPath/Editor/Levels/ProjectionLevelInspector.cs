using UnityEditor;
using UnityEngine;
namespace DelayNoMore.LightPath.Editor
{
    /** @brief 关卡资产检查器将布局编辑统一导向受约束的关卡编辑器。 */
    [CustomEditor(typeof(ProjectionLevel))]
    public sealed class ProjectionLevelInspector : UnityEditor.Editor
    {
        /** @brief 展示关卡参数与布局入口，不暴露可绕过邻接约束的原始数组。 */
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Config"), new GUIContent("关卡参数"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SlideSeconds"), new GUIContent("滑动秒数"));
            serializedObject.ApplyModifiedProperties();
            ProjectionLevel level = (ProjectionLevel)target;
            EditorGUILayout.LabelField("房间数量", level.RoomCount.ToString());
            EditorGUILayout.HelpBox("房间排布与四边行为在关卡编辑器中修改。无相邻房间时，只能选择阻挡或危险。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("打开关卡编辑器"))
                    LightLevelEditor.EditLevel(level);
        }
    }
}
