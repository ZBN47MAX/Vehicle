using System.Collections.Generic;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 模型子网格几何快照，编辑器和运行时共用；不修改或生成场景对象。 */
    public sealed class ProjectionMeshGeometry
    {
        /** @brief 几何所属源。 */
        private readonly ProjectionSource _source;
        /** @brief 按网格共享BVH，变换时不重复读取三角数据。 */
        private readonly Dictionary<Mesh, ProjectionMeshData> _meshes = new Dictionary<Mesh, ProjectionMeshData>();
        /** @brief 本次活动模型部分。 */
        private readonly List<Part> _parts = new List<Part>();
        /** @brief 复用组件查询列表。 */
        private readonly List<MeshFilter> _filters = new List<MeshFilter>();
        /** @brief 子模型的几何与逆矩阵。 */
        private struct Part
        {
            /** @brief 三角树。 */
            public ProjectionMeshData Data;
            /** @brief 世界到模型变换。 */
            public Matrix4x4 Inverse;
        }
        /** @brief 是否有模型；有模型时不可退化成简化Collider。 */
        public bool HasMeshes { get; private set; }
        /** @brief 枚举实际网格的世界顶点。 @return 世界顶点序列 */
        public IEnumerable<Vector3> WorldVertices()
        {
            Refresh();
            foreach (Part part in _parts)
            {
                Matrix4x4 matrix = part.Inverse.inverse;
                foreach (Vector3 point in part.Data.Vertices)
                    yield return matrix.MultiplyPoint3x4(point);
            }
        }
        /** @brief 无法读取或不受支持的网格数量。 */
        public int MissingMeshes { get; private set; }
        /** @brief 实际模型世界包围。 */
        public Bounds Bounds { get; private set; }
        /** @brief 是否有可求解的三角面。 */
        public bool HasBounds => _parts.Count > 0;
        /** @brief 最近几何状态。 */
        public int Signature { get; private set; }
        /** @brief 收集源的模型部分。@param source 投影源 */
        public ProjectionMeshGeometry(ProjectionSource source)
        {
            _source = source;
            Refresh();
        }
        /** @brief 更新子模型姿态、激活和替换状态；Renderer.enabled仅是幕后隐藏，不决定光学。@return 是否改变 */
        public bool Refresh()
        {
            _parts.Clear(); _filters.Clear(); HasMeshes = false; MissingMeshes = 0;
            foreach (SkinnedMeshRenderer skin in _source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.GetComponentInParent<ProjectionSource>(true) == _source)
                {
                    HasMeshes = true;
                    MissingMeshes++;
                }
            _source.GetComponentsInChildren(true, _filters);
            int hash = 17;
            foreach (MeshFilter filter in _filters)
            {
                if (!filter.sharedMesh || !filter.GetComponent<MeshRenderer>() || filter.GetComponentInParent<ProjectionSource>(true) != _source) continue;
                HasMeshes = true;
                if (!ActiveWithinSource(filter.transform, _source.transform)) continue;
                Mesh mesh = filter.sharedMesh;
                if (!_meshes.TryGetValue(mesh, out ProjectionMeshData data))
                {
                    try { data = new ProjectionMeshData(mesh); }
                    catch (System.Exception error)
                    {
                        Debug.LogWarning(error.Message, _source);
                    }
                    _meshes[mesh] = data;
                }
                if (data == null) { MissingMeshes++; continue; }
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                if (Mathf.Abs(matrix.determinant) < 1e-12f) continue;
                hash = unchecked(hash * 31 + matrix.GetHashCode());
                hash = unchecked(hash * 31 + mesh.GetHashCode());
                Bounds local = data.Bounds;
                Bounds world = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                    world.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                if (_parts.Count == 0) Bounds = world;
                else { Bounds merged = Bounds; merged.Encapsulate(world); Bounds = merged; }
                _parts.Add(new Part { Data = data, Inverse = matrix.inverse });
            }
            bool changed = hash != Signature;
            Signature = hash;
            return changed;
        }
        /** @brief 忽略房间根停用，保留源与源内子物体停用状态。@param child 形状节点 @param root 源根 @return 是否参与 */
        public static bool ActiveWithinSource(Transform child, Transform root)
        {
            for (Transform current = child; current && current != root; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return root.gameObject.activeSelf;
        }
        /** @brief 沿真实模型三角面检测光线。@param ray 单位世界射线 @param distance 至幕布距离 @return 是否命中 */
        public bool Intersects(Ray ray, float distance)
        {
            foreach (Part part in _parts)
                if (part.Data.Intersects(ray, part.Inverse, distance)) return true;
            return false;
        }
    }
}
