using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 策划手动操作的房间模板编辑器，提供资产面板、网格放置、登记和门配对。 */
    public sealed partial class LightLevelEditor : EditorWindow
    {
        /** @brief 当前编辑房间。 */
        [SerializeField] private LightRoom _room;
        /** @brief 项目预制体清单。 */
        private readonly List<GameObject> _prefabs = new List<GameObject>();
        /** @brief 选中的笔刷预制体。 */
        private GameObject _brush;
        /** @brief 搜索字符串。 */
        private string _search = "";
        /** @brief 网格间距。 */
        private float _grid = .5f;
        /** @brief 是否启用Scene放置。 */
        private bool _placing;
        /** @brief 当前放置深度。 */
        private float _depth;
        /** @brief 长地图尺寸。 */
        private Vector2 _size = new Vector2(18, 8);
        /** @brief 长地图游戏配置。 */
        private LightGameConfig _config;
        /** @brief 滚动位置。 */
        private Vector2 _scroll;
        /** @brief 检查输出。 */
        private string _report = "";
        /** @brief 打开编辑窗口。 */
        [MenuItem("Tools/Light Path/关卡模板编辑器", false, 0)]
        [MenuItem("Window/Light Path/关卡模板编辑器 %&l", false, 2000)]
        public static void Open()
        {
            LightLevelEditor window = GetWindow<LightLevelEditor>("光路关卡编辑");
            window.minSize = new Vector2(1200, 700);
            window.Show();
            window.Focus();
            Debug.Log("LIGHT_LEVEL_EDITOR_OPENED");
        }
        /** @brief 从关卡资产检查器进入编辑器。 @param level 关卡资产 */
        public static void EditLevel(ProjectionLevel level)
        {
            Open();
            GetWindow<LightLevelEditor>().OpenLevel(level);
        }
        /** @brief 绑定场景绘制并发现已有模板。 */
        private void OnEnable()
        {
            minSize = new Vector2(1200, 700);
            SceneView.duringSceneGui += DrawScene;
            Undo.undoRedoPerformed += Repaint;
            Refresh();
        }
        /** @brief 解绑场景绘制。 */
        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            Undo.undoRedoPerformed -= Repaint;
            ReleasePreview();
            ReleaseAuthoringResources();
        }
        /** @brief 搜索本项目可用预制体，不生成资源。 */
        private void Refresh()
        {
            _prefabs.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/LightPath" }))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (CanPlaceTemplate(prefab))
                    _prefabs.Add(prefab);
            }
        }
        /** @brief 新增或移除模板资产后自动刷新目录。 */
        private void OnProjectChange()
        {
            Refresh();
            Repaint();
        }
        /** @brief 仅收录可布设的玩法物件，排除房间、角色和UI。 @param prefab 模板 @return 是否允许放置 */
        private static bool CanPlaceTemplate(GameObject prefab)
        {
            if (prefab && prefab.GetComponent<RoomDecoration>()) return true;
            return prefab && !prefab.GetComponentInChildren<LightRoom>(true)
                && !prefab.GetComponentInChildren<LightGame>(true)
                && !prefab.GetComponentInChildren<Rigidbody2D>(true)
                && !prefab.GetComponentInChildren<CharacterController>(true)
                && !prefab.GetComponentInChildren<Canvas>(true)
                && (prefab.GetComponent<ProjectionSource>() || prefab.GetComponent<ColorPlatform>()
                    || prefab.GetComponent<RoomSpawnPoint>() || prefab.GetComponent<RoomCheckpoint>() || prefab.GetComponent<LevelExit>() || prefab.GetComponent<ColorAbilityKey>() || prefab.GetComponent<ProjectionAbilityKey>());
        }
        /** @brief 判断当前编辑上下文是否合法。 @return 是否能修改 */
        private bool Editable() => _room && _room.gameObject.scene.IsValid() && !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorUtility.IsPersistent(_room);
        /** @brief 绘制策划面板。 */
        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(310));
            DrawLevels();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            LightRoom previousRoom = _room;
            using (new EditorGUI.DisabledScope(_levelContents))
                _room = (LightRoom)EditorGUILayout.ObjectField("目标房间", _room, typeof(LightRoom), true);
            if (!_levelContents && GUILayout.Button("使用选中对象所属房间"))
                _room = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<LightRoom>() : null;
            if (_room != previousRoom)
            {
                ReleasePreview();
                _picked = null;
                _config = _room && _room.Surface ? _room.Surface.Configuration : null;
                FrameMap();
                BuildPreview();
            }
            EditorGUILayout.HelpBox("选择房间后在右侧2D画布编辑。滚轮缩放、中键平移，左键选择拖动。可创建平台、放置模板、点击两门配对。", MessageType.Info);
            DrawCatalog();
            _brush = (GameObject)EditorGUILayout.ObjectField("放置预制体", _brush, typeof(GameObject), false);
            _grid = Mathf.Max(.05f, EditorGUILayout.FloatField("房间内网格（米）", _grid));
            _depth = EditorGUILayout.FloatField("放置世界Z", _depth);
            using (new EditorGUI.DisabledScope(!Editable()))
            {
                _placing = EditorGUILayout.Toggle("Scene点击放置", _placing);
                if (GUILayout.Button("将选中对象登记到房间"))
                    RegisterSelection();
                if (GUILayout.Button("配对选中的两个传送源"))
                    Pair();
                if (GUILayout.Button("解除选中传送源的互相配对"))
                    Unpair();
                EditorGUILayout.Space();
                _config = (LightGameConfig)EditorGUILayout.ObjectField("关卡参数", _config, typeof(LightGameConfig), false);
                _size = EditorGUILayout.Vector2Field("地图宽高", _size);
                if (GUILayout.Button("应用房间范围"))
                    Resize();
                if (GUILayout.Button("检查房间登记与门引用"))
                    ValidateRoom();
            }
            if (!string.IsNullOrEmpty(_report))
                EditorGUILayout.HelpBox(_report, MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            DrawWorkspace();
            DrawPersistentInspector();
            EditorGUILayout.EndHorizontal();
        }
        /** @brief 原子编辑操作，失败时回滚本次Undo组。 @param label 名称 @param operation 操作 */
        private void Edit(string label, System.Action operation)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            try
            {
                operation();
                EditorSceneManager.MarkSceneDirty(_room.gameObject.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch (System.Exception error)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogException(error);
            }
        }
        /** @brief 登记对象中已有组件，不创建玩法组件。 @param root 已有实例 */
        private void Register(GameObject root)
        {
            if (root.GetComponentInParent<LightRoom>() != _room)
                throw new System.InvalidOperationException("对象必须属于目标房间。");
            Append(_room, "_platforms", root.GetComponentsInChildren<ColorPlatform>(true));
            Append(_room.Surface, "_sources", root.GetComponentsInChildren<ProjectionSource>(true));
            var roomData = new SerializedObject(_room);
            if (root.GetComponent<RoomSpawnPoint>())
                roomData.FindProperty("_spawn").objectReferenceValue = root.transform;
            if (root.GetComponent<LevelExit>() && !root.GetComponent<ProjectionSource>())
                roomData.FindProperty("_exit").objectReferenceValue = root.transform;
            roomData.ApplyModifiedProperties();
            if (root.GetComponent<RoomCheckpoint>() || root.GetComponent<RoomSpawnPoint>())
                Append(_room, "_worldPresentation", new Object[] { root.transform });
        }
        /** @brief 去重添加序列化对象引用。 @param target 组件 @param field 数组 @param values 新成员 */
        private static void Append(Object target, string field, IEnumerable<Object> values)
        {
            if (!target)
                throw new System.InvalidOperationException("请先绑定房间的Surface。");
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(field);
            foreach (Object value in values)
            {
                bool exists = false;
                for (int i = 0; i < array.arraySize; i++)
                    exists |= array.GetArrayElementAtIndex(i).objectReferenceValue == value;
                if (exists)
                    continue;
                int index = array.arraySize++;
                array.GetArrayElementAtIndex(index).objectReferenceValue = value;
            }
            serialized.ApplyModifiedProperties();
        }
        /** @brief 由用户选择路径保存单个无外部门引用的玩法模板。 */
        private void SaveTemplate()
        {
            GameObject root = Selection.activeGameObject;
            if (!root || root.GetComponentInParent<LightRoom>() != _room || root == _room.gameObject)
            {
                _report = "请选择房间内的平台或投影源物件。";
                return;
            }
            foreach (ProjectionSource source in root.GetComponentsInChildren<ProjectionSource>(true))
                if (source.Partner && !source.Partner.transform.IsChildOf(root.transform))
                {
                    _report = "门目标位于模板之外。请先解除配对，或选择同时包含门对的父对象。";
                    return;
                }
            string path = EditorUtility.SaveFilePanelInProject("另存玩法模板", root.name, "prefab", "选择新的模板路径", "Assets/LightPath/Prefabs");
            if (string.IsNullOrEmpty(path))
                return;
            if (AssetDatabase.LoadMainAssetAtPath(path))
            {
                _report = "请选择新的文件名，避免覆盖已有模板。";
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Refresh();
            _report = "模板已保存。撤销场景操作不会删除已保存的资产。";
        }
        /** @brief 登记选中的已有实例。 */
        private void RegisterSelection() => Edit("登记房间对象", () =>
        {
            foreach (GameObject root in Selection.gameObjects)
                Register(root);
        });
        /** @brief 修改门引用并记录预制体实例覆盖。 @param source 源 @param partner 目标 */
        private static void SetPartner(ProjectionSource source, ProjectionSource partner)
        {
            var serialized = new SerializedObject(source);
            serialized.FindProperty("_partner").objectReferenceValue = partner;
            serialized.ApplyModifiedProperties();
        }
        /** @brief 配对同房间两扇未配对门。 */
        private void Pair() => Edit("配对传送门", () =>
        {
            ProjectionSource[] sources = Selection.gameObjects.Select(x => x.GetComponent<ProjectionSource>()).Where(x => x).Distinct().ToArray();
            if (sources.Length != 2 || sources.Any(x => !x.Definition || !x.Definition.Portal || !x.Definition.Transmissive
                || x.GetComponent<RoomPortalLink>() || x.GetComponent<ProjectionAbilityKey>() || x.GetComponent<ColorAbilityKey>() || x.GetComponent<LevelExit>() || x.GetComponentInParent<LightRoom>() != _room || (x.Partner && !sources.Contains(x.Partner))))
                throw new System.InvalidOperationException("请选择同一房间两扇透光传送源，已有连接请先移除；单向或跨房间连接请使用右侧目标选择。");
            Register(sources[0].gameObject);
            Register(sources[1].gameObject);
            SetPartner(sources[0], sources[1]);
            SetPartner(sources[1], sources[0]);
        });
        /** @brief 解除本房间选中源的配对及原目标反向引用。 */
        private void Unpair() => Edit("解除传送门", () =>
        {
            foreach (GameObject root in Selection.gameObjects)
            {
                ProjectionSource source = root.GetComponent<ProjectionSource>();
                if (!source || source.GetComponentInParent<LightRoom>() != _room)
                    continue;
                ProjectionSource partner = source.Partner;
                if (partner && partner.GetComponentInParent<LightRoom>() == _room && partner.Partner == source)
                    SetPartner(partner, null);
                SetPartner(source, null);
            }
        });
        /** @brief 调整投影范围和共享配置的镜头参数。 */
        private void Resize() => Edit("配置房间范围", () =>
        {
            if (!_room.Surface || !_config || _size.x < 1 || _size.y < 1)
                throw new System.InvalidOperationException("绑定Surface与Game配置，并填写有效尺寸。");
            var surface = new SerializedObject(_room.Surface);
            surface.FindProperty("_size").vector2Value = _size;
            surface.ApplyModifiedProperties();
            BuildPreview();
            _report = "已调整房间投影范围。镜头保持固定；请检查幕布、地板、出生点、出口与固定镜头视野。";
        });
        /** @brief 检查登记遗漏与门关系。 */
        private void ValidateRoom()
        {
            var issues = new List<string>();
            if (!_room.Surface || !_room.Definition)
                issues.Add("缺少Surface或Definition。");
            if (_room.Surface)
                foreach (ProjectionSource source in _room.GetComponentsInChildren<ProjectionSource>(true))
                {
                    if (!_room.Surface.Sources.Contains(source))
                        issues.Add("未登记投影源 " + source.name);
                    if (!source.Definition)
                        issues.Add("源缺少定义 " + source.name);
                    else if (source.Definition.Portal && !source.GetComponent<LevelExit>() && !source.GetComponent<ColorAbilityKey>() && !source.GetComponent<ProjectionAbilityKey>())
                    {
                        string issue = PortalConnectionIssue(source.gameObject);
                        if (!string.IsNullOrEmpty(issue))
                            issues.Add(source.name + "：" + issue);
                    }
                }
            foreach (ColorPlatform platform in _room.GetComponentsInChildren<ColorPlatform>(true))
                if (!_room.Platforms.Contains(platform))
                    issues.Add("未登记平台 " + platform.name);
            _report = issues.Count == 0 ? "登记与配对检查通过，请继续Play验收。" : string.Join("\n", issues);
        }
        /** @brief Scene网格放置和门连线。 @param view 当前视图 */
        private void DrawScene(SceneView view)
        {
            if (!Editable())
                return;
            foreach (ProjectionSource source in _room.GetComponentsInChildren<ProjectionSource>(true))
                if (source.Partner)
                {
                    Handles.color = Color.magenta;
                    Handles.DrawDottedLine(source.transform.position, source.Partner.transform.position, 5);
                }
            if (!_placing || !CanPlaceTemplate(_brush) || !PrefabUtility.IsPartOfPrefabAsset(_brush))
                return;
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                _placing = false;
                Repaint();
                current.Use();
                return;
            }
            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!new Plane(Vector3.forward, new Vector3(0, 0, _depth)).Raycast(ray, out float distance))
                return;
            Vector3 point = ray.GetPoint(distance);
            point.x = Mathf.Round(point.x / _grid) * _grid;
            point.y = Mathf.Round(point.y / _grid) * _grid;
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(point, Vector3.forward, _grid / 2);
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt)
                return;
            Edit("放置关卡模板", () =>
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(_brush, _room.transform);
                Undo.RegisterCreatedObjectUndo(instance, "放置关卡模板");
                instance.transform.position = point;
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                Register(instance);
                Selection.activeGameObject = instance;
            });
            current.Use();
        }
    }
}
