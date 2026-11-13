using System;
using UnityEditor;
using UnityEngine;
namespace DelayNoMore.LightPath.Editor
{
    /** @brief 独立关卡资产列表与编辑会话。 */
    public sealed partial class LightLevelEditor
    {
        /** @brief 当前独立关卡。 */
        [SerializeField] private ProjectionLevel _level;
        /** @brief 正在编辑的预制体内容。 */
        [SerializeField] private GameObject _levelContents;
        /** @brief 当前关卡内部房间编号。 */
        [SerializeField] private int _editingRoom;
        /** @brief 关卡列表滚动。 */
        private Vector2 _levelScroll;
        /** @brief 对象列表滚动。 */
        private Vector2 _objectsScroll;
        /** @brief 成功打开或保存时的房间内容快照。 */
        [SerializeField] private string _savedRoomSnapshot;
        /** @brief 比较实际可保存内容，预览场景脏标记不代表用户尚未保存。 @return 是否存在未保存内容 */
        private bool HasUnsavedLevel()
        {
            return _levelContents && (RoomSnapshot() != _savedRoomSnapshot || (_level && EditorUtility.IsDirty(_level)));
        }
        /** @brief 获取当前房间的可序列化组件内容。 @return 同一编辑会话内可比较的快照 */
        private string RoomSnapshot()
        {
            if (!_levelContents)
                return "";
            var snapshot = new System.Text.StringBuilder();
            foreach (Transform node in _levelContents.GetComponentsInChildren<Transform>(true))
            {
                snapshot.Append(node.gameObject.name).Append(node.gameObject.activeSelf).Append(node.gameObject.layer);
                foreach (Component component in node.GetComponents<Component>())
                    if (component)
                        snapshot.Append(component.GetType().FullName).Append(EditorJsonUtility.ToJson(component));
            }
            return snapshot.ToString();
        }
        /** @brief 关闭窗口时处理未保存关卡并释放编辑场景。 */
        private void OnDestroy()
        {
            if (!_levelContents)
                return;
            if (HasUnsavedLevel() && EditorUtility.DisplayDialog("保存关卡", "关闭前保存关卡修改？", "保存", "放弃"))
                SaveLevel();
            PrefabUtility.UnloadPrefabContents(_levelContents);
        }
        /** @brief 保存并关闭独立关卡编辑会话。 @return 是否允许关闭 */
        private bool CloseLevel()
        {
            if (!_levelContents)
                return true;
            if (HasUnsavedLevel())
            {
                int answer = EditorUtility.DisplayDialogComplex("关卡有修改", "保存当前关卡后继续？", "保存", "取消", "放弃修改");
                if (answer == 1)
                    return false;
                if (answer == 0)
                    SaveLevel();
            }
            ReleasePreview();
            _dragging = null;
            PrefabUtility.UnloadPrefabContents(_levelContents);
            _levelContents = null;
            _room = null;
            _picked = null;
            return true;
        }
        /** @brief 打开独立关卡模板。 @param level 目标资产 */
        private void OpenLevel(ProjectionLevel level)
        {
            OpenRoom(level, 0);
        }
        /** @brief 打开关卡中指定房间。 @param level 关卡 @param index 房间编号 */
        private void OpenRoom(ProjectionLevel level, int index)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (level && (index < 0 || index >= level.RoomCount || !level.Prefab(index)))
            {
                _report = "房间模板缺失，无法打开。";
                return;
            }
            if (!CloseLevel())
                return;
            _level = level;
            _editingRoom = index;
            _layoutSelected = index;
            if (!level || !level.RoomPrefab)
                return;
            if (!level.HasLayout)
            {
                Undo.RecordObject(level, "迁移关卡布局");
                level.InitializeLayout();
                EditorUtility.SetDirty(level);
                _report = "旧关卡已按水平房间布局迁移；非相邻旧连接已停用，请检查四边规则。保存后写入资产。";
            }
            _levelContents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(index == 0 ? level.RoomPrefab : level.Rooms[index - 1].Prefab));
            _room = _levelContents.GetComponent<LightRoom>();
            _layoutMode = 0;
            _inspectTemplate = false;
            _tool = 0;
            _config = level.Config;
            _size = _room.Surface.Bounds.size;
            FrameMap();
            BuildPreview();
            _savedRoomSnapshot = RoomSnapshot();
        }
        /** @brief 保存当前关卡。 */
        private void SaveLevel()
        {
            if (!_levelContents || !_level)
                return;
            PrefabUtility.SaveAsPrefabAsset(_levelContents, AssetDatabase.GetAssetPath(_editingRoom == 0 ? _level.RoomPrefab : _level.Rooms[_editingRoom - 1].Prefab), out bool success);
            if (!success)
                throw new InvalidOperationException("房间保存失败，请检查资产路径与写入权限。");

            AssetDatabase.SaveAssets();
            _savedRoomSnapshot = RoomSnapshot();
        }
        /** @brief 创建一个独立的空白投影关卡及最小运行引用。 */
        private void CreateLevel()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建关卡", "Projection Level", "asset", "保存关卡定义", "Assets/LightPath");
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadMainAssetAtPath(path))
                return;
            if (!CloseLevel())
                return;
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var created = new System.Collections.Generic.List<string>();
            try
            {
                var level = CreateInstance<ProjectionLevel>();
                LightGameConfig original = AssetDatabase.LoadAssetAtPath<LightGameConfig>("Assets/LightPath/Config/Global/Game.asset");
                level.Config = original ? Instantiate(original) : CreateInstance<LightGameConfig>();
                string configPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path, null) + "-Config.asset");
                AssetDatabase.CreateAsset(level.Config, configPath);
                created.Add(configPath);
                LightRoomDefinition definition = CreateInstance<LightRoomDefinition>();
                definition.Configure(System.IO.Path.GetFileNameWithoutExtension(path), "获得滤镜能力，利用投影与颜色找到出口", false);
                string rulesPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path, null) + "-Room.asset");
                AssetDatabase.CreateAsset(definition, rulesPath);
                created.Add(rulesPath);
                LightRoom room = CreateBlankRoom(System.IO.Path.GetFileNameWithoutExtension(path), level.Config, definition, preview);
                GameObject root = room.gameObject;
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path, ".prefab"));
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                created.Add(prefabPath);
                if (!prefab)
                    throw new InvalidOperationException("关卡模板保存失败");
                level.RoomPrefab = prefab.GetComponent<LightRoom>();
                AssetDatabase.CreateAsset(level, path);
                created.Add(path);
                AssetDatabase.SaveAssets();
                OpenLevel(level);
            }
            catch (Exception error)
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    AssetDatabase.DeleteAsset(created[i]);
                Debug.LogException(error);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
        }
        /** @brief 创建空白房间的最小运行结构。 @param name 房间名称 @param config 关卡配置 @param definition 房间定义 @param preview 所属预览场景 @return 房间组件 */
        private static LightRoom CreateBlankRoom(string name, LightGameConfig config, LightRoomDefinition definition, UnityEngine.SceneManagement.Scene preview)
        {
            GameObject root = new GameObject(name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            root.name = name;
            LightRoom room = root.AddComponent<LightRoom>();
            GameObject origin = Child(root, "投影光源", new Vector3(0, 4, -12));
            GameObject surfaceObject = Child(root, "投影平面", Vector3.zero);
            ProjectionSurface surface = surfaceObject.AddComponent<ProjectionSurface>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/LightPath/Materials/Devices/Projection.mat");
            var surfaceData = new SerializedObject(surface);
            surfaceData.FindProperty("_config").objectReferenceValue = config;
            surfaceData.FindProperty("_origin").objectReferenceValue = origin.transform;
            surfaceData.FindProperty("_material").objectReferenceValue = material;
            surfaceData.FindProperty("_sources").arraySize = 0;
            surfaceData.ApplyModifiedPropertiesWithoutUndo();
            GameObject screen = CubeChild(root, "幕布", new Vector3(0, 4, .3f), new Vector3(18, 8, .1f), material);
            GameObject floor = CubeChild(root, "基础地板", new Vector3(0, -.5f, 0), new Vector3(18, 1, .2f), material);
            floor.layer = 9;
            floor.AddComponent<BoxCollider2D>();
            ColorPlatform platform = floor.AddComponent<ColorPlatform>();
            platform.Configure(-1);
            GameObject spawn = Child(root, "出生点", new Vector3(-8, 1, 0));
            GameObject exit = CubeChild(root, "出口", new Vector3(8, 1, 0), new Vector3(.5f, 2, .2f), material);
            exit.AddComponent<LevelExit>();
            room.Configure(definition, surface, null, new[] { platform }, spawn.transform, exit.transform, new Transform[0], screen.GetComponent<Renderer>());
            return room;
        }
        /** @brief 新建空白房间并加入当前关卡，共享本关参数。 */
        private void CreateRoom()
        {
            if (!_level || !_level.Config || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string path = EditorUtility.SaveFilePanelInProject("新建房间", "Room", "prefab", "保存当前关卡的新房间", "Assets/LightPath/Prefabs/Rooms");
            if (string.IsNullOrEmpty(path))
                return;
            if (AssetDatabase.LoadMainAssetAtPath(path))
            {
                _report = "该房间文件已存在，请填写新的名称。";
                return;
            }
            ProjectionLevel level = _level;
            int previousRoom = _editingRoom;
            if (!CloseLevel())
                return;
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            string definitionPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path, null) + "-Room.asset");
            ProjectionLevel.RoomConnection[] originalRooms = level.Rooms ?? new ProjectionLevel.RoomConnection[0];
            bool definitionCreated = false;
            bool prefabCreated = false;
            try
            {
                LightRoomDefinition definition = CreateInstance<LightRoomDefinition>();
                definition.Configure(System.IO.Path.GetFileNameWithoutExtension(path), "利用投影与颜色探索房间", false);
                AssetDatabase.CreateAsset(definition, definitionPath);
                definitionCreated = true;
                LightRoom room = CreateBlankRoom(System.IO.Path.GetFileNameWithoutExtension(path), level.Config, definition, preview);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(room.gameObject, path);
                prefabCreated = prefab;
                if (!prefab)
                    throw new InvalidOperationException("新房间模板保存失败");
                var rooms = new System.Collections.Generic.List<ProjectionLevel.RoomConnection>(originalRooms);
                rooms.Add(new ProjectionLevel.RoomConnection { Prefab = prefab.GetComponent<LightRoom>(), Position = level.FreePosition() });
                level.Rooms = rooms.ToArray();
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssets();
            }
            catch (Exception error)
            {
                level.Rooms = originalRooms;
                EditorUtility.SetDirty(level);
                if (prefabCreated)
                    AssetDatabase.DeleteAsset(path);
                if (definitionCreated)
                    AssetDatabase.DeleteAsset(definitionPath);
                AssetDatabase.SaveAssets();
                Debug.LogException(error);
                OpenRoom(level, previousRoom);
                return;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
            OpenRoom(level, level.Rooms.Length);
            _report = "已创建空白房间并加入当前关卡。请配置四向连接或跨房间传送门。";
        }
        /** @brief 建立关卡子物体。 @param root 父对象 @param name 名称 @param position 局部位置 @return 子物体 */
        private static GameObject Child(GameObject root, string name, Vector3 position)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = position;
            return child;
        }
        /** @brief 创建基础展示网格。 @param root 父对象 @param name 名称 @param position 位置 @param size 尺寸 @param material 材质 @return 对象 */
        private static GameObject CubeChild(GameObject root, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.name = name;
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = position;
            child.transform.localScale = size;
            DestroyImmediate(child.GetComponent<Collider>());
            child.GetComponent<Renderer>().sharedMaterial = material;
            return child;
        }
        /** @brief 绘制左上关卡列表。 */
        private void DrawLevels()
        {
            GUILayout.Label("关卡", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("新建关卡"))
                    CreateLevel();
                using (new EditorGUI.DisabledScope(!_level || !_level.Config))
                    if (GUILayout.Button("新建房间"))
                        CreateRoom();
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("创建第三关：六轴合奏（7房间）"))
                {
                    if (CloseLevel())
                    {
                        CreateThirdLevelAssets();
                        OpenLevel(AssetDatabase.LoadAssetAtPath<ProjectionLevel>(ThirdLevelPath));
                    }
                }
            _levelScroll = EditorGUILayout.BeginScrollView(_levelScroll, GUILayout.Height(250));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存"))
                SaveLevel();
            if (GUILayout.Button("删除") && _level && EditorUtility.DisplayDialog("删除关卡条目", "删除关卡定义资产？房间模板及配置保留。", "删除", "取消"))
            {
                string path = AssetDatabase.GetAssetPath(_level);
                if (CloseLevel())
                {
                    AssetDatabase.MoveAssetToTrash(path);
                    _level = null;
                }
            }
            EditorGUILayout.EndHorizontal();
            DrawRoomConnections();

            foreach (string guid in AssetDatabase.FindAssets("t:ProjectionLevel", new[] { "Assets/LightPath" }))
            {
                ProjectionLevel level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>(AssetDatabase.GUIDToAssetPath(guid));
                if (GUILayout.Button(level.name))
                    OpenLevel(level);
            }
            EditorGUILayout.EndScrollView();
        }
        /** @brief 编辑同一关卡的房间模板和连接。 */
        private void DrawRoomConnections()
        {
            if (!_level)
                return;
            var data = new SerializedObject(_level);
            EditorGUILayout.PropertyField(data.FindProperty("SlideSeconds"), new GUIContent("滑动秒数"));
            if (data.ApplyModifiedProperties())
                AssetDatabase.SaveAssets();
            GUILayout.Label("关卡模式拖动房间排布；房间模式配置四边行为。", EditorStyles.wordWrappedMiniLabel);
            string[] rooms = new string[_level.Rooms.Length + 1];
            rooms[0] = "0 主房间";
            for (int i = 1; i < rooms.Length; i++)
                rooms[i] = i + " " + (_level.Rooms[i - 1]?.Prefab ? _level.Rooms[i - 1].Prefab.name : "未绑定");
            int selected = EditorGUILayout.Popup("正在编辑房间", Mathf.Clamp(_editingRoom, 0, rooms.Length - 1), rooms);
            if (selected != _editingRoom && (selected == 0 || _level.Rooms[selected - 1]?.Prefab))
                OpenRoom(_level, selected);
            if (GUILayout.Button("复制当前房间为同关新房间") && _room)
            {
                string path = EditorUtility.SaveFilePanelInProject("新房间模板", "Room", "prefab", "保存房间", "Assets/LightPath/Prefabs/Rooms");
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadMainAssetAtPath(path))
                    return;
                SaveLevel();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_room.gameObject, path);
                Undo.RecordObject(_level, "添加房间");
                var entries = new System.Collections.Generic.List<ProjectionLevel.RoomConnection>(_level.Rooms);
                entries.Add(new ProjectionLevel.RoomConnection { Prefab = prefab.GetComponent<LightRoom>(), Position = _level.FreePosition() });
                _level.Rooms = entries.ToArray();
                EditorUtility.SetDirty(_level);
                AssetDatabase.SaveAssets();
                OpenRoom(_level, entries.Count);
            }
        }
        /** @brief 绘制右上关卡对象列表。 */
        private void DrawLevelObjects()
        {
            GUILayout.Label(_layoutMode == 1 ? "当前关卡房间" : "当前房间对象", EditorStyles.boldLabel);
            _objectsScroll = EditorGUILayout.BeginScrollView(_objectsScroll, GUILayout.Height(180));
            if (_layoutMode == 1 && _level)
            {
                for (int i = 0; i < _level.RoomCount; i++)
                    if (GUILayout.Button(RoomName(i)))
                    {
                        _layoutSelected = i;
                        Repaint();
                    }
            }
            else if (_room)
                foreach (Transform child in _room.GetComponentsInChildren<Transform>(true))
                    if (child != _room.transform && (child.parent == _room.transform || child.GetComponent<ProjectionSource>()))
                        if (GUILayout.Button(child.name))
                        {
                            _picked = child.gameObject;
                            _inspectTemplate = false;
                            _center = DisplayPosition(_picked);
                        }
            EditorGUILayout.EndScrollView();
        }
        /** @brief 中下3D关卡预览及物件位姿调整。 */
        private void DrawLevel3D()
        {
            GUILayout.Label("3D投影工作区", EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(100, 10000, 190, 190);
            if (_room)
                DrawModelPreview(_room.gameObject, rect);
            else
                GUI.Box(rect, "选择关卡");
            if (_picked && Editable())
            {
                EditorGUI.BeginChangeCheck();
                Vector3 position = EditorGUILayout.Vector3Field("3D位置", _picked.transform.position);
                Vector3 rotation = EditorGUILayout.Vector3Field("3D旋转", _picked.transform.eulerAngles);
                if (EditorGUI.EndChangeCheck())
                {
                    Edit("调整3D物件", () => { Undo.RecordObject(_picked.transform, "调整3D物件"); _picked.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation)); });
                    BuildPreview();
                }
            }
            else
            {
                EditorGUILayout.Vector3Field("3D位置", Vector3.zero);
                EditorGUILayout.Vector3Field("3D旋转", Vector3.zero);
            }
        }
        /** @brief 快速调整关卡专属采样参数。 */
        private void DrawSampling()
        {
            if (!_config)
                return;
            var settings = new SerializedObject(_config);
            EditorGUILayout.PropertyField(settings.FindProperty("_defaultBackgroundColor"), new GUIContent("幕布默认全局颜色"));
            EditorGUILayout.PropertyField(settings.FindProperty("_cellSize"), new GUIContent("采样格尺寸"));
            EditorGUILayout.PropertyField(settings.FindProperty("_edgeRefinement"), new GUIContent("边缘细化级别"));
            if (settings.ApplyModifiedProperties() && _room)
                BuildPreview();
        }
    }
}
