using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 多光源、启动供光与严格目标集合的纯数据回归。 */
    public sealed class RepairPowerTests
    {
        /** @brief 开局限定次数包含首阶段，预告在边界使用正确阶段池，新局重置次数。@param count 限定阶段次数 */
        [TestCase(1), TestCase(3), TestCase(8)]
        public void InitialPhases_RestrictThenUseFullPool(int count)
        {
            _layout.RandomPhaseOrder = true;
            _layout.InitialPhaseCount = count;
            foreach (RepairLayout.Phase phase in _layout.Phases) phase.Targets = new[] { "a", "b" };
            _layout.Phases[1].AllowInitially = true;
            string before = JsonUtility.ToJson(_layout);
            var session = new RepairSession(_layout, (min, max) => min);
            for (int occurrence = 1; occurrence <= count; occurrence++)
            {
                Assert.That(session.PhaseIndex, Is.EqualTo(1));
                int expectedNext = occurrence < count ? 1 : 0;
                Assert.That(session.NextPhaseIndex, Is.EqualTo(expectedNext));
                Assert.That(session.NextPhaseIndex, Is.EqualTo(expectedNext));
                session.Tick(1000);
            }
            Assert.That(session.PhaseIndex, Is.Zero);
            Assert.That(JsonUtility.ToJson(_layout), Is.EqualTo(before));
            Assert.That(new RepairSession(_layout, (min, max) => min).PhaseIndex, Is.EqualTo(1));
        }
        /** @brief 限定次数及阶段池校验，顺序模式继续遵循索引。 */
        [Test] public void InitialPhases_ValidateSelectionAndSequentialMode()
        {
            _layout.RandomPhaseOrder = true;
            _layout.InitialPhaseCount = 2;
            Assert.That(_layout.TryValidate(out _), Is.False);
            _layout.Phases[1].AllowInitially = true;
            _layout.Phases[1].Weight = 0;
            Assert.That(_layout.TryValidate(out _), Is.False);
            _layout.Phases[1].Weight = 1;
            Assert.That(_layout.TryValidate(out _), Is.True);
            _layout.InitialPhaseCount = -1;
            Assert.That(_layout.TryValidate(out _), Is.False);
            _layout.InitialPhaseCount = 2;
            _layout.RandomPhaseOrder = false;
            Assert.That(new RepairSession(_layout).PhaseIndex, Is.Zero);
        }
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
        /** @brief 独立加权抽取遵守区间边界，权重0不出现，预告稳定且配置只读。@param sample 单位采样 @param expected 预期阶段 */
        [TestCase(0f, 1), TestCase(.249f, 1), TestCase(.25f, 2), TestCase(1f, 2)]
        public void RandomPhases_UseWeights(float sample, int expected)
        {
            _layout.RandomPhaseOrder = true;
            _layout.Phases = new[]
            {
                new RepairLayout.Phase { Weight = 0, Targets = new[] { "a", "b" } },
                new RepairLayout.Phase { Weight = 1, Targets = new[] { "a", "b" } },
                new RepairLayout.Phase { Weight = 3, Targets = new[] { "a", "b" } }
            };
            string before = JsonUtility.ToJson(_layout);
            var session = new RepairSession(_layout, (min, max) => min == 0 ? sample : min);
            Assert.That(session.PhaseIndex, Is.Zero);
            for (int i = 0; i < 10; i++)
            {
                Assert.That(session.NextPhaseIndex, Is.EqualTo(expected));
                Assert.That(session.NextPhaseIndex, Is.EqualTo(expected));
                session.Tick(1000);
                Assert.That(session.PhaseIndex, Is.EqualTo(expected));
            }
            Assert.That(JsonUtility.ToJson(_layout), Is.EqualTo(before));
        }
        /** @brief 不同阶段数量与随机种子都从0开始，单阶段保持0。@param count 阶段数量 */
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
                for (int i = 1; i < count; i++)
                {
                    int next = session.NextPhaseIndex;
                    Assert.That(next, Is.InRange(0, count - 1));
                    session.Tick(1000);
                    Assert.That(session.PhaseIndex, Is.EqualTo(next));
                }
                session.Tick(1000);
                if (count == 1) Assert.That(session.PhaseIndex, Is.Zero);
            }
        }
        /** @brief 权重必须有限非负，随机模式全部为0时拒绝启动。@param weight 待验证权重 */
        [TestCase(-1f), TestCase(float.NaN), TestCase(float.PositiveInfinity), TestCase(0f)]
        public void RandomPhases_RejectInvalidWeights(float weight)
        {
            _layout.RandomPhaseOrder = true;
            foreach (RepairLayout.Phase phase in _layout.Phases) phase.Weight = weight;
            Assert.That(_layout.TryValidate(out _), Is.False);
        }
        /** @brief 两个光源在同一节点合流后，全部下游光段带合流标记，两个入射段保留来源。 */
        [Test] public void MultipleSources_ColorMergedOutputAndUnexpectedExit()
        {
            _layout.Devices = new[]
            {
                new RepairLayout.Device { Id = "s1", Type = RepairLayout.Kind.Source, Position = new Vector2(-4, 0) },
                new RepairLayout.Device { Id = "s2", Type = RepairLayout.Kind.Source, Position = new Vector2(0, -4), Direction = 2 },
                new RepairLayout.Device { Id = "m", Type = RepairLayout.Kind.Splitter, Position = Vector2.zero },
                new RepairLayout.Device { Id = "a", Type = RepairLayout.Kind.Exit, Position = new Vector2(4, 0) }
            };
            var result = RepairPreviewSolver.TraceAngles(_layout, null);
            Assert.That(result.Segments.FindAll(segment => segment.From == Vector2.zero).Count, Is.EqualTo(2));
            foreach (RepairPreviewSolver.Segment segment in result.Segments)
            {
                if (segment.From == Vector2.zero)
                    Assert.That(segment.SourceIndex, Is.EqualTo(-1));
                else
                    Assert.That(segment.SourceIndex, Is.GreaterThanOrEqualTo(0));
            }
            Assert.That(_layout.BeamColor(0), Is.Not.EqualTo(_layout.BeamColor(1)));
            Assert.That(_layout.BeamColor(-1), Is.EqualTo(_layout.MergedBeamColor));
            Assert.That(_layout.NodeColor(_layout.Devices[3], result, false), Is.EqualTo(_layout.UnexpectedExitColor));
            Assert.That(_layout.NodeColor(_layout.Devices[3], result, true), Is.EqualTo(Color.green));
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
