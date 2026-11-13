using System.Collections.Generic;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 在投影仪顶面的XY坐标系绘制维修网格，不生成场景对象。 */
    public sealed class RepairStageMesh : System.IDisposable
    {
        /** @brief 运行时拥有的网格。 */
        private readonly Mesh _mesh = new Mesh { name = "Repair Stage Runtime" };
        /** @brief 顶点缓冲。 */
        private readonly List<Vector3> _vertices = new List<Vector3>();
        /** @brief 索引缓冲。 */
        private readonly List<int> _indices = new List<int>();
        /** @brief 顶点颜色。 */
        private readonly List<Color> _colors = new List<Color>();
        /** @brief 最近绘制的结果身份，用于跳过未变化的网格。 */
        private RepairPreviewSolver.Result _last;
        /** @brief 最近目标阶段。 */
        private int _phase = -1;
        /** @brief 最近预警阶段标记。 */
        private bool _warning;
        /** @brief 根据最新结果重建并提交顶面绘制。@param layout 布局 @param session 状态 @param config 表现参数 @param frame 顶面坐标系 @param material 顶点色材质 @param camera 演示相机 */
        public void Draw(RepairLayout layout, RepairSession session, LightStageConfig config, Transform frame, Material material, Camera camera)
        {
            if (_last != session.Result || _phase != session.PhaseIndex || _warning != session.Warning)
            {
                _last = session.Result;
                _phase = session.PhaseIndex;
                _warning = session.Warning;
                _vertices.Clear(); _indices.Clear(); _colors.Clear();
                foreach (var node in layout.Devices) AddDirections(node, config);
                foreach (RepairPreviewSolver.Segment segment in session.Result.Segments)
                    AddLine(segment.From, segment.To, config.BeamWidth, Color.yellow);
                for (int i = 0; i < layout.Devices.Length; i++)
                {
                    RepairLayout.Device node = layout.Devices[i];
                    if (session.IsTarget(node.Id, session.Warning))
                        AddSquare(node.Position, config.NodeRadius * 1.4f, new Color(1, .5f, .1f));
                    Color color = session.Result.Lit.Contains(node.Id) ? Color.green : node.Type == RepairLayout.Kind.Source ? Color.yellow : Color.cyan;
                    if (node.Type == RepairLayout.Kind.Lens)
                    {
                        Vector2 a = Unit(node.Direction * 45) * config.NodeRadius;
                        Vector2 side = new Vector2(-a.y, a.x);
                        AddQuad(node.Position + a, node.Position - a + side, node.Position - a - side, node.Position + a, color);
                    }
                    else if (node.Type == RepairLayout.Kind.Mirror)
                    {
                        for (int edge = 0; edge < 8; edge++)
                            AddQuad(node.Position, node.Position + Unit(edge * 45 + 22.5f) * config.NodeRadius,
                                node.Position + Unit((edge + 1) * 45 + 22.5f) * config.NodeRadius, node.Position, color);
                    }
                    else AddSquare(node.Position, config.NodeRadius, color);
                    if (node.Type != RepairLayout.Kind.Exit)
                    {
                        float angle = session.Angle(i) * Mathf.Deg2Rad;
                        AddLine(node.Position, node.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * config.NodeRadius * 2, config.BeamWidth, Color.white);
                    }
                }
                _mesh.Clear();
                _mesh.SetVertices(_vertices); _mesh.SetColors(_colors); _mesh.SetTriangles(_indices, 0);
                _mesh.RecalculateBounds();
            }
            float scale = Scale(layout, config);
            Matrix4x4 matrix = Matrix4x4.TRS(frame.position, frame.rotation, Vector3.one * scale)
                * Matrix4x4.Translate(new Vector3(-layout.Area.center.x, -layout.Area.center.y, 0));
            Graphics.DrawMesh(_mesh, matrix, material, config.DisplayLayer, camera, 0, null,
                UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
        /** @brief 将角度转换为单位方向。 */
        private static Vector2 Unit(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        /** @brief 绘制短虚线与范围弧，仅表示可选方向，不参与光路计算。 */
        private void AddDirections(RepairLayout.Device node, LightStageConfig config)
        {
            if (node.Type != RepairLayout.Kind.Mirror && node.Type != RepairLayout.Kind.Lens) return;
            Color color = new Color(.35f, .45f, .5f);
            int count = node.Type == RepairLayout.Kind.Mirror ? 8 : 5;
            for (int i = 0; i < count; i++)
            {
                if (node.Type == RepairLayout.Kind.Mirror && (node.Allowed & (1 << i)) == 0) continue;
                float angle = node.Type == RepairLayout.Kind.Mirror ? i * 45 : node.Direction * 45 + Mathf.Lerp(node.AngleRange.x, node.AngleRange.y, i / 4f);
                Vector2 direction = Unit(angle) * config.NodeRadius;
                for (float t = 1.5f; t < 3.5f; t += .6f)
                    AddLine(node.Position + direction * t, node.Position + direction * (t + .3f), config.BeamWidth * .5f, color);
            }
            if (node.Type != RepairLayout.Kind.Lens) return;
            for (int i = 0; i < 24; i++)
                AddLine(node.Position + Unit(node.Direction * 45 + Mathf.Lerp(node.AngleRange.x, node.AngleRange.y, i / 24f)) * config.NodeRadius * 3.5f,
                    node.Position + Unit(node.Direction * 45 + Mathf.Lerp(node.AngleRange.x, node.AngleRange.y, (i + 1) / 24f)) * config.NodeRadius * 3.5f, config.BeamWidth * .5f, color);
        }
        /** @brief 获取包含边界节点的等比缩放。@param layout 布局 @param config 参数 @return 世界缩放 */
        private static float Scale(RepairLayout layout, LightStageConfig config) => Mathf.Min(config.RepairSize.x / (layout.Area.width + config.NodeRadius * 4), config.RepairSize.y / (layout.Area.height + config.NodeRadius * 4));
        /** @brief 在顶面射线命中点寻找可旋转节点。@param layout 布局 @param config 参数 @param frame 顶面坐标系 @param ray 世界射线 @return 索引或负一 */
        public static int Pick(RepairLayout layout, LightStageConfig config, Transform frame, Ray ray)
        {
            if (!TryPoint(layout, config, frame, ray, out Vector2 point)) return -1;
            float nearest = config.NodeRadius * 1.8f;
            int result = -1;
            for (int i = 0; i < layout.Devices.Length; i++)
            {
                RepairLayout.Device node = layout.Devices[i];
                if (node.Type == RepairLayout.Kind.Source || node.Type == RepairLayout.Kind.Exit) continue;
                float delta = Vector2.Distance(point, node.Position);
                if (delta < nearest) { nearest = delta; result = i; }
            }
            return result;
        }
        /** @brief 将指针射线映射到维修配置平面，拖出节点后仍可瞄准。 */
        public static bool TryPoint(RepairLayout layout, LightStageConfig config, Transform frame, Ray ray, out Vector2 point)
        {
            point = default;
            if (!new Plane(frame.forward, frame.position).Raycast(ray, out float distance))
                return false;
            Vector3 local = Quaternion.Inverse(frame.rotation) * (ray.GetPoint(distance) - frame.position);
            point = (Vector2)local / Scale(layout, config) + layout.Area.center;
            return true;
        }
        /** @brief 添加方块。@param center 中心 @param radius 半宽 @param color 色彩 */
        private void AddSquare(Vector2 center, float radius, Color color)
        {
            AddQuad(center + new Vector2(-radius, -radius), center + new Vector2(radius, -radius), center + new Vector2(radius, radius), center + new Vector2(-radius, radius), color);
        }
        /** @brief 添加具有宽度的线段。@param a 起点 @param b 终点 @param width 宽度 @param color 色彩 */
        private void AddLine(Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 d = (b - a).normalized;
            Vector2 side = new Vector2(-d.y, d.x) * width / 2;
            AddQuad(a - side, b - side, b + side, a + side, color);
        }
        /** @brief 添加双三角形并以微小深度区分覆盖顺序。@param a 第一角 @param b 第二角 @param c 第三角 @param d 第四角 @param color 颜色 */
        private void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int index = _vertices.Count;
            float z = -index * .00001f;
            foreach (Vector2 p in new[] { a, b, c, d }) { _vertices.Add(new Vector3(p.x, p.y, z)); _colors.Add(color); }
            _indices.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
        }
        /** @brief 释放本实例拥有的网格。 */
        public void Dispose()
        {
            if (!_mesh) return;
            if (Application.isPlaying) Object.Destroy(_mesh);
            else Object.DestroyImmediate(_mesh);
        }
    }
}
