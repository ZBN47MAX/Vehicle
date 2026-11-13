using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 分类模板制作、固定属性栏和3D投影源预览。 */
    public sealed partial class LightLevelEditor
    {
        /** @brief 模板类别筛选。 */
        private int _category;
        /** @brief 模板草稿类别。 */
        private int _draftKind;
        /** @brief 模板附加功能。 */
        private int _draftEffect;
        /** @brief 新建投影能力钥匙的操作类型。 */
        private ProjectionAbility _draftAbility;
        /** @brief 功能检查器。 */
        private UnityEditor.Editor _effectEditor;
        /** @brief 模板草稿名称。 */
        private string _draftName = "New Light Object";
        /** @brief 模板草稿尺寸。 */
        private Vector3 _draftSize = new Vector3(3, .5f, .2f);
        /** @brief 可选美术预制体。 */
        private GameObject _draftVisual;
        /** @brief 初始形状。 */
        private PrimitiveType _draftShape = PrimitiveType.Cube;
        /** @brief 透光开关。 */
        private bool _draftTransmissive = true;
        /** @brief 门开关。 */
        private bool _draftPortal;
        /** @brief 三维物件在游玩时是否允许调整。 */
        private bool _draftMovable = true;
        /** @brief 模板预设色。 */
        private int _draftColor;
        /** @brief 模板自定义色。 */
        private Color _draftCustom = Color.cyan;
        /** @brief 属性面板滚动位置。 */
        private Vector2 _inspectorScroll;
        /** @brief 预览渲染器。 */
        private PreviewRenderUtility _modelPreview;
        /** @brief 复用运行时描边网格及顶点色材质。 */
        private ProjectionObjectMarkers _markerPreview;
        private Material _markerMaterial;
        /** @brief 3D预览视角。 */
        private Vector2 _orbit = new Vector2(20, -25);
        /** @brief 3D预览距离倍率。 */
        private float _previewDistance = 1;
        /** @brief 当前属性目标是否采用模板资产。 */
        private bool _inspectTemplate;
        /** @brief 分类名称。 */
        private static readonly string[] Categories = { "全部", "平台", "投影实体", "局部滤镜", "传送门", "房间", "角色/UI/其他", "能力滤镜", "关卡出口", "投影操作能力钥匙", "存档点", "出生点" };
        /** @brief 按组件与定义归类预制体。 @param prefab 模板 @return 分类编号 */
        private static int Category(GameObject prefab)
        {
            if (prefab.GetComponent<RoomPortalLink>()) return 4;
            if (prefab.TryGetComponent(out RoomDecoration decoration)) return decoration.Type == RoomDecoration.Kind.Image ? 12 : 13;
            if (prefab.GetComponent<RoomCheckpoint>()) return 10;
            if (prefab.GetComponent<RoomSpawnPoint>()) return 11;
            if (prefab.GetComponent<ProjectionAbilityKey>())
                return 9;
            if (prefab.GetComponent<ColorAbilityKey>())
                return 7;
            if (prefab.GetComponent<LevelExit>())
                return 8;
            if (prefab.GetComponent<LightRoom>())
                return 5;
            ProjectionSource source = prefab.GetComponent<ProjectionSource>();
            if (source && source.Definition)
                return source.Definition.Portal ? 4 : source.Definition.Transmissive ? 3 : 2;
            return prefab.GetComponent<ColorPlatform>() ? 1 : 6;
        }
        /** @brief 绘制可搜索分类列表与新建草稿。 */
        private void DrawCatalog()
        {
            if (_category == 5 || _category == 6)
                _category = 0;
            _category = EditorGUILayout.IntPopup("模板分类", _category,
                new[] { "全部", "平台", "投影实体", "局部滤镜", "传送门", "能力滤镜", "关卡出口", "投影操作能力钥匙", "存档点", "出生点", "背景图片", "背景文字" },
                new[] { 0, 1, 2, 3, 4, 7, 8, 9, 10, 11, 12, 13 });
            using (new EditorGUI.DisabledScope(!Editable()))
            {
                if (GUILayout.Button("放置背景图片")) _tool = 2;
                if (GUILayout.Button("放置背景文字")) _tool = 3;
            }
            _search = EditorGUILayout.TextField("搜索模板", _search);
            if (GUILayout.Button("刷新模板库"))
                Refresh();
            foreach (GameObject prefab in _prefabs)
            {
                if ((_category != 0 && Category(prefab) != _category) || prefab.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                GUIContent item = new GUIContent(prefab.name, AssetPreview.GetMiniThumbnail(prefab), AssetDatabase.GetAssetPath(prefab));
                if (GUILayout.Button(item, GUILayout.Height(28)))
                {
                    _brush = prefab;
                    _depth = prefab.GetComponent<ProjectionSource>() ? (_room && _room.Surface && _room.Surface.Origin ? (_room.Surface.ProjectionOrigin.z + _room.Surface.transform.position.z) / 2 : -5) : 0;
                    if (prefab.GetComponent<RoomDecoration>()) _depth = _room && _room.Surface ? _room.Surface.transform.position.z + .1f : .1f;
                    _inspectTemplate = true;
                    _tool = 1;
                    Repaint();
                }
            }
            EditorGUILayout.Space();
            GUILayout.Label("新建模板", EditorStyles.boldLabel);
            _draftName = EditorGUILayout.TextField("名称", _draftName);
            _draftKind = EditorGUILayout.Popup("用途", _draftKind, new[] { "固定2D平台", "3D投影物件" });
            _draftEffect = EditorGUILayout.Popup("附加功能", _draftEffect, new[] { "普通物件", "滤镜能力钥匙", "关卡出口", "投影操作能力钥匙", "传送门" });
            if (_draftEffect == 4)
            {
                _draftPortal = true;
                _draftTransmissive = true;
                EditorGUILayout.HelpBox("用途选固定2D平台可创建纯2D门，选3D投影物件可创建投影门。放置后配置抵达位置或目标门。", MessageType.Info);
            }
            if (_draftEffect == 3)
                _draftAbility = (ProjectionAbility)EditorGUILayout.Popup("解锁能力", (int)_draftAbility, new[] { "X轴移动", "Y轴移动", "Z轴移动", "X轴旋转", "Y轴旋转", "Z轴旋转" });
            _draftVisual = (GameObject)EditorGUILayout.ObjectField("美术预制体（可选）", _draftVisual, typeof(GameObject), false);
            _draftShape = (PrimitiveType)EditorGUILayout.EnumPopup("基础形状", _draftShape);
            _draftSize = EditorGUILayout.Vector3Field("尺寸", _draftSize);
            _draftColor = EditorGUILayout.IntSlider(_draftKind == 0 ? "平台色（-1基础）" : "预设色（-1自定义）", _draftColor, -1, 3);
            if (_draftKind == 1)
            {
                _draftMovable = EditorGUILayout.Toggle("游玩时可移动/旋转", _draftMovable);
                _draftTransmissive = EditorGUILayout.Toggle("透光", _draftTransmissive);
                _draftPortal = EditorGUILayout.Toggle("作为传送门", _draftPortal);
                if (_draftColor < 0)
                    _draftCustom = EditorGUILayout.ColorField("自定义色", _draftCustom);
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("创建模板资产并加入列表"))
                    CreateDraftTemplate();
        }
        /** @brief 由用户保存新模板及独立配置，失败时清理本次资产。 */
        private void CreateDraftTemplate()
        {
            if (_draftColor == LightGameConfig.RedColorIndex || (_draftKind == 1 && _draftColor < 0 && LightGameConfig.IsPureRed(_draftCustom)))
            {
                _report = "正红色仅供存档点使用，请选择其他颜色。";
                return;
            }
            if (_draftEffect == 1 && !LightGameConfig.IsManualColor(_draftColor))
            {
                _report = "滤镜能力钥匙只能解锁颜色1至4。正红色仅供存档点使用。";
                return;
            }
            if (_draftSize.x <= 0 || _draftSize.y <= 0 || _draftSize.z <= 0)
            {
                _report = "尺寸各轴必须大于0。";
                return;
            }
            string path = EditorUtility.SaveFilePanelInProject("创建关卡模板", _draftName, "prefab", "请选择新的模板文件名", "Assets/LightPath/Prefabs");
            if (string.IsNullOrEmpty(path))
                return;
            if (AssetDatabase.LoadMainAssetAtPath(path))
            {
                _report = "该文件已经存在，请选择新的名称。";
                return;
            }
            string definitionPath = AssetDatabase.GenerateUniqueAssetPath("Assets/LightPath/Config/Projection/" + Path.GetFileNameWithoutExtension(path) + "-Definition.asset");
            GameObject root = null;
            bool definitionCreated = false;
            try
            {
                root = new GameObject(Path.GetFileNameWithoutExtension(path));
                GameObject visual = _draftVisual ? Instantiate(_draftVisual) : GameObject.CreatePrimitive(_draftShape);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                root.transform.localScale = _draftSize;
                if (_draftKind == 0)
                {
                    foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                        DestroyImmediate(collider);
                    root.AddComponent<BoxCollider2D>();
                    root.AddComponent<ColorPlatform>().Configure(_draftColor);
                    root.layer = 9;
                }
                else
                {
                    foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
                        DestroyImmediate(collider);
                    if (root.GetComponentsInChildren<Collider>(true).Length == 0)
                        visual.AddComponent<BoxCollider>();
                    ProjectionDefinition definition = CreateInstance<ProjectionDefinition>();
                    definition.Configure(_draftEffect > 0 || _draftPortal || _draftTransmissive, _draftEffect > 0 || _draftPortal, _draftColor, _draftMovable, _draftCustom);
                    AssetDatabase.CreateAsset(definition, definitionPath);
                    definitionCreated = true;
                    root.AddComponent<ProjectionSource>().Configure(definition);
                }
                Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/LightPath/Materials/Environment/Platform_0.mat");
                if (!_draftVisual && material)
                    visual.GetComponent<Renderer>().sharedMaterial = material;
                // 固定平台的Renderer与ColorPlatform需在同一对象，保持运行时组件契约。
                if (_draftKind == 0)
                {
                    MeshFilter filter = visual.GetComponent<MeshFilter>();
                    MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
                    if (!filter || !renderer || visual.transform.childCount != 0)
                        throw new InvalidOperationException("固定平台请使用基础形状。复杂美术请以现有平台模板编辑。");
                    root.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    root.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                    DestroyImmediate(visual);
                }
                if (_draftEffect == 1)
                    root.AddComponent<ColorAbilityKey>().Configure(_draftColor);
                if (_draftEffect == 3)
                    root.AddComponent<ProjectionAbilityKey>().Configure(_draftAbility);
                if (_draftEffect == 4)
                    root.AddComponent<RoomPortalLink>().UseArrivalPosition = true;
                if (_draftEffect == 2)
                    root.AddComponent<LevelExit>().Configure(root.GetComponent<ProjectionSource>(), _draftColor);
                if (_draftEffect > 0 && root.TryGetComponent(out Collider2D trigger))
                    trigger.isTrigger = true;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (!prefab)
                    throw new InvalidOperationException("保存模板失败。");
                _brush = prefab;
                _inspectTemplate = true;
                _tool = 1;
                AssetDatabase.SaveAssets();
                Refresh();
                _report = "模板已加入分类列表。右侧可继续调整，选放模板工具后点击2D画布布设。";
            }
            catch (Exception error)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path))
                    AssetDatabase.DeleteAsset(path);
                if (definitionCreated)
                    AssetDatabase.DeleteAsset(definitionPath);
                _report = error.Message;
                Debug.LogException(error);
            }
            finally { if (root) DestroyImmediate(root); }
        }
        /** @brief 固定宽度属性栏，始终占据相同布局空间。 */
        private void DrawPersistentInspector()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            DrawLevelObjects();
            GUILayout.Label("属性与3D投影", EditorStyles.boldLabel);
            _inspectTemplate = GUILayout.Toolbar(_inspectTemplate ? 1 : 0, new[] { "场景物件", "模板资产" }) == 1;
            if (!_inspectTemplate && _room)
            {
                ProjectionSource[] sources = _room.GetComponentsInChildren<ProjectionSource>(true);
                string[] names = new[] { "选择3D投影源…" }.Concat(sources.Select(x => x.name)).ToArray();
                int selected = EditorGUILayout.Popup("3D源列表", 0, names);
                if (selected > 0)
                    _picked = sources[selected - 1].gameObject;
            }
            DrawRoomEdges();
            GameObject target = _layoutMode == 1 ? null : _inspectTemplate ? _brush : _picked;
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
            if (target)
            {
                GUILayout.Label(target.name, EditorStyles.boldLabel);
                DrawNodeColor(target);
                DrawDecorationProperties(target);

                if (_inspectTemplate)
                    DrawTemplateProperties(target);
                else if (Editable())
                    DrawObjectProperties();
                UnityEngine.Object effect = target.GetComponent<LevelExit>();
                if (!effect) effect = target.GetComponent<RoomCheckpoint>();
                if (!effect)
                    effect = target.GetComponent<ColorAbilityKey>();
                if (!effect)
                    effect = target.GetComponent<ProjectionAbilityKey>();
                if (effect)
                {
                    UnityEditor.Editor.CreateCachedEditor(effect, null, ref _effectEditor);
                    _effectEditor.OnInspectorGUI();
                }
                if (target.GetComponent<RoomPortalLink>() && !target.GetComponent<ProjectionSource>() && !_inspectTemplate)
                    DrawPortalConnection(target);
                ProjectionSource source = target.GetComponent<ProjectionSource>();
                if (source)
                {
                    if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                        EditorGUILayout.HelpBox("此处投影使用静态MeshFilter模型；骨骼模型请先将目标姿态烘焙为静态网格。不会使用简化碰撞体冒充其形状。", MessageType.Warning);
                    if (source.GetComponentsInChildren<MeshFilter>(true).Any(x => x.sharedMesh && !x.sharedMesh.isReadable))
                        EditorGUILayout.HelpBox("编辑器可预览此模型的真实形状；打包运行的三角面投影与双世界描边需要模型开启 Read/Write。请在模型导入设置勾选后 Apply。", MessageType.Warning);
                    DrawDefinition(source);
                    if (!_inspectTemplate && source.Definition && source.Definition.Portal)
                        DrawPortalConnection(source.gameObject);
                    if (!_inspectTemplate && _room && _room.Surface)
                    {
                        EditorGUILayout.LabelField("投影位置", DisplayPosition(target).ToString("F2"));
                        if (GUILayout.Button("刷新2D投影结果"))
                            BuildPreview();
                        if (GUILayout.Button("在Scene聚焦3D源"))
                        {
                            Selection.activeGameObject = target;
                            SceneView.lastActiveSceneView?.FrameSelected();
                        }
                    }
                }
            }
            else
                EditorGUILayout.HelpBox("从画布选择物件，或从分类列表选择模板。此属性栏始终保持占位。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
        /** @brief 为固定节点提供统一色号入口，写入原组件并支持撤销和预制体覆盖。@param target 当前实例或模板 */
        private void DrawNodeColor(GameObject target)
        {
            if (target.GetComponent<ProjectionSource>()) return;
            Component component = target.GetComponent<ColorPlatform>();
            string field = "_colorIndex";
            if (!component) component = target.GetComponent<RoomCheckpoint>();
            if (!component) { component = target.GetComponent<LevelExit>(); field = "_color"; }
            if (!component) return;
            var data = new SerializedObject(component);
            var color = data.FindProperty(field);
            if (target.GetComponent<RoomCheckpoint>())
            {
                EditorGUILayout.LabelField("自身颜色", "正红色（存档点专用）");
                if (color.intValue != LightGameConfig.RedColorIndex)
                {
                    color.intValue = LightGameConfig.RedColorIndex;
                    data.ApplyModifiedProperties();
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                return;
            }
            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup("自身颜色", color.intValue + 1, new[] { "基础色（不消隐）", "颜色1", "颜色2", "颜色3", "颜色4" }) - 1;
            if (EditorGUI.EndChangeCheck())
            {
                color.intValue = index;
                data.ApplyModifiedProperties();
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                Repaint();
            }
            EditorGUILayout.HelpBox("使用关卡色表；与全局滤镜同色时隐藏，并停用碰撞或交互。基础色始终可见（断光除外）。", MessageType.Info);
        }
        /** @brief 修改模板资产中的尺寸与组件。 @param prefab 模板 */
        private void DrawTemplateProperties(GameObject prefab)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 scale = EditorGUILayout.Vector3Field("模板尺寸", prefab.transform.localScale);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(prefab.transform, "调整模板尺寸");
                prefab.transform.localScale = scale;
                EditorUtility.SetDirty(prefab.transform);
            }
            UnityEngine.Object component = prefab.GetComponent<ColorPlatform>();
            if (!component)
                component = prefab.GetComponent<ProjectionSource>();
            if (component)
            {
                UnityEditor.Editor.CreateCachedEditor(component, null, ref _objectEditor);
                _objectEditor.OnInspectorGUI();
            }
            if (GUILayout.Button("保存模板修改"))
            {
                PrefabUtility.SavePrefabAsset(prefab);
                AssetDatabase.SaveAssets();
            }
        }
        /** @brief 编辑光学定义，明确显示共享范围并提供独立副本。 @param source 投影源 */
        private void DrawDefinition(ProjectionSource source)
        {
            if (!source.Definition)
                return;
            EditorGUILayout.HelpBox("下方描边/颜色/透光/传送门属性保存在共享SO，所有引用者同步变化。可先建立独立定义。", MessageType.Info);
            if (GUILayout.Button("为当前物件建立独立定义"))
            {
                string path = EditorUtility.SaveFilePanelInProject("独立投影定义", source.name + "-Definition", "asset", "保存独立SO", "Assets/LightPath/Config/Projection");
                if (!string.IsNullOrEmpty(path) && !AssetDatabase.LoadMainAssetAtPath(path))
                {
                    ProjectionDefinition copy = Instantiate(source.Definition);
                    AssetDatabase.CreateAsset(copy, path);
                    var serialized = new SerializedObject(source);
                    serialized.FindProperty("_definition").objectReferenceValue = copy;
                    serialized.ApplyModifiedProperties();
                }
            }
            var definition = new SerializedObject(source.Definition);
            definition.Update();
            EditorGUILayout.PropertyField(definition.FindProperty("_transmissive"), new GUIContent("允许透光"));
            EditorGUILayout.PropertyField(definition.FindProperty("_portal"), new GUIContent("作为传送门"));
            EditorGUILayout.PropertyField(definition.FindProperty("_movable"), new GUIContent("游戏中允许操作"));
            var palette = definition.FindProperty("_paletteIndex");
            palette.intValue = EditorGUILayout.Popup("预设颜色", palette.intValue + 1,
                new[] { "自定义色", "颜色1", "颜色2", "颜色3", "颜色4" }) - 1;
            EditorGUILayout.PropertyField(definition.FindProperty("_color"), new GUIContent("自定义色"));
            EditorGUILayout.PropertyField(definition.FindProperty("_weight"), new GUIContent("混色权重"));
            EditorGUILayout.PropertyField(definition.FindProperty("_travel"), new GUIContent("游戏中移动范围"));
            EditorGUILayout.PropertyField(definition.FindProperty("_markerWidth"), new GUIContent("标识描边宽度", "默认0.018；与运行时一致。共享此定义的对象同步更新。"));
            EditorGUILayout.PropertyField(definition.FindProperty("_requiresMatch"), new GUIContent("需要预设匹配检查"));
            if (definition.FindProperty("_portal").boolValue)
                definition.FindProperty("_transmissive").boolValue = true;
            if (definition.ApplyModifiedProperties())
                Repaint();
            EditorGUILayout.LabelField("描边实时预览（拖动旋转／滚轮缩放）", EditorStyles.boldLabel);
            DrawModelPreview(source.gameObject, GUILayoutUtility.GetRect(180, 220, GUILayout.ExpandWidth(true)));
        }
        /** @brief 预览网格并通过鼠标旋转预览相机。 @param target 场景源或模板 @param rect 固定预览矩形 */
        private void DrawModelPreview(GameObject target, Rect rect)
        {
            Event e = Event.current;
            if (rect.Contains(e.mousePosition) && e.type == EventType.MouseDrag)
            {
                _orbit += e.delta;
                e.Use();
                Repaint();
            }
            if (rect.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
            {
                _previewDistance = Mathf.Clamp(_previewDistance * Mathf.Exp(e.delta.y * .08f), .3f, 5);
                e.Use();
                Repaint();
            }
            if (e.type != EventType.Repaint)
                return;
            if (_modelPreview == null)
                _modelPreview = new PreviewRenderUtility();
            MeshRenderer[] renderers = target.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                GUI.Box(rect, "当前对象无MeshRenderer");
                return;
            }
            Bounds bounds = renderers[0].bounds;
            foreach (MeshRenderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            _modelPreview.BeginPreview(rect, GUIStyle.none);
            Texture previewImage;
            try
            {
                Camera camera = _modelPreview.camera;
                camera.fieldOfView = 35;
                Quaternion rotation = Quaternion.Euler(-_orbit.y, _orbit.x, 0);
                float radius = Mathf.Max(.1f, bounds.extents.magnitude);
                camera.transform.SetPositionAndRotation(rotation * Vector3.back * radius * 4 * _previewDistance, rotation);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = radius * 100 + 100;
                camera.clearFlags = CameraClearFlags.Color;
                camera.backgroundColor = new Color(.12f, .13f, .16f);
                foreach (MeshRenderer renderer in renderers)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (!filter || !filter.sharedMesh)
                        continue;
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                    {
                        Material material = renderer.sharedMaterials.Length > i ? renderer.sharedMaterials[i] : null;
                        if (material)
                            _modelPreview.DrawMesh(filter.sharedMesh, Matrix4x4.Translate(-bounds.center) * filter.transform.localToWorldMatrix, material, i);
                    }
                }
                _markerPreview ??= new ProjectionObjectMarkers();
                if (!_markerMaterial) _markerMaterial = new Material(Shader.Find("Light Path/Repair Stage")) { hideFlags = HideFlags.HideAndDontSave };
                Mesh markers = _markerPreview.BuildPreview(target.GetComponentsInChildren<ProjectionSource>(true), Matrix4x4.Translate(-bounds.center), camera, PreviewBidirectional, target.GetComponentsInChildren<RoomPortalLink>(true));
                _modelPreview.DrawMesh(markers, Matrix4x4.identity, _markerMaterial, 0);
                _modelPreview.Render(true);
            }
            finally { previewImage = _modelPreview.EndPreview(); }
            GUI.DrawTexture(rect, previewImage, ScaleMode.StretchToFill, false);
        }
        /** @brief 从编辑关卡解析双向门，包括自动回程端。@param source 门 @return 是否双向 */
        private bool PreviewBidirectional(ProjectionSource source)
        {
            RoomPortalLink link = source.GetComponent<RoomPortalLink>();
            if (!link) return false;
            if (!link.ArrivalOnly) return !link.UseArrivalPosition && link.Bidirectional;
            if (!_level || !source.GetComponentInParent<LightRoom>()) return false;
            for (int i = 0; i < _level.RoomCount; i++)
                foreach (RoomPortalLink incoming in _level.Prefab(i).GetComponentsInChildren<RoomPortalLink>(true))
                    if (!incoming.ArrivalOnly && !incoming.UseArrivalPosition && incoming.Bidirectional
                        && incoming.TargetRoom >= 0 && incoming.TargetRoom < _level.RoomCount
                        && _level.Prefab(incoming.TargetRoom) && incoming.TargetPortal == source.name
                        && _level.Prefab(incoming.TargetRoom).name == source.GetComponentInParent<LightRoom>().name)
                        return true;
            return false;
        }
        /** @brief 清理工作区预览资源。 */
        private void ReleaseAuthoringResources()
        {
            if (_effectEditor)
                DestroyImmediate(_effectEditor);
            _modelPreview?.Cleanup();
            _modelPreview = null;
            _markerPreview?.Dispose(); _markerPreview = null;
            if (_markerMaterial) DestroyImmediate(_markerMaterial);

        }
    }
}
