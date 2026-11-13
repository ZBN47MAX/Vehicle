using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DelayNoMore.LightPath.Editor;

namespace DelayNoMore.LightPath.Tests
{
    /** @brief 真实模型投影回归，仅读取已有预制体或建立内存网格，不搭建场景对象。 */
    public sealed class ProjectionMeshTests
    {
        /** @brief 使用正式圆门网格核对精确轮廓与独立逐三角射线，包括存在轮廓折叠的门。@param x 模型横坐标 */
        [TestCase(1.08f)] [TestCase(2.58f)]
        public void SphereSilhouette_MatchesTriangleOracle(float x)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LightPath/Prefabs/Props/Gate.prefab");
            var data = new ProjectionMeshData(prefab.GetComponentInChildren<MeshFilter>().sharedMesh);
            Vector3 lamp = new Vector3(-1.34f, -4.96f, -27.69f);
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(x, .52f, -13.845f), Quaternion.identity, new Vector3(1, 1, .2f));
            var projected = new System.Collections.Generic.List<Vector2>();
            foreach (Vector3 vertex in data.Vertices)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertex), delta = world - lamp;
                projected.Add(lamp + delta * (-lamp.z / delta.z));
            }
            Assert.That(data.TrySilhouette(projected.ToArray(), out Vector2[] outline), Is.True);
            var pieces = ProjectionContours.ConvexPieces(outline);
            Assert.That(pieces.Count, Is.InRange(1, 100));
            Vector2 min = outline[0], max = outline[0];
            foreach (Vector2 point in outline) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            var random = new System.Random(713);
            for (int i = 0; i < 10000; i++)
            {
                Vector2 point = new Vector2(Mathf.Lerp(min.x - .03f, max.x + .03f, (float)random.NextDouble()), Mathf.Lerp(min.y - .03f, max.y + .03f, (float)random.NextDouble()));
                Vector3 delta = (Vector3)point - lamp;
                bool exact = data.Intersects(new Ray(lamp, delta.normalized), matrix.inverse, delta.magnitude);
                bool contour = pieces.Exists(piece => InsideConvex(point, piece));
                Assert.That(contour, Is.EqualTo(exact), $"投影点 {point:R}");
            }
        }
        /** @brief 开放孔洞模型必须保留原求解；凹轮廓分片不得填平凹口。 */
        [Test] public void ContourFallback_PreservesHolesAndConcavity()
        {
            Mesh mesh = Ring();
            try
            {
                var data = new ProjectionMeshData(mesh);
                var points = new System.Collections.Generic.List<Vector2>();
                foreach (Vector3 vertex in data.Vertices) points.Add(vertex);
                Assert.That(data.TrySilhouette(points.ToArray(), out _), Is.False);
            }
            finally { Object.DestroyImmediate(mesh); }
            Vector2[] notch = { new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 1), new Vector2(1, 1), new Vector2(1, 3), new Vector2(0, 3) };
            var pieces = ProjectionContours.ConvexPieces(notch);
            Assert.That(pieces.Exists(piece => InsideConvex(new Vector2(2, 2), piece)), Is.False);
            Assert.That(pieces.Exists(piece => InsideConvex(new Vector2(.5f, 2), piece)), Is.True);
        }
        /** @brief 凸多边形点包含，独立于角色碰撞的收缩容差。 */
        private static bool InsideConvex(Vector2 point, Vector2[] polygon)
        {
            for (int i = 0; i < polygon.Length; i++)
                if (ProjectionContours.Cross(polygon[(i + 1) % polygon.Length] - polygon[i], point - polygon[i]) < -1e-7f) return false;
            return true;
        }
        /** @brief 固定门的编辑预览取真实圆模型轮廓，不取BoxCollider2D的方形包围。 */
        [Test] public void FixedPortalPreview_UsesRoundModelOutline()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LightPath/Prefabs/Props/2DGate.prefab");
            var shape = prefab.GetComponent<Collider2D>();
            Assert.That(shape, Is.Not.Null);
            var points = new System.Collections.Generic.List<Vector2>();
            foreach (Vector3 point in ProjectionObjectMarkers.FixedPortalOutline(shape)) points.Add(point);
            Assert.That(ProjectionContours.Hull(points).Length, Is.GreaterThan(12), "圆门不应只有四个方框顶点");
        }
        /** @brief 裁剪产生重复顶点时仍须检测真实重叠，接触边不触发混色回退。 */
        [Test] public void ContourOverlap_IgnoresDuplicateEdgesButPreservesOverlap()
        {
            Vector2[] a = { Vector2.zero, Vector2.right * 2, Vector2.one * 2, Vector2.up * 2, Vector2.up * 2 };
            Vector2[] b = { Vector2.one, new Vector2(3, 1), Vector2.one * 3, new Vector2(1, 3) };
            Vector2[] touching = { new Vector2(2, 0), new Vector2(4, 0), new Vector2(4, 2), new Vector2(2, 2) };
            Assert.That(ProjectionContours.Overlaps(a, b), Is.True);
            Assert.That(ProjectionContours.Overlaps(a, touching), Is.False);
        }
        /** @brief 在现有模型的未保存编辑副本验证换网格、旋转和碰撞器不填孔，不创建玩法对象。 */
        [Test] public void ExistingSource_MeshReplacementAndTransformRefresh()
        {
            GameObject root = PrefabUtility.LoadPrefabContents("Assets/LightPath/Prefabs/Props/3DPlatform.prefab");
            Mesh mesh = Ring();
            try
            {
                var source = root.GetComponent<ProjectionSource>();
                var filter = root.GetComponentInChildren<MeshFilter>(true);
                source.RefreshGeometry();
                filter.sharedMesh = mesh;
                filter.transform.SetPositionAndRotation(new Vector3(8, 11, -7), Quaternion.Euler(37, 63, 11));
                filter.transform.localScale = new Vector3(-2, .3f, 1.7f);
                Assert.That(source.RefreshGeometry(), Is.True, "网格与子物件变换刷新");
                var preview = new ProjectionPreviewGeometry(new[] { source });
                Vector3 direction = filter.transform.TransformVector(Vector3.forward).normalized;
                Ray hole = new Ray(filter.transform.TransformPoint(new Vector3(0, 0, -2)), direction);
                Ray solid = new Ray(filter.transform.TransformPoint(new Vector3(1.5f, 0, -2)), direction);
                Assert.That(source.Intersects(hole, 10), Is.False, "运行时不填孔");
                Assert.That(preview.Intersects(source, hole, 10), Is.False, "编辑器不填孔");
                Assert.That(source.Intersects(solid, 10), Is.True);
                Assert.That(preview.Intersects(source, solid, 10), Is.True);
                Assert.That(source.Geometry.Bounds.Contains(filter.transform.TransformPoint(new Vector3(1.5f, 0, 0))), Is.True, "候选范围来自真实网格");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); Object.DestroyImmediate(mesh); }
        }
        /** @brief 实际Unity圆柱的平顶和圆侧在旋转、负缩放后保持正确。@param angle 旋转角度 @param scaleX 横向缩放 */
        [TestCase(0, 1), TestCase(45, 2), TestCase(90, -2), TestCase(137, .2f)]
        public void Cylinder_UsesFlatCapsInsteadOfCapsule(float angle, float scaleX)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LightPath/Prefabs/Powers/ColorChangeBlue.prefab");
            Mesh mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            var geometry = new ProjectionMeshData(mesh);
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(13, -7, 2), Quaternion.Euler(angle, 29, 17), new Vector3(scaleX, 1.7f, .6f));
            Vector3 center = mesh.bounds.center, extents = mesh.bounds.extents;
            Vector3 local = center + new Vector3(extents.x * .85f, extents.y * .95f, -extents.z * 6);
            Vector3 direction = matrix.MultiplyVector(Vector3.forward);
            Ray ray = new Ray(matrix.MultiplyPoint3x4(local), direction.normalized);
            Assert.That(geometry.Intersects(ray, matrix.inverse, extents.z * 12 * direction.magnitude), Is.True, mesh.name + "靠近平顶的直圆柱侧面");
            local.x = center.x + extents.x * 1.02f;
            Assert.That(geometry.Intersects(new Ray(matrix.MultiplyPoint3x4(local), direction.normalized), matrix.inverse, 100), Is.False);
        }
        /** @brief 多子网格方环保留孔洞，背面入射也遮光，射线不能穿过幕布后方物体。 */
        [Test] public void ConcaveMultiSubmesh_PreservesHoleAndDistance()
        {
            Mesh mesh = Ring();
            try
            {
                var data = new ProjectionMeshData(mesh);
                Assert.That(data.Intersects(new Ray(new Vector3(0, 0, -2), Vector3.forward), Matrix4x4.identity, 4), Is.False, "孔洞");
                foreach (Vector2 point in new[] { new Vector2(1.5f, 0), new Vector2(-1.5f, 0), new Vector2(0, 1.5f), new Vector2(0, -1.5f) })
                {
                    Assert.That(data.Intersects(new Ray(new Vector3(point.x, point.y, -2), Vector3.forward), Matrix4x4.identity, 4), Is.True, "全部子网格");
                    Assert.That(data.Intersects(new Ray(new Vector3(point.x, point.y, 2), Vector3.back), Matrix4x4.identity, 4), Is.True, "双面");
                    Assert.That(data.Intersects(new Ray(new Vector3(point.x, point.y, -2), Vector3.forward), Matrix4x4.identity, 1), Is.False, "幕布距离");
                }
                mesh.UploadMeshData(true);
                Assert.That(mesh.isReadable, Is.False);
                var unreadable = new ProjectionMeshData(mesh);
                Assert.That(unreadable.Intersects(new Ray(new Vector3(1.5f, 0, -2), Vector3.forward), Matrix4x4.identity, 4), Is.True, "编辑器未开启Read/Write也能预览");
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        /** @brief 全部已有源的预览与运行时共用几何结果，绕过Prefab场景物理状态。 */
        [Test] public void ExistingSources_PreviewMatchesRuntimeMeshSampling()
        {
            int checkedSources = 0;
            string[] roomGuids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/LightPath/Rooms" });
            Assert.That(roomGuids.Length, Is.GreaterThan(0), "已有关卡房间");
            foreach (string guid in roomGuids)
            {
                var room = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponent<LightRoom>();
                if (!room) continue;
                var preview = new ProjectionPreviewGeometry(room.Surface.Sources);
                foreach (ProjectionSource source in room.Surface.Sources)
                {
                    if (!source || !source.gameObject.activeSelf) continue;
                    source.InvalidateGeometry(); source.RefreshGeometry();
                    if (!source.Geometry.HasMeshes || !source.Geometry.HasBounds) continue;
                    Bounds bounds = source.Geometry.Bounds;
                    for (int y = 0; y < 9; y++)
                        for (int x = 0; x < 9; x++)
                        {
                            Vector3 origin = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 8f), Mathf.Lerp(bounds.min.y, bounds.max.y, y / 8f), bounds.min.z - 5);
                            Ray ray = new Ray(origin, Vector3.forward);
                            Assert.That(preview.Intersects(source, ray, 20), Is.EqualTo(source.Intersects(ray, 20)), source.name);
                        }
                    checkedSources++;
                }
            }
            Assert.That(checkedSources, Is.GreaterThan(0));
        }
        /** @brief 创建四个子网格组成的方环几何，中心完全没有面。@return 内存网格 */
        private static Mesh Ring()
        {
            var mesh = new Mesh { name = "Projection test ring" };
            mesh.vertices = new[] { new Vector3(-2,-2), new Vector3(2,-2), new Vector3(2,2), new Vector3(-2,2), new Vector3(-1,-1), new Vector3(1,-1), new Vector3(1,1), new Vector3(-1,1) };
            mesh.subMeshCount = 4;
            mesh.SetTriangles(new[] {0,1,5,0,5,4}, 0);
            mesh.SetTriangles(new[] {1,2,6,1,6,5}, 1);
            mesh.SetTriangles(new[] {2,3,7,2,7,6}, 2);
            mesh.SetTriangles(new[] {3,0,4,3,4,7}, 3);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
