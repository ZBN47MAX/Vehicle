using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 验证八向折镜、连续透镜、循环和会话状态，不组装场景。 */
    public sealed class RepairAngleTests
    {
        /** @brief 接触边框即可命中，越过边框则不命中。 */
        [Test] public void NodeBoundary_IncludesTangentAndExcludesOutside()
        {
            Assert.That(RepairPreviewSolver.HitNode(new Vector2(-2, .45f), Vector2.right, Vector2.zero, .45f, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(1.55f).Within(.0001f));
            Assert.That(RepairPreviewSolver.HitNode(new Vector2(-2, .451f), Vector2.right, Vector2.zero, .45f, out _), Is.False);
        }
        /** @brief 独立内存布局。 */
        private RepairLayout _layout;
        /** @brief 创建光源、透镜和12度目标，命中容差很小以排除八向近似。 */
        [SetUp] public void SetUp()
        {
            _layout = ScriptableObject.CreateInstance<RepairLayout>();
            _layout.RandomPhaseOrder = false;
            _layout.HitRadius = .01f;
            _layout.Devices = new[] {
                new RepairLayout.Device { Id = "s", Type = RepairLayout.Kind.Source, Position = new Vector2(-4, 0) },
                new RepairLayout.Device { Id = "n", Type = RepairLayout.Kind.Lens },
                new RepairLayout.Device { Id = "t", Type = RepairLayout.Kind.Exit, Position = Unit(12) * 4 } };
            _layout.Phases = new[] { new RepairLayout.Phase { Targets = new[] { "t" } } };
        }
        /** @brief 释放临时SO。 */
        [TearDown] public void TearDown() => Object.DestroyImmediate(_layout);
        /** @brief 获取平面单位向量。 */
        private static Vector2 Unit(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        /** @brief 12度可精确接通且不修改SO，越界钳制，不接受离散换档或非法数值。 */
        [Test] public void Lens_ContinuousRepairClampsAndPreservesAsset()
        {
            string before = JsonUtility.ToJson(_layout);
            var session = new RepairSession(_layout);
            session.Tick(1000); Assert.That(session.Powered, Is.False);
            Assert.That(session.Rotate(1), Is.False);
            Assert.That(session.SetLensAngle(1, 12), Is.True);
            Assert.That(session.Angle(1), Is.EqualTo(12).Within(.0001f));
            Assert.That(session.Powered, Is.True); Assert.That(session.RepairCount, Is.EqualTo(1));
            Assert.That(session.Result.Segments.Count, Is.EqualTo(2));
            session.SetLensAngle(1, 85); Assert.That(session.Angle(1), Is.EqualTo(30));
            session.SetLensAngle(1, -85); Assert.That(session.Angle(1), Is.EqualTo(-30));
            Assert.That(session.SetLensAngle(1, float.NaN), Is.False);
            Assert.That(session.SetLensAngle(0, 12), Is.False);
            Assert.That(JsonUtility.ToJson(_layout), Is.EqualTo(before));
        }
        /** @brief 镜面指向就是输出方向，不再用反射角公式翻倍。 */
        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7)]
        public void Mirror_OutputsSelectedCompassDirection(int direction)
        {
            _layout.Devices[0].Direction = (direction + 2) % 8;
            _layout.Devices[0].Position = -Unit(_layout.Devices[0].Direction * 45) * 4;
            _layout.Devices[1].Type = RepairLayout.Kind.Mirror;
            _layout.Devices[1].Direction = direction;
            _layout.Devices[2].Position = Unit(direction * 45) * 3;
            var result = RepairPreviewSolver.Trace(_layout, null);
            Assert.That(result.Lit.Contains("t"), Is.True);
            Assert.That(result.Segments.Count, Is.EqualTo(2));
        }
        /** @brief 透镜不自行发光；未被照到时只有光源射线。 */
        [Test] public void Lens_RequiresIncidentLight()
        {
            _layout.Devices[1].Position = new Vector2(0, 2);
            Assert.That(RepairPreviewSolver.Trace(_layout, null).Segments.Count, Is.EqualTo(1));
        }
        /** @brief 自身朝向可跨零角，透镜不使用八向允许掩码。 */
        [Test] public void Lens_LocalRangeWrapsAndIgnoresDiscreteMask()
        {
            var lens = _layout.Devices[1]; lens.Direction = 7; lens.AngleRange = new Vector2(-20, 70); lens.Allowed = 0;
            var session = new RepairSession(_layout);
            session.SetLensAngle(1, 5); Assert.That(session.Angle(1), Is.EqualTo(365).Within(.0001f));
            lens.InitialAngle = 80; Assert.That(_layout.TryValidate(out _), Is.False);
            lens.InitialAngle = 0; lens.AngleRange.x = float.NaN; Assert.That(_layout.TryValidate(out _), Is.False);
        }
        /** @brief 互相回射的折镜有界终止。 */
        [Test] public void MirrorLoop_IsDetected()
        {
            _layout.Devices[1].Type = RepairLayout.Kind.Mirror; _layout.Devices[1].Direction = 2;
            _layout.Devices[2].Type = RepairLayout.Kind.Mirror; _layout.Devices[2].Direction = 6; _layout.Devices[2].Position = new Vector2(0, 3);
            var result = RepairPreviewSolver.Trace(_layout, null);
            Assert.That(result.Repeated, Is.True); Assert.That(result.Segments.Count, Is.EqualTo(3));
        }
    }
}
