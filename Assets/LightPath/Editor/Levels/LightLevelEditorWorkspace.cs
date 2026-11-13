using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 关卡窗口的2D预览画布与手动对象编辑工作区。 */
    public sealed partial class LightLevelEditor
    {
        /** @brief 画布中心世界坐标。 */
        private Vector2 _center = new Vector2(0, 4);
        /** @brief 每米显示像素。 */
        private float _zoom = 35;
        /** @brief 当前画布工具。 */
        private int _tool;
        /** @brief 当前选择对象。 */
        private GameObject _picked;
        /** @brief 等待配对的首个门。 */
        /** @brief 拖动中的对象。 */
        private GameObject _dragging;
        /** @brief 拖动抓取偏移。 */
        private Vector2 _grabOffset;
        /** @brief 当前拖动Undo组。 */
        private int _dragGroup;
        /** @brief 对象检查器。 */
        private UnityEditor.Editor _objectEditor;
        /** @brief 投影预览图片。 */
        private Texture2D _projectionPreview;
        /** @brief 图片对应世界矩形。 */
        private Rect _previewWorld;
        /** @brief 编辑器预览全局色号，-1为基础白色。 */
        private int _previewColor = -1;
        /** @brief 是否显示投影源标记、名称和网格。 */
        private bool _previewGuides = true;
        /** @brief 上次投影内容签名。 */
        private int _previewSignature;
        /** @brief 最近一次布局的画布矩形，用于窗口输入回归定位。 */
        private Rect _canvasRect;
        /** @brief GUI内容相对原生窗口的偏移，用于模拟原生鼠标事件。 */
        private Vector2 _canvasWindowOffset;
        /** @brief 以编辑器更新频率自动检查投影变更。 */
        private void OnInspectorUpdate()
        {
            Repaint();
            if (!Editable() || !_room.Surface || !_room.Surface.Origin || _layoutMode != 0)
                return;
            if (!_projectionPreview || _previewSignature != PreviewSignature())
            {
                BuildPreview();
                Repaint();
            }
        }
        /** @brief 获取影响预览的几何与配置签名。 @return 内容签名 */
        private int PreviewSignature()
        {
            unchecked
            {
                int hash = _previewColor ^ LightStagePreview.Signature;
                hash = hash * 31 + _room.Surface.transform.localToWorldMatrix.GetHashCode();
                hash = hash * 31 + _room.Surface.ProjectionOrigin.GetHashCode();
                hash = hash * 31 + _room.Surface.Size.GetHashCode();
                if (_config)
                    hash = hash * 31 + EditorJsonUtility.ToJson(_config).GetHashCode();
                if (_room.Surface.Sources != null)
                    foreach (ProjectionSource source in _room.Surface.Sources)
                    {
                        if (!source)
                            continue;
                        hash = hash * 31 + source.GetHashCode();
                        hash = hash * 31 + source.gameObject.activeSelf.GetHashCode();
                        if (source.Definition)
                            hash = hash * 31 + EditorJsonUtility.ToJson(source.Definition).GetHashCode();
                        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                        {
                            hash = hash * 31 + filter.transform.localToWorldMatrix.GetHashCode();
                            hash = hash * 31 + ProjectionMeshGeometry.ActiveWithinSource(filter.transform, source.transform).GetHashCode();
                            if (filter.sharedMesh)
                            {
                                hash = hash * 31 + filter.sharedMesh.GetHashCode();
                                hash = hash * 31 + EditorUtility.GetDirtyCount(filter.sharedMesh);
                                string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                                if (!string.IsNullOrEmpty(path)) hash = hash * 31 + AssetDatabase.GetAssetDependencyHash(path).GetHashCode();
                            }
                        }
                        foreach (Collider shape in source.GetComponentsInChildren<Collider>(true))
                        {
                            hash = hash * 31 + shape.transform.localToWorldMatrix.GetHashCode();
                            hash = hash * 31 + EditorJsonUtility.ToJson(shape).GetHashCode();
                            hash = hash * 31 + shape.gameObject.activeSelf.GetHashCode();
                        }
                    }
                return hash;
            }
        }
        /** @brief 绘制右侧工作区。 */
        private void DrawWorkspace()
        {
            if (_picked && _picked.GetComponentInParent<LightRoom>() != _room)
                _picked = null;
            EditorGUILayout.BeginVertical();
            _layoutMode = GUILayout.Toolbar(_layoutMode, new[] { "编辑房间", "编辑关卡" });
            if (_layoutMode == 1)
            {
                DrawLayoutWorkspace();
                EditorGUILayout.EndVertical();
                return;
            }
            using (new EditorGUI.DisabledScope(!Editable()))
            {
                int tool = GUILayout.Toolbar(_tool < 2 ? _tool : -1, new[] { "选择/移动", "放模板" });
                if (tool >= 0) _tool = tool;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("全图"))
                    FrameMap();
                if (GUILayout.Button("刷新投影预览"))
                    BuildPreview();
                if (GUILayout.Button("删除选中"))
                    DeletePicked();
                if (GUILayout.Button("复制选中"))
                    DuplicatePicked();
                EditorGUILayout.EndHorizontal();
            }
            DrawSampling();
            _previewColor = EditorGUILayout.Popup("预览全局滤镜", Mathf.Clamp(_previewColor, -1, 3) + 1, new[] { "幕布默认色", "颜色1", "颜色2", "颜色3", "颜色4" }) - 1;
            _previewGuides = EditorGUILayout.Toggle("显示编辑辅助标记", _previewGuides);
            Rect canvas = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _canvasRect = canvas;
            _canvasWindowOffset = GUIUtility.GUIToScreenPoint(Vector2.zero) - position.position;
            DrawCanvas(canvas);
            DrawLevel3D();

            GUILayout.Label("滚轮缩放 · 中键平移 · Delete删除 · Esc取消连门/拖动 · Ctrl+Z撤销", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
        /** @brief 获取幕布矩形。 @return 世界XY矩形 */
        private Rect MapRect()
        {
            if (!_room || !_room.Surface)
                return new Rect(-9, 0, 18, 8);
            Vector2 size = new SerializedObject(_room.Surface).FindProperty("_size").vector2Value;
            Vector3 position = _room.Surface.transform.position;
            return new Rect(position.x - size.x / 2, position.y, size.x, size.y);
        }
        /** @brief 缩放到全图。 */
        private void FrameMap()
        {
            Rect map = MapRect();
            _center = map.center;
            _zoom = Mathf.Clamp((position.width - 660) / Mathf.Max(1, map.width), 2, 150);
        }
        /** @brief 世界点映射画布。 @param world 世界XY @param canvas 画布 @return 像素点 */
        private Vector2 ToCanvas(Vector2 world, Rect canvas) => canvas.center + new Vector2(world.x - _center.x, _center.y - world.y) * _zoom;
        /** @brief 画布点映射世界。 @param point 像素点 @param canvas 画布 @return 世界XY */
        private Vector2 ToWorld(Vector2 point, Rect canvas) => _center + new Vector2(point.x - canvas.center.x, canvas.center.y - point.y) / _zoom;
        /** @brief 获取源在2D幕布上的编辑位置。 @param root 对象 @return 平面坐标 */
        private Vector2 DisplayPosition(GameObject root)
        {
            if (root.GetComponent<ProjectionSource>() && _room.Surface && _room.Surface.Origin)
                return _room.Surface.ProjectPoint(root.transform.position);
            return root.transform.position;
        }
        /** @brief 按2D编辑位置移动源，保留3D深度。 @param root 对象 @param target 平面坐标 */
        private void MoveTo(GameObject root, Vector2 target)
        {
            Vector3 world = root.transform.position;
            if (root.GetComponent<ProjectionSource>() && _room.Surface && _room.Surface.Origin)
            {
                Vector3 origin = _room.Surface.ProjectionOrigin;
                float denominator = _room.Surface.transform.position.z - origin.z;
                if (Mathf.Abs(denominator) < .001f)
                    return;
                float ratio = (world.z - origin.z) / denominator;
                world.x = origin.x + (target.x - origin.x) * ratio;
                world.y = origin.y + (target.y - origin.y) * ratio;
            }
            else
            {
                world.x = target.x;
                world.y = target.y;
            }
            root.transform.position = world;
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        }
        /** @brief 获取可直接编辑的已有对象。 @return 去重对象集合 */
        private List<GameObject> Objects()
        {
            var objects = new List<GameObject>();
            objects.AddRange(_room.GetComponentsInChildren<RoomDecoration>(true).Select(x => x.gameObject));
            objects.AddRange(_room.GetComponentsInChildren<LevelExit>(true).Select(x => x.gameObject));
            objects.AddRange(_room.GetComponentsInChildren<RoomCheckpoint>(true).Select(x => x.gameObject));
            objects.AddRange(_room.GetComponentsInChildren<RoomSpawnPoint>(true).Select(x => x.gameObject));
            objects.AddRange(_room.GetComponentsInChildren<ProjectionAbilityKey>(true).Select(x => x.gameObject));
            objects.AddRange(_room.GetComponentsInChildren<ColorPlatform>(true).Select(x => x.gameObject));
            objects.AddRange(_room.GetComponentsInChildren<ProjectionSource>(true).Select(x => x.gameObject));
            var serialized = new SerializedObject(_room);
            foreach (string field in new[] { "_spawn", "_exit" })
            {
                Transform marker = serialized.FindProperty(field).objectReferenceValue as Transform;
                if (marker)
                    objects.Add(marker.gameObject);
            }
            return objects.Distinct().ToList();
        }
        /** @brief 绘制画布、轮廓、门线并处理鼠标。 @param canvas 画布矩形 */
        private void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(.08f, .09f, .12f));
            if (!Editable())
            {
                GUI.Label(canvas, "选择场景中的目标房间后开始编辑");
                return;
            }
            GUI.BeginClip(canvas);
            Rect local = new Rect(0, 0, canvas.width, canvas.height);
            Handles.BeginGUI();
            Rect map = MapRect();
            Vector2 top = ToCanvas(new Vector2(map.xMin, map.yMax), local);
            Rect mapPixels = new Rect(top, map.size * _zoom);
            EditorGUI.DrawRect(mapPixels, new Color(.15f, .17f, .21f));
            if (_projectionPreview)
            {
                Vector2 imageTop = ToCanvas(new Vector2(_previewWorld.xMin, _previewWorld.yMax), local);
                GUI.DrawTexture(new Rect(imageTop, _previewWorld.size * _zoom), _projectionPreview);
            }
            float spacing = Mathf.Max(_grid, Mathf.Pow(10, Mathf.Ceil(Mathf.Log10(18 / _zoom))));
            Vector2 lo = ToWorld(new Vector2(0, local.height), local), hi = ToWorld(new Vector2(local.width, 0), local);
            Handles.color = new Color(1, 1, 1, .08f);
            for (float x = _previewGuides ? Mathf.Ceil(lo.x / spacing) * spacing : float.PositiveInfinity; x <= hi.x; x += spacing)
                Handles.DrawLine(ToCanvas(new Vector2(x, lo.y), local), ToCanvas(new Vector2(x, hi.y), local));
            for (float y = _previewGuides ? Mathf.Ceil(lo.y / spacing) * spacing : float.PositiveInfinity; y <= hi.y; y += spacing)
                Handles.DrawLine(ToCanvas(new Vector2(lo.x, y), local), ToCanvas(new Vector2(hi.x, y), local));
            List<GameObject> objects = Objects();
            GameObject hit = null;
            Vector2 mouse = Event.current.mousePosition;
            foreach (GameObject root in objects)
            {
                Vector2 point = ToCanvas(DisplayPosition(root), local);
                ColorPlatform platform = root.GetComponent<ColorPlatform>();
                Rect box = new Rect(point - Vector2.one * 7, Vector2.one * 14);
                Color color = Color.cyan;
                ProjectionSource source = root.GetComponent<ProjectionSource>();
                LevelExit exit = root.GetComponent<LevelExit>();
                RoomCheckpoint checkpoint = root.GetComponent<RoomCheckpoint>();
                Vector3[] outline = null;
                if (root.TryGetComponent(out RoomDecoration decoration))
                {
                    outline = ObjectPreviewOutline(root, local);
                    Vector2 min = outline[0], max = outline[0];
                    foreach (Vector3 corner in outline) { min = Vector2.Min(min, corner); max = Vector2.Max(max, corner); }
                    box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    if (root.TryGetComponent(out UnityEngine.UI.RawImage image))
                    {
                        Color previous = GUI.color; GUI.color = image.color;
                        GUI.DrawTexture(box, image.texture ? image.texture : Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                        GUI.color = previous;
                    }
                    if (root.TryGetComponent(out TMPro.TMP_Text label)) GUI.Label(box, label.text, EditorStyles.wordWrappedLabel);
                }
                if (!source && (platform || exit || checkpoint))
                {
                    outline = ObjectPreviewOutline(root, local);
                    if (outline != null)
                    {
                        Vector2 min = outline[0], max = outline[0];
                        foreach (Vector3 corner in outline)
                        {
                            min = Vector2.Min(min, corner);
                            max = Vector2.Max(max, corner);
                        }
                        box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    }
                    int colorIndex = exit ? exit.ColorIndex : platform ? platform.ColorIndex : checkpoint ? checkpoint.ColorIndex : -1;
                    color = _config ? _config.DisplayColor(_config.GetColor(colorIndex)) : Color.gray;
                    if (platform && colorIndex < 0) color = new Color(.06f, .07f, .09f);
                    if (checkpoint && !platform && !exit && !_config)
                    {
                        Renderer renderer = root.GetComponentInChildren<Renderer>(true);
                        Material material = renderer ? renderer.sharedMaterial : null;
                        color = Color.cyan;
                        if (material && material.HasProperty("_BaseColor"))
                            color = material.GetColor("_BaseColor");
                        else if (material && material.HasProperty("_Color"))
                            color = material.GetColor("_Color");
                    }
                    bool hidden = colorIndex >= 0 && colorIndex == _previewColor;
                    if (!hidden && outline != null)
                    {
                        Handles.color = color;
                        Handles.DrawAAConvexPolygon(outline);
                    }
                }
                if (_previewGuides)
                {
                    if (source)
                        EditorGUI.DrawRect(box, color);
                    if (root == _picked)
                    {
                        Handles.color = Color.yellow;
                        Vector3[] border = outline ?? new[] { new Vector3(box.xMin, box.yMin), new Vector3(box.xMax, box.yMin), new Vector3(box.xMax, box.yMax), new Vector3(box.xMin, box.yMax) };
                        Handles.DrawAAPolyLine(3, border.Concat(new[] { border[0] }).ToArray());
                    }
                }
                if (_previewGuides)
                    GUI.Label(new Rect(point.x + 9, point.y - 8, 160, 20), root.name, EditorStyles.whiteMiniLabel);
                if (outline != null ? PointInOutline(mouse, outline) : box.Contains(mouse))
                    hit = root;
            }
            Handles.EndGUI();
            HandleCanvasEvent(local, hit);
            GUI.EndClip();
        }
        /** @brief 使用本地形状角点与真实变换绘制2D对象，保留旋转。 @param root 对象 @param canvas 画布 @return 画布四角 */
        private Vector3[] ObjectPreviewOutline(GameObject root, Rect canvas)
        {
            if (root.GetComponent<RoomPortalLink>() && root.TryGetComponent(out Collider2D portalShape))
            {
                var points = new List<Vector2>();
                foreach (Vector3 point in ProjectionObjectMarkers.FixedPortalOutline(portalShape)) points.Add(ToCanvas(point, canvas));
                return ProjectionContours.Hull(points).Select(point => (Vector3)point).ToArray();
            }
            Transform shapeTransform = root.transform;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            if (root.GetComponent<RoomDecoration>() && root.transform is RectTransform rect)
                bounds = new Bounds(rect.rect.center, rect.rect.size);
            else if (root.TryGetComponent(out BoxCollider2D box))
                bounds = new Bounds(box.offset, box.size);
            else
            {
                MeshFilter mesh = root.GetComponentInChildren<MeshFilter>(true);
                if (mesh && mesh.sharedMesh)
                {
                    bounds = mesh.sharedMesh.bounds;
                    shapeTransform = mesh.transform;
                }
            }
            Vector3 min = bounds.min, max = bounds.max;
            Vector3[] corners = { new Vector3(min.x, min.y, bounds.center.z), new Vector3(max.x, min.y, bounds.center.z), new Vector3(max.x, max.y, bounds.center.z), new Vector3(min.x, max.y, bounds.center.z) };
            for (int i = 0; i < corners.Length; i++)
                corners[i] = ToCanvas(shapeTransform.TransformPoint(corners[i]), canvas);
            return corners;
        }
        /** @brief 使用真实四边形拾取旋转对象。 @param point 鼠标 @param outline 四角 @return 是否在内部 */
        private static bool PointInOutline(Vector2 point, Vector3[] outline)
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Vector3 a = outline[i], b = outline[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
        /** @brief 处理画布编辑。 @param canvas 局部画布 @param hit 当前对象 */
        private void HandleCanvasEvent(Rect canvas, GameObject hit)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseUp && _dragging)
            {
                _dragging = null;
                GUIUtility.hotControl = 0;
                Undo.CollapseUndoOperations(_dragGroup);
                e.Use();
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (_dragging)
                    Undo.RevertAllDownToGroup(_dragGroup);
                _dragging = null;
                GUIUtility.hotControl = 0;
                e.Use();
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && !EditorGUIUtility.editingTextField)
            {
                DeletePicked();
                e.Use();
            }
            if (!canvas.Contains(e.mousePosition) && !_dragging)
                return;
            Vector2 target = ToWorld(e.mousePosition, canvas);
            if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom * Mathf.Exp(-e.delta.y * .08f), 2, 250);
                _center += target - ToWorld(e.mousePosition, canvas);
                e.Use();
                Repaint();
            }
            if (e.type == EventType.MouseDrag && e.button == 2)
            {
                _center += new Vector2(-e.delta.x, e.delta.y) / _zoom;
                e.Use();
                Repaint();
            }
            if (e.type == EventType.MouseDrag && _dragging)
            {
                Undo.RecordObject(_dragging.transform, "移动关卡对象");
                MoveTo(_dragging, Snap(target + _grabOffset));
                _roomWasEdited = true;
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_room.gameObject.scene);
                e.Use();
                Repaint();
            }
            if (e.type != EventType.MouseDown || e.button != 0)
                return;
            GUI.FocusControl(null);
            if (_tool == 2 || _tool == 3)
                PlaceDecoration(Snap(target), _tool == 3);
            else if (_tool == 1)
                PlaceTemplate(Snap(target));
            else
            {
                _picked = hit;
                _inspectTemplate = false;
                if (hit)
                {
                    Selection.activeGameObject = hit;
                    Undo.IncrementCurrentGroup();
                    _dragGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("移动关卡对象");
                    _dragging = hit;
                    _grabOffset = DisplayPosition(hit) - target;
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                }
            }
            e.Use();
            Repaint();
        }
        /** @brief 对齐编辑网格。 @param point 世界坐标 @return 网格坐标 */
        private Vector2 Snap(Vector2 point) => new Vector2(Mathf.Round(point.x / _grid) * _grid, Mathf.Round(point.y / _grid) * _grid);
        /** @brief 放置已有模板并登记。 @param point 2D位置 */
        private void PlaceTemplate(Vector2 point) => Edit("放置2D模板", () =>
        {
            if (!CanPlaceTemplate(_brush) || !PrefabUtility.IsPartOfPrefabAsset(_brush))
                throw new System.InvalidOperationException("请在左侧选择平台或投影源模板。");
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(_brush, _room.transform);
            Undo.RegisterCreatedObjectUndo(root, "放置2D模板");
            Vector3 position = root.transform.position;
            position.z = _depth;
            root.transform.position = position;
            MoveTo(root, point);
            Register(root);
            _picked = root;
            _inspectTemplate = false;
        });
        /** @brief 复制当前物件，并登记。 */
        private void DuplicatePicked()
        {
            if (!_picked || _picked.GetComponentInParent<LightRoom>() != _room)
                return;
            Edit("复制关卡对象", () =>
            {
                GameObject copy = Instantiate(_picked, _room.transform);
                Undo.RegisterCreatedObjectUndo(copy, "复制关卡对象");
                copy.name = _picked.name + " Copy";
                copy.transform.position += Vector3.right * _grid;
                Register(copy);
                _picked = copy;
                Selection.activeGameObject = copy;
            });
        }
        /** @brief 删除物件并清理房间登记及所有指向它的门引用。 */
        private void DeletePicked()
        {
            if (!_picked || _picked.GetComponentInParent<LightRoom>() != _room)
                return;
            GameObject target = _picked;
            Edit("删除关卡对象", () =>
            {
                CleanReferences(_room, target, new[] { "_platforms", "_worldPresentation", "_spawn", "_exit" });
                if (_room.Surface)
                    CleanReferences(_room.Surface, target, new[] { "_sources" });
                Undo.DestroyObjectImmediate(target);
                _picked = null;
            });
        }
        /** @brief 删除前清理数组及单项引用。 @param owner 引用组件 @param root 删除根 @param fields 字段名 */
        private static void CleanReferences(Object owner, GameObject root, string[] fields)
        {
            var serialized = new SerializedObject(owner);
            foreach (string field in fields)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property.isArray)
                {
                    for (int i = property.arraySize - 1; i >= 0; i--)
                    {
                        SerializedProperty item = property.GetArrayElementAtIndex(i);
                        Component component = item.objectReferenceValue as Component;
                        if (component && component.transform.IsChildOf(root.transform))
                        {
                            item.objectReferenceValue = null;
                            property.DeleteArrayElementAtIndex(i);
                        }
                    }
                }
                else if (property.objectReferenceValue is Component component && component.transform.IsChildOf(root.transform))
                    property.objectReferenceValue = null;
            }
            serialized.ApplyModifiedProperties();
        }
        /** @brief 编辑选中物件位姿和平台参数。 */
        private void DrawObjectProperties()
        {
            GUILayout.Label("选中 " + _picked.name, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector3 position = EditorGUILayout.Vector3Field("世界位置", _picked.transform.position);
            Vector3 scale = EditorGUILayout.Vector3Field("局部尺寸", _picked.transform.localScale);
            Vector3 rotation = EditorGUILayout.Vector3Field("旋转角度", _picked.transform.eulerAngles);
            if (EditorGUI.EndChangeCheck())
                Edit("调整关卡对象", () =>
            {
                Undo.RecordObject(_picked.transform, "调整关卡对象");
                _picked.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
                _picked.transform.localScale = scale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(_picked.transform);
            });
            Object component = _picked.GetComponent<ColorPlatform>();
            if (!component)
                component = _picked.GetComponent<ProjectionSource>();
            if (component)
            {
                UnityEditor.Editor.CreateCachedEditor(component, null, ref _objectEditor);
                _objectEditor.OnInspectorGUI();
            }
        }
        /** @brief 采样当前可视区的真实投影色，保存为预览底图。 */
        private void BuildPreview()
        {
            if (!_room || !_room.Surface || !_room.Surface.Origin)
                return;
            if (!new SerializedObject(_room.Surface).FindProperty("_config").objectReferenceValue)
            {
                _report = "请先绑定Surface的Game Config。";
                return;
            }
            if (_projectionPreview)
                DestroyImmediate(_projectionPreview);
            LightStagePreview.Configure(_room.Surface);
            var geometry = new ProjectionPreviewGeometry(_room.Surface.Sources);
            Rect map = MapRect();
            float width = map.width, height = map.height;
            _previewWorld = map;
            LightGameConfig sampling = new SerializedObject(_room.Surface).FindProperty("_config").objectReferenceValue as LightGameConfig;
            float sampleStep = sampling.CellSize / Mathf.Pow(2, sampling.EdgeRefinement);
            int columns = Mathf.Clamp(Mathf.CeilToInt(width / sampleStep), 16, 512);
            int rows = Mathf.Clamp(Mathf.CeilToInt(height / sampleStep), 16, 512);
            _projectionPreview = new Texture2D(columns, rows, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var colors = new Color[columns * rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    Vector2 point = _previewWorld.min + Vector2.Scale(_previewWorld.size, new Vector2((x + .5f) / columns, (y + .5f) / rows));
                    ProjectionRegion region = _room.Surface.SamplePreview(new Vector3(point.x, point.y, _room.Surface.transform.position.z), geometry.Intersects);
                    Color background = sampling.DisplayColor(_previewColor < 0 ? sampling.DefaultBackgroundColor : sampling.GetColor(_previewColor));
                    bool hidden = region.Colored && sampling.Matches(region.Color, _previewColor);
                    colors[y * columns + x] = region.Exists && !hidden ? (region.Colored ? sampling.DisplayColor(region.Color) : Color.black) : background;
                }
            _projectionPreview.SetPixels(colors);
            _projectionPreview.Apply();
            _previewSignature = PreviewSignature();
            _report = $"真实模型三角面预览 {columns}×{rows}：保留凹口和孔洞；模型、子物件、屏幕或光源变更会自动刷新。";
            int unregistered = _room.GetComponentsInChildren<ProjectionSource>(true).Count(source => _room.Surface.Sources == null || !_room.Surface.Sources.Contains(source));
            if (unregistered > 0)
                _report += $" {unregistered} 个投影源尚未登记到Surface，请使用登记工具。";
            if (geometry.MissingShapes > 0)
                _report += $" {geometry.MissingShapes} 个源缺少可用静态网格或逻辑碰撞形状，请检查模型类型与网格数据。";
        }
        /** @brief 释放编辑器临时资源。 */
        private void ReleasePreview()
        {
            if (_projectionPreview)
                DestroyImmediate(_projectionPreview);
            if (_objectEditor)
                DestroyImmediate(_objectEditor);
        }
    }
}
