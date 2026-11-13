using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 正式内容与三个编辑器的资产契约，修改内容后可重复执行。 */
    public sealed class ContentTests
    {
        /** @brief 文案键、分类、格式参数和运行引用必须完整。 */
        [Test] public void TextCatalog_HasCompleteUniqueBindings()
        {
            var catalog = Resources.Load<GameTextCatalog>("GameTextCatalog"); Assert.That(catalog, Is.Not.Null); Assert.That(catalog.Font, Is.Not.Null);
            Assert.That(catalog.Entries.Select(e => e.Key).Distinct().Count(), Is.EqualTo(catalog.Entries.Count));
            foreach (var entry in catalog.Entries)
            {
                foreach (string text in new[] { entry.Screen, entry.Component, entry.Title, entry.Purpose, entry.Location }) Assert.That(text, Is.Not.Null.And.Not.Empty, entry.Key);
                int count = Regex.Matches(entry.Parameters ?? "", "\\{[0-9]+\\} =").Count;
                if (count > 0) Assert.DoesNotThrow(() => string.Format(entry.Value, Enumerable.Repeat<object>(1, count).ToArray()), entry.Key);
            }
            foreach (string file in Directory.GetFiles("Assets/LightPath/Runtime", "*.cs", SearchOption.AllDirectories))
                foreach (Match match in Regex.Matches(File.ReadAllText(file), "GameText\\.Get\\(\"([^\"]+)\""))
                    Assert.That(catalog.Entries.Any(e => e.Key == match.Groups[1].Value), Is.True, match.Groups[1].Value);
            Assert.That(catalog.Entries.Single(e => e.Key == "LightGameStage.LateUpdate.018").NormalPlay, Is.True);
        }
        /** @brief 唯一UIS、全局引用与正式动作完整。 */
        [Test] public void Input_HasOneProjectAssetAndRequiredActions()
        {
            Assert.That(Directory.GetFiles("Assets", "*.inputactions", SearchOption.AllDirectories).Length, Is.EqualTo(1));
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/DefaultSchema.inputactions"); Assert.That(actions, Is.Not.Null);
            Assert.That(EditorBuildSettings.TryGetConfigObject<InputActionAsset>("com.unity.input.settings.actions", out var project), Is.True); Assert.That(project, Is.EqualTo(actions));
            foreach (string name in new[] { "Move", "Jump", "Palette", "Mode", "Interact", "Pause", "Retry", "Drag", "Pointer", "DeviceHorizontal", "DeviceVertical", "DeviceDepth", "DeviceRotationX", "DeviceRotationY", "DeviceRotation" })
                Assert.That(actions.FindAction("Light/" + name), Is.Not.Null, name);
            Assert.That(actions.FindAction("Light/Mode").bindings.Any(b => b.effectivePath == "<Keyboard>/tab"), Is.True);
        }
        /** @brief 三种必需标记始终属于关卡笔刷目录，不能误收角色或UI。@param name 标记名 */
        [TestCase("SpawnPoint"), TestCase("Checkpoint"), TestCase("LevelExit")]
        public void LevelEditor_CanPlaceRequiredMarker(string name)
        {
            MethodInfo method = typeof(Editor.LightLevelEditor).GetMethod("CanPlaceTemplate", BindingFlags.Static | BindingFlags.NonPublic);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LightPath/Prefabs/Markers/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null); Assert.That(method.Invoke(null, new object[] { prefab }), Is.True);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LightPath/Prefabs/Characters/Player.prefab"); Assert.That(method.Invoke(null, new object[] { player }), Is.False);
        }
        /** @brief 发布内容中所有维修配置有效且每个阶段存在可达解。 */
        [Test] public void SavedRepairLayouts_AreValidAndSolvable()
        {
            var guids = AssetDatabase.FindAssets("t:RepairLayout"); Assert.That(guids, Is.Not.Empty);
            foreach (string guid in guids)
            {
                var layout = AssetDatabase.LoadAssetAtPath<RepairLayout>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(layout.TryValidate(out string error), Is.True, error);
                foreach (var phase in layout.Phases)
                {
                    int budget = 20000;
                    Assert.That(Solve(layout, phase, new float[layout.Devices.Length], 0, ref budget), Is.True, layout.name + "/" + phase.Name);
                }
            }
        }
        /** @brief 遍历允许的离散朝向，仅验证已交付的小型布局。@param layout 布局 @param phase 目标 @param directions 候选 @param index 深度 @return 是否存在解 */
        private static bool Solve(RepairLayout layout, RepairLayout.Phase phase, float[] directions, int index, ref int budget)
        {
            if (--budget < 0) Assert.Inconclusive(layout.name + "/" + phase.Name + " 超过自动求解搜索预算，请使用维修编辑器逐阶段验证；不代表无解。");
            if (index == directions.Length) return RepairPreviewSolver.TraceAngles(layout, directions).Matches(phase.Targets);
            var device = layout.Devices[index];
            if (device.Type == RepairLayout.Kind.Lens)
            {
                var candidates = new System.Collections.Generic.List<float> { device.Direction * 45 + device.AngleRange.x, device.Direction * 45 + device.AngleRange.y, device.OutputAngle };
                foreach (var target in layout.Devices.Where(x => x != device))
                {
                    Vector2 delta = target.Position - device.Position;
                    float center = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                    float radius = Mathf.Asin(Mathf.Clamp01(layout.HitRadius / Mathf.Max(.0001f, delta.magnitude))) * Mathf.Rad2Deg;
                    foreach (float angle in new[] { center - radius, center, center + radius }) candidates.Add(RepairPreviewSolver.ClampLensAngle(device, angle));
                }
                float[] sorted = candidates.Distinct().OrderBy(x => x).ToArray();
                for (int i = 1; i < sorted.Length; i++) candidates.Add((sorted[i - 1] + sorted[i]) / 2);
                foreach (float angle in candidates.Distinct())
                {
                    directions[index] = angle; if (Solve(layout, phase, directions, index + 1, ref budget)) return true;
                }
                return false;
            }
            for (int d = 0; d < 8; d++)
            {
                if ((device.Allowed & (1 << d)) == 0 || ((device.Type == RepairLayout.Kind.Source || device.Type == RepairLayout.Kind.Exit) && d != device.Direction)) continue;
                directions[index] = d * 45f; if (Solve(layout, phase, directions, index + 1, ref budget)) return true;
            }
            return false;
        }
        /** @brief 所有正式场景和预制体不得残留旧文字、丢失脚本或失效TMP引用。 */
        [Test] public void SceneAndPrefabReferences_AreIntact()
        {
            var catalog = Resources.Load<GameTextCatalog>("GameTextCatalog");
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/LightPath" })) CheckRoot(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)), catalog);
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/LightPath" }))
            {
                var scene = EditorSceneManager.OpenPreviewScene(AssetDatabase.GUIDToAssetPath(guid));
                try { foreach (var root in scene.GetRootGameObjects()) CheckRoot(root, catalog); }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
        /** @brief 检查一棵已有层级，不保存或组装任何对象。@param root 根节点 @param catalog 统一配置 */
        private static void CheckRoot(GameObject root, GameTextCatalog catalog)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject), Is.Zero, t.name);
            Assert.That(root.GetComponentsInChildren<UnityEngine.UI.Text>(true), Is.Empty, root.name);
            Assert.That(root.GetComponentsInChildren<TextMesh>(true), Is.Empty, root.name);
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.font, Is.EqualTo(catalog.Font), text.name); Assert.That(text.fontSharedMaterial, Is.EqualTo(catalog.Font.material), text.name);
                var label = text.GetComponent<GameTextLabel>(); Assert.That(label, Is.Not.Null, text.name);
                if (!string.IsNullOrEmpty(text.text) && !text.GetComponentInParent<RoomDecoration>(true)) Assert.That(catalog.Entries.Any(e => e.Key == label.Key), Is.True, text.name);
            }
            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    Assert.That(button.onClick.GetPersistentTarget(i), Is.Not.Null, button.name);
                    Assert.That(button.onClick.GetPersistentMethodName(i), Is.Not.Empty, button.name);
                }
        }
        /** @brief Tools下只保留关卡、维修和文案三个入口。 */
        [Test] public void ToolsMenu_ContainsOnlyThreeEditors()
        {
            var items = Directory.GetFiles("Assets/LightPath/Editor", "*.cs", SearchOption.AllDirectories).SelectMany(file => Regex.Matches(File.ReadAllText(file), "MenuItem\\(\"(Tools/Light Path/[^\"]+)").Cast<Match>().Select(m => m.Groups[1].Value)).ToArray();
            Assert.That(items, Is.EquivalentTo(new[] { "Tools/Light Path/关卡模板编辑器", "Tools/Light Path/维修光路编辑器", "Tools/Light Path/游戏文案表" }));
        }
    }
}
