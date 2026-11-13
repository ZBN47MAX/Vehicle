using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 独立关卡格布局、边界属性和门目标选择。 */
    public sealed partial class LightLevelEditor
    {
        /** @brief 0房间内容，1关卡布局。 */
        [SerializeField] private int _layoutMode;
        /** @brief 当前布局选中房间。 */
        private int _layoutSelected;
        /** @brief 正在拖动的房间。 */
        private int _layoutDragging = -1;
        /** @brief 布局画布像素偏移。 */
        private Vector2 _layoutPan;
        /** @brief 每个关卡格的像素宽度，不使用房间米制网格。 */
        private float _layoutScale = 150;
        /** @brief 拖动预览目标格。 */
        private Vector2Int _layoutDrop;
        /** @brief 房间显示名称。 @param index 房间编号 @return 编号与名称 */
        private string RoomName(int index) => index + " " + (_level.Prefab(index) ? _level.Prefab(index).name : "缺少模板");
        /** @brief 关卡格转换为画布矩形。 @param cell 格坐标 @param area 画布 @return 像素矩形 */
        private Rect LayoutRect(Vector2Int cell, Rect area)
        {
            Vector2 center = area.center + _layoutPan + new Vector2(cell.x, -cell.y) * _layoutScale;
            return new Rect(center - Vector2.one * (_layoutScale * .46f), Vector2.one * (_layoutScale * .92f));
        }
        /** @brief 绘制可拖动的关卡布局，鼠标释放时提交一次Undo。 */
        private void DrawLayoutWorkspace()
        {
            EditorGUILayout.HelpBox("关卡网格：一格一房间，独立于房间内米制网格。左键选择/拖动；双击编辑房间；中键平移、滚轮缩放。", MessageType.Info);
            if (GUILayout.Button("居中显示布局"))
            {
                _layoutPan = Vector2.zero;
                if (_level)
                {
                    Vector2 sum = Vector2.zero;
                    for (int i = 0; i < _level.RoomCount; i++)
                        sum += (Vector2)_level.Position(i);
                    sum /= _level.RoomCount;
                    _layoutPan = new Vector2(-sum.x, sum.y) * _layoutScale;
                }
            }
            Rect canvas = GUILayoutUtility.GetRect(100, 10000, 250, 10000, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(canvas, new Color(.08f, .09f, .12f));
            if (!_level)
            {
                GUI.Label(canvas, "请先选择或新建关卡");
                return;
            }
            GUI.BeginClip(canvas);
            Rect area = new Rect(Vector2.zero, canvas.size);
            Event e = Event.current;
            Vector2 origin = area.center + _layoutPan;
            Handles.BeginGUI();
            Handles.color = new Color(1, 1, 1, .1f);
            for (float x = Mathf.Repeat(origin.x - _layoutScale / 2, _layoutScale); x < area.width; x += _layoutScale)
                Handles.DrawLine(new Vector2(x, 0), new Vector2(x, area.height));
            for (float y = Mathf.Repeat(origin.y - _layoutScale / 2, _layoutScale); y < area.height; y += _layoutScale)
                Handles.DrawLine(new Vector2(0, y), new Vector2(area.width, y));
            for (int i = 0; i < _level.RoomCount; i++)
            {
                Rect rect = LayoutRect(_level.Position(i), area);
                EditorGUI.DrawRect(rect, i == _layoutSelected ? new Color(.22f, .4f, .55f) : new Color(.18f, .21f, .25f));
                GUI.Label(new Rect(rect.x + 4, rect.y + 3, rect.width - 8, 20), RoomName(i), EditorStyles.boldLabel);
                GUI.Label(new Rect(rect.x + 4, rect.y + 23, rect.width - 8, 20), _level.Position(i).ToString());
                DrawRoomThumbnail(i, new Rect(rect.x + 6, rect.y + 45, rect.width - 12, Mathf.Max(10, rect.height - 65)));
                for (int d = 0; d < 4; d++)
                {
                    Vector2 offset = ProjectionLevel.Offset(d);
                    Vector2 point = rect.center + new Vector2(offset.x, -offset.y) * rect.width * .45f;
                    ProjectionLevel.EdgeBehavior edge = _level.Edge(i, d);
                    Handles.color = edge == ProjectionLevel.EdgeBehavior.Move ? Color.green : edge == ProjectionLevel.EdgeBehavior.Hazard ? Color.red : Color.gray;
                    Vector2 tangent = new Vector2(offset.y, offset.x) * rect.width * .2f;
                    Handles.DrawAAPolyLine(4, point - tangent, point + tangent);
                }
                if (!EditorApplication.isPlayingOrWillChangePlaymode && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    _layoutSelected = i;
                    if (e.clickCount == 2)
                    {
                        OpenRoom(_level, i);
                        _layoutMode = 0;
                    }
                    else
                    {
                        _layoutDragging = i;
                        _layoutDrop = _level.Position(i);
                    }
                    e.Use();
                }
            }
            if (_layoutDragging >= 0)
            {
                Rect ghost = LayoutRect(_layoutDrop, area);
                Handles.color = Color.yellow;
                Handles.DrawWireCube(ghost.center, ghost.size);
            }
            Handles.EndGUI();
            if (area.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
            {
                float old = _layoutScale;
                _layoutScale = Mathf.Clamp(_layoutScale * Mathf.Exp(-e.delta.y * .06f), 75, 300);
                _layoutPan *= _layoutScale / old;
                e.Use();
            }
            if (e.type == EventType.MouseDrag && e.button == 2 && area.Contains(e.mousePosition))
            {
                _layoutPan += e.delta;
                e.Use();
            }
            if (_layoutDragging >= 0 && e.type == EventType.MouseDrag && e.button == 0)
            {
                Vector2 delta = (e.mousePosition - area.center - _layoutPan) / _layoutScale;
                _layoutDrop = new Vector2Int(Mathf.RoundToInt(delta.x), -Mathf.RoundToInt(delta.y));
                e.Use();
            }
            if (_layoutDragging >= 0 && e.type == EventType.MouseUp && e.button == 0)
            {
                if (area.Contains(e.mousePosition) && _layoutDrop != _level.Position(_layoutDragging))
                {
                    Undo.RecordObject(_level, "移动关卡房间");
                    if (_level.TryMoveRoom(_layoutDragging, _layoutDrop))
                    {
                        EditorUtility.SetDirty(_level);
                        _report = "房间布局已更新；失去邻居的移动边已改为阻挡，新相邻边需手动开启。";
                    }
                    else
                        _report = "该格已有房间，移动未生效。";
                }
                _layoutDragging = -1;
                e.Use();
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _layoutDragging = -1;
                e.Use();
            }
            GUI.EndClip();
            if (GUI.changed || e.type == EventType.Used)
                Repaint();
            GUILayout.Label("绿边：移动　红边：危险　灰边：阻挡。布局修改使用左侧保存按钮写入。", EditorStyles.miniLabel);
            if (_layoutSelected == _editingRoom)
                DrawLevel3D();
            else
            {
                GUILayout.Label("所选房间3D预览（进入房间模式后编辑）", EditorStyles.boldLabel);
                Rect preview = GUILayoutUtility.GetRect(100, 10000, 190, 190);
                if (_level.Prefab(_layoutSelected))
                    DrawModelPreview(_level.Prefab(_layoutSelected).gameObject, preview);
            }
        }
        /** @brief 绘制房间固定平台与投影源位置的布局缩略图。 @param index 房间 @param rect 缩略范围 */
        private void DrawRoomThumbnail(int index, Rect rect)
        {
            LightRoom room = index == _editingRoom && _room ? _room : _level.Prefab(index);
            if (!room || !room.Surface)
                return;
            LightStagePreview.Configure(room.Surface);
            Rect bounds = room.Surface.Bounds;
            foreach (ColorPlatform platform in room.GetComponentsInChildren<ColorPlatform>(true))
            {
                Vector3 point = platform.transform.position;
                Vector3 size = platform.transform.lossyScale;
                float x = rect.x + (point.x - bounds.xMin) / bounds.width * rect.width;
                float y = rect.yMax - (point.y - bounds.yMin) / bounds.height * rect.height;
                Rect mark = new Rect(x - Mathf.Abs(size.x) / bounds.width * rect.width / 2, y - 2, Mathf.Abs(size.x) / bounds.width * rect.width, 4);
                if (rect.Contains(mark.center))
                    EditorGUI.DrawRect(mark, Color.gray);
            }
            foreach (ProjectionSource source in room.GetComponentsInChildren<ProjectionSource>(true))
            {
                Vector2 point = room.Surface.ProjectPoint(source.transform.position);
                Vector2 pixel = new Vector2(rect.x + (point.x - bounds.xMin) / bounds.width * rect.width, rect.yMax - (point.y - bounds.yMin) / bounds.height * rect.height);
                if (rect.Contains(pixel))
                    EditorGUI.DrawRect(new Rect(pixel - Vector2.one * 3, Vector2.one * 6), Color.cyan);
            }
        }
        /** @brief 在固定右侧属性栏编辑房间四边。 */
        private void DrawRoomEdges()
        {
            if (!_level)
                return;
            int index = _layoutMode == 1 ? Mathf.Clamp(_layoutSelected, 0, _level.RoomCount - 1) : _editingRoom;
            GUILayout.Label("房间规则：" + RoomName(index), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                Vector2Int cell = EditorGUILayout.Vector2IntField("关卡格坐标", _level.Position(index));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_level, "调整房间格坐标");
                    if (_level.TryMoveRoom(index, cell))
                        EditorUtility.SetDirty(_level);
                    else
                        _report = "目标格已被占用。";
                }
                string[] directions = { "左边", "右边", "下边", "上边" };
                for (int d = 0; d < 4; d++)
                {
                    int neighbor = _level.Neighbor(index, d);
                    string[] choices = neighbor >= 0 ? new[] { "阻挡", "危险", "移动 → " + RoomName(neighbor) } : new[] { "阻挡", "危险" };
                    EditorGUI.BeginChangeCheck();
                    int value = EditorGUILayout.Popup(directions[d], (int)_level.Edge(index, d), choices);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_level, "修改房间边界");
                        _level.SetEdge(index, d, (ProjectionLevel.EdgeBehavior)value);
                        EditorUtility.SetDirty(_level);
                    }
                }
                if (_layoutMode == 1 && GUILayout.Button("编辑此房间内容"))
                {
                    OpenRoom(_level, index);
                    _layoutMode = 0;
                }
            }
        }
        /** @brief 获取房间内两类传送门配置候选。@param room 房间 @return 可连接门 */
        private static GameObject[] EditorPortals(LightRoom room)
        {
            return room.GetComponentsInChildren<Transform>(true).Select(x => x.gameObject).Where(x =>
            {
                var projection = x.GetComponent<ProjectionSource>();
                bool gate = projection ? projection.Definition && projection.Definition.Portal : x.GetComponent<RoomPortalLink>() && x.GetComponent<Collider2D>();
                return gate && !x.GetComponent<LevelExit>() && !x.GetComponent<ColorAbilityKey>() && !x.GetComponent<ProjectionAbilityKey>();
            }).ToArray();
        }
        /** @brief 检查当前门的显式连接、双向回程或旧配对。 @param source 门 @return 问题或空字符串 */
        private string PortalConnectionIssue(GameObject source)
        {
            RoomPortalLink link = source.GetComponent<RoomPortalLink>();
            if (link && !link.ArrivalOnly)
            {
                if (!_level || link.TargetRoom < 0 || link.TargetRoom >= _level.RoomCount)
                    return "目标房间无效";
                LightRoom room = link.TargetRoom == _editingRoom ? _room : _level.Prefab(link.TargetRoom);
                if (!room)
                    return "目标房间模板缺失";
                if (link.UseArrivalPosition)
                {
                    Vector2 point = link.ResolveArrival(room.transform.position);
                    return room.Surface.Bounds.Contains(point) ? "" : "抵达坐标位于目标房间幕布之外";
                }
                GameObject[] matches = EditorPortals(room).Where(x => x.name == link.TargetPortal).ToArray();
                if (matches.Length != 1 || matches[0] == source)
                    return "目标门缺失、重名或指向自身";
                if (link.Bidirectional && matches[0].TryGetComponent(out RoomPortalLink reverse) && !reverse.ArrivalOnly && (reverse.UseArrivalPosition || reverse.TargetRoom != _editingRoom || reverse.TargetPortal != source.name))
                    return "双向目标门已有不同的显式出口，请先移除冲突连接";
                return "";
            }
            if (_level)
            {
                int incoming = 0;
                for (int i = 0; i < _level.RoomCount; i++)
                {
                    LightRoom room = i == _editingRoom ? _room : _level.Prefab(i);
                    if (room)
                        incoming += room.GetComponentsInChildren<RoomPortalLink>(true).Count(x => !x.ArrivalOnly && !x.UseArrivalPosition && x.Bidirectional && x.TargetRoom == _editingRoom && x.TargetPortal == source.name);
                }
                if (incoming > 1)
                    return "多个双向连接指向此门";
                if (incoming == 1)
                    return "";
            }
            return source.GetComponent<ProjectionSource>() && source.GetComponent<ProjectionSource>().Partner ? "" : "未配置出口（若作为单向到达端可忽略）";
        }
        /** @brief 从房间和门列表配置独立传送连接。 @param source 当前投影门 */
        private void DrawPortalConnection(GameObject source)
        {
            if (!_level || source.GetComponent<LevelExit>() || source.GetComponent<ColorAbilityKey>() || source.GetComponent<ProjectionAbilityKey>())
                return;
            RoomPortalLink link = source.GetComponent<RoomPortalLink>();
            var projection = source.GetComponent<ProjectionSource>();
            if (!link && GUILayout.Button("配置传送门目标"))
            {
                if (projection && projection.Partner && projection.Partner.Partner == projection)
                    SetPartner(projection.Partner, null);
                if (projection) SetPartner(projection, null);
                link = Undo.AddComponent<RoomPortalLink>(source);
            }
            if (!link)
                return;
            var data = new SerializedObject(link);
            EditorGUILayout.PropertyField(data.FindProperty("ArrivalOnly"), new GUIContent("仅作为抵达端/自动回程"));
            if (data.FindProperty("ArrivalOnly").boolValue)
            {
                data.ApplyModifiedProperties();
                EditorGUILayout.HelpBox("作为目标门接收传送。其他门以双向连接指向此门时，可自动回程。", MessageType.Info);
                return;
            }
            string[] rooms = Enumerable.Range(0, _level.RoomCount).Select(RoomName).ToArray();
            int oldRoom = data.FindProperty("TargetRoom").intValue;
            int targetRoom = EditorGUILayout.Popup("目标房间", Mathf.Clamp(oldRoom, 0, rooms.Length - 1), rooms);
            data.FindProperty("TargetRoom").intValue = targetRoom;
            LightRoom target = targetRoom == _editingRoom ? _room : _level.Prefab(targetRoom);
            var usePosition = data.FindProperty("UseArrivalPosition");
            usePosition.boolValue = EditorGUILayout.Popup("抵达方式", usePosition.boolValue ? 0 : 1, new[] { "指定位置（单向）", "连接另一扇门" }) == 0;
            if (usePosition.boolValue)
            {
                EditorGUILayout.PropertyField(data.FindProperty("ArrivalPosition"), new GUIContent("抵达坐标（目标房间内）"));
                data.FindProperty("Bidirectional").boolValue = false;
                EditorGUILayout.HelpBox("坐标相对目标房间根，按F传送。抵达点需容纳角色且避开实体。", MessageType.Info);
            }
            GameObject[] portals = target ? EditorPortals(target).Where(x => x != source).ToArray() : new GameObject[0];
            string oldName = data.FindProperty("TargetPortal").stringValue;
            string[] names = new[] { "请选择目标门" }.Concat(portals.Select(x => x.name)).ToArray();
            int selected = targetRoom == oldRoom ? System.Array.FindIndex(portals, x => x.name == oldName) + 1 : 0;
            int choice;
            using (new EditorGUI.DisabledScope(usePosition.boolValue))
                choice = EditorGUILayout.Popup("目标门", selected, names);
            if (choice > 0)
                data.FindProperty("TargetPortal").stringValue = portals[choice - 1].name;
            else if (oldRoom != targetRoom)
                data.FindProperty("TargetPortal").stringValue = "";
            if (!usePosition.boolValue)
            {
                bool twoWay = data.FindProperty("Bidirectional").boolValue;
                data.FindProperty("Bidirectional").boolValue = EditorGUILayout.Popup("方向", twoWay ? 1 : 0, new[] { "单向：当前门 → 目标门", "双向：两门互通" }) == 1;
            }
            data.ApplyModifiedProperties();
            string issue = PortalConnectionIssue(source);
            if (!string.IsNullOrEmpty(issue))
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            if (projection && projection.Partner)
                EditorGUILayout.HelpBox("此连接优先于旧Partner。建议解除旧配对，避免残留反向通路。", MessageType.Warning);
            if (portals.GroupBy(x => x.name).Any(x => x.Count() > 1))
                EditorGUILayout.HelpBox("目标房间有同名门，请重命名为唯一名称后保存。", MessageType.Error);
            if (!usePosition.boolValue && selected == 0 && !string.IsNullOrEmpty(oldName))
                EditorGUILayout.HelpBox("原目标门已失效，请重新选择。", MessageType.Warning);
            if (GUILayout.Button("移除传送连接"))
                Undo.DestroyObjectImmediate(link);
        }
    }
}
