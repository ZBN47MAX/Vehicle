using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace DelayNoMore.LightPath.Editor
{
    /** @brief 整局维修装置的独立三栏编辑器。 */
    public sealed class RepairLayoutEditor : EditorWindow
    {
        /** @brief 正在编辑的配置。 */
        [SerializeField] private RepairLayout _layout;
        /** @brief 当前选中机关。 */
        private int _selected = -1;
        /** @brief 当前阶段。 */
        private int _phase;
        /** @brief 试玩朝向，空表示编辑。 */
        private float[] _trial;
        /** @brief 画布缩放。 */
        private float _zoom = 32;
        /** @brief 画布偏移。 */
        private Vector2 _pan;
        /** @brief 拖动状态。 */
        private bool _drag;
        /** @brief 与游戏运行共用的节点角位移拖动状态。 */
        private RepairLensDrag _lensDrag;
        /** @brief 右栏滚动。 */
        private Vector2 _scroll;
        /** @brief 类型中文名。 */
        private static readonly string[] Names = { "光源", "发射透镜", "分光器", "折镜节点", "出口" };
        /** @brief 打开窗口。 */
        [MenuItem("Tools/Light Path/维修光路编辑器", false, 1)]
        [MenuItem("Window/Light Path/维修光路编辑器 %&r", false, 2001)]
        public static void Open()
        {
            RepairLayoutEditor window = GetWindow<RepairLayoutEditor>("维修光路编辑器");
            window.Show();
            window.Focus();
        }
        /** @brief 初始化尺寸与撤销刷新。 */
        private void OnEnable() { minSize = new Vector2(1050, 650); Undo.undoRedoPerformed += Changed; }
        /** @brief 关闭时提示保存配置。 */
        private void OnDestroy()
        {
            if (_layout && EditorUtility.IsDirty(_layout) && EditorUtility.DisplayDialog("保存维修配置", "关闭前保存修改？", "保存", "放弃")) AssetDatabase.SaveAssetIfDirty(_layout);
        }
        /** @brief 解绑事件。 */
        private void OnDisable() { Undo.undoRedoPerformed -= Changed; }
        /** @brief 撤销后退出试玩并刷新。 */
        private void Changed() { _trial = null; _selected = -1; Repaint(); }
        /** @brief 写入配置前登记Undo。 */
        private void Edit() { Undo.RecordObject(_layout, "编辑维修光路"); EditorUtility.SetDirty(_layout); }
        /** @brief 切换配置前处理保存。 @return 是否继续 */
        private bool SavePrompt()
        {
            if (!_layout || !EditorUtility.IsDirty(_layout)) return true;
            int answer = EditorUtility.DisplayDialogComplex("维修配置有修改", "保存当前配置后继续？", "保存", "取消", "放弃");
            if (answer == 1) return false;
            if (answer == 0) AssetDatabase.SaveAssetIfDirty(_layout);
            else AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(_layout), ImportAssetOptions.ForceUpdate);
            return true;
        }
        /** @brief 绘制整体窗口。 */
        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                RepairLayout next = (RepairLayout)EditorGUILayout.ObjectField(_layout, typeof(RepairLayout), false);
                if (next != _layout && SavePrompt()) { _layout = next; Changed(); }
                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(55)) && SavePrompt())
                {
                    string path = EditorUtility.SaveFilePanelInProject("新建维修配置", "Repair Layout", "asset", "整局共用的维修布局");
                    if (!string.IsNullOrEmpty(path) && !AssetDatabase.LoadMainAssetAtPath(path))
                    { _layout = CreateInstance<RepairLayout>(); AssetDatabase.CreateAsset(_layout, path); Changed(); }
                }
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(55)) && _layout) AssetDatabase.SaveAssetIfDirty(_layout);
                if (_layout && GUILayout.Button(_trial == null ? "进入试玩" : "退出试玩", EditorStyles.toolbarButton, GUILayout.Width(85)))
                    _trial = _trial == null ? _layout.Devices.Select(x => x.OutputAngle).ToArray() : null;
                EditorGUILayout.EndHorizontal();
                if (!_layout) { EditorGUILayout.HelpBox("选择或新建整局维修配置。", MessageType.Info); return; }
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.Width(170));
                GUILayout.Label("可用机关", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(_trial != null))
                    for (int i = 0; i < Names.Length; i++)
                            if (GUILayout.Button("添加" + Names[i]))
                            {
                                Edit();
                                var devices = _layout.Devices.ToList();
                                devices.Add(new RepairLayout.Device { Name = Names[i] + (devices.Count + 1), Type = (RepairLayout.Kind)i, Position = -_pan / _zoom });
                                _layout.Devices = devices.ToArray(); _selected = devices.Count - 1;
                            }
                GUILayout.Label("已有机关", EditorStyles.boldLabel);
                for (int i = 0; i < _layout.Devices.Length; i++)
                    if (GUILayout.Button(_layout.Devices[i].Name)) _selected = i;
                EditorGUILayout.EndVertical();
                EditorGUILayout.BeginVertical();
                Canvas(GUILayoutUtility.GetRect(200, 10000, 400, 10000, GUILayout.ExpandHeight(true)));
                GUILayout.Label(_trial == null ? "左键选中拖动 · 中键平移 · 滚轮缩放" : "折镜点击换档；透镜按住左键拖动瞄准，或使用右侧角度滑条", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.BeginVertical(GUILayout.Width(300));
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                using (new EditorGUI.DisabledScope(_trial != null)) { Settings(); Inspector(); }
                if (_trial != null && _selected >= 0 && _selected < _layout.Devices.Length && _layout.Devices[_selected].Type == RepairLayout.Kind.Lens)
                {
                    var lens = _layout.Devices[_selected];
                    float basis = lens.Direction * 45f;
                    _trial[_selected] = basis + EditorGUILayout.Slider("试玩偏转角（不保存）", _trial[_selected] - basis, lens.AngleRange.x, lens.AngleRange.y);
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
        }
        /** @brief 世界平面映射画布。 @param p 平面点 @param rect 画布 @return 画布点 */
        private Vector2 Point(Vector2 p, Rect rect) => rect.center + _pan + new Vector2(p.x, -p.y) * _zoom;
        /** @brief 绘制光路与交互。 @param rect 画布 */
        private void Canvas(Rect rect)
        {
            GUI.BeginClip(rect);
            Rect area = new Rect(Vector2.zero, rect.size);
            EditorGUI.DrawRect(area, new Color(.08f, .1f, .13f));
            Handles.BeginGUI();
            Rect bounds = _layout.Area;
            Vector2 top = Point(new Vector2(bounds.xMin, bounds.yMax), area);
            Handles.color = Color.gray;
            Handles.DrawWireCube(top + bounds.size * _zoom / 2, bounds.size * _zoom);
            RepairPreviewSolver.Result result = RepairPreviewSolver.TraceAngles(_layout, _trial);
            Handles.color = Color.yellow;
            foreach (var segment in result.Segments) Handles.DrawAAPolyLine(2, Point(segment.From, area), Point(segment.To, area));
            Event e = Event.current;
            for (int i = 0; i < _layout.Devices.Length; i++)
            {
                RepairLayout.Device device = _layout.Devices[i];
                Vector2 p = Point(device.Position, area);
                Rect hit = new Rect(p - Vector2.one * (_layout.NodeRadius * _zoom), Vector2.one * (_layout.NodeRadius * _zoom * 2));
                EditorGUI.DrawRect(hit, i == _selected ? Color.cyan : result.Lit.Contains(device.Id) ? Color.green : Color.gray);
                float direction = _trial == null ? device.OutputAngle : _trial[i];
                float angle = direction * Mathf.Deg2Rad;
                if (i == _selected) DrawDirections(device, p);
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(3, p, p + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * 19);
                bool required = _layout.Phases.Length > 0 && _layout.Phases[Mathf.Clamp(_phase, 0, _layout.Phases.Length - 1)].Targets.Contains(device.Id);
                GUI.Label(new Rect(p.x + 16, p.y - 12, 180, 22), device.Name + (required ? " [目标]" : ""));
                if (e.type == EventType.MouseDown && e.button == 0 && (hit.Contains(e.mousePosition) || RepairPreviewSolver.AngleLineDistance(e.mousePosition, p, -direction, 19) <= 8))
                {
                    _selected = i;
                    if (_trial != null && device.Type == RepairLayout.Kind.Lens)
                    {
                        _drag = true;
                        Vector2 start = (e.mousePosition - area.center - _pan) / _zoom;
                        _lensDrag.Begin(new Vector2(start.x, -start.y));
                    }
                    else if (_trial != null && device.Type != RepairLayout.Kind.Source && device.Type != RepairLayout.Kind.Exit) _trial[i] = RepairPreviewSolver.Next(Mathf.RoundToInt(_trial[i] / 45f), device.Allowed) * 45f;
                    else if (_trial == null) { Edit(); _drag = true; }
                    e.Use();
                }
            }
            Handles.EndGUI();
            if (!_drag && area.Contains(e.mousePosition) && e.type == EventType.ScrollWheel) { _zoom = Mathf.Clamp(_zoom * Mathf.Exp(-e.delta.y * .08f), 8, 100); e.Use(); }
            if (!_drag && e.type == EventType.MouseDrag && e.button == 2) { _pan += e.delta; e.Use(); }
            if (_drag && _trial == null && _selected >= 0 && e.type == EventType.MouseDrag && e.button == 0)
            {
                Vector2 p = (e.mousePosition - area.center - _pan) / _zoom;
                _layout.Devices[_selected].Position = new Vector2(Mathf.Clamp(p.x, bounds.xMin, bounds.xMax), Mathf.Clamp(-p.y, bounds.yMin, bounds.yMax)); e.Use();
            }
            if (_drag && _trial != null && _selected >= 0 && e.type == EventType.MouseDrag && e.button == 0)
            {
                var device = _layout.Devices[_selected];
                Vector2 pointer = (e.mousePosition - area.center - _pan) / _zoom;
                float next = _lensDrag.Step(device.Position, new Vector2(pointer.x, -pointer.y), true, _trial[_selected], _layout.NodeRadius * .25f, _layout.LensDragSensitivity);
                _trial[_selected] = RepairPreviewSolver.ClampLensAngle(device, next);
                e.Use();
            }
            if (e.type == EventType.MouseUp) _drag = false;
            string status = "请配置阶段目标";
            if (_layout.Phases.Length > 0)
            {
                _phase = Mathf.Clamp(_phase, 0, _layout.Phases.Length - 1);
                string[] targets = _layout.Phases[_phase].Targets;
                status = result.Matches(targets) ? "当前阶段精确接通" : "当前阶段未接通（缺少目标或点亮了额外出口）";
            }
            GUI.Label(new Rect(8, 8, area.width - 16, 24), status + (result.Repeated ? " · 存在循环或合流" : ""));
            GUI.EndClip();
            if (e.type == EventType.Used) Repaint();
        }
        /** @brief 灰色虚线表示可选方向；透镜额外画连续区间弧线，不代表实际分光。 */
        private static void DrawDirections(RepairLayout.Device device, Vector2 center)
        {
            Handles.color = new Color(.55f, .65f, .75f, .65f);
            if (device.Type == RepairLayout.Kind.Mirror)
            {
                for (int i = 0; i < 8; i++)
                    if ((device.Allowed & (1 << i)) != 0)
                    {
                        float radians = i * 45 * Mathf.Deg2Rad;
                        Handles.DrawDottedLine(center, center + new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)) * 65, 4);
                    }
            }
            if (device.Type != RepairLayout.Kind.Lens) return;
            Vector3[] arc = new Vector3[25];
            for (int i = 0; i < arc.Length; i++)
            {
                float angle = (device.Direction * 45 + Mathf.Lerp(device.AngleRange.x, device.AngleRange.y, i / 24f)) * Mathf.Deg2Rad;
                arc[i] = center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * 65;
                if (i % 6 == 0) Handles.DrawDottedLine(center, arc[i], 4);
            }
            Handles.DrawAAPolyLine(1, arc);
        }
        /** @brief 编辑整局时序和阶段目标。 */
        private void Settings()
        {
            GUILayout.Label("整局维修规则", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector2 interval = EditorGUILayout.Vector2Field("事件间隔秒数 最短/最长", _layout.Interval);
            bool randomOrder = EditorGUILayout.Toggle("阶段随机循环（每轮不重复）", _layout.RandomPhaseOrder);
            float warning = EditorGUILayout.FloatField("预警秒数", _layout.WarningSeconds);
            Rect bounds = EditorGUILayout.RectField("操作范围", _layout.Area);
            float sensitivity = EditorGUILayout.Slider("透镜绕心拖动灵敏度", _layout.LensDragSensitivity, .05f, 1f);
            float radius = EditorGUILayout.FloatField("节点边框半宽（同时用于命中）", _layout.NodeRadius);
            if (EditorGUI.EndChangeCheck())
            {
                Edit(); _layout.LensDragSensitivity = Mathf.Clamp(sensitivity, .05f, 1f); _layout.WarningSeconds = Mathf.Max(.1f, warning);
                _layout.RandomPhaseOrder = randomOrder;
                _layout.Interval = new Vector2(Mathf.Max(_layout.WarningSeconds + .1f, interval.x), Mathf.Max(_layout.WarningSeconds + .1f, interval.x, interval.y));
                bounds.width = Mathf.Max(1, bounds.width); bounds.height = Mathf.Max(1, bounds.height); _layout.Area = bounds; _layout.NodeRadius = Mathf.Max(.01f, radius);
            }
            if (GUILayout.Button("增加目标阶段")) { Edit(); var phases = _layout.Phases.ToList(); phases.Add(new RepairLayout.Phase()); _layout.Phases = phases.ToArray(); _phase = phases.Count - 1; }
            if (_layout.Phases.Length > 0)
            {
                _phase = EditorGUILayout.Popup("预览阶段", Mathf.Clamp(_phase, 0, _layout.Phases.Length - 1), _layout.Phases.Select((x, i) => i + " " + x.Name).ToArray());
                RepairLayout.Phase phase = _layout.Phases[_phase];
                string name = EditorGUILayout.TextField("阶段名称", phase.Name);
                if (name != phase.Name) { Edit(); phase.Name = name; }
                foreach (var exit in _layout.Devices.Where(x => x.Type == RepairLayout.Kind.Exit))
                {
                    bool before = phase.Targets.Contains(exit.Id);
                    bool after = EditorGUILayout.ToggleLeft("需要点亮 " + exit.Name, before);
                    if (before != after) { Edit(); var targets = phase.Targets.ToList(); if (after) targets.Add(exit.Id); else targets.Remove(exit.Id); phase.Targets = targets.ToArray(); }
                }
                if (GUILayout.Button("删除此阶段")) { Edit(); _layout.Phases = _layout.Phases.Where((x, i) => i != _phase).ToArray(); }
            }
            if (!_layout.Devices.Any(x => x.Type == RepairLayout.Kind.Source)) EditorGUILayout.HelpBox("需要至少一个光源，可添加多个光源。", MessageType.Warning);
            if (_layout.Phases.Length == 0 || _layout.Phases.Any(x => x.Targets.Length == 0)) EditorGUILayout.HelpBox("每个阶段至少选择一个目标出口。", MessageType.Warning);
            if (!_layout.TryValidate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            for (int i = 0; i < _layout.Devices.Length; i++)
                if (_layout.Devices.Skip(i + 1).Any(x => Vector2.Distance(x.Position, _layout.Devices[i].Position) <= _layout.NodeRadius * 2)) { EditorGUILayout.HelpBox("机关存在重叠，请分开放置。", MessageType.Warning); break; }
        }
        /** @brief 固定右栏编辑选中机关。 */
        private void Inspector()
        {
            GUILayout.Space(15); GUILayout.Label("选中机关参数", EditorStyles.boldLabel);
            if (_selected < 0 || _selected >= _layout.Devices.Length) { GUILayout.Label("请在预览中选择机关"); return; }
            var device = _layout.Devices[_selected];
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("名称", device.Name);
            EditorGUILayout.LabelField("类型", Names[(int)device.Type]);
            Vector2 position = EditorGUILayout.Vector2Field("位置", device.Position);
            int direction = EditorGUILayout.IntSlider(device.Type == RepairLayout.Kind.Lens ? "自身基准朝向 ×45°" : "初始出射方向 ×45°", device.Direction, 0, 7);
            int mask = device.Allowed;
            Vector2 range = device.AngleRange;
            float initial = device.InitialAngle;
            if (device.Type == RepairLayout.Kind.Lens)
            {
                range = EditorGUILayout.Vector2Field("连续偏转区间 最小/最大°", range);
                range.x = Mathf.Clamp(range.x, -180, 180);
                range.y = Mathf.Clamp(range.y, range.x, 180);
                initial = EditorGUILayout.Slider("初始偏转角°", initial, range.x, range.y);
                EditorGUILayout.HelpBox("范围相对自身基准朝向；只有收到光才输出一束光。试玩不保存角度。", MessageType.Info);
            }
            else for (int i = 0; i < 8; i++)
            {
                bool allowed = EditorGUILayout.ToggleLeft("允许 " + i * 45 + "°", (mask & (1 << i)) != 0);
                mask = allowed ? mask | (1 << i) : mask & ~(1 << i);
            }
            if (EditorGUI.EndChangeCheck()) { Edit(); device.Name = name; device.Position = position; device.Direction = direction; device.Allowed = mask; device.AngleRange = range; device.InitialAngle = initial; }
            if (GUILayout.Button("删除机关"))
            {
                Edit(); foreach (var phase in _layout.Phases) phase.Targets = phase.Targets.Where(x => x != device.Id).ToArray();
                _layout.Devices = _layout.Devices.Where((x, i) => i != _selected).ToArray(); _selected = -1;
            }
        }
    }
}
