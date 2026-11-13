using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace DelayNoMore.LightPath.Editor
{
    /** @brief 纯背景图文的手工布设和属性编辑，只有用户点击画布时才创建并保存到房间。 */
    public sealed partial class LightLevelEditor
    {
        /** @brief 创建没有碰撞和交互的世界空间UI物件。 */
        private void PlaceDecoration(Vector2 position, bool text) => Edit("放置房间背景图文", () =>
        {
            var root = new GameObject(text ? "背景文字" : "背景图片", typeof(RectTransform), typeof(Canvas), typeof(RoomDecoration));
            Undo.RegisterCreatedObjectUndo(root, "放置房间背景图文");
            root.transform.SetParent(_room.transform, false);
            root.layer = _room.gameObject.layer;
            root.transform.position = new Vector3(position.x, position.y, _room.Surface.transform.position.z + .1f);
            root.transform.localScale = Vector3.one * .01f;
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 200);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = -100;
            root.GetComponent<RoomDecoration>().Type = text ? RoomDecoration.Kind.Text : RoomDecoration.Kind.Image;
            if (text)
            {
                var label = root.AddComponent<TextMeshProUGUI>();
                label.text = "在右侧编辑文字"; label.fontSize = 36; label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
                GameText.ApplyFont(label); root.AddComponent<GameTextLabel>();
            }
            else root.AddComponent<RawImage>().raycastTarget = false;
            _picked = root; _inspectTemplate = false; _tool = 0;
        });
        /** @brief 文字内容直接保存在实例/模板，不进入文案SO；字体仍采用全局TMP字体。 */
        private void DrawDecorationProperties(GameObject target)
        {
            if (!target.TryGetComponent(out RoomDecoration decoration)) return;
            var rect = new SerializedObject(target.GetComponent<RectTransform>());
            EditorGUILayout.PropertyField(rect.FindProperty("m_SizeDelta"), new GUIContent("图文尺寸（100像素=1米）"));
            rect.ApplyModifiedProperties();
            if (target.TryGetComponent(out RawImage image))
            {
                var data = new SerializedObject(image);
                EditorGUILayout.PropertyField(data.FindProperty("m_Texture"), new GUIContent("背景图片（Texture）"));
                EditorGUILayout.PropertyField(data.FindProperty("m_Color"), new GUIContent("图片颜色/透明度"));
                data.ApplyModifiedProperties();
            }
            if (target.TryGetComponent(out TMP_Text label))
            {
                var data = new SerializedObject(label);
                EditorGUILayout.PropertyField(data.FindProperty("m_text"), new GUIContent("房间文字（不进文案表）"));
                EditorGUILayout.PropertyField(data.FindProperty("m_fontSize"), new GUIContent("字号"));
                EditorGUILayout.PropertyField(data.FindProperty("m_fontColor"), new GUIContent("文字颜色"));
                data.ApplyModifiedProperties();
            }
            EditorGUILayout.HelpBox("纯背景，无碰撞或投影效果。内容随房间保存；字体跟随全局TMP字体。", MessageType.Info);
        }
    }
}
