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
