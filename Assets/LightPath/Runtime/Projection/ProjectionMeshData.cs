using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 双面三角网格射线查询，BVH保留凹口和孔洞，不使用碰撞器凸包。 */
    public sealed class ProjectionMeshData
    {
        /** @brief 本地顶点。 */
        private readonly Vector3[] _vertices;
        /** @brief 只读顶点供完整投影边界计算复用。 */
        public IReadOnlyList<Vector3> Vertices => _vertices;
        /** @brief 已按BVH排序的三角索引。 */
        private readonly int[] _triangles;
        /** @brief 空间树节点。 */
        private readonly List<Node> _nodes = new List<Node>();
        /** @brief 网格真实顶点范围。 */
        public Bounds Bounds => _nodes[0].Bounds;
        /** @brief 焊接后的三角索引；仅用于拓扑检查，射线仍使用原始顶点。 */
        private int[] _welded;
        /** @brief 提取闭合网格的单环投影轮廓；孔洞、多环及无法消除的自交回退原射线求解。@param points 逐顶点透视投影 @param outline 逆时针边界 @return 是否为单个简单轮廓 */
        public bool TrySilhouette(Vector2[] points, out Vector2[] outline)
        {
            outline = null;
            if (_welded == null)
            {
                _welded = new int[_vertices.Length];
                var welded = new List<Vector3>();
                float epsilon = Mathf.Max(.000001f, Bounds.size.magnitude * .000001f);
                for (int i = 0; i < _vertices.Length; i++)
                {
                    int id = -1;
                    for (int j = 0; j < welded.Count; j++)
                        if ((_vertices[i] - welded[j]).sqrMagnitude <= epsilon * epsilon) { id = j; break; }
                    if (id < 0) { id = welded.Count; welded.Add(_vertices[i]); }
                    _welded[i] = id;
                }
            }
            var edges = new Dictionary<(int, int), (int Count, float Facing, int From)>();
            var positions = new Dictionary<int, Vector2>();
            var neighbors = new Dictionary<int, List<int>>();
            for (int i = 0; i < _triangles.Length; i += 3)
            {
                int a = _triangles[i], b = _triangles[i + 1], c = _triangles[i + 2];
                if (_welded[a] == _welded[b] || _welded[b] == _welded[c] || _welded[c] == _welded[a]) continue;
                Vector2 u = points[b] - points[a], v = points[c] - points[a];
                float facing = u.x * v.y - u.y * v.x;
                if (Mathf.Abs(facing) < 1e-10f) return false;
                for (int j = 0; j < 3; j++)
                {
                    int x = _triangles[i + j], y = _triangles[i + (j + 1) % 3];
                    int wx = _welded[x], wy = _welded[y];
                    positions[wx] = points[x]; positions[wy] = points[y];
                    var key = (Mathf.Min(wx, wy), Mathf.Max(wx, wy));
                    if (!edges.TryGetValue(key, out var edge)) edges[key] = (1, facing, wx);
                    else
                    {
                        if (edge.Count != 1 || edge.From == wx) return false;
                        if (edge.Facing * facing < 0)
                        {
                            if (!neighbors.TryGetValue(wx, out var nx)) neighbors[wx] = nx = new List<int>();
                            if (!neighbors.TryGetValue(wy, out var ny)) neighbors[wy] = ny = new List<int>();
                            nx.Add(wy); ny.Add(wx);
                        }
                        edges[key] = (2, facing, wx);
                    }
                }
            }
            foreach (var edge in edges.Values) if (edge.Count != 2) return false;
            int start = -1;
            foreach (var pair in neighbors) { if (pair.Value.Count != 2) return false; start = pair.Key; }
            if (start < 0) return false;
            var loop = new List<Vector2>();
            int previous = -1, current = start;
            do
            {
                loop.Add(positions[current]);
                var next = neighbors[current];
                int value = next[0] == previous ? next[1] : next[0];
                previous = current; current = value;
                if (loop.Count > neighbors.Count) return false;
            } while (current != start);
            if (loop.Count != neighbors.Count) return false;
            if (!ProjectionContours.IsSimple(loop) && !ResolveSilhouette(loop, points, out loop)) return false;
            float area = 0;
            for (int i = 0; i < loop.Count; i++)
                area += ProjectionContours.Cross(loop[i], loop[(i + 1) % loop.Count]);
            if (Mathf.Abs(area) < 1e-8f) return false;
            if (area < 0) loop.Reverse();
            outline = loop.ToArray();
            return true;
        }
        /** @brief 轮廓折叠时切开交点，按真实三角覆盖移除内部边；只接收单个闭合外环。@param input 候选轮廓 @param projected 全部投影顶点 @param output 真实边界 @return 是否成功 */
        private bool ResolveSilhouette(List<Vector2> input, Vector2[] projected, out List<Vector2> output)
        {
            output = null;
            var vertices = new List<Vector2>();
            var links = new Dictionary<int, List<int>>();
            for (int i = 0; i < input.Count; i++)
            {
                Vector2 a = input[i], edge = input[(i + 1) % input.Count] - a;
                var cuts = new List<float> { 0, 1 };
                for (int j = 0; j < input.Count; j++)
                {
                    if (j == i) continue;
                    Vector2 b = input[j], other = input[(j + 1) % input.Count] - b;
                    float cross = ProjectionContours.Cross(edge, other);
                    if (Mathf.Abs(cross) < 1e-12f) continue;
                    float t = ProjectionContours.Cross(b - a, other) / cross;
                    float u = ProjectionContours.Cross(b - a, edge) / cross;
                    if (t > 0 && t < 1 && u >= 0 && u <= 1) cuts.Add(t);
                }
                cuts.Sort();
                for (int k = 0; k < cuts.Count - 1; k++)
                {
                    Vector2 from = a + edge * cuts[k], to = a + edge * cuts[k + 1];
                    float length = Vector2.Distance(from, to);
                    if (length < 1e-6f) continue;
                    Vector2 mid = (from + to) * .5f;
                    float offset = Mathf.Min(.00001f, length * .001f);
                    for (int j = 0; j < input.Count; j++)
                    {
                        if (j == i) continue;
                        Vector2 origin = input[j], segment = input[(j + 1) % input.Count] - origin;
                        float t = Mathf.Clamp01(Vector2.Dot(mid - origin, segment) / segment.sqrMagnitude);
                        offset = Mathf.Min(offset, Vector2.Distance(mid, origin + segment * t) * .1f);
                    }
                    if (offset < 1e-7f) return false;
                    Vector2 normal = new Vector2(-edge.y, edge.x).normalized * offset;
                    if (ContainsProjected(mid + normal, projected) == ContainsProjected(mid - normal, projected)) continue;
                    int x = WeldPoint(vertices, from), y = WeldPoint(vertices, to);
                    if (!links.TryGetValue(x, out var nx)) links[x] = nx = new List<int>();
                    if (!links.TryGetValue(y, out var ny)) links[y] = ny = new List<int>();
                    if (!nx.Contains(y)) { nx.Add(y); ny.Add(x); }
                }
            }
            int start = -1;
            foreach (var pair in links) { if (pair.Value.Count != 2) return false; start = pair.Key; }
            if (start < 0) return false;
            var loop = new List<Vector2>(); int previous = -1, current = start;
            do
            {
                loop.Add(vertices[current]);
                var next = links[current]; int value = next[0] == previous ? next[1] : next[0];
                previous = current; current = value;
                if (loop.Count > links.Count) return false;
            } while (current != start);
            if (loop.Count != links.Count || !ProjectionContours.IsSimple(loop)) return false;
            output = loop; return true;
        }
        /** @brief 焊接浮点交点；容差仅覆盖求交舍入。@param points 交点表 @param point 新交点 @return 索引 */
        private static int WeldPoint(List<Vector2> points, Vector2 point)
        {
            for (int i = 0; i < points.Count; i++) if ((points[i] - point).sqrMagnitude < 1e-12f) return i;
            points.Add(point); return points.Count - 1;
        }
        /** @brief 查询实际投影三角形并集，不能用凸包替代。@param point 查询点 @param projected 全部投影顶点 @return 是否覆盖 */
        private bool ContainsProjected(Vector2 point, Vector2[] projected)
        {
            for (int i = 0; i < _triangles.Length; i += 3)
            {
                Vector2 a = projected[_triangles[i]], b = projected[_triangles[i + 1]], c = projected[_triangles[i + 2]];
                float x = ProjectionContours.Cross(b - a, point - a), y = ProjectionContours.Cross(c - b, point - b), z = ProjectionContours.Cross(a - c, point - c);
                if ((x > 0 && y > 0 && z > 0) || (x < 0 && y < 0 && z < 0)) return true;
            }
            return false;
        }
        /** @brief 空间分割或叶片中的三角范围。 */
        private struct Node
        {
            /** @brief 本地包围。 */
            public Bounds Bounds;
            /** @brief 三角起点。 */
            public int Start;
            /** @brief 三角数量，非叶为零。 */
            public int Count;
            /** @brief 左子树。 */
            public int Left;
            /** @brief 右子树。 */
            public int Right;
        }
        /** @brief 从全部三角/四边形子网格读取，编辑器支持未开启读写的导入模型。@param mesh 网格 */
        public ProjectionMeshData(Mesh mesh)
        {
#if UNITY_EDITOR
            using var snapshot = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh);
#else
            if (!mesh.isReadable) throw new InvalidOperationException("Projection mesh requires Read/Write: " + mesh.name);
            using var snapshot = Mesh.AcquireReadOnlyMeshData(mesh);
#endif
            var data = snapshot[0];
            using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            data.GetVertices(vertices);
            _vertices = vertices.ToArray();
            var triangles = new List<int>();
            for (int part = 0; part < data.subMeshCount; part++)
            {
                var sub = data.GetSubMesh(part);
                if (sub.topology != MeshTopology.Triangles && sub.topology != MeshTopology.Quads) continue;
                using var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp);
                data.GetIndices(indices, part, true);
                int stride = sub.topology == MeshTopology.Quads ? 4 : 3;
                for (int i = 0; i + stride <= indices.Length; i += stride)
                {
                    triangles.Add(indices[i]); triangles.Add(indices[i + 1]); triangles.Add(indices[i + 2]);
                    if (stride == 4) { triangles.Add(indices[i]); triangles.Add(indices[i + 2]); triangles.Add(indices[i + 3]); }
                }
            }
            if (triangles.Count == 0) throw new InvalidOperationException("Projection mesh has no faces: " + mesh.name);
            _triangles = triangles.ToArray();
            Build(0, _triangles.Length / 3);
        }
        /** @brief 递归构建空间中点分割树。@param start 三角起点 @param count 数量 @return 节点索引 */
        private int Build(int start, int count)
        {
            Bounds bounds = new Bounds(_vertices[_triangles[start * 3]], Vector3.zero);
            for (int i = start * 3; i < (start + count) * 3; i++) bounds.Encapsulate(_vertices[_triangles[i]]);
            int index = _nodes.Count;
            _nodes.Add(new Node { Bounds = bounds, Start = start, Count = count });
            if (count <= 8) return index;
            Vector3 size = bounds.size;
            int axis = size.x > size.y ? 0 : 1;
            if (size.z > size[axis]) axis = 2;
            int split = start;
            for (int i = start; i < start + count; i++)
            {
                float center = (_vertices[_triangles[i * 3]][axis] + _vertices[_triangles[i * 3 + 1]][axis] + _vertices[_triangles[i * 3 + 2]][axis]) / 3;
                if (center >= bounds.center[axis]) continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int saved = _triangles[split * 3 + corner];
                    _triangles[split * 3 + corner] = _triangles[i * 3 + corner];
                    _triangles[i * 3 + corner] = saved;
                }
                split++;
            }
            if (split == start || split == start + count) split = start + count / 2;
            int left = Build(start, split - start), right = Build(split, start + count - split);
            _nodes[index] = new Node { Bounds = bounds, Left = left, Right = right };
            return index;
        }
        /** @brief 世界距离参数不因非等比或负缩放改变。@param ray 世界单位射线 @param inverse 世界到模型矩阵 @param distance 最大世界距离 @return 是否穿过任何实际面 */
        public bool Intersects(Ray ray, Matrix4x4 inverse, float distance)
        {
            return Visit(0, inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction), distance);
        }
        /** @brief BVH包围只用于排除，最终结果由三角形决定。@param index 节点 @param origin 本地起点 @param direction 未归一化本地方向 @param distance 最大参数 @return 命中 */
        private bool Visit(int index, Vector3 origin, Vector3 direction, float distance)
        {
            Node node = _nodes[index];
            if (!IntersectsBounds(node.Bounds, origin, direction, distance)) return false;
            if (node.Count == 0) return Visit(node.Left, origin, direction, distance) || Visit(node.Right, origin, direction, distance);
            for (int i = node.Start * 3; i < (node.Start + node.Count) * 3; i += 3)
            {
                Vector3 a = _vertices[_triangles[i]], edge = _vertices[_triangles[i + 1]] - a, other = _vertices[_triangles[i + 2]] - a;
                Vector3 cross = Vector3.Cross(direction, other);
                double determinant = Vector3.Dot(edge, cross);
                if (Math.Abs(determinant) <= 1e-10 * Math.Sqrt(edge.sqrMagnitude * other.sqrMagnitude * direction.sqrMagnitude)) continue;
                Vector3 relative = origin - a;
                double u = Vector3.Dot(relative, cross) / determinant;
                if (u < -1e-6 || u > 1 + 1e-6) continue;
                Vector3 q = Vector3.Cross(relative, edge);
                double v = Vector3.Dot(direction, q) / determinant;
                double t = Vector3.Dot(other, q) / determinant;
                if (v >= -1e-6 && u + v <= 1 + 1e-6 && t >= 0 && t <= distance) return true;
            }
            return false;
        }
        /** @brief 射线区间与包围盒相交，支持轴向零分量。@param bounds 本地范围 @param origin 起点 @param direction 方向 @param distance 最大参数 @return 是否相交 */
        private static bool IntersectsBounds(Bounds bounds, Vector3 origin, Vector3 direction, float distance)
        {
            float near = 0, far = distance;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < 1e-12f)
                {
                    if (origin[axis] < bounds.min[axis] || origin[axis] > bounds.max[axis]) return false;
                    continue;
                }
                float a = (bounds.min[axis] - origin[axis]) / direction[axis], b = (bounds.max[axis] - origin[axis]) / direction[axis];
                near = Mathf.Max(near, Mathf.Min(a, b)); far = Mathf.Min(far, Mathf.Max(a, b));
                if (near > far) return false;
            }
            return true;
        }
    }
}
