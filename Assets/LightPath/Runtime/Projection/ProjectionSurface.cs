using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DelayNoMore.LightPath
{
    /** @brief 分块缓存的自适应投影轮廓；显示与碰撞共享同一多边形数据。 */
    public sealed class ProjectionSurface : MonoBehaviour
    {
        /** @brief 全局参数。 */
        [SerializeField, Tooltip("采样、边界细化与分块参数")] private LightGameConfig _config;
        /** @brief 点光源。 */
        [SerializeField, Tooltip("投影光源")] private Transform _origin;
        /** @brief 世界XY幕布尺寸。 */
        [SerializeField, Tooltip("幕布宽高，不受屏幕分辨率限制")] private Vector2 _size = new Vector2(18, 8);
        /** @brief 可投影模型集合。 */
        [SerializeField, Tooltip("参与投影的模型")] private ProjectionSource[] _sources;
        /** @brief 显示材质。 */
        [SerializeField, Tooltip("URP Unlit材质")] private Material _material;
        /** @brief 绑定关卡统一配置。 @param config 关卡配置 */
        public void BindConfig(LightGameConfig config)
        {
            _config = config;
            Invalidate();
        }
        /** @brief 获取幕布世界矩形。 */
        public Rect Bounds => WorldBounds();
        /** @brief 块数据。 */
        private sealed class Chunk
        {
            /** @brief 世界范围。 */
            public Rect Bounds;
            /** @brief 根对象。 */
            public GameObject Root;
            /** @brief 区域数据。 */
            public readonly List<ProjectionRegion> Regions = new List<ProjectionRegion>();
            /** @brief 批次对象。 */
            public readonly List<GameObject> Batches = new List<GameObject>();
            /** @brief 是否待重建。 */
            public bool Dirty = true;
        }
        /** @brief 候选源及范围。 */
        private readonly Dictionary<ProjectionSource, Rect> _sourceBounds = new Dictionary<ProjectionSource, Rect>();
        /** @brief 用于侦测外部位姿改变。 */
        private readonly Dictionary<ProjectionSource, Matrix4x4> _sourcePoses = new Dictionary<ProjectionSource, Matrix4x4>();
        /** @brief 已加载投影块。 */
        private readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        /** @brief 当前活动区域。 */
        private readonly List<ProjectionRegion> _regions = new List<ProjectionRegion>();
        /** @brief 当前构建候选源。 */
        private readonly List<ProjectionSource> _candidates = new List<ProjectionSource>();
        /** @brief 单块共享顶点查询缓存。 */
        private readonly Dictionary<Vector2, ProjectionRegion> _samples = new Dictionary<Vector2, ProjectionRegion>();
        /** @brief 渲染批次对应的颜色规则。 */
        private readonly Dictionary<GameObject, ProjectionRegion> _batchRules = new Dictionary<GameObject, ProjectionRegion>();
        /** @brief 缓存显示参数。 */
        private MaterialPropertyBlock _properties;
        /** @brief 当前加载中心。 */
        private Vector2 _focus;
        /** @brief 中心是否初始化。 */
        private bool _focused;
        /** @brief 当前活动块包围。 */
        private RectInt _loadedRange;
        /** @brief 分块使用的幕布世界范围；房间平移后必须重建世界坐标缓存。 */
        private Rect _cachedWorldBounds;
        /** @brief 分块使用的平面深度。 */
        private float _cachedPlaneDepth;
        /** @brief 是否记录过分块布局。 */
        private bool _hasCachedLayout;
        /** @brief 幕布移动或尺寸改变后释放旧位置的分块，保持渲染和碰撞共用新轮廓。 */
        private void RefreshChunkLayout()
        {
            Rect bounds = WorldBounds();
            float depth = transform.position.z;
            if (_hasCachedLayout && _cachedWorldBounds == bounds && Mathf.Approximately(_cachedPlaneDepth, depth)) return;
            foreach (Chunk chunk in _chunks.Values) DisposeChunk(chunk);
            _chunks.Clear();
            _regions.Clear();
            _sourceBounds.Clear();
            _sourcePoses.Clear();
            _loadedRange = default;
            _cachedWorldBounds = bounds;
            _cachedPlaneDepth = depth;
            _hasCachedLayout = true;
        }
        /** @brief 投影参数版本缓存。 */
        private Vector3 _lastOrigin;
        /** @brief 最近一次显示参数。 */
        private int _selected = -1;
        /** @brief 预览状态。 */
        private bool _preview;
        /** @brief 供光状态。 */
        private bool _powered = true;
        /** @brief 获取区域。 */
        public IReadOnlyList<ProjectionRegion> Regions => _regions;
        /** @brief 获取模型集合。 */
        public IReadOnlyList<ProjectionSource> Sources => _sources;
        /** @brief 获取光源。 */
        public Transform Origin => _origin;
        /** @brief 舞台换算后的有效光源；不修改原场景锚点或共享配置。 */
        private Vector3? _stageOrigin;
        /** @brief 实际用于投影求解的光源。 */
        public Vector3 ProjectionOrigin => _stageOrigin ?? _origin.position;
        /** @brief 当前关卡参数。 */
        public LightGameConfig Configuration => _config;
        /** @brief 设置舞台真实光源在模拟空间的位置，变更后使轮廓与碰撞缓存失效。@param origin 光源位置，空值恢复关卡原光源 @return 是否改变 */
        public bool SetStageOrigin(Vector3? origin)
        {
            if (_stageOrigin == origin) return false;
            _stageOrigin = origin;
            Invalidate();
            return true;
        }
        /** @brief 获取幕布尺寸。 */
        public Vector2 Size => _size;
        /** @brief 最近重建耗时，仅CPU，不包含GPU帧时间。 */
        public double LastBuildMilliseconds
        {
            get; private set;
        }
        /** @brief 最近重建中实际查询的缓存采样数。 */
        public int LastSampleCount
        {
            get; private set;
        }
        /** @brief 获取当前缓存块数量。 */
        public int LoadedChunks => _chunks.Count;
        /** @brief 装配接口。 @param config 配置 @param origin 光源 @param sources 源 @param material 材质 */
        public void Configure(LightGameConfig config, Transform origin, ProjectionSource[] sources, Material material)
        {
            _config = config;
            _origin = origin;
            _sources = sources;
            _material = material;
        }
        /** @brief 投影点到幕布。 @param point 世界点 @return 世界XY点 */
        public Vector3 ProjectPoint(Vector3 point)
        {
            Vector3 direction = point - ProjectionOrigin;
            return Mathf.Abs(direction.z) < .0001f ? point : ProjectionOrigin + direction * ((transform.position.z - ProjectionOrigin.z) / direction.z);
        }
        /** @brief 查询所有源的连续投影。 @param point 世界采样点 @return 区域标签 */
        public ProjectionRegion Sample(Vector3 point) => SampleSources(point, _sources);
        /** @brief 编辑器注入几何查询，复用运行时连续混色规则。 @param point 幕布点 @param intersects 几何查询 @return 投影区域 */
        public ProjectionRegion SamplePreview(Vector3 point, System.Func<ProjectionSource, Ray, float, bool> intersects) => SampleSources(point, _sources, intersects);
        /** @brief 查询给定候选集合。 @param point 世界点 @param sources 候选 @return 标签 */
        private ProjectionRegion SampleSources(Vector3 point, IReadOnlyList<ProjectionSource> sources, System.Func<ProjectionSource, Ray, float, bool> intersects = null)
        {
            Vector3 delta = point - ProjectionOrigin;
            Ray ray = new Ray(ProjectionOrigin, delta.normalized);
            Color sum = Color.clear;
            float weight = 0;
            bool opaque = false;
            ProjectionSource portal = null;
            if (sources == null)
                return new ProjectionRegion(new Bounds(point, Vector3.zero), Color.black, false, null, false);
            foreach (ProjectionSource source in sources)
            {
                if (!source || !source.Definition || (intersects == null && !source.gameObject.activeInHierarchy) || !(intersects == null ? source.Intersects(ray, delta.magnitude) : intersects(source, ray, delta.magnitude)))
                    continue;
                if (!source.Definition.Transmissive)
                    opaque = true;
                else
                {
                    float w = source.Definition.Weight;
                    sum += source.Definition.GetColor(_config).linear * w;
                    weight += w;
                    if (source.Definition.Portal && (!portal || string.CompareOrdinal(source.name, portal.name) < 0))
                        portal = source;
                }
            }
            Color color = weight > 0 ? (sum / weight).gamma : Color.black;
            color.a = 1;
            return new ProjectionRegion(new Bounds(point, Vector3.zero), opaque ? Color.black : color, !opaque && weight > 0, opaque ? null : portal, opaque || weight > 0);
        }
        /** @brief 获取源的保守幕布范围。 @param source 源 @return 世界矩形 */
        private Rect SourceBounds(ProjectionSource source)
        {
            source.RefreshGeometry();
            bool found = false;
            Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
            foreach (Bounds bounds in GeometryBounds(source))
            {
                if (bounds.min.z <= ProjectionOrigin.z + .001f)
                    return WorldBounds();
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector2 projected = ProjectPoint(point);
                    min = Vector2.Min(min, projected);
                    max = Vector2.Max(max, projected);
                    found = true;
                }
            }
            return found ? Rect.MinMaxRect(min.x - .001f, min.y - .001f, max.x + .001f, max.y + .001f) : new Rect();
        }
        /** @brief 候选范围来自实际模型，避免Collider过小截断异形投影。@param source 光学源 @return 世界包围集合 */
        private static IEnumerable<Bounds> GeometryBounds(ProjectionSource source)
        {
            if (source.Geometry.HasMeshes)
            {
                if (source.Geometry.HasBounds) yield return source.Geometry.Bounds;
                yield break;
            }
            foreach (Collider collider in source.GetComponentsInChildren<Collider>())
                if (collider.enabled) yield return collider.bounds;
        }
        /** @brief 幕布世界范围。 @return 矩形 */
        private Rect WorldBounds() => new Rect(transform.position.x - _size.x / 2, transform.position.y, _size.x, _size.y);
        /** @brief 将变更源的新旧范围标记为脏。 @param source 空值标记所有已加载块 */
        public void Invalidate(ProjectionSource source = null)
        {
            Physics.SyncTransforms();
            if (!source)
            {
                foreach (Chunk chunk in _chunks.Values)
                    chunk.Dirty = true;
                _sourceBounds.Clear();
                return;
            }
            Rect next = SourceBounds(source);
            Rect previous = _sourceBounds.TryGetValue(source, out Rect old) ? old : next;
            Rect union = Rect.MinMaxRect(Mathf.Min(previous.xMin, next.xMin), Mathf.Min(previous.yMin, next.yMin), Mathf.Max(previous.xMax, next.xMax), Mathf.Max(previous.yMax, next.yMax));
            foreach (Chunk chunk in _chunks.Values)
                if (chunk.Bounds.Overlaps(union))
                    chunk.Dirty = true;
            _sourceBounds[source] = next;
        }
        /** @brief 更新角色附近加载范围，先生成碰撞再允许运动。 @param focus 世界中心 */
        public void SetFocus(Vector2 focus)
        {
            RefreshChunkLayout();
            _focus = focus;
            _focused = true;
            if (!_config || !_origin || !_material)
                return;
            Rect world = WorldBounds();
            float size = _config.ChunkSize, radius = _config.ActiveRadius;
            int columns = Mathf.Max(1, Mathf.CeilToInt(world.width / size)), rows = Mathf.Max(1, Mathf.CeilToInt(world.height / size));
            int x0 = Mathf.Clamp(Mathf.FloorToInt((focus.x - radius - world.xMin) / size), 0, columns - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((focus.x + radius - world.xMin) / size), 0, columns - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((focus.y - radius - world.yMin) / size), 0, rows - 1), y1 = Mathf.Clamp(Mathf.FloorToInt((focus.y + radius - world.yMin) / size), 0, rows - 1);
            RectInt range = new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            if (range == _loadedRange && _chunks.Count > 0)
                return;
            _loadedRange = range;
            var remove = new List<Vector2Int>();
            foreach (var pair in _chunks)
                if (!range.Contains(pair.Key))
                {
                    DisposeChunk(pair.Value);
                    remove.Add(pair.Key);
                }
            foreach (Vector2Int key in remove)
                _chunks.Remove(key);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2Int key = new Vector2Int(x, y);
                    if (_chunks.ContainsKey(key))
                        continue;
                    Rect rect = Rect.MinMaxRect(world.xMin + x * size, world.yMin + y * size, Mathf.Min(world.xMax, world.xMin + (x + 1) * size), Mathf.Min(world.yMax, world.yMin + (y + 1) * size));
                    _chunks.Add(key, new Chunk { Bounds = rect });
                }
            Rebuild();
        }
        /** @brief 重建脏块，结束前显示与碰撞原子切换。 */
        public void Rebuild()
        {
            if (!_config || !_origin || !_material || _sources == null)
                return;
            RefreshChunkLayout();
            if (_chunks.Count == 0)
            {
                SetFocus(_focused ? _focus : (Vector2)transform.position);
                return;
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            LastSampleCount = 0;
            Physics.SyncTransforms();
            if (_lastOrigin != ProjectionOrigin)
            {
                Invalidate();
                _lastOrigin = ProjectionOrigin;
            }
            foreach (ProjectionSource source in _sources)
                if (source)
                {
                    source.Initialize();
                    bool geometryChanged = source.RefreshGeometry();
                    Matrix4x4 pose = source.transform.localToWorldMatrix;
                    if (geometryChanged || (_sourcePoses.TryGetValue(source, out Matrix4x4 previous) && previous != pose))
                        Invalidate(source);
                    _sourcePoses[source] = pose;
                    if (!_sourceBounds.ContainsKey(source))
                        _sourceBounds[source] = SourceBounds(source);
                }
            foreach (Chunk chunk in _chunks.Values)
            {
                if (!chunk.Dirty)
                    continue;
                _candidates.Clear();
                _samples.Clear();
                foreach (ProjectionSource source in _sources)
                    if (source && _sourceBounds[source].Overlaps(chunk.Bounds))
                        _candidates.Add(source);
                chunk.Regions.Clear();
                if (_candidates.Count > 0)
                {
                    float cell = _config.CellSize;
                    for (float y = chunk.Bounds.yMin; y < chunk.Bounds.yMax - .00001f; y += cell)
                        for (float x = chunk.Bounds.xMin; x < chunk.Bounds.xMax - .00001f; x += cell)
                            BuildCell(chunk, Rect.MinMaxRect(x, y, Mathf.Min(x + cell, chunk.Bounds.xMax), Mathf.Min(y + cell, chunk.Bounds.yMax)), 0);
                }
                LastSampleCount += _samples.Count;
                MergeRectangles(chunk);
                BuildBatches(chunk);
                chunk.Dirty = false;
            }
            _regions.Clear();
            foreach (Chunk chunk in _chunks.Values)
                _regions.AddRange(chunk.Regions);
            Refresh(_selected, _preview, _powered);
            Physics2D.SyncTransforms();
            LastBuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        }
        /** @brief 查询当前构建的XY点。 @param point 点 @return 标签 */
        private ProjectionRegion At(Vector2 point)
        {
            if (!_samples.TryGetValue(point, out ProjectionRegion value))
            {
                value = SampleSources(new Vector3(point.x, point.y, transform.position.z), _candidates);
                _samples[point] = value;
            }
            return value;
        }
        /** @brief 自适应细分变化区域。 @param chunk 块 @param rect 网格 @param depth 层级 */
        private void BuildCell(Chunk chunk, Rect rect, int depth)
        {
            bool covered = false;
            foreach (ProjectionSource source in _candidates)
                if (_sourceBounds[source].Overlaps(rect))
                {
                    covered = true;
                    break;
                }
            if (!covered)
                return;
            Vector2 a = new Vector2(rect.xMin, rect.yMin), b = new Vector2(rect.xMax, rect.yMin), c = new Vector2(rect.xMax, rect.yMax), d = new Vector2(rect.xMin, rect.yMax), center = rect.center;
            ProjectionRegion label = At(center);
            bool same = label.SameAs(At(a)) && label.SameAs(At(b)) && label.SameAs(At(c)) && label.SameAs(At(d)) && label.SameAs(At((a + b) / 2)) && label.SameAs(At((b + c) / 2)) && label.SameAs(At((c + d) / 2)) && label.SameAs(At((d + a) / 2));
            if (same)
                foreach (ProjectionSource source in _candidates)
                {
                    Rect bounds = _sourceBounds[source];
                    if (bounds.Overlaps(rect) && !(bounds.Contains(a) && bounds.Contains(c)))
                    {
                        same = false;
                        break;
                    }
                }
            if (same)
            {
                if (label.Exists)
                    AddPolygon(chunk, label, new[] { a, b, c, d });
                return;
            }
            if (depth < _config.EdgeRefinement)
            {
                BuildCell(chunk, Rect.MinMaxRect(a.x, a.y, center.x, center.y), depth + 1);
                BuildCell(chunk, Rect.MinMaxRect(center.x, a.y, b.x, center.y), depth + 1);
                BuildCell(chunk, Rect.MinMaxRect(center.x, center.y, c.x, c.y), depth + 1);
                BuildCell(chunk, Rect.MinMaxRect(d.x, center.y, center.x, d.y), depth + 1);
                return;
            }
            BuildTriangle(chunk, a, b, center);
            BuildTriangle(chunk, b, c, center);
            BuildTriangle(chunk, c, d, center);
            BuildTriangle(chunk, d, a, center);
        }
        /** @brief 对三角形中每种标签裁切边界。 @param chunk 块 @param a 顶点 @param b 顶点 @param c 顶点 */
        private void BuildTriangle(Chunk chunk, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2[] vertices = { a, b, c };
            ProjectionRegion[] labels = { At(a), At(b), At(c) };
            for (int k = 0; k < 3; k++)
            {
                ProjectionRegion label = labels[k];
                if (!label.Exists)
                    continue;
                bool duplicate = false;
                for (int j = 0; j < k; j++)
                    if (label.SameAs(labels[j]))
                        duplicate = true;
                if (duplicate)
                    continue;
                var polygon = new List<Vector2>();
                for (int i = 0; i < 3; i++)
                {
                    int previous = (i + 2) % 3;
                    bool inside = label.SameAs(labels[i]), wasInside = label.SameAs(labels[previous]);
                    if (inside != wasInside)
                    {
                        Vector2 low = vertices[previous], high = vertices[i];
                        for (int iteration = 0; iteration < 7; iteration++)
                        {
                            Vector2 mid = (low + high) / 2;
                            if (At(mid).SameAs(label) == wasInside)
                                low = mid;
                            else
                                high = mid;
                        }
                        polygon.Add((low + high) / 2);
                    }
                    if (inside)
                        polygon.Add(vertices[i]);
                }
                if (polygon.Count >= 3)
                    AddPolygon(chunk, label, polygon.ToArray());
            }
        }
        /** @brief 保存共同渲染/碰撞多边形。 @param chunk 块 @param label 标签 @param polygon 轮廓 */
        private void AddPolygon(Chunk chunk, ProjectionRegion label, Vector2[] polygon)
        {
            float twiceArea = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i] - polygon[0], b = polygon[(i + 1) % polygon.Length] - polygon[0];
                twiceArea += a.x * b.y - b.x * a.y;
            }
            if (Mathf.Abs(twiceArea) < .0000001f)
                return;
            Bounds bounds = new Bounds(new Vector3(polygon[0].x, polygon[0].y, transform.position.z), Vector3.zero);
            foreach (Vector2 point in polygon)
                bounds.Encapsulate(new Vector3(point.x, point.y, transform.position.z));
            if (bounds.size.x < .0001f || bounds.size.y < .0001f)
                return;
            chunk.Regions.Add(new ProjectionRegion(bounds, label.Color, label.Colored, label.Portal, true, polygon));
        }
        /** @brief 合并同一行的内部矩形，降低碰撞路径和顶点数量。 @param chunk 块 */
        private static void MergeRectangles(Chunk chunk)
        {
            var rows = new Dictionary<(Color, bool, ProjectionSource, float, float), List<ProjectionRegion>>();
            var result = new List<ProjectionRegion>();
            foreach (ProjectionRegion region in chunk.Regions)
            {
                bool rectangle = region.Outline.Length == 4;
                foreach (Vector2 point in region.Outline)
                    rectangle &= (Mathf.Abs(point.x - region.Bounds.min.x) < .00001f || Mathf.Abs(point.x - region.Bounds.max.x) < .00001f) && (Mathf.Abs(point.y - region.Bounds.min.y) < .00001f || Mathf.Abs(point.y - region.Bounds.max.y) < .00001f);
                if (!rectangle)
                {
                    result.Add(region);
                    continue;
                }
                var key = (region.Color, region.Colored, region.Portal, region.Bounds.min.y, region.Bounds.max.y);
                if (!rows.TryGetValue(key, out List<ProjectionRegion> list))
                    rows.Add(key, list = new List<ProjectionRegion>());
                list.Add(region);
            }
            foreach (List<ProjectionRegion> row in rows.Values)
            {
                row.Sort((a, b) => a.Bounds.min.x.CompareTo(b.Bounds.min.x));
                ProjectionRegion current = row[0];
                for (int i = 1; i < row.Count; i++)
                {
                    ProjectionRegion next = row[i];
                    if (Mathf.Abs(current.Bounds.max.x - next.Bounds.min.x) > .00001f)
                    {
                        result.Add(current);
                        current = next;
                        continue;
                    }
                    Bounds bounds = current.Bounds;
                    bounds.Encapsulate(next.Bounds);
                    Vector2 min = bounds.min, max = bounds.max;
                    current = new ProjectionRegion(bounds, current.Color, current.Colored, current.Portal, true, new[] { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y) });
                }
                result.Add(current);
            }
            chunk.Regions.Clear();
            chunk.Regions.AddRange(result);
        }
        /** @brief 根据标签合并显示对象，保留精确分区多边形。 @param chunk 块 */
        private void BuildBatches(Chunk chunk)
        {
            GameObject oldRoot = chunk.Root;
            foreach (GameObject batch in chunk.Batches)
            {
                _batchRules.Remove(batch);
                Mesh mesh = batch.GetComponent<MeshFilter>().sharedMesh;
                if (mesh)
                    Destroy(mesh);
            }
            chunk.Batches.Clear();
            chunk.Root = new GameObject("Projection chunk");
            chunk.Root.transform.SetParent(transform, true);
            chunk.Root.transform.position = new Vector3(chunk.Bounds.xMin, chunk.Bounds.yMin, transform.position.z);
            chunk.Root.SetActive(false);
            var groups = new Dictionary<(Color, bool, ProjectionSource), List<ProjectionRegion>>();
            foreach (ProjectionRegion region in chunk.Regions)
            {
                var key = (region.Color, region.Colored, region.Portal);
                if (!groups.TryGetValue(key, out List<ProjectionRegion> list))
                    groups.Add(key, list = new List<ProjectionRegion>());
                list.Add(region);
            }
            foreach (List<ProjectionRegion> list in groups.Values)
            {
                GameObject batch = new GameObject("Projection contour", typeof(MeshFilter), typeof(MeshRenderer), typeof(PolygonCollider2D));
                batch.transform.SetParent(chunk.Root.transform, false);
                batch.layer = 9;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                PolygonCollider2D collider = batch.GetComponent<PolygonCollider2D>();
                collider.pathCount = list.Count;
                collider.isTrigger = list[0].Portal;
                for (int index = 0; index < list.Count; index++)
                {
                    Vector2[] outline = list[index].Outline;
                    Vector2[] local = new Vector2[outline.Length];
                    int start = vertices.Count;
                    for (int i = 0; i < outline.Length; i++)
                    {
                        local[i] = outline[i] - (Vector2)chunk.Root.transform.position;
                        vertices.Add(local[i]);
                    }
                    for (int i = 1; i < outline.Length - 1; i++)
                    {
                        triangles.Add(start);
                        triangles.Add(start + i + 1);
                        triangles.Add(start + i);
                    }
                    collider.SetPath(index, local);
                }
                Mesh mesh = new Mesh { name = "Projection contour", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                batch.GetComponent<MeshFilter>().sharedMesh = mesh;
                batch.GetComponent<Renderer>().sharedMaterial = _material;
                chunk.Batches.Add(batch);
                _batchRules[batch] = list[0];
            }
            if (oldRoot)
            {
                oldRoot.SetActive(false);
                Destroy(oldRoot);
            }
            chunk.Root.SetActive(true);
        }
        /** @brief 释放卸载块。 @param chunk 块 */
        private void DisposeChunk(Chunk chunk)
        {
            foreach (GameObject batch in chunk.Batches)
                if (batch)
                {
                    _batchRules.Remove(batch);
                    Mesh mesh = batch.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh)
                        Destroy(mesh);
                }
            if (chunk.Root)
            {
                chunk.Root.SetActive(false);
                Destroy(chunk.Root);
            }
        }
        /** @brief 更新颜色显示，不重建几何。 @param selected 全局色 @param preview 预览 @param powered 供光 */
        public void Refresh(int selected, bool preview, bool powered)
        {
            if (_properties == null)
                _properties = new MaterialPropertyBlock();
            _selected = selected;
            _preview = preview;
            _powered = powered;
            foreach (var pair in _batchRules)
            {
                if (!pair.Key)
                    continue;
                ProjectionRegion region = pair.Value;
                bool hidden = region.Colored && _config.Matches(region.Color, selected);
                Renderer renderer = pair.Key.GetComponent<Renderer>();
                renderer.enabled = powered && (!hidden || preview);
                pair.Key.GetComponent<Collider2D>().enabled = powered && !hidden;
                Color color = region.Colored ? _config.DisplayColor(region.Color) : new Color(.06f, .07f, .09f);
                if (hidden && preview)
                    color = Color.Lerp(color, Color.white, .45f);
                _properties.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(_properties);
            }
        }
        /** @brief 检查角色与有效/隐藏区域。 @param body 角色 @param selected 全局色 @param hiddenOnly 仅隐藏区域 @return 是否相交 */
        public bool Overlaps(Bounds body, int selected, bool hiddenOnly)
        {
            foreach (ProjectionRegion region in _regions)
                if (!region.Portal && hiddenOnly == (region.Colored && _config.Matches(region.Color, selected)) && region.Contains(body))
                    return true;
            return false;
        }
        /** @brief 获取传送入口。 @param body 角色 @param selected 全局色 @return 门或空 */
        public ProjectionSource FindPortal(Bounds body, int selected)
        {
            foreach (ProjectionRegion region in _regions)
                if (region.Portal && !_config.Matches(region.Color, selected) && region.Contains(body))
                    return region.Portal;
            return null;
        }
        /** @brief 在最终可见投影上反查可操作源，采用真实网格射线而非包围盒。@param point 幕布世界点 @param selected 背景色 @return 最近的可操作源索引 */
        public int PickSource(Vector3 point, int selected)
        {
            if (!WorldBounds().Contains(point)) return -1;
            foreach (ProjectionSource source in _sources) if (source) source.RefreshGeometry();
            ProjectionRegion region = Sample(point);
            if (!region.Exists || (region.Colored && _config.Matches(region.Color, selected))) return -1;
            Vector3 direction = point - ProjectionOrigin;
            float length = direction.magnitude;
            Ray ray = new Ray(ProjectionOrigin, direction.normalized);
            int result = -1; float nearest = float.PositiveInfinity;
            for (int i = 0; i < _sources.Length; i++)
            {
                ProjectionSource source = _sources[i];
                if (!source || !source.gameObject.activeInHierarchy || !source.Definition || !source.Definition.Movable) continue;
                if (!source.Intersects(ray, length)) continue;
                if (region.Portal && region.Portal != source) continue;
                if (!region.Colored && source.Definition.Transmissive) continue;
                float distance = source.TryHit(ray, out float hit) ? hit : Vector3.Distance(ProjectionOrigin, source.transform.position);
                if (distance < nearest) { nearest = distance; result = i; }
            }
            return result;
        }
        /** @brief 查询指定门的光学投影是否有效，独立于目标块是否已加载。 @param source 门 @param selected 全局色 @return 是否有效 */
        public bool PortalActive(ProjectionSource source, int selected)
        {
            Vector3 point = ProjectPoint(source.transform.position);
            if (!WorldBounds().Contains(point))
                return false;
            ProjectionRegion region = Sample(point);
            return region.Portal == source && !_config.Matches(region.Color, selected);
        }
        /** @brief 释放运行时网格。 */
        private void OnDestroy()
        {
            foreach (Chunk chunk in _chunks.Values)
                foreach (GameObject batch in chunk.Batches)
                    if (batch && batch.GetComponent<MeshFilter>().sharedMesh)
                        Destroy(batch.GetComponent<MeshFilter>().sharedMesh);
        }
        /** @brief 显示世界幕布范围。 */
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position + Vector3.up * _size.y / 2, new Vector3(_size.x, _size.y, .05f));
        }
    }
}
