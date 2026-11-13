using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 多光源、启动供光与严格目标集合的纯数据回归。 */
    public sealed class RepairPowerTests
    {
        /** @brief 本用例独立配置。 */
        private RepairLayout _layout;
        /** @brief 两光源各自照亮一个出口，第一阶段只需要其中一个。 */
        [SetUp] public void SetUp()
        {
            _layout = ScriptableObject.CreateInstance<RepairLayout>();
            _layout.RandomPhaseOrder = false;
            _layout.Devices = new[] {
                new RepairLayout.Device { Id="s1", Type=RepairLayout.Kind.Source, Position=new Vector2(-4, 0) },
                new RepairLayout.Device { Id="s2", Type=RepairLayout.Kind.Source, Position=new Vector2(-4, 3) },
                new RepairLayout.Device { Id="a", Type=RepairLayout.Kind.Exit, Position=new Vector2(4, 0) },
                new RepairLayout.Device { Id="b", Type=RepairLayout.Kind.Exit, Position=new Vector2(4, 3) } };
            _layout.Phases = new[] { new RepairLayout.Phase { Targets=new[] { "a" } }, new RepairLayout.Phase { Targets=new[] { "a", "b" } } };
        }
        /** @brief 释放临时配置。 */
        [TearDown] public void TearDown() => Object.DestroyImmediate(_layout);
        /** @brief 五阶段洗牌每轮各出现一次，预警目标稳定，轮界不重复且SO顺序不变。 */
        [Test] public void RandomPhases_VisitAllFiveBeforeRepeating()
        {
            _layout.RandomPhaseOrder = true;
            _layout.Phases = new RepairLayout.Phase[5];
            for (int i = 0; i < 5; i++)
                _layout.Phases[i] = new RepairLayout.Phase { Name = i.ToString(), Targets = new[] { "a", "b" } };
            var random = new System.Random(2718);
            var session = new RepairSession(_layout, (min, max) => min + (float)random.NextDouble() * (max - min));
            Assert.That(session.PhaseIndex, Is.Zero);
            int previous = -1;
            for (int round = 0; round < 4; round++)
            {
                var visited = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < 5; i++)
                {
                    Assert.That(visited.Add(session.PhaseIndex), Is.True);
                    Assert.That(session.PhaseIndex, Is.Not.EqualTo(previous));
                    previous = session.PhaseIndex;
                    int next = session.NextPhaseIndex;
                    Assert.That(session.NextPhaseIndex, Is.EqualTo(next));
                    session.Tick(1000);
                    Assert.That(session.PhaseIndex, Is.EqualTo(next));
                }
                Assert.That(visited.Count, Is.EqualTo(5));
            }
            for (int i = 0; i < 5; i++) Assert.That(_layout.Phases[i].Name, Is.EqualTo(i.ToString()));
        }
        /** @brief 不同阶段数量与随机种子都从0开始，单阶段保持0，首轮其余阶段不重复。@param count 阶段数量 */
        [TestCase(1), TestCase(2), TestCase(5), TestCase(6)]
        public void RandomPhases_AlwaysStartAtZero(int count)
        {
            _layout.RandomPhaseOrder = true;
            _layout.Phases = new RepairLayout.Phase[count];
            for (int i = 0; i < count; i++)
                _layout.Phases[i] = new RepairLayout.Phase { Targets = new[] { "a", "b" } };
            for (int seed = 0; seed < 16; seed++)
            {
                var random = new System.Random(seed);
                var session = new RepairSession(_layout, (min, max) => min + (float)random.NextDouble() * (max - min));
                Assert.That(session.PhaseIndex, Is.Zero);
                var visited = new System.Collections.Generic.HashSet<int> { 0 };
                for (int i = 1; i < count; i++)
                {
                    int next = session.NextPhaseIndex;
                    Assert.That(next, Is.Not.Zero);
                    session.Tick(1000);
                    Assert.That(session.PhaseIndex, Is.EqualTo(next));
                    Assert.That(visited.Add(session.PhaseIndex), Is.True);
                }
                Assert.That(visited.Count, Is.EqualTo(count));
                session.Tick(1000);
                if (count == 1) Assert.That(session.PhaseIndex, Is.Zero);
            }
        }
        /** @brief 多光源均被追踪，额外出口在阶段0初始化时立即断光。@param random 是否随机循环 */
        [TestCase(false), TestCase(true)] public void MultipleSources_ExtraExitBlocksStartup(bool random)
        {
            _layout.RandomPhaseOrder = random;
            Assert.That(_layout.TryValidate(out _), Is.True);
            var session = new RepairSession(_layout);
            Assert.That(session.PhaseIndex, Is.Zero);
            Assert.That(session.Result.Lit, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(session.Powered, Is.False); Assert.That(session.RemainingSeconds, Is.Zero);
            session.Tick(1000); Assert.That(session.PhaseIndex, Is.Zero);
        }
        /** @brief 初始精确匹配直接供光；计时结束后按下一阶段严格判定。 */
        [Test] public void ExactStartup_PowersWithoutCountingRepair_ThenChecksNextPhase()
        {
            _layout.Phases[0].Targets = new[] { "b", "a" };
            _layout.Phases[1].Targets = new[] { "b" };
            var session = new RepairSession(_layout);
            Assert.That(session.Powered, Is.True); Assert.That(session.RepairCount, Is.Zero);
            Assert.That(session.RemainingSeconds, Is.GreaterThan(0));
            session.Tick(1000);
            Assert.That(session.PhaseIndex, Is.EqualTo(1)); Assert.That(session.Powered, Is.False);
        }
        /** @brief 强制状态冻结阶段计时，不篡改真实解法、维修次数或共享配置。 */
        [Test] public void DebugPower_OverridesBothStatesAndRestoresRealCircuit()
        {
            string before = JsonUtility.ToJson(_layout);
            var session = new RepairSession(_layout);
            Assert.That(session.Powered, Is.False);
            session.SetDebugPower(true);
            Assert.That(session.Powered && session.DebugPowerForced, Is.True);
            session.Tick(1000);
            Assert.That(session.PhaseIndex, Is.Zero);
            Assert.That(session.RepairCount, Is.Zero);
            session.SetDebugPower(null);
            Assert.That(session.Powered || session.DebugPowerForced, Is.False);
            _layout.Phases[0].Targets = new[] { "a", "b" };
            session = new RepairSession(_layout);
            float seconds = session.RemainingSeconds;
            session.SetDebugPower(false);
            session.Tick(1000);
            Assert.That(session.Powered || session.Warning, Is.False);
            Assert.That(session.RemainingSeconds, Is.EqualTo(seconds));
            session.SetDebugPower(null);
            Assert.That(session.Powered, Is.True);
            Assert.That(session.RepairCount, Is.Zero);
            _layout.Phases[0].Targets = new[] { "a" };
            Assert.That(JsonUtility.ToJson(_layout), Is.EqualTo(before));
        }
        /** @brief 缺少任意指定出口也不能供光。 */
        [Test] public void MissingExit_BlocksStartup()
        {
            _layout.Devices[1].Direction = 2;
            _layout.Phases[0].Targets = new[] { "a", "b" };
            Assert.That(new RepairSession(_layout).Powered, Is.False);
        }
        /** @brief 多束光命中同一出口只计一个出口；目标顺序无关，空目标不通过。 */
        [Test] public void ExitSet_UsesUniqueIdsAndExactEquality()
        {
            _layout.Devices[1].Position = new Vector2(4, 5); _layout.Devices[1].Direction = 6;
            _layout.Devices[3].Position = new Vector2(-8, -5);
            var result = RepairPreviewSolver.Trace(_layout, null);
            Assert.That(result.Matches(new[] { "a" }), Is.True);
            Assert.That(result.Matches(new[] { "a", "b" }), Is.False);
            Assert.That(result.Matches(new string[0]), Is.False);
        }
    }
}
