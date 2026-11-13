using System.Collections.Generic;
using UnityEngine;
namespace DelayNoMore.LightPath.Editor
{
    /** @brief 优先使用共享模型三角面；无模型的逻辑源才使用独立于物理场景的碰撞查询。 */
    public sealed class ProjectionPreviewGeometry
    {
        /** @brief 每个投影源对应的形状快照。 */
        private readonly Dictionary<ProjectionSource, List<Shape>> _sources = new Dictionary<ProjectionSource, List<Shape>>();
        /** @brief 与运行时共用的实际模型三角面。 */
        private readonly Dictionary<ProjectionSource, ProjectionMeshGeometry> _meshes = new Dictionary<ProjectionSource, ProjectionMeshGeometry>();
        /** @brief 不支持或没有碰撞形状的源数量。 */
        public int MissingShapes
        {
            get; private set;
        }
        /** @brief 单个碰撞形状的编辑器快照。 */
        private sealed class Shape
        {
            /** @brief 原碰撞组件。 */
            public Collider Collider;
            /** @brief 本次预览固定的世界到本地变换。 */
            public Matrix4x4 WorldToLocal;
            /** @brief 网格本地顶点。 */
            public Vector3[] Vertices;
            /** @brief 网格三角形索引。 */
            public int[] Triangles;
        }
        /** @brief 缓存本次采样所需几何，不创建场景对象。 @param sources 登记源 */
        public ProjectionPreviewGeometry(IReadOnlyList<ProjectionSource> sources)
        {
            if (sources == null)
                return;
            foreach (ProjectionSource source in sources)
            {
                if (!source)
                    continue;
                var geometry = new ProjectionMeshGeometry(source);
                if (geometry.HasMeshes)
                {
                    _meshes[source] = geometry;
                    if (geometry.MissingMeshes > 0) MissingShapes++;
                    continue;
                }
                var shapes = new List<Shape>();
                foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled || !ActiveWithinSource(collider.transform, source.transform))
                        continue;
                    Shape shape = new Shape { Collider = collider, WorldToLocal = collider.transform.worldToLocalMatrix };
                    if (collider is MeshCollider mesh && mesh.sharedMesh)
                    {
                        try
                        {
                            shape.Vertices = mesh.sharedMesh.vertices;
                            shape.Triangles = mesh.sharedMesh.triangles;
                        }
                        catch (System.Exception) { continue; }
                    }
                    else if (!(collider is BoxCollider) && !(collider is SphereCollider) && !(collider is CapsuleCollider))
                        continue;
                    shapes.Add(shape);
                }
                if (shapes.Count == 0)
                    MissingShapes++;
                _sources[source] = shapes;
            }
        }
        /** @brief 忽略房间根停用，但保留源内部子物体的停用状态。 @param child 形状节点 @param root 源根 @return 是否参与预览 */
        private static bool ActiveWithinSource(Transform child, Transform root)
        {
            for (Transform current = child; current && current != root; current = current.parent)
                if (!current.gameObject.activeSelf)
                    return false;
            return root.gameObject.activeSelf;
        }
        /** @brief 查询指定源是否遮住光线。 @param source 源 @param ray 世界射线 @param distance 至幕布距离 @return 是否命中 */
        public bool Intersects(ProjectionSource source, Ray ray, float distance)
        {
            if (_meshes.TryGetValue(source, out ProjectionMeshGeometry geometry))
                return geometry.Intersects(ray, distance);
            if (!_sources.TryGetValue(source, out List<Shape> shapes))
                return false;
            foreach (Shape shape in shapes)
            {
                Collider collider = shape.Collider;
                Transform transform = collider.transform;
                Vector3 origin = shape.WorldToLocal.MultiplyPoint3x4(ray.origin);
                Vector3 direction = shape.WorldToLocal.MultiplyVector(ray.direction);
                if (collider is BoxCollider box)
                {
                    if (Box(origin - box.center, direction, box.size * .5f, distance))
                        return true;
                }
                else if (collider is SphereCollider sphere)
                {
                    Vector3 scale = transform.lossyScale;
                    float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    if (Sphere(ray.origin - transform.TransformPoint(sphere.center), ray.direction, radius, distance))
                        return true;
                }
                else if (collider is CapsuleCollider capsule)
                {
                    Vector3 scale = transform.lossyScale;
                    int axis = capsule.direction;
                    float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
                    float half = Mathf.Max(0, capsule.height * Mathf.Abs(scale[axis]) * .5f - radius);
                    Vector3 unit = Vector3.zero;
                    unit[axis] = 1;
                    Vector3 up = transform.TransformDirection(unit).normalized;
                    Vector3 offset = ray.origin - transform.TransformPoint(capsule.center);
                    if (Sphere(offset - up * half, ray.direction, radius, distance) || Sphere(offset + up * half, ray.direction, radius, distance))
                        return true;
                    Vector3 flatOrigin = offset - up * Vector3.Dot(offset, up);
                    Vector3 flatDirection = ray.direction - up * Vector3.Dot(ray.direction, up);
                    float a = flatDirection.sqrMagnitude, b = Vector3.Dot(flatOrigin, flatDirection), c = flatOrigin.sqrMagnitude - radius * radius;
                    float discriminant = b * b - a * c;
                    if (a > .0000001f && discriminant >= 0)
                    {
                        float t = (-b - Mathf.Sqrt(discriminant)) / a;
                        if (t >= 0 && t <= distance && Mathf.Abs(Vector3.Dot(offset + ray.direction * t, up)) <= half)
                            return true;
                    }
                }
                else if (shape.Vertices != null)
                {
                    Mesh mesh = ((MeshCollider)collider).sharedMesh;
                    if (!Box(origin - mesh.bounds.center, direction, mesh.bounds.extents, distance))
                        continue;
                    for (int i = 0; i + 2 < shape.Triangles.Length; i += 3)
                    {
                        Vector3 a = shape.Vertices[shape.Triangles[i]], b = shape.Vertices[shape.Triangles[i + 1]], c = shape.Vertices[shape.Triangles[i + 2]];
                        Vector3 edge = b - a, other = c - a;
                        Vector3 cross = Vector3.Cross(direction, other);
                        float determinant = Vector3.Dot(edge, cross);
                        if (Mathf.Abs(determinant) < .0000001f)
                            continue;
                        Vector3 relative = origin - a;
                        float u = Vector3.Dot(relative, cross) / determinant;
                        if (u < 0 || u > 1)
                            continue;
                        Vector3 q = Vector3.Cross(relative, edge);
                        float v = Vector3.Dot(direction, q) / determinant;
                        float t = Vector3.Dot(other, q) / determinant;
                        if (v >= 0 && u + v <= 1 && t >= 0 && t <= distance)
                            return true;
                    }
                }
            }
            return false;
        }
        /** @brief 射线与轴对齐包围盒的区间相交。 @param origin 相对中心起点 @param direction 未归一化方向 @param extents 半尺寸 @param distance 最大参数 @return 是否相交 */
        private static bool Box(Vector3 origin, Vector3 direction, Vector3 extents, float distance)
        {
            float near = 0, far = distance;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < .0000001f)
                {
                    if (Mathf.Abs(origin[axis]) > extents[axis])
                        return false;
                    continue;
                }
                float a = (-extents[axis] - origin[axis]) / direction[axis], b = (extents[axis] - origin[axis]) / direction[axis];
                near = Mathf.Max(near, Mathf.Min(a, b));
                far = Mathf.Min(far, Mathf.Max(a, b));
                if (near > far)
                    return false;
            }
            return true;
        }
        /** @brief 射线与球相交。 @param origin 相对球心起点 @param direction 方向 @param radius 半径 @param distance 最大参数 @return 是否相交 */
        private static bool Sphere(Vector3 origin, Vector3 direction, float radius, float distance)
        {
            float a = direction.sqrMagnitude, b = Vector3.Dot(origin, direction), c = origin.sqrMagnitude - radius * radius;
            float discriminant = b * b - a * c;
            if (a <= .0000001f || discriminant < 0)
                return false;
            float t = (-b - Mathf.Sqrt(discriminant)) / a;
            return t >= 0 && t <= distance;
        }
    }
}
