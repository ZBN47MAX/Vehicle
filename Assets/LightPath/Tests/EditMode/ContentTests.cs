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
                foreach (Match match in Regex.Matches(File.ReadAllText(file), "GameText\\.Get\\(\"([^\"]+)\"\\s*[,)]"))
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
                    float[] initial = layout.Devices.Select(device => device.OutputAngle).ToArray();
                    if (RepairPreviewSolver.TraceAngles(layout, initial).Matches(phase.Targets)) continue;
                    int budget = 2000000;
                    bool[] assigned = layout.Devices.Select(device => device.Type == RepairLayout.Kind.Source || device.Type == RepairLayout.Kind.Exit).ToArray();
                    Assert.That(Solve(layout, phase, initial, assigned, ref budget), Is.True, layout.name + "/" + phase.Name);
                }
            }
        }
        /** @brief 只为光线实际经过的节点分配角度，未经过节点不影响当前结果。 */
        private static bool Solve(RepairLayout layout, RepairLayout.Phase phase, float[] directions, bool[] assigned, ref int budget)
        {
            if (--budget < 0) Assert.Inconclusive(layout.name + "/" + phase.Name + " 超过自动求解搜索预算，请使用维修编辑器逐阶段验证；不代表无解。");
            var result = RepairPreviewSolver.TraceAngles(layout, directions);
            if (result.Matches(phase.Targets)) return true;
            int index = -1;
            foreach (var segment in result.Segments)
            {
                Vector2 delta = segment.To - segment.From;
                if (delta.sqrMagnitude < .000001f) continue;
                for (int candidate = 0; candidate < layout.Devices.Length; candidate++)
                {
                    if (assigned[candidate]) continue;
                    if (RepairPreviewSolver.HitNode(segment.From, delta.normalized, layout.Devices[candidate].Position, layout.NodeRadius, out float along)
                        && Mathf.Abs(along - delta.magnitude) < .001f)
                    {
                        index = candidate;
                        break;
                    }
                }
                if (index >= 0) break;
            }
            if (index < 0) return false;
            assigned[index] = true;
            bool solved = SolveNode(layout, phase, directions, assigned, index, ref budget);
            assigned[index] = false;
            return solved;
        }
        /** @brief 遍历已命中节点的合法朝向，所有候选均通过正式追踪器验证。 */
        private static bool SolveNode(RepairLayout layout, RepairLayout.Phase phase, float[] directions, bool[] assigned, int index, ref int budget)
        {
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
                foreach (float angle in candidates.Distinct().GroupBy(angle => OutgoingKey(layout, index, angle)).Select(group => group.First()))
                {
                    directions[index] = angle; if (Solve(layout, phase, directions, assigned, ref budget)) return true;
                }
                return false;
            }
            // 优先尝试策划初始朝向，随后遍历其余允许方向，避免在明显解之前耗尽预算。
            var outgoing = new System.Collections.Generic.HashSet<string>();
            for (int offset = 0; offset < 8; offset++)
            {
                int d = (device.Direction + offset) % 8;
                if ((device.Allowed & (1 << d)) == 0 || ((device.Type == RepairLayout.Kind.Source || device.Type == RepairLayout.Kind.Exit) && d != device.Direction)) continue;
                if (!outgoing.Add(OutgoingKey(layout, index, d * 45f))) continue;
                directions[index] = d * 45f; if (Solve(layout, phase, directions, assigned, ref budget)) return true;
            }
            return false;
        }
        /** @brief 节点输出只取决于下一命中节点；相同命中组合的角度无需重复搜索。 */
        private static string OutgoingKey(RepairLayout layout, int index, float angle)
        {
            string key = "";
            int rays = layout.Devices[index].Type == RepairLayout.Kind.Splitter ? 2 : 1;
            for (int ray = 0; ray < rays; ray++)
            {
                float radians = (angle + ray * 45) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                Vector2 start = layout.Devices[index].Position;
                float nearest = 1000;
                for (int axis = 0; axis < 2; axis++)
                {
                    float min = axis == 0 ? layout.Area.xMin : layout.Area.yMin;
                    float max = axis == 0 ? layout.Area.xMax : layout.Area.yMax;
                    if (direction[axis] > .0001f) nearest = Mathf.Min(nearest, (max - start[axis]) / direction[axis]);
                    if (direction[axis] < -.0001f) nearest = Mathf.Min(nearest, (min - start[axis]) / direction[axis]);
                }
                int hit = -1;
                for (int candidate = 0; candidate < layout.Devices.Length; candidate++)
                {
                    if (candidate == index) continue;
                    if (RepairPreviewSolver.HitNode(start, direction, layout.Devices[candidate].Position, layout.NodeRadius, out float distance) && distance < nearest)
                    {
                        nearest = distance;
                        hit = candidate;
                    }
                }
                key += hit + "/";
            }
            return key;
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
