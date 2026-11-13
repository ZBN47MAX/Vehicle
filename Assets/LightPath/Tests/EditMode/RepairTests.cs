using System;
using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 维修规则回归：时序、只读状态、节点行为和非法布局。 */
    public sealed class RepairTests
    {
        /** @brief 每项测试独占的内存配置，不创建资产。 */
        private RepairLayout _layout;
        /** @brief 构造一源一镜两出口、两个阶段的确定性布局。 */
        [SetUp] public void SetUp()
        {
            _layout = ScriptableObject.CreateInstance<RepairLayout>();
            _layout.RandomPhaseOrder = false;
            _layout.Interval = new Vector2(8, 8);
            _layout.WarningSeconds = 2;
            _layout.Devices = new[] { Node("source", RepairLayout.Kind.Source, -4, 0), Node("mirror", RepairLayout.Kind.Mirror, 0, 0), Node("up", RepairLayout.Kind.Exit, 0, 4), Node("down", RepairLayout.Kind.Exit, 0, -4) };
            _layout.Phases = new[] { new RepairLayout.Phase { Targets = new[] { "up" } }, new RepairLayout.Phase { Targets = new[] { "down" } } };
        }
        /** @brief 释放临时配置。 */
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_layout);
        /** @brief 创建纯数据节点。@param id 唯一标识 @param kind 类型 @param x 横坐标 @param y 纵坐标 @return 节点 */
        private static RepairLayout.Device Node(string id, RepairLayout.Kind kind, float x, float y) => new RepairLayout.Device { Id = id, Type = kind, Position = new Vector2(x, y) };
        /** @brief 完整时序与SO只读性。 */
        [Test] public void Warning_Blackout_Repair_AndPhaseLoop()
        {
            string before = JsonUtility.ToJson(_layout);
            var session = new RepairSession(_layout, (min, max) => min);
            Assert.That(session.Powered, Is.False); Assert.That(session.PhaseIndex, Is.Zero);
            Assert.That(session.RemainingSeconds, Is.Zero);
            session.Tick(100); Assert.That(session.PhaseIndex, Is.Zero);
            session.Rotate(1); session.Rotate(1); Assert.That(session.Powered, Is.True); Assert.That(session.RepairCount, Is.EqualTo(1));
            session.Tick(6); Assert.That(session.Warning, Is.True); Assert.That(session.RemainingSeconds, Is.EqualTo(2));
            session.Tick(2); Assert.That(session.Powered, Is.False); Assert.That(session.PhaseIndex, Is.EqualTo(1));
            session.Tick(100); Assert.That(session.RemainingSeconds, Is.Zero); Assert.That(session.PhaseIndex, Is.EqualTo(1));
            session.Rotate(1); session.Rotate(1); session.Rotate(1); session.Rotate(1);
            Assert.That(session.Powered, Is.True); Assert.That(session.RepairCount, Is.EqualTo(2));
            session.Tick(8); Assert.That(session.PhaseIndex, Is.Zero); Assert.That(session.Powered, Is.False);
            Assert.That(JsonUtility.ToJson(_layout), Is.EqualTo(before), "运行不改写共享SO");
            Assert.That(new RepairSession(_layout).Direction(1), Is.Zero);
        }
        /** @brief 无效步长不得推进时间。@param delta 输入秒数 */
        [TestCase(-1f), TestCase(0f), TestCase(float.NaN), TestCase(float.PositiveInfinity)]
        public void InvalidDelta_DoesNotAdvance(float delta)
        {
            _layout.Devices[1].Direction = 2; var session = new RepairSession(_layout); session.Tick(delta); Assert.That(session.RemainingSeconds, Is.EqualTo(8));
        }
        /** @brief 光源出口和越界索引不可旋转。@param index 索引 */
        [TestCase(-1), TestCase(0), TestCase(2), TestCase(3), TestCase(4)]
        public void FixedOrMissingNode_CannotRotate(int index) => Assert.That(new RepairSession(_layout).Rotate(index), Is.False);
        /** @brief 朝向按允许掩码循环。@param current 当前朝向 @param mask 可用位 @param expected 期望 */
        [TestCase(0, 5, 2), TestCase(2, 5, 0), TestCase(7, 255, 0), TestCase(3, 8, 3), TestCase(2, 0, 2)]
        public void Rotation_RespectsMask(int current, int mask, int expected) => Assert.That(RepairPreviewSolver.Next(current, mask), Is.EqualTo(expected));
        /** @brief 单阶段已接通时不强制打乱或重复累计维修。 */
        [Test] public void SolvedSinglePhase_RemainsPowered()
        {
            _layout.Phases = new[] { _layout.Phases[0] };
            var session = new RepairSession(_layout); session.Tick(8); session.Rotate(1); session.Rotate(1); session.Tick(8);
            Assert.That(session.Powered, Is.True); Assert.That(session.RepairCount, Is.EqualTo(1)); Assert.That(session.RemainingSeconds, Is.EqualTo(8));
        }
        /** @brief 非法配置必须拒绝并解释，不允许创建会话。@param fault 错误种类 */
        [TestCase("devices"), TestCase("phases"), TestCase("interval"), TestCase("area"), TestCase("duplicate"), TestCase("source"), TestCase("target"), TestCase("direction"), TestCase("allowed"), TestCase("nan")]
        public void InvalidLayout_IsRejected(string fault)
        {
            switch (fault)
            {
                case "devices": _layout.Devices = Array.Empty<RepairLayout.Device>(); break;
                case "phases": _layout.Phases = Array.Empty<RepairLayout.Phase>(); break;
                case "interval": _layout.Interval = Vector2.one; break;
                case "area": _layout.Area = new Rect(0, 0, 0, 1); break;
                case "duplicate": _layout.Devices[1].Id = "source"; break;
                case "source": _layout.Devices[0].Type = RepairLayout.Kind.Exit; break;
                case "target": _layout.Phases[0].Targets = new[] { "missing" }; break;
                case "direction": _layout.Devices[1].Direction = 8; break;
                case "allowed": _layout.Devices[1].Allowed = 0; break;
                case "nan": _layout.HitRadius = float.NaN; break;
            }
            Assert.That(_layout.TryValidate(out string error), Is.False); Assert.That(error, Is.Not.Empty);
            Assert.Throws<ArgumentException>(() => new RepairSession(_layout));
        }
        /** @brief 分光器同时点亮两个出口，镜片只沿单个方向出光。@param splitter 是否分支 */
        [TestCase(true), TestCase(false)] public void SplitterAndLens_HaveDistinctOutputs(bool splitter)
        {
            _layout.Devices[1].Type = splitter ? RepairLayout.Kind.Splitter : RepairLayout.Kind.Lens;
            _layout.Devices[2].Position = new Vector2(4, 0); _layout.Devices[3].Position = new Vector2(4, 4);
            var result = RepairPreviewSolver.Trace(_layout, null);
            Assert.That(result.Lit.Contains("up"), Is.True); Assert.That(result.Lit.Contains("down"), Is.EqualTo(splitter));
        }
        /** @brief 最近出口截断光线，后方出口不能穿透命中。 */
        [Test] public void NearestNode_BlocksFurtherExit()
        {
            _layout.Devices[1].Type = RepairLayout.Kind.Exit;
            _layout.Devices[2].Position = new Vector2(4, 0);
            var result = RepairPreviewSolver.Trace(_layout, null);
            Assert.That(result.Lit, Is.EquivalentTo(new[] { "mirror" }));
        }
    }
}
