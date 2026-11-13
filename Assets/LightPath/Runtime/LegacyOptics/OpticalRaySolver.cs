using System.Collections.Generic;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 无场景依赖的有限线段光路求解器，按最近命中追踪。 */
    public static class OpticalRaySolver
    {
        /** @brief 一处光学界面的瞬时几何与参数。 */
        public struct Node
        {
            /** @brief 线段中心。 */
            public Vector2 Center;
            /** @brief 指向正面介质的单位法线。 */
            public Vector2 Normal;
            /** @brief 有效半长。 */
            public float HalfLength;
            /** @brief 响应规则。 */
            public OpticalNodeDefinition.Response Kind;
            /** @brief 固定偏转角。 */
            public float Angle;
            /** @brief 正面介质折射率。 */
            public float FrontIndex;
            /** @brief 背面介质折射率。 */
            public float BackIndex;
        }
        /** @brief 求射线与有限镜面交点。 @param origin 起点 @param direction 单位方向 @param node 节点 @param distance 正向距离 @return 是否命中 */
        public static bool Intersect(Vector2 origin, Vector2 direction, Node node, out float distance)
        {
            float denominator = Vector2.Dot(direction, node.Normal);
            distance = 0;
            if (Mathf.Abs(denominator) < .00001f)
                return false;
            distance = Vector2.Dot(node.Center - origin, node.Normal) / denominator;
            return distance > .0001f && Vector2.Distance(origin + direction * distance, node.Center) <= node.HalfLength;
        }
        /** @brief 斯涅尔方向，全反射返回零。 @param direction 入射方向 @param normal 入射侧法线 @param ratio 入射比出射折射率 @return 折射方向 */
        public static Vector2 Refract(Vector2 direction, Vector2 normal, float ratio)
        {
            direction.Normalize();
            normal.Normalize();
            if (Vector2.Dot(direction, normal) > 0)
                normal = -normal;
            float cosine = -Vector2.Dot(direction, normal);
            float k = 1 - ratio * ratio * (1 - cosine * cosine);
            return k < 0 ? Vector2.zero : (ratio * direction + (ratio * cosine - Mathf.Sqrt(k)) * normal).normalized;
        }
        /** @brief 应用单节点规则。 @param direction 入射 @param node 节点 @return 出射，阻挡返回零 */
        public static Vector2 Respond(Vector2 direction, Node node)
        {
            bool front = Vector2.Dot(direction, node.Normal) < 0;
            switch (node.Kind)
            {
                case OpticalNodeDefinition.Response.Block:
                    return Vector2.zero;
                case OpticalNodeDefinition.Response.Reflect:
                    return Vector2.Reflect(direction, node.Normal).normalized;
                case OpticalNodeDefinition.Response.Deflect:
                    return (Vector2)(Quaternion.Euler(0, 0, front ? node.Angle : -node.Angle) * direction);
                default:
                    float ratio = front ? node.FrontIndex / node.BackIndex : node.BackIndex / node.FrontIndex;
                    Vector2 bent = Refract(direction, node.Normal, ratio);
                    return bent == Vector2.zero ? Vector2.Reflect(direction, node.Normal).normalized : bent;
            }
        }
        /** @brief 求解光路并保存折点。 @param start 起点 @param direction 初始方向 @param target 接收中心 @param radius 接收半径 @param nodes 节点集合 @param maxSegments 最大段数 @param maxDistance 总长度上限 @param points 输出路径 @return 是否连接 */
        public static bool Trace(Vector2 start, Vector2 direction, Vector2 target, float radius, IReadOnlyList<Node> nodes, int maxSegments, float maxDistance, List<Vector2> points)
        {
            points.Clear();
            points.Add(start);
            direction.Normalize();
            if (direction.sqrMagnitude < .5f)
                return false;
            Vector2 current = start;
            for (int segment = 0; segment < maxSegments && maxDistance > .001f; segment++)
            {
                float nearest = maxDistance;
                int index = -1;
                for (int i = 0; i < nodes.Count; i++)
                    if (Intersect(current, direction, nodes[i], out float distance) && distance < nearest)
                    {
                        nearest = distance;
                        index = i;
                    }
                Vector2 toTarget = target - current;
                float along = Vector2.Dot(toTarget, direction);
                float perpendicular = toTarget.sqrMagnitude - along * along;
                if (along >= 0 && perpendicular <= radius * radius)
                {
                    float entry = Mathf.Max(0, along - Mathf.Sqrt(Mathf.Max(0, radius * radius - perpendicular)));
                    if (entry < nearest)
                    {
                        points.Add(current + direction * entry);
                        return true;
                    }
                }
                current += direction * nearest;
                points.Add(current);
                maxDistance -= nearest;
                if (index < 0)
                    return false;
                direction = Respond(direction, nodes[index]);
                if (direction.sqrMagnitude < .5f)
                    return false;
                current += direction * .001f;
                maxDistance -= .001f;
            }
            return false;
        }
    }
}
