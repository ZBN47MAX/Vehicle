using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 屏幕诊断、轮廓几何与纯UI背景布设的回归。 */
    public sealed class PresentationToolsTests
    {
        /** @brief 滚轮方向、首尾循环及停用节点跳过规则。 */
        [Test] public void ProjectionSelection_WrapsAndSkipsUnavailable()
        {
            Assert.That(ProjectionSelection.NextIndex(4,0,-1,i=>i!=3),Is.EqualTo(2));
            Assert.That(ProjectionSelection.NextIndex(4,3,1,i=>i!=0),Is.EqualTo(1));
            Assert.That(ProjectionSelection.NextIndex(4,2,1,i=>false),Is.EqualTo(2));
            Assert.That(ProjectionSelection.NextIndex(0,0,1,i=>true),Is.Zero);
        }
        /** @brief 检查持久诊断面板不再属于世界平面。 */
        [Test] public void DebugPanel_IsPersistentScreenOverlay()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/LightPath/Scenes/LightGame.unity");
            try
            {
                var panel=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<LightDebugPanel>(true)).Single();
                var canvas=panel.GetComponentInParent<Canvas>();
                Assert.That(canvas.renderMode,Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvas.transform.parent,Is.Null);
                Assert.That(canvas.GetComponent<CanvasScaler>(),Is.Not.Null);
                var rect=panel.GetComponent<RectTransform>(); Assert.That(rect.anchorMin,Is.EqualTo(new Vector2(0,1)));
                Assert.That(rect.anchoredPosition,Is.EqualTo(new Vector2(24,-24)));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        /** @brief 使用已存在房间测试UI图文的作者入口，不保存或创建玩法资产。 */
        [TestCase(false),TestCase(true)] public void DecorationPlacement_IsUiOnlyAndLocallyEditable(bool text)
        {
            var path=AssetDatabase.FindAssets("t:ProjectionLevel",new[]{"Assets/LightPath"}).Select(AssetDatabase.GUIDToAssetPath).First();
            var level=AssetDatabase.LoadAssetAtPath<ProjectionLevel>(path);
            var root=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(level.RoomPrefab));
            var window=ScriptableObject.CreateInstance<Editor.LightLevelEditor>();
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            try
            {
                typeof(Editor.LightLevelEditor).GetField("_room",flags).SetValue(window,root.GetComponent<LightRoom>());
                typeof(Editor.LightLevelEditor).GetMethod("PlaceDecoration",flags).Invoke(window,new object[]{Vector2.zero,text});
                var node=(GameObject)typeof(Editor.LightLevelEditor).GetField("_picked",flags).GetValue(window);
                Assert.That(node.GetComponent<RoomDecoration>(),Is.Not.Null);
                Assert.That(node.GetComponentsInChildren<Collider>(),Is.Empty); Assert.That(node.GetComponentsInChildren<Collider2D>(),Is.Empty);
                Assert.That(node.GetComponent<ProjectionSource>(),Is.Null);
                Assert.That(node.GetComponent<Canvas>().renderMode,Is.EqualTo(RenderMode.WorldSpace));
                if(text)
                {
                    var label=node.GetComponent<TMP_Text>(); label.text="房间独立文字";
                    Assert.That(node.GetComponent<GameTextLabel>().Key,Is.Null.Or.Empty);
                    Assert.That(label.font,Is.EqualTo(GameText.Font));
                    Assert.That(label.raycastTarget,Is.False);
                }
                else Assert.That(node.GetComponent<RawImage>().raycastTarget,Is.False);
                node.GetComponent<RoomDecoration>().Refresh(false); Assert.That(node.GetComponent<Canvas>().enabled,Is.False);
            }
            finally { Object.DestroyImmediate(window); PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
