using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 色表匹配规则回归。 */
    public sealed class PaletteRulesTests
    {
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
