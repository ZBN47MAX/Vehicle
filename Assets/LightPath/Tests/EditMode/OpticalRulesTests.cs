using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 连续光学兼容代码仍被房间引用，保留独立边界回归。 */
    public sealed class OpticalRulesTests
    {
        /** @brief 固定偏转、真实反射与吸收具有不同结果。@param response 响应 @param reaches 是否命中上方出口 */
        [TestCase(OpticalNodeDefinition.Response.Deflect, true), TestCase(OpticalNodeDefinition.Response.Reflect, true), TestCase(OpticalNodeDefinition.Response.Block, false)]
        public void OpticalResponse_HasExpectedPath(OpticalNodeDefinition.Response response, bool reaches)
        {
            var node = new OpticalRaySolver.Node { Center = new Vector2(2, 0), Normal = new Vector2(-1, 1).normalized, HalfLength = 1, Kind = response, Angle = 90, FrontIndex = 1, BackIndex = 1.5f };
            Assert.That(OpticalRaySolver.Trace(Vector2.zero, Vector2.right, new Vector2(2, 4), .1f, new[] { node }, 32, 20, new List<Vector2>()), Is.EqualTo(reaches));
        }
        /** @brief 连续折射遵循介质方向而非八向吸附。 */
        [Test] public void Refraction_ReachesNonOrthogonalTarget()
        {
            var node = new OpticalRaySolver.Node { Center = new Vector2(2, 0), Normal = new Vector2(-1, 1).normalized, HalfLength = 1, Kind = OpticalNodeDefinition.Response.Refract, FrontIndex = 1, BackIndex = 1.5f };
            var direction = OpticalRaySolver.Respond(Vector2.right, node);
            Assert.That(direction.x, Is.GreaterThan(0)); Assert.That(direction.y, Is.LessThan(0));
            Assert.That(OpticalRaySolver.Trace(Vector2.zero, Vector2.right, node.Center + direction * 4, .05f, new[] { node }, 32, 20, new List<Vector2>()), Is.True);
        }
        /** @brief 镜面环路受光段预算限制，不会无限执行。 */
        [Test] public void MirrorLoop_IsBounded()
        {
            var nodes = new[] {
                new OpticalRaySolver.Node { Center = new Vector2(2, 0), Normal = Vector2.left, HalfLength = 1, Kind = OpticalNodeDefinition.Response.Reflect },
                new OpticalRaySolver.Node { Center = new Vector2(-2, 0), Normal = Vector2.right, HalfLength = 1, Kind = OpticalNodeDefinition.Response.Reflect } };
            var path = new List<Vector2>(); Assert.That(OpticalRaySolver.Trace(Vector2.zero, Vector2.right, new Vector2(0, 4), .1f, nodes, 8, 100, path), Is.False); Assert.That(path.Count, Is.LessThanOrEqualTo(9));
        }
        /** @brief 色彩匹配使用配置颜色，非法索引不视作相同滤镜。@param index 有效滤镜号 */
        [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
        public void Palette_MatchesOwnColorOnly(int index)
        {
            var config = ScriptableObject.CreateInstance<LightGameConfig>();
            try { Assert.That(config.Matches(config.GetColor(index), index), Is.True); Assert.That(config.Matches(config.GetColor(index), -1), Is.False); }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
