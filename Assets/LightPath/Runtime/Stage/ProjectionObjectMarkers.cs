using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DelayNoMore.LightPath
{
    /** @brief 为可操作对象绘制统一四角抓手，复用舞台与二维捕获相机。 */
    public sealed class ProjectionObjectMarkers : System.IDisposable
    {
        /** @brief 各视角独立网格，避免提交后修改同一网格。 */
        private readonly Mesh _display = new Mesh { name = "Movable markers 3D" }, _world = new Mesh { name = "Movable markers 2D" };
        /** @brief 复用网格数据。 */
        private readonly List<Vector3> _vertices = new List<Vector3>();
        /** @brief 复用三角索引。 */
        private readonly List<int> _triangles = new List<int>();
        /** @brief 黑白双层线条颜色。 */
        private readonly List<Color> _colors = new List<Color>();
        /** @brief 绘制两个空间的对象标识。 @param room 房间 @param mapping 舞台映射 @param view 三维相机 @param capture 二维相机 @param config 表现配置 @param material 顶点色材质 */
        public void Draw(LightRoom room, Matrix4x4 mapping, Camera view, Camera capture, LightStageConfig config, Material material)
        {
            Build(_display, room, mapping, view, false, config.SelectionWidth);
            Build(_world, room, Matrix4x4.identity, capture, true, config.SelectionWidth);
            Graphics.DrawMesh(_display, Matrix4x4.identity, material, config.DisplayLayer, view, 0, null, ShadowCastingMode.Off, false);
            Graphics.DrawMesh(_world, Matrix4x4.identity, material, room.Surface.gameObject.layer, capture, 0, null, ShadowCastingMode.Off, false);
        }
        /** @brief 按相机平面构造四角框。 @param mesh 输出网格 @param room 房间 @param mapping 映射 @param camera 相机 @param projected 是否投影 @param width 线宽 */
        private void Build(Mesh mesh, LightRoom room, Matrix4x4 mapping, Camera camera, bool projected, float width)
        {
            _vertices.Clear(); _triangles.Clear(); _colors.Clear();
            foreach (ProjectionSource source in room.Surface.Sources)
            {
                if (!source || !source.isActiveAndEnabled || !source.Definition || !source.Definition.Movable) continue;
                Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
                bool found = false;
                foreach (Vector3 vertex in source.Geometry.WorldVertices())
                {
                    Vector3 point = projected ? room.Surface.ProjectPoint(vertex) : mapping.MultiplyPoint3x4(vertex);
                    point = camera.transform.InverseTransformPoint(point);
                    min = Vector3.Min(min, point); max = Vector3.Max(max, point); found = true;
                }
                if (!found || min.z <= 0) continue;
                Frame(min, max, camera, width, true);
            }
            mesh.Clear(); mesh.SetVertices(_vertices); mesh.SetTriangles(_triangles, 0); mesh.SetColors(_colors); mesh.RecalculateBounds();
        }
        /** @brief 绘制统一抓手框。 @param min 左下近点 @param max 右上远点 @param camera 相机 @param width 线宽 @param movable 是否可移动 */
        private void Frame(Vector3 min, Vector3 max, Camera camera, float width, bool movable)
        {
            float length = Mathf.Max(width * 3, Mathf.Min(max.x - min.x, max.y - min.y) * .22f);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                {
                    Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, min.z - width * 2);
                    Stroke(camera.transform.TransformPoint(corner), camera.transform.TransformPoint(corner + Vector3.right * (x == 0 ? length : -length)), camera.transform.forward, width);
                    Stroke(camera.transform.TransformPoint(corner), camera.transform.TransformPoint(corner + Vector3.up * (y == 0 ? length : -length)), camera.transform.forward, width);
                }
        }
        /** @brief 黑白双线确保明暗背景均可读。 @param a 起点 @param b 终点 @param normal 平面法线 @param width 线宽 */
        private void Stroke(Vector3 a, Vector3 b, Vector3 normal, float width)
        {
            Line(a, b, normal, width * 2.5f, Color.black);
            Line(a - normal * .001f, b - normal * .001f, normal, width, Color.white);
        }
        /** @brief 添加线段四边形。 @param a 起点 @param b 终点 @param normal 平面法线 @param width 线宽 @param color 颜色 */
        private void Line(Vector3 a, Vector3 b, Vector3 normal, float width, Color color)
        {
            Vector3 side = Vector3.Cross(b - a, normal).normalized * width * .5f;
            int start = _vertices.Count;
            _vertices.AddRange(new[] { a - side, b - side, b + side, a + side });
            _triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            for (int i = 0; i < 4; i++) _colors.Add(color);
        }
        /** @brief 释放实例网格。 */
        public void Dispose()
        {
            Object.Destroy(_display); Object.Destroy(_world);
        }
    }
}
