using System.Reflection;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using DelayNoMore.LightPath.Editor;

namespace DelayNoMore.LightPath.Tests
{
    /** @brief 使用已有资产验证关卡编辑会话与节点颜色；不保存或创建玩法资产。 */
    public sealed class LevelEditorRegressionTests
    {
        /** @brief 私有窗口入口的测试访问范围。 */
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        /** @brief 使用窗口真实鼠标事件验证2D选取、拖动和预览绘制；不保存修改。 */
        [UnityTest] public IEnumerator Canvas_MouseDragMovesExistingPlatform()
        {
            var window = ScriptableObject.CreateInstance<LightLevelEditor>();
            Vector3 original = Vector3.zero;
            ColorPlatform platform = null;
            try
            {
                var level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>("Assets/LightPath/Levels/Level1.asset");
                Assert.That(level, Is.Not.Null);
                PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(level.RoomPrefab));
                yield return null;
                yield return null;
                window.Show(); window.position = new Rect(20, 20, 1400, 900);
                typeof(LightLevelEditor).GetMethod("OpenRoom", Flags).Invoke(window, new object[] { level, 0 });
                var room = (LightRoom)typeof(LightLevelEditor).GetField("_room", Flags).GetValue(window);
                Assert.That(room, Is.Not.Null, "打开房间失败：" + typeof(LightLevelEditor).GetField("_report", Flags).GetValue(window));
                platform = room.GetComponentInChildren<ColorPlatform>(true);
                Assert.That(platform, Is.Not.Null);
                original = platform.transform.position;
                window.Repaint();
                for (int i = 0; i < 8; i++) yield return null;
                Rect canvas = (Rect)typeof(LightLevelEditor).GetField("_canvasRect", Flags).GetValue(window);
                Assert.That(canvas.width, Is.GreaterThan(100)); Assert.That(canvas.height, Is.GreaterThan(80));
                Vector2 point = (Vector2)typeof(LightLevelEditor).GetMethod("ToCanvas", Flags).Invoke(window, new object[] { (Vector2)original, canvas });
                Assert.That(canvas.Contains(point), Is.True);
                point += (Vector2)typeof(LightLevelEditor).GetField("_canvasWindowOffset", Flags).GetValue(window);
                float zoom = (float)typeof(LightLevelEditor).GetField("_zoom", Flags).GetValue(window);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
                Assert.That(typeof(LightLevelEditor).GetField("_picked", Flags).GetValue(window), Is.EqualTo(platform.gameObject), "画布点击必须选中平台；坐标 " + point + " 画布 " + canvas);
                window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = point + Vector2.right * zoom, delta = Vector2.right * zoom });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point + Vector2.right * zoom });
                Assert.That(platform.transform.position.x, Is.EqualTo(original.x + 1).Within(.001f));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (platform) platform.transform.position = original;
                typeof(LightLevelEditor).GetMethod("CloseLevel", Flags).Invoke(window, null);
                Object.DestroyImmediate(window);
                StageUtility.GoToMainStage();
            }
        }
        /** @brief 打开所有已有关卡房间，验证实际采样、图形坐标和编辑上下文。 */
        [Test] public void ExistingRooms_OpenWithEditableCanvasAndPreview()
        {
            var window = ScriptableObject.CreateInstance<LightLevelEditor>();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:ProjectionLevel", new[] { "Assets/LightPath" }))
                {
                    var level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>(AssetDatabase.GUIDToAssetPath(guid));
                    for (int i = 0; i < level.RoomCount; i++)
                    {
                        typeof(LightLevelEditor).GetField("_layoutMode", Flags).SetValue(window, 1);
                        typeof(LightLevelEditor).GetMethod("OpenRoom", Flags).Invoke(window, new object[] { level, i });
                        Assert.That(typeof(LightLevelEditor).GetMethod("Editable", Flags).Invoke(window, null), Is.True, level.name);
                        Assert.That(typeof(LightLevelEditor).GetField("_layoutMode", Flags).GetValue(window), Is.EqualTo(0));
                        var preview = (Texture2D)typeof(LightLevelEditor).GetField("_projectionPreview", Flags).GetValue(window);
                        Assert.That(preview, Is.Not.Null, level.name);
                        Assert.That(preview.width, Is.GreaterThan(0));
                        var room = (LightRoom)typeof(LightLevelEditor).GetField("_room", Flags).GetValue(window);
                        var platform = room.GetComponentInChildren<ColorPlatform>(true);
                        if (platform)
                        {
                            Vector3 original = platform.transform.position;
                            var target = (Vector2)original + Vector2.right;
                            typeof(LightLevelEditor).GetMethod("MoveTo", Flags).Invoke(window, new object[] { platform.gameObject, target });
                            Assert.That((Vector2)platform.transform.position, Is.EqualTo(target));
                            Assert.That(platform.transform.position.z, Is.EqualTo(original.z));
                            platform.transform.position = original;
                        }
                        typeof(LightLevelEditor).GetMethod("CloseLevel", Flags).Invoke(window, null);
                    }
                }
            }
            finally { Object.DestroyImmediate(window); }
        }
        /** @brief 存档点颜色控制渲染与接触，隐藏不清除已保存的复活位置。 */
        [Test] public void Checkpoint_ColorControlsAppearanceAndTrigger()
        {
            GameObject root = PrefabUtility.LoadPrefabContents("Assets/LightPath/Prefabs/Markers/Checkpoint.prefab");
            var config = ScriptableObject.CreateInstance<LightGameConfig>();
            try
            {
                var checkpoint = root.GetComponent<RoomCheckpoint>();
                var data = new SerializedObject(checkpoint);
                data.FindProperty("_colorIndex").intValue = 1;
                data.ApplyModifiedPropertiesWithoutUndo();
                checkpoint.Refresh(config, 0, true);
                var renderer = root.GetComponentInChildren<Renderer>();
                Assert.That(renderer.enabled, Is.True);
                Assert.That(root.GetComponent<Collider2D>().enabled, Is.True);
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Assert.That(Vector4.Distance(block.GetColor("_BaseColor"), config.DisplayColor(config.GetColor(1))), Is.LessThan(.0001f));
                checkpoint.Refresh(config, 1, true);
                Assert.That(renderer.enabled, Is.False);
                Assert.That(checkpoint.Touches(new Bounds(root.transform.position, Vector3.one * 10)), Is.False);
                checkpoint.Refresh(config, 0, false);
                Assert.That(renderer.enabled, Is.False);
                checkpoint.Refresh(config, 0, true);
                Assert.That(root.GetComponent<Collider2D>().enabled, Is.True);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); Object.DestroyImmediate(config); }
        }
    }
}
