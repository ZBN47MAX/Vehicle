using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace DelayNoMore.LightPath
{
    /** @brief 按真实外轮廓绘制物件与门：可移动虚线加完整折角，固定门实线。 */
    public sealed class ProjectionObjectMarkers : System.IDisposable
    {
        /** @brief 各空间独立缓存，预览网格禁止被场景卸载回收。 */
        private readonly Mesh _display = new Mesh { name = "Object markers 3D", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 },
            _world = new Mesh { name = "Object markers 2D", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
        /** @brief 线段顶点缓存。 */
        private readonly List<Vector3> _vertices = new List<Vector3>();
        /** @brief 线段三角索引缓存。 */
        private readonly List<int> _triangles = new List<int>();
        /** @brief 逐顶点标识颜色。 */
        private readonly List<Color> _colors = new List<Color>();
        /** @brief 最终投影的颜色遮罩，三维模型和作者预览不使用此遮罩。 */
        private ProjectionSurface _maskSurface;
        /** @brief 当前最终投影对应的源。 */
        private ProjectionSource _maskSource;
        /** @brief 向轮廓内部查询的中心。 */
        private Vector3 _maskCenter;
        /** @brief 背景选色。 */
        private int _selectedColor;
        /** @brief 当前选中或常规线色。 */
        private Color _lineColor = Color.white;
        /** @brief 两套标识网格的内容签名。 */
        private int _displaySignature, _worldSignature;
        /** @brief 对应缓存是否有效。 */
        private bool _displayReady, _worldReady;
        /** @brief 两种空间共用的门查询缓冲，避免每帧创建数组。 */
        private readonly List<RoomPortalLink> _portals = new List<RoomPortalLink>();
        /** @brief 当前边的虚线可见区间。 */
        private readonly List<Vector2> _dashIntervals = new List<Vector2>();
        /** @brief 提交两个空间的标识，姿态与可见状态不变时复用网格。 */
        public void Draw(LightRoom room, Matrix4x4 mapping, Camera view, Camera capture, LightStageConfig config, Material material, LightGame game, ProjectionSource selected = null)
        {
            int color = game ? game.SelectedColor : -1;
            bool displayVisible = (view.cullingMask & (1 << config.DisplayLayer)) != 0;
            if (!displayVisible && !room.Powered) return;
            room.GetComponentsInChildren(false, _portals);
            foreach (ProjectionSource source in room.Surface.Sources)
                if (source && source.Definition) source.RefreshGeometry();
            if (displayVisible)
            {
                Build(_display, room, mapping, view, false, config, game, selected, color);
                Graphics.DrawMesh(_display, Matrix4x4.identity, material, config.DisplayLayer, view, 0, null, ShadowCastingMode.Off, false);
            }
            if (room.Powered)
            {
                Build(_world, room, Matrix4x4.identity, capture, true, config, game, selected, color);
                Graphics.DrawMesh(_world, Matrix4x4.identity, material, room.Surface.gameObject.layer, capture, 0, null, ShadowCastingMode.Off, false);
            }
        }
        /** @brief 构造当前相机的轮廓；最终投影的线条和箭头统一按真实混色结果遮罩。 */
        private void Build(Mesh mesh, LightRoom room, Matrix4x4 mapping, Camera camera, bool projected, LightStageConfig config, LightGame game, ProjectionSource selected, int color)
        {
            int hash = camera.worldToCameraMatrix.GetHashCode() ^ camera.orthographic.GetHashCode() ^ camera.nearClipPlane.GetHashCode() ^ mapping.GetHashCode() ^ room.Surface.ProjectionOrigin.GetHashCode() ^ color ^ room.GetHashCode() ^ (selected ? selected.GetHashCode() : 0) ^ config.SelectionColor.GetHashCode() ^ config.MarkerWidth.GetHashCode() ^ room.Surface.Bounds.GetHashCode() ^ room.Surface.transform.position.z.GetHashCode();
            foreach (ProjectionSource source in room.Surface.Sources)
                if (source && source.Definition)
                {
                    hash = unchecked(hash * 31 + source.Geometry.Signature + source.transform.localToWorldMatrix.GetHashCode() + source.Definition.Movable.GetHashCode() + source.Definition.Portal.GetHashCode() + source.Definition.MarkerWidth.GetHashCode() + source.Definition.GetColor(room.Surface.Configuration).GetHashCode() + source.gameObject.activeInHierarchy.GetHashCode());
                }
            foreach (RoomPortalLink portal in _portals)
                hash = unchecked(hash * 31 + portal.transform.localToWorldMatrix.GetHashCode() + (portal.TryGetComponent(out Collider2D collider) ? collider.enabled.GetHashCode() : 0) + portal.Bidirectional.GetHashCode());
            if (projected ? _worldReady && _worldSignature == hash : _displayReady && _displaySignature == hash) return;
            Clear(); _selectedColor = color;
            _maskSurface = projected ? room.Surface : null;
            foreach (ProjectionSource source in room.Surface.Sources)
            {
                if (!source || !source.isActiveAndEnabled || !source.Definition) continue;
                _lineColor = selected == source ? config.SelectionColor : Color.white;
                AddSource(source, room.Surface, mapping, camera, projected, game && game.PortalBidirectional(source.gameObject), false);
            }
            _maskSurface = null; _lineColor = Color.white;
            if (projected)
                foreach (RoomPortalLink portal in _portals)
                    if (!portal.GetComponent<ProjectionSource>() && portal.TryGetComponent(out Collider2D shape) && shape.enabled)
                        AddFixedPortal(shape, Matrix4x4.identity, camera, config.MarkerWidth, game && game.PortalBidirectional(portal.gameObject));
            Upload(mesh);
            if (projected) { _worldSignature = hash; _worldReady = true; } else { _displaySignature = hash; _displayReady = true; }
        }
        /** @brief 作者预览与运行使用同一轮廓和虚实线算法。 */
        public Mesh BuildPreview(ProjectionSource[] sources, Matrix4x4 mapping, Camera camera, System.Func<ProjectionSource, bool> bidirectional, RoomPortalLink[] fixedPortals = null)
        {
            Clear(); _maskSurface = null; _lineColor = Color.white;
            foreach (ProjectionSource source in sources) if (source && source.Definition) AddSource(source, null, mapping, camera, false, bidirectional(source));
            if (fixedPortals != null)
                foreach (RoomPortalLink portal in fixedPortals)
                    if (!portal.GetComponent<ProjectionSource>() && portal.TryGetComponent(out Collider2D shape)) AddFixedPortal(shape, mapping, camera, .018f, !portal.UseArrivalPosition && portal.Bidirectional);
            Upload(_display); _displayReady = false;
            return _display;
        }
        /** @brief 获取投影源的实际可见外轮廓。@param source 投影源 @param surface 二维投影面 @param mapping 空间映射 @param camera 标识相机 @param projected 是否为二维投影 @param bidirectional 是否双向通行 @param refreshGeometry 是否先刷新模型快照 */
        private void AddSource(ProjectionSource source, ProjectionSurface surface, Matrix4x4 mapping, Camera camera, bool projected, bool bidirectional, bool refreshGeometry = true)
        {
            bool portal = source.Definition.Portal && !source.GetComponent<ColorAbilityKey>() && !source.GetComponent<ProjectionAbilityKey>() && !source.GetComponent<LevelExit>();
            if (!source.Definition.Movable && !portal) return;
            var cameraPoints = new List<Vector3>(); float depth = float.PositiveInfinity;
            foreach (Vector3 vertex in source.Geometry.WorldVertices(refreshGeometry))
            {
                Vector3 point = camera.transform.InverseTransformPoint(projected ? surface.ProjectPoint(vertex) : mapping.MultiplyPoint3x4(vertex));
                cameraPoints.Add(point); depth = Mathf.Min(depth, point.z);
            }
            if (cameraPoints.Count < 3 || depth <= 0) return;
            _maskSource = source;
            float width = source.Definition.MarkerWidth;
            depth = Mathf.Max(camera.nearClipPlane, depth - width * 3);
            DrawContour(CameraContour(cameraPoints, camera, depth), depth, camera, width, source.Definition.Movable, portal, bidirectional);
        }
        /** @brief 固定门优先采用圆碰撞轮廓，其余模型使用真实顶点外轮廓。 */
        private void AddFixedPortal(Collider2D shape, Matrix4x4 mapping, Camera camera, float width, bool bidirectional)
        {
            var cameraPoints = new List<Vector3>(); float depth = float.PositiveInfinity;
            foreach (Vector3 vertex in FixedPortalOutline(shape))
            { Vector3 point = camera.transform.InverseTransformPoint(mapping.MultiplyPoint3x4(vertex)); cameraPoints.Add(point); depth = Mathf.Min(depth, point.z); }
            if (depth <= 0 || cameraPoints.Count < 3) return;
            depth = Mathf.Max(camera.nearClipPlane, depth - width * 3);
            DrawContour(CameraContour(cameraPoints, camera, depth), depth, camera, width, false, true, bidirectional);
        }
        /** @brief 透视相机按屏幕射线将顶点压到标识平面，避免倾斜观察时轮廓漂移。@param points 相机坐标 @param camera 当前相机 @param depth 绘制深度 @return 视线一致的外轮廓 */
        private static Vector2[] CameraContour(List<Vector3> points, Camera camera, float depth)
        {
            var planar = new List<Vector2>(points.Count);
            foreach (Vector3 point in points)
                planar.Add((Vector2)point * (camera.orthographic ? 1 : depth / point.z));
            return ProjectionContours.Hull(planar);
        }
        /** @brief 返回固定门世界外轮廓供编辑画布和运行共用。 */
        public static IEnumerable<Vector3> FixedPortalOutline(Collider2D shape)
        {
            if (shape is CircleCollider2D circle) { foreach (Vector3 point in ProjectionContours.Circle(circle)) yield return point; yield break; }
            foreach (MeshFilter filter in shape.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh) { foreach (Vector3 point in new ProjectionMeshData(filter.sharedMesh).Vertices) yield return filter.transform.TransformPoint(point); yield break; }
            Bounds bounds = shape.bounds;
            yield return bounds.min; yield return new Vector3(bounds.max.x, bounds.min.y, bounds.center.z); yield return bounds.max; yield return new Vector3(bounds.min.x, bounds.max.y, bounds.center.z);
        }
        /** @brief 虚线环绕完整轮廓，明显折角额外补实线角；圆门保留连续圆弧。 */
        private void DrawContour(Vector2[] hull, float depth, Camera camera, float width, bool movable, bool portal, bool bidirectional)
        {
            if (hull.Length < 3 || width <= 0 || float.IsNaN(width) || float.IsInfinity(width)) return;
            Vector2 min = hull[0], max = hull[0];
            double centerX = 0, centerY = 0, perimeter = 0;
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 point = hull[i];
                float length = Vector2.Distance(point, hull[(i + 1) % hull.Length]);
                if (float.IsNaN(length) || float.IsInfinity(length)) return;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
                centerX += point.x;
                centerY += point.y;
                perimeter += length;
            }
            Vector2 center = new Vector2((float)(centerX / hull.Length), (float)(centerY / hull.Length));
            _maskCenter = camera.transform.TransformPoint(new Vector3(center.x, center.y, depth));
            double phase = 0, dash = System.Math.Max((double)width * 6, perimeter / 48);
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Length]; float length = Vector2.Distance(a, b);
                if (!movable) CameraStroke(a, b, depth, camera, width);
                else
                {
                    LightStageMath.MarkerDashIntervals(phase, length, dash, _dashIntervals);
                    foreach (Vector2 interval in _dashIntervals)
                    {
                        CameraStroke(Vector2.Lerp(a, b, interval.x), Vector2.Lerp(a, b, interval.y), depth, camera, width);
                    }
                    phase += length;
                    Vector2 before = hull[(i + hull.Length - 1) % hull.Length] - a, after = b - a;
                    if (Vector2.Angle(before, after) < 150)
                    {
                        float corner = Mathf.Min(before.magnitude, after.magnitude) * .18f;
                        CameraStroke(a + before.normalized * corner, a, depth, camera, width);
                        CameraStroke(a, a + after.normalized * corner, depth, camera, width);
                    }
                }
            }
            if (portal)
            {
                float length = (max.x - min.x) * .5f, spacing = (max.y - min.y) * .17f;
                Arrow(center + Vector2.up * (bidirectional ? spacing : 0), length, depth, camera, width);
                if (bidirectional) Arrow(center - Vector2.up * spacing, -length, depth, camera, width);
            }
        }
        /** @brief 通行箭头服从与轮廓相同的最终投影颜色遮罩。 */
        private void Arrow(Vector2 center, float length, float depth, Camera camera, float width)
        {
            Vector2 end = center + Vector2.right * length * .5f;
            CameraStroke(center - Vector2.right * length * .5f, end, depth, camera, width);
            CameraStroke(end, end - Vector2.right * length * .25f + Vector2.up * Mathf.Abs(length) * .2f, depth, camera, width);
            CameraStroke(end, end - Vector2.right * length * .25f - Vector2.up * Mathf.Abs(length) * .2f, depth, camera, width);
        }
        /** @brief 转为世界线段并按最终混色结果决定可见性。 */
        private void CameraStroke(Vector2 a, Vector2 b, float depth, Camera camera, float width)
        {
            Vector3 worldA = camera.transform.TransformPoint(new Vector3(a.x, a.y, depth)), worldB = camera.transform.TransformPoint(new Vector3(b.x, b.y, depth));
            if (_maskSurface)
            {
                Vector3 point = Vector3.Lerp((worldA + worldB) * .5f, _maskCenter, .025f); point.z = _maskSurface.transform.position.z;
                if (!_maskSurface.Bounds.Contains(point)) return;
                ProjectionRegion sample = _maskSurface.Sample(point);
                if (!sample.Exists || (sample.Colored && _maskSurface.Configuration.Matches(sample.Color, _selectedColor)) || (sample.Portal && sample.Portal != _maskSource)) return;
            }
            Line(worldA, worldB, camera.transform.forward, width * 1.6f, Color.black);
            Line(worldA - camera.transform.forward * .001f, worldB - camera.transform.forward * .001f, camera.transform.forward, width, _lineColor);
        }
        /** @brief 写入线段网格，避免每条线分配临时数组。 */
        private void Line(Vector3 a, Vector3 b, Vector3 normal, float width, Color color)
        {
            Vector3 side = Vector3.Cross(b - a, normal).normalized * width * .5f; int start = _vertices.Count;
            _vertices.Add(a - side); _vertices.Add(b - side); _vertices.Add(b + side); _vertices.Add(a + side);
            _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2); _triangles.Add(start); _triangles.Add(start + 2); _triangles.Add(start + 3);
            for (int i = 0; i < 4; i++) _colors.Add(color);
        }
        /** @brief 清空复用缓存。 */
        private void Clear() { _vertices.Clear(); _triangles.Clear(); _colors.Clear(); }
        /** @brief 上传目标网格。 */
        private void Upload(Mesh mesh) { mesh.Clear(); mesh.SetVertices(_vertices); mesh.SetTriangles(_triangles, 0); mesh.SetColors(_colors); mesh.RecalculateBounds(); }
        /** @brief 清理实例资源。 */
        public void Dispose() { if (Application.isPlaying) { Object.Destroy(_display); Object.Destroy(_world); } else { Object.DestroyImmediate(_display); Object.DestroyImmediate(_world); } }
    }
}
