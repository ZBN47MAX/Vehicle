using System.Collections.Generic;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 围绕透镜中心跟踪指针角位移，运行与编辑器试玩共用，不改变按下瞬间角度。 */
    public struct RepairLensDrag
    {
        /** @brief 上一有效平面指针位置。 */
        private Vector2 _previous;
        /** @brief 是否持有有效拖动起点。 */
        private bool _tracking;
        /** @brief 开始拖动并保留原角度。@param point 平面指针 @param valid 平面命中是否有效 */
        public void Begin(Vector2 point, bool valid = true) { _previous = point; _tracking = valid; }
        /** @brief 按节点中心的指针角位移调整，穿过中心或失去平面命中时重新锚定，避免翻转。@param center 节点中心 @param point 指针 @param valid 有效平面命中 @param angle 当前透镜角 @param radius 中心死区半径 @param sensitivity 角位移倍率 @return 未裁剪的新角度 */
        public float Step(Vector2 center, Vector2 point, bool valid, float angle, float radius, float sensitivity)
        {
            if (!valid) { _tracking = false; return angle; }
            Vector2 previous = _previous;
            _previous = point;
            if (!_tracking) { _tracking = true; return angle; }
            Vector2 from = previous - center, to = point - center;
            Vector2 segment = to - from;
            float t = Mathf.Clamp01(-Vector2.Dot(from, segment) / Mathf.Max(segment.sqrMagnitude, .000001f));
            if ((from + segment * t).sqrMagnitude <= radius * radius) return angle;
            float before = Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg;
            float after = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            return angle + Mathf.DeltaAngle(before, after) * sensitivity;
        }
    }
    /** @brief 八向折镜与连续透镜共用的光路查询，包含分支、最近节点和循环保护。 */
    public static class RepairPreviewSolver
    {
        /** @brief 光线段。 */
        public struct Segment { public Vector2 From; public Vector2 To; }
        /** @brief 本次光路结果。 */
        public sealed class Result
        {
            /** @brief 可绘制光段。 */
            public readonly List<Segment> Segments = new List<Segment>();
            /** @brief 已命中出口。 */
            public readonly HashSet<string> Lit = new HashSet<string>();
            /** @brief 是否遇到循环或合流。 */
            public bool Repeated;
            /** @brief 只有指定出口全部且仅这些出口被点亮才接通；空目标不可供光。 */
            public bool Matches(string[] targets) => targets != null && targets.Length > 0 && Lit.SetEquals(targets);
        }
        /** @brief 下一合法朝向，单向循环。 @param direction 当前朝向 @param mask 允许位 @return 下一朝向 */
        public static int Next(int direction, int mask)
        {
            for (int step = 1; step <= 8; step++)
            {
                int next = (direction + step) % 8;
                if ((mask & (1 << next)) != 0) return next;
            }
            return direction;
        }
        /** @brief 将目标角限制到透镜自身基准方向附近的允许区间。 */
        public static float ClampLensAngle(RepairLayout.Device device, float degrees) => device.Direction * 45f
            + Mathf.Clamp(Mathf.DeltaAngle(device.Direction * 45f, degrees), device.AngleRange.x, device.AngleRange.y);
        /** @brief 指针到可见调角线的距离。 @param point 指针 @param center 起点 @param angle 角度 @param length 长度 @return 最短距离 */
        public static float AngleLineDistance(Vector2 point, Vector2 center, float angle, float length)
        {
            Vector2 line = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * length;
            float amount = Mathf.Clamp01(Vector2.Dot(point - center, line) / Mathf.Max(.000001f, line.sqrMagnitude));
            return Vector2.Distance(point, center + line * amount);
        }
        /** @brief 求射线首次接触节点方框的位置，包含擦边。 @param start 起点 @param direction 单位方向 @param center 节点中心 @param radius 半宽 @param distance 接触距离 @return 是否相交 */
        public static bool HitNode(Vector2 start, Vector2 direction, Vector2 center, float radius, out float distance)
        {
            float near = 0, far = float.PositiveInfinity;
            for (int axis = 0; axis < 2; axis++)
            {
                float offset = start[axis] - center[axis];
                if (Mathf.Abs(direction[axis]) < .000001f)
                {
                    if (Mathf.Abs(offset) > radius + .000001f) { distance = 0; return false; }
                    continue;
                }
                float a = (-radius - offset) / direction[axis];
                float b = (radius - offset) / direction[axis];
                near = Mathf.Max(near, Mathf.Min(a, b));
                far = Mathf.Min(far, Mathf.Max(a, b));
            }
            distance = near;
            return far >= near && far > .0001f;
        }
        /** @brief 根据布局与试玩朝向追踪光路。 @param layout 配置 @param directions 可选试玩朝向 @return 光路 */
        public static Result Trace(RepairLayout layout, int[] directions)
        {
            float[] angles = null;
            if (directions != null)
            {
                angles = new float[directions.Length];
                for (int i = 0; i < angles.Length; i++) angles[i] = directions[i] * 45f + (layout.Devices[i].Type == RepairLayout.Kind.Lens ? layout.Devices[i].InitialAngle : 0);
            }
            return TraceAngles(layout, angles);
        }
        /** @brief 用连续实际出射角追踪；折镜的八向约束由配置与会话保证。@param layout 布局 @param angles 各节点实际角度或空 @return 光路结果 */
        public static Result TraceAngles(RepairLayout layout, float[] angles)
        {
            var result = new Result();
            var queue = new Queue<(int node, float direction)>();
            var visited = new HashSet<(int, float)>();
            for (int i = 0; i < layout.Devices.Length; i++)
                if (layout.Devices[i].Type == RepairLayout.Kind.Source) queue.Enqueue((i, angles == null ? layout.Devices[i].OutputAngle : angles[i]));
            while (queue.Count > 0 && result.Segments.Count < 1024)
            {
                var ray = queue.Dequeue();
                if (!visited.Add(ray)) { result.Repeated = true; continue; }
                Vector2 start = layout.Devices[ray.node].Position;
                float radians = ray.direction * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                float distance = 1000;
                Rect area = layout.Area;
                if (direction.x > .0001f) distance = Mathf.Min(distance, (area.xMax - start.x) / direction.x);
                if (direction.x < -.0001f) distance = Mathf.Min(distance, (area.xMin - start.x) / direction.x);
                if (direction.y > .0001f) distance = Mathf.Min(distance, (area.yMax - start.y) / direction.y);
                if (direction.y < -.0001f) distance = Mathf.Min(distance, (area.yMin - start.y) / direction.y);
                int hit = -1;
                for (int i = 0; i < layout.Devices.Length; i++)
                {
                    if (i == ray.node) continue;
                    if (HitNode(start, direction, layout.Devices[i].Position, layout.NodeRadius, out float along) && along < distance)
                    { hit = i; distance = along; }
                }
                result.Segments.Add(new Segment { From = start, To = start + direction * Mathf.Max(0, distance) });
                if (hit < 0) continue;
                RepairLayout.Device device = layout.Devices[hit];
                float facing = angles == null ? device.OutputAngle : angles[hit];
                if (device.Type == RepairLayout.Kind.Exit) { result.Lit.Add(device.Id); continue; }
                if (device.Type == RepairLayout.Kind.Source) continue;
                float output = Mathf.Repeat(facing, 360);
                queue.Enqueue((hit, output));
                if (device.Type == RepairLayout.Kind.Splitter) queue.Enqueue((hit, Mathf.Repeat(output + 45, 360)));
            }
            return result;
        }
    }
}
