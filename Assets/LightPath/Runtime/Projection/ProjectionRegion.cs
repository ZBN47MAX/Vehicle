using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 保存一条颜色一致的投影区域。 */
    public readonly struct ProjectionRegion
    {
        /** @brief 区域边界。 */
        public readonly Bounds Bounds;
        /** @brief 连续逻辑色。 */
        public readonly Color Color;
        /** @brief 是否具有颜色。 */
        public readonly bool Colored;
        /** @brief 传送源引用。 */
        public readonly ProjectionSource Portal;
        /** @brief 世界XY轮廓，空值兼容旧矩形区域。 */
        public readonly Vector2[] Outline;
        /** @brief 是否存在投影。 */
        public readonly bool Exists;
        /** @brief 创建区域。 @param bounds 边界 @param color 色彩 @param colored 有色 @param portal 传送源 @param exists 是否存在 */
        public ProjectionRegion(Bounds bounds, Color color, bool colored, ProjectionSource portal, bool exists, Vector2[] outline = null)
        {
            Outline = outline;
            Bounds = bounds;
            Color = color;
            Colored = colored;
            Portal = portal;
            Exists = exists;
        }
        /** @brief 比较可合并属性。 @param other 另一区域 @return 是否相同 */
        public bool SameAs(ProjectionRegion other) => Exists == other.Exists && Colored == other.Colored && Color == other.Color && Portal == other.Portal;
        /** @brief 检查角色矩形是否穿入区域。 @param body 角色边界 @return 是否穿入 */
        public bool Contains(Bounds body)
        {
            if (!(body.min.x < Bounds.max.x - .015f && body.max.x > Bounds.min.x + .015f && body.min.y < Bounds.max.y - .015f && body.max.y > Bounds.min.y + .015f))
                return false;
            if (Outline == null)
                return true;
            Vector2 min = (Vector2)body.min + Vector2.one * .015f, max = (Vector2)body.max - Vector2.one * .015f;
            Vector2[] rectangle = { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y) };
            for (int i = 0; i < Outline.Length; i++)
            {
                Vector2 edge = Outline[(i + 1) % Outline.Length] - Outline[i];
                Vector2 axis = new Vector2(-edge.y, edge.x);
                float aMin = float.PositiveInfinity, aMax = float.NegativeInfinity, bMin = float.PositiveInfinity, bMax = float.NegativeInfinity;
                foreach (Vector2 point in Outline)
                {
                    float value = Vector2.Dot(point, axis);
                    aMin = Mathf.Min(aMin, value);
                    aMax = Mathf.Max(aMax, value);
                }
                foreach (Vector2 point in rectangle)
                {
                    float value = Vector2.Dot(point, axis);
                    bMin = Mathf.Min(bMin, value);
                    bMax = Mathf.Max(bMax, value);
                }
                if (aMax <= bMin || bMax <= aMin)
                    return false;
            }
            return true;
        }
    }
}
