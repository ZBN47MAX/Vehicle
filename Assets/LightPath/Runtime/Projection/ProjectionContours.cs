using System.Collections.Generic;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 标识与凸体快路径共用的二维轮廓工具。 */
    public static class ProjectionContours
    {
        /** @brief 单调链生成逆时针外轮廓，保留圆门的网格圆弧。@param points 平面点 @return 轮廓 */
        public static Vector2[] Hull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var unique = new List<Vector2>();
            foreach (Vector2 point in points) if (unique.Count == 0 || (point - unique[unique.Count - 1]).sqrMagnitude > 1e-10f) unique.Add(point);
            if (unique.Count < 3) return unique.ToArray();
            var hull = new List<Vector2>();
            foreach (Vector2 point in unique) { while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(point); }
            int lower = hull.Count;
            for (int i = unique.Count - 2; i >= 0; i--) { Vector2 point = unique[i]; while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(point); }
            hull.RemoveAt(hull.Count - 1); return hull.ToArray();
        }
        /** @brief 二维叉积。@param a 向量 @param b 向量 @return 有符号面积 */
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        /** @brief 排除自交、重叠边与非相邻接触点，避免错误填充孔洞。@param points 环 @return 是否简单 */
        public static bool IsSimple(IReadOnlyList<Vector2> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Count];
                if ((a - b).sqrMagnitude < 1e-12f) return false;
                for (int j = i + 2; j < points.Count; j++)
                {
                    if ((j + 1) % points.Count == i) continue;
                    Vector2 c = points[j], d = points[(j + 1) % points.Count];
                    float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
                    float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
                    if (abC * abD <= 0 && cdA * cdB <= 0 &&
                        Mathf.Max(a.x, b.x) >= Mathf.Min(c.x, d.x) && Mathf.Max(c.x, d.x) >= Mathf.Min(a.x, b.x) &&
                        Mathf.Max(a.y, b.y) >= Mathf.Min(c.y, d.y) && Mathf.Max(c.y, d.y) >= Mathf.Min(a.y, b.y)) return false;
                }
            }
            return true;
        }
        /** @brief 凸轮廓原样返回；简单凹轮廓耳切为不重叠三角形，显示与碰撞共用。@param polygon 逆时针简单环 @return 凸片集合，失败为空 */
        public static List<Vector2[]> ConvexPieces(Vector2[] polygon)
        {
            var pieces = new List<Vector2[]>();
            bool convex = true;
            for (int i = 0; i < polygon.Length; i++)
                convex &= Cross(polygon[(i + 1) % polygon.Length] - polygon[i], polygon[(i + 2) % polygon.Length] - polygon[(i + 1) % polygon.Length]) >= 0;
            if (convex) { pieces.Add(polygon); return pieces; }
            var ring = new List<Vector2>(polygon);
            while (ring.Count > 3)
            {
                bool removed = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int before = (i + ring.Count - 1) % ring.Count, after = (i + 1) % ring.Count;
                    Vector2 a = ring[before], b = ring[i], c = ring[after];
                    if (Cross(b - a, c - b) <= 1e-10f) continue;
                    bool occupied = false;
                    for (int j = 0; j < ring.Count; j++)
                        if (j != before && j != i && j != after && Cross(b - a, ring[j] - a) >= -1e-10f && Cross(c - b, ring[j] - b) >= -1e-10f && Cross(a - c, ring[j] - c) >= -1e-10f) { occupied = true; break; }
                    if (occupied) continue;
                    pieces.Add(new[] { a, b, c }); ring.RemoveAt(i); removed = true; break;
                }
                if (!removed) return new List<Vector2[]>();
            }
            pieces.Add(ring.ToArray()); return pieces;
        }
        /** @brief 将凸轮廓裁剪至矩形，显示和碰撞共同使用结果。@param input 轮廓 @param bounds 边界 @return 裁剪轮廓 */
        public static Vector2[] Clip(Vector2[] input, Rect bounds)
        {
            var points = new List<Vector2>(input);
            for (int side = 0; side < 4 && points.Count > 0; side++)
            {
                int axis = side / 2; bool minimum = side % 2 == 0;
                float edge = axis == 0 ? (minimum ? bounds.xMin : bounds.xMax) : (minimum ? bounds.yMin : bounds.yMax);
                var output = new List<Vector2>(); Vector2 previous = points[points.Count - 1];
                bool prevInside = minimum ? previous[axis] >= edge : previous[axis] <= edge;
                foreach (Vector2 current in points)
                {
                    bool inside = minimum ? current[axis] >= edge : current[axis] <= edge;
                    if (inside != prevInside) output.Add(Vector2.LerpUnclamped(previous, current, (edge - previous[axis]) / (current[axis] - previous[axis])));
                    if (inside) output.Add(current);
                    previous = current; prevInside = inside;
                }
                points = output;
            }
            return points.ToArray();
        }
        /** @brief 使用分离轴判断两个凸轮廓是否有面积重叠，接触边不算重叠。@param a 轮廓 @param b 轮廓 @return 是否重叠 */
        public static bool Overlaps(Vector2[] a, Vector2[] b)
        {
            if (a.Length < 3 || b.Length < 3) return false;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector2[] polygon = pass == 0 ? a : b;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                    Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                    float amin = float.PositiveInfinity, amax = float.NegativeInfinity, bmin = float.PositiveInfinity, bmax = float.NegativeInfinity;
                    foreach (Vector2 point in a) { float value = Vector2.Dot(point, axis); amin = Mathf.Min(amin, value); amax = Mathf.Max(amax, value); }
                    foreach (Vector2 point in b) { float value = Vector2.Dot(point, axis); bmin = Mathf.Min(bmin, value); bmax = Mathf.Max(bmax, value); }
                    if (amax <= bmin + .000001f || bmax <= amin + .000001f) return false;
                }
            }
            return true;
        }
        /** @brief 圆形碰撞标记按真实变换获得圆弧，避免包围方框。@param circle 圆碰撞 @return 世界轮廓 */
        public static Vector3[] Circle(CircleCollider2D circle)
        {
            var points = new Vector3[48];
            for (int i = 0; i < points.Length; i++) { float angle = i * Mathf.PI * 2 / points.Length; points[i] = circle.transform.TransformPoint(circle.offset + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * circle.radius); }
            return points;
        }
    }

}
