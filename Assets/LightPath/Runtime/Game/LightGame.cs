using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DelayNoMore.LightPath
{
    /** @brief 统一管理观看、装置、暂停、断光、颜色及局内存档。 */
    public sealed partial class LightGame : MonoBehaviour
    {
        /** @brief 共享玩法参数。 */
        [SerializeField, Tooltip("玩法配置 SO")] private LightGameConfig _config;
        /** @brief 项目唯一动作数据。 */
        [SerializeField, Tooltip("Project-wide Actions")] private InputActionAsset _actions;
        /** @brief 角色刚体。 */
        [SerializeField, Tooltip("2D 角色")] private Rigidbody2D _player;
        /** @brief 场景相机。 */
        [SerializeField, Tooltip("观看和装置共用相机")] private Camera _camera;
        /** @brief 界面引用。 */
        [SerializeField, Tooltip("完整界面")] private LightMenu _menu;
        /** @brief 按顺序排列的房间。 */
        private LightRoom[] _rooms;
        /** @brief 游戏唯一关卡入口，按顺序读取关卡文件。 */
        [SerializeField, InspectorName("关卡文件"), Tooltip("拖入ProjectionLevel关卡资产，按数组顺序游玩")] private ProjectionLevel[] _levels = new ProjectionLevel[0];
        /** @brief 整局共用的维修布局配置；运行时只读，动态状态由维修会话持有。 */
        [SerializeField, InspectorName("维修光路配置"), Tooltip("拖入维修光路编辑器保存的 RepairLayout。整局共享计时、目标阶段与机关朝向；运行必需。")]
        private RepairLayout _repairLayout;
        /** @brief 当前房间编号。 */
        private int _roomIndex;
        /** @brief 当前全局滤镜。 */
        private int _color = -1;
        /** @brief 当前待提交滤镜。 */
        private int _candidate = -1;
        /** @brief 当前模型选择。 */
        private int _selected;
        /** @brief 缓存角色表现。 */
        private Renderer[] _playerRenderers;
        /** @brief 角色序列帧表现；旧主角未安装此组件时保持兼容。 */
        private LightPlayerAnimation _playerAnimation;
        /** @brief 是否已开始。 */
        private bool _started;
        /** @brief 是否用户暂停。 */
        private bool _paused;
        /** @brief 是否装置模式。 */
        private bool _editing;
        /** @brief 是否聚焦维修装置。 */
        private bool _repairFocus;
        /** @brief 是否换色预览。 */
        private bool _preview;
        /** @brief 输入是否已就绪。 */
        private bool _ready;
        /** @brief 跳跃缓存截止时刻。 */
        private float _jumpUntil;
        /** @brief 最后接地时刻。 */
        private float _groundedAt = -10;
        /** @brief 传送冷却结束时刻。 */
        private float _portalUntil;
        /** @brief 投影刷新节流。 */
        private float _projectionAt;
        /** @brief 实际游玩经过时间。 */
        private float _playSeconds;
        /** @brief 本局死亡次数。 */
        private int _deaths;
        /** @brief 已获取滤镜。 */
        private readonly bool[] _unlocked = new bool[4];
        /** @brief 缓存角色碰撞。 */
        private Collider2D _body;
        /** @brief 当前提示。 */
        private string _notice;
        /** @brief 提示截止时间。 */
        private float _noticeUntil;
        /** @brief 排队显示的能力通知，不被普通状态消息覆盖。 */
        private readonly Queue<string> _unlockNotices = new Queue<string>();
        /** @brief 当前能力通知的结束时刻。 */
        private float _unlockUntil;
        /** @brief 当前能力通知正文。 */
        private string _unlockNotice;
        /** @brief 登记本局能力通知。 @param message 文案 */
        private void NotifyUnlock(string message) => _unlockNotices.Enqueue(message);
        /** @brief 提取能力提示并计算淡出。 @return 富文本或空 */
        private string UnlockNotice()
        {
            if (Time.unscaledTime >= _unlockUntil && _unlockNotices.Count > 0)
            {
                _unlockNotice = _unlockNotices.Dequeue();
                _unlockUntil = Time.unscaledTime + _config.UnlockNoticeSeconds + _config.UnlockFadeSeconds;
            }
            if (Time.unscaledTime >= _unlockUntil)
                return "";
            Color color = _config.UnlockNoticeColor;
            color.a *= Mathf.Clamp01((_unlockUntil - Time.unscaledTime) / Mathf.Max(.01f, _config.UnlockFadeSeconds));
            return "<size=115%><color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">" + _unlockNotice + "</color></size>";
        }
        /** @brief 获取当前房间。 */
        public LightRoom Room => _rooms[_roomIndex];
        /** @brief 获取整局维修布局，只供读取；未绑定时为空。 */
        public RepairLayout RepairLayout => _repairLayout;
        /** @brief 获取当前模式。 */
        public bool Editing => _editing;
        /** @brief 获取全局色。 */
        public int SelectedColor => _color;
        /** @brief 获取游玩秒数。 */
        public float PlaySeconds => _playSeconds;
        /** @brief 获取整局已解锁能力数量。 @return 已解锁数 */
        private int UnlockedCount()
        {
            int count = 0;
            for (int i = 0; i < _unlocked.Length; i++)
                if (IsUnlocked(i))
                    count++;
            return count;
        }
        /** @brief 获取滤镜获取状态。 @param index 编号 @return 是否已获得 */
        public bool IsUnlocked(int index) => LightGameConfig.IsManualColor(index) && (_debugColors[index] ?? _unlocked[index]);
        /** @brief 场景装配入口。 @param config 配置 @param actions 输入 @param player 角色 @param camera 相机 @param menu 界面 @param levels 关卡文件 */
        public void Configure(LightGameConfig config, InputActionAsset actions, Rigidbody2D player, Camera camera, LightMenu menu, ProjectionLevel[] levels)
        {
            _config = config;
            _actions = actions;
            _player = player;
            _camera = camera;
            _menu = menu;
            _levels = levels ?? new ProjectionLevel[0];
        }
        /** @brief 验证依赖并初始化。 */
        private void Start()
        {
            if (_levels == null || _levels.Length == 0)
            {
                Debug.LogError(GameText.Get("LightGame.Start.001"), this);
                enabled = false;
                return;
            }
            if (!_actions || !_player || !_camera || !_menu || !_player.GetComponent<Collider2D>())
            {
                Debug.LogError(GameText.Get("LightGame.Start.002"), this);
                enabled = false;
                return;
            }
            foreach (ProjectionLevel level in _levels)
            {
                if (!level || !level.RoomPrefab || !level.Config || !level.RoomPrefab.Surface || !level.RoomPrefab.Definition)
                {
                    Debug.LogError(GameText.Get("LightGame.Start.003"), this);
                    enabled = false;
                    return;
                }
            }
            if (!InitializeRepair())
            {
                enabled = false;
                return;
            }
            CreateLevelRooms();
            BindRepairRooms();
            _config = _levels[0].Config;
            _body = _player.GetComponent<Collider2D>();
            _playerRenderers = _player.GetComponentsInChildren<Renderer>(true);
            _playerAnimation = _player.GetComponentInChildren<LightPlayerAnimation>(true);
            _player.gravityScale = _config.GravityScale;
            if (!InitializeStage())
            {
                enabled = false;
                return;
            }
            // Project-wide Actions 自动启用所有 Map；停用未使用的模板 Player，避免同键触发多套动作。
            _actions.FindActionMap("Player", false)?.Disable();
            _actions.FindActionMap("Light", true).Enable();
            _ready = true;
            _menu.BindConfig(_config, this);
            _menu.BindInputHelp(_actions);
            for (int i = 0; i < _rooms.Length; i++)
                _rooms[i].gameObject.SetActive(i == 0);
            if (_levels != null && _levels.Length > 0)
            {
                _config = _levels[_roomLevels[_roomIndex]].Config;
                _menu.BindConfig(_config, this);
                _player.gravityScale = _config.GravityScale;
            }
            Room.ResetRoom();
            _visited[_roomIndex] = true;
            _player.position = RespawnPosition();
            Room.Surface.SetFocus(_player.position);
            ApplyState();
            _menu.ShowStart();
        }
        /** @brief 读取动作。 @param name 动作名 @return 动作对象 */
        private InputAction Action(string name) => _actions.FindAction("Light/" + name, true);
        /** @brief 检测脚下实体。 @return 是否接地 */
        public bool Grounded()
        {
            Bounds bounds = _body.bounds;
            if (_roomLevels != null && _levels[_roomLevels[_roomIndex]].Edge(_roomIndex - _levelFirst[_roomLevels[_roomIndex]], 2) == ProjectionLevel.EdgeBehavior.Block
                && bounds.min.y <= Room.Surface.Bounds.yMin + .03f)
                return true;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(1 << 9);
            filter.useTriggers = false;
            Collider2D[] results = new Collider2D[2];
            return Physics2D.OverlapBox(new Vector2(bounds.center.x, bounds.min.y - .025f), new Vector2(bounds.size.x * .8f, .1f), 0, filter, results) > 0;
        }
        /** @brief 统一输入、流程和表现更新。 */
        private void Update()
        {
            if (!_ready || !_started || _transitioning)
                return;
            if (Action("Pause").WasPressedThisFrame())
                SetPaused(!_paused);
            if (_paused)
                return;
            if (_stage && _stage.Moving)
            {
                ApplyState();
                return;
            }
            _playSeconds += Time.unscaledDeltaTime;
            Room.Surface.SetFocus(_player.position);
            if (Action("Retry").WasPressedThisFrame())
                Respawn(true, "手动读档");
            if (Action("Mode").WasPressedThisFrame())
                ToggleMode();
            if (Noclip && !_editing)
                MoveNoclip();
            else if (!_editing && Room.Powered)
            {
                ReadPalette();
                if (Action("Jump").WasPressedThisFrame())
                    _jumpUntil = Time.time + _config.JumpBuffer;
                TickRepair(Time.deltaTime);
                if (!Room.Powered)
                {
                    _preview = false;
                    _jumpUntil = 0;
                    Notify(_stage ? GameText.Get("LightGame.Update.004", Label("Mode")) : GameText.Get("LightGame.Update.005", Label("Mode")));
                }
                if (Room.Powered && _player.position.y < Room.transform.position.y + _config.DeathHeight)
                    Respawn(true, "跌落死亡");
                if (Room.Powered && !_editing)
                {
                    if (TryBoundaryTravel())
                        return;
                    CollectCheckpoints();
                    CollectAndTravel();
                    if (_transitioning)
                        return;
                }
            }
            else if (_editing)
                EditDevice();
            ApplyState();
            UpdateCamera();
            string mode = _stage.View == StageView.Overview ? GameText.Get("LightGame.Update.009") : _stage.View == StageView.Projection ? GameText.Get("LightGame.Update.010") : _stage.View == StageView.Repair ? GameText.Get("LightGame.Update.011") : GameText.Get("LightGame.Update.012");
            string status = Room.Definition.Hint;
            if (_stage && _stage.View == StageView.Overview)
                status = _stageHover;
            if (_editing && !_repairFocus && (!_stage || _stage.View == StageView.Projection) && Room.Surface.Sources.Count > 0)
            {
                ProjectionSource source = Room.Surface.Sources[_selected];
                status = GameText.Get("LightGame.Update.016") + source.name + GameText.Get("LightGame.Update.017") + (source.Definition.Movable ? GameText.Get("LightGame.Update.018") : GameText.Get("LightGame.Update.019"));
            }
            if (Time.unscaledTime < _noticeUntil)
                status = _notice;
            string unlock = UnlockNotice();
            if (unlock.Length > 0)
                status = unlock;
            if (Noclip) status = GameText.Get("Debug.NoclipActive") + "\n" + status;
            if (_repair != null)
                status += "\n" + RepairStatus();
            _menu.UpdateGame(mode, $"Level{_roomLevels[_roomIndex] + 1}Room{_roomIndex - _levelFirst[_roomLevels[_roomIndex]]}", status, BuildHelp(), _preview, CanSwitch(), _candidate, _color, UnlockedCount(), _roomLevels[_roomIndex] + 1, _levels.Length, _playSeconds);
        }
        /** @brief 读取轮盘并在松开时提交。 */
        private void ReadPalette()
        {
            bool held = Action("Palette").IsPressed();
            if (held && !_preview)
                _candidate = _color;
            if (held)
            {
                _preview = true;
                int pointed = _menu.PointedColor(Action("Pointer").ReadValue<Vector2>());
                if (pointed >= 0 && IsUnlocked(pointed))
                    _candidate = pointed;
            }
            else if (_preview)
            {
                if (_candidate >= 0 && CanSwitch())
                {
                    RecordTelemetry("选用颜色", "颜色" + _candidate, "轮盘松开提交");
                    _color = _candidate;
                }
                else if (_candidate >= 0) RecordTelemetry("换色被阻止", "颜色" + _candidate, "角色处于禁止换色区域");
                _preview = false;
            }
        }
        /** @brief 生成使用当前输入数据的帮助。 @return 绑定提示 */
        private string BuildHelp() => StageHelp();
        /** @brief 获取动作展示文本。 @param name 动作名 @return 当前绑定 */
        private string Label(string name) => Action(name).GetBindingDisplayString();
        /** @brief 切换模式，返回前检验全部有效实体。 */
        public void ToggleMode()
        {
            if (_ready && _started && !_paused && !_transitioning && !_stage.Moving && _stage.View != StageView.Overview)
                SetStageView(StageView.Overview);
        }
        /** @brief 在已确认的四视角方案中处理装置操作。 */
        private void EditDevice()
        {
            if (!_preview) EditStage();
        }
        /** @brief 键盘操作后尚待刷新的投影。 */
        private bool _keyboardProjectionDirty;
        /** @brief 鼠标仅选择投影模型，位移与旋转统一读取UIS键盘动作。 */
        private void EditProjectionKeyboard()
        {
            var sources = Room.Surface.Sources;
            if (sources == null || sources.Count == 0)
                return;
            float scroll = Action("Scroll").ReadValue<Vector2>().y;
            if (scroll != 0 && !(UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
            {
                _selected = ProjectionSelection.Next(sources, _selected, scroll > 0 ? -1 : 1);
                if (_selected >= 0 && _selected < sources.Count && sources[_selected]) RecordTelemetry("对象选择", TelemetryObject(sources[_selected].transform), "滚轮");
            }
            if (Action("Drag").WasPressedThisFrame() && !(UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
            {
                int picked = _stage.PickProjectionObject(Action("Pointer").ReadValue<Vector2>(), Room, _color);
                if (picked >= 0) { _selected = picked; RecordTelemetry("对象选择", TelemetryObject(sources[picked].transform), "鼠标"); }
            }
            _selected = Mathf.Clamp(_selected, 0, sources.Count - 1);
            ProjectionSource source = sources[_selected];
            if (!source || !source.Definition || !source.Definition.Movable || !source.gameObject.activeInHierarchy)
                return;
            Vector3 movement = new Vector3(
                AbilityInput(ProjectionAbility.MoveX, Action("DeviceHorizontal").ReadValue<float>()),
                AbilityInput(ProjectionAbility.MoveY, Action("DeviceVertical").ReadValue<float>()),
                AbilityInput(ProjectionAbility.MoveZ, Action("DeviceDepth").ReadValue<float>()));
            Vector3 rotation = new Vector3(
                AbilityInput(ProjectionAbility.RotateX, Action("DeviceRotationX").ReadValue<float>()),
                AbilityInput(ProjectionAbility.RotateY, Action("DeviceRotationY").ReadValue<float>()),
                AbilityInput(ProjectionAbility.RotateZ, Action("DeviceRotation").ReadValue<float>()));
            if (movement == Vector3.zero && rotation == Vector3.zero)
            {
                if (_keyboardProjectionDirty)
                    Room.Surface.Rebuild();
                _keyboardProjectionDirty = false;
                return;
            }
            Vector3 target = source.transform.position + movement * (_config.EditSpeed * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(movement.z, 0))
            {
                float a = Room.Surface.ProjectionOrigin.z, b = Room.Surface.transform.position.z;
                target.z = Mathf.Clamp(target.z, Mathf.Min(a, b) + .1f, Mathf.Max(a, b) - .1f);
            }
            Vector3 angles = rotation * (_config.RotationSpeed * Time.unscaledDeltaTime);
            Quaternion change = Quaternion.AngleAxis(angles.z, Vector3.forward) * Quaternion.AngleAxis(angles.y, Vector3.up) * Quaternion.AngleAxis(angles.x, Vector3.right);
            Vector3 previousPosition = source.transform.position;
            Quaternion previousRotation = source.transform.rotation;
            source.SetWorldPose(target, change * previousRotation);
            if (!CorrectProjectionClearance(source))
            {
                source.SetWorldPose(previousPosition, previousRotation);
                Notify(GameText.Get("Projection.ClearanceBlocked"));
            }
            TelemetryObjectMotion(source, movement, rotation, previousPosition, previousRotation);
            Room.Surface.Invalidate(source);
            _keyboardProjectionDirty = true;
            if (Time.unscaledTime >= _projectionAt)
            {
                Room.Surface.Rebuild();
                _projectionAt = Time.unscaledTime + .06f;
                _keyboardProjectionDirty = false;
            }
        }
        /** @brief 由UI按钮打开装置色盘。 */
        public void OpenDevicePalette()
        {
            if (!_started || _paused || !_editing || !Room.Powered || RepairVisible || (_stage && (_stage.Moving || _stage.View != StageView.Projection)))
                return;
            _preview = !_preview;
            _candidate = _color;
        }
        /** @brief 推进物理运动，消费跳跃缓存。 */
        private void FixedUpdate()
        {
            if (Noclip || !_ready || !_started || _paused || _editing || _transitioning || !Room.Powered || (_stage && _stage.Moving))
                return;
            if (Grounded())
                _groundedAt = Time.time;
            Vector2 velocity = _player.linearVelocity;
            velocity.x = Action("Move").ReadValue<float>() * _config.Speed;
            if (_jumpUntil >= Time.time && Time.time - _groundedAt <= _config.CoyoteSeconds)
            {
                velocity.y = _config.JumpSpeed;
                _jumpUntil = 0;
                _groundedAt = -10;
            }
            _player.linearVelocity = velocity;
        }
        /** @brief 检查整个轮盘是否允许换色。 @return 是否安全 */
        public bool CanSwitch() => _ready && !Room.Overlaps(_body.bounds, _color, true);
        /** @brief UI 选择候选色，松开统一提交。 @param index 色号 */
        public void SelectColor(int index)
        {
            if (_started && !_paused && _preview && IsUnlocked(index) && !CanSwitch())
                RecordTelemetry("换色被阻止", "颜色" + index, "角色处于禁止换色区域");
            if (_started && !_paused && _preview && IsUnlocked(index) && CanSwitch())
            {
                _candidate = index;
                if (_editing)
                {
                    RecordTelemetry("选用颜色", "颜色" + index, "投影调整轮盘提交");
                    _color = index;
                    _preview = false;
                }
            }
        }
        /** @brief 收集、传送和出口流程。 */
        private void CollectAndTravel()
        {
            CollectProjectionAbilities();
            foreach (ColorAbilityKey key in Room.GetComponentsInChildren<ColorAbilityKey>(true))
            {
                ProjectionSource projectedKey = key.GetComponent<ProjectionSource>();
                bool touchingKey = projectedKey ? Room.Surface.FindPortal(_body.bounds, _color) == projectedKey
                    : Vector2.Distance(_player.position, key.transform.position) < _config.CollectRadius;
                if (!_unlocked[key.Color] && key.gameObject.activeInHierarchy && touchingKey)
                {
                    RecordTelemetry("能力解锁", "颜色" + key.Color, TelemetryObject(key.transform));
                    _unlocked[key.Color] = true;
                    key.gameObject.SetActive(false);
                    ProjectionSource keySource = key.GetComponent<ProjectionSource>();
                    if (keySource)
                    {
                        Room.Surface.Invalidate(keySource);
                        Room.Surface.Rebuild();
                    }
                    NotifyUnlock(GameText.Get("LightGame.CollectAndTravel.029"));
                }
                key.gameObject.SetActive(!_unlocked[key.Color]);
            }
            if (!Action("Interact").WasPressedThisFrame())
                return;
            if (Time.time >= _portalUntil)
                foreach (RoomPortalLink gate in Room.GetComponentsInChildren<RoomPortalLink>(true))
                {
                    if (gate.GetComponent<ProjectionSource>() || !PortalAvailable(Room, gate.gameObject)) continue;
                    var trigger = gate.GetComponent<Collider2D>();
                    if (trigger && trigger.enabled && trigger.Distance(_body).isOverlapped && TryPortalObjectTravel(gate.gameObject)) return;
                }
            ProjectionSource portal = Room.Surface.FindPortal(_body.bounds, _color);
            if (portal && !portal.GetComponent<LevelExit>() && !portal.GetComponent<ColorAbilityKey>() && !portal.GetComponent<ProjectionAbilityKey>() && Time.time >= _portalUntil && TryPortalTravel(portal))
                return;
            LevelExit[] exits = Room.GetComponentsInChildren<LevelExit>(true);
            bool canExit = false;
            foreach (LevelExit exit in exits)
                canExit |= exit.Available(Room.Surface, _body.bounds, _color, _config.ExitRadius);
            if (exits.Length > 0 && !canExit)
                return;
            if (exits.Length == 0 && Vector2.Distance(_player.position, Room.Exit) > _config.ExitRadius)
                return;
            RecordTelemetry("关卡完成", "Level" + (_roomLevels[_roomIndex] + 1), "使用关卡出口");
            BreakTelemetryVisit("关卡出口", true);
            int nextLevel = _roomLevels[_roomIndex] + 1;
            if (nextLevel == _levels.Length)
            {
                RecordTelemetry("游戏通关");
                _started = false;
                _preview = false;
                _menu.ShowEnd(_playSeconds, _deaths, UnlockedCount());
                ApplyState();
            }
            else
            {
                ClearCheckpoint();
                Room.gameObject.SetActive(false);
                _roomIndex = _levelFirst[nextLevel];
                Room.gameObject.SetActive(true);
                Respawn(false, "下一关");
                Notify(GameText.Get("LightGame.CollectAndTravel.035"));
            }
        }
        /** @brief 从当前存档点恢复并保留已获取内容。 @param countDeath 是否计入死亡 @param reason 可识别的恢复原因，供本次记录使用 */
        public void Respawn(bool countDeath = true, string reason = "重生")
        {
            if (!_ready)
                return;
            int previousRoom = _roomIndex;
            RecordTelemetry(reason == "手动读档" ? "手动读档" : reason.Contains("死亡") ? "死亡" : countDeath ? "重生" : "出生定位", TelemetryObject(_checkpoint ? _checkpoint.transform : null), reason);
            if (countDeath && HasValidCheckpoint()) RecordTelemetry("从存档恢复", TelemetryObject(_checkpoint.transform), reason);
            BreakTelemetryVisit(reason);
            if (countDeath)
                _deaths++;
            RestoreRespawnRoom();
            _candidate = _color;
            _selected = 0;
            _preview = false;
            _editing = false;
            _repairFocus = false;
            _jumpUntil = 0;
            _groundedAt = -10;
            _portalUntil = 0;
            if (_levels != null && _levels.Length > 0)
            {
                _config = _levels[_roomLevels[_roomIndex]].Config;
                _menu.BindConfig(_config, this);
                _player.gravityScale = _config.GravityScale;
            }
            Room.ResetRoom();
            _visited[_roomIndex] = true;
            _player.position = RespawnPosition();
            Room.Surface.SetFocus(_player.position);
            _player.linearVelocity = Vector2.zero;
            if (_playerAnimation) _playerAnimation.ResetPose();
            UpdateCamera();
            if (_repair != null && !_repair.Powered)
            {
                _editing = true;
                _repairFocus = true;
            }
            if (_stage && _stage.Ready)
                SetStageView(Room.Powered ? StageView.World : StageView.Overview, true);
            Physics2D.SyncTransforms();
            ApplyState();
            if (PlaytestRecording.Enabled && previousRoom == _roomIndex)
            {
                ObserveTelemetry();
                PlaytestRecording.Recorder.SplitVisit(Time.realtimeSinceStartupAsDouble, reason);
            }
            ObserveTelemetry();
        }
        /** @brief 开始全新游玩。 */
        public void Restart()
        {
            RecordTelemetry("重新开始");
            BreakTelemetryVisit("重新开始");
            ResetNoclip();
            _unlockNotices.Clear();
            _unlockUntil = 0;
            if (!_ready)
                return;
            if (!InitializeRepair())
            {
                BindRepairRooms();
                _started = false;
                ApplyState();
                return;
            }
            BindRepairRooms();
            ClearCheckpoint();
            _color = -1;
            _candidate = -1;
            _projectionAbilities.Reset();
            DebugRestoreAbilities(false);
            foreach (LightRoom room in _rooms)
                foreach (ProjectionAbilityKey key in room.GetComponentsInChildren<ProjectionAbilityKey>(true))
                    key.SetCollected(false);
            for (int i = 0; i < 4; i++)
                _unlocked[i] = false;
            foreach (LightRoom room in _rooms)
                room.gameObject.SetActive(false);
            System.Array.Clear(_visited, 0, _visited.Length);
            _roomIndex = 0;
            Room.gameObject.SetActive(true);
            _started = true;
            _paused = false;
            _playSeconds = 0;
            _deaths = 0;
            Respawn(false, "重新开始");
            // 初始化断光只冻结世界，不强制将玩家带入维修或3D总览。
            if (_stage && _stage.Ready) SetStageView(StageView.World, true);
            else { _editing = false; _repairFocus = false; ApplyState(); }
            _menu.ShowGame();
        }
        /** @brief 仅重试当前存档。 */
        public void RetryCheckpoint()
        {
            _paused = false;
            Respawn(true, "手动读档");
            _menu.ShowGame();
        }
        /** @brief 设置用户暂停。 @param paused 是否暂停 */
        public void SetPaused(bool paused)
        {
            if (!_started)
                return;
            ObserveTelemetry();
            _paused = paused;
            _preview = false;
            _jumpUntil = 0;
            _menu.ShowPause(paused);
            ApplyState();
            ObserveTelemetry();
        }
        /** @brief 返回主菜单。 */
        public void MainMenu()
        {
            RecordTelemetry("返回主菜单");
            BreakTelemetryVisit("返回主菜单");
            ResetNoclip();
            _started = false;
            _paused = false;
            _preview = false;
            _menu.ShowStart();
            ApplyState();
        }
        /** @brief 统一派生时间与表现。 */
        private void ApplyState()
        {
            if (!_ready)
                return;
            Time.timeScale = !_started || _paused || _editing || _transitioning || !Room.Powered || (_stage && _stage.Moving) ? 0 : _preview ? _config.SlowMotion : 1;
            RefreshProjectionKeys(Room);
            Room.Refresh(_config, _color, _preview, false, _selected);
            foreach (Renderer renderer in _playerRenderers)
                if (renderer)
                    renderer.enabled = Room.Powered || Noclip;
        }
        /** @brief 更新观看与装置相机。 */
        private void UpdateCamera()
        {
            _camera.orthographic = true;
            _camera.orthographicSize = _config.CameraHalfHeight;
            _camera.transform.SetPositionAndRotation(Room.transform.position + new Vector3(0, 3.7f, -22), Quaternion.identity);
        }
        /** @brief 显示短时反馈。 @param message 内容 @param seconds 持续秒数 */
        private void Notify(string message, float seconds = 3)
        {
            _notice = message;
            _noticeUntil = Time.unscaledTime + seconds;
        }
        /** @brief 禁用会话时释放记录绑定与调试状态。 */
        private void OnDisable()
        {
            DetachTelemetry();
            ResetNoclip();
        }
        /** @brief 清理输入与时间。 */
        private void OnDestroy()
        {
            ResetNoclip();
            if (_stage && _ownsStage)
                _stage.Release();
            if (_actions)
                _actions.FindActionMap("Light", false)?.Disable();
            Time.timeScale = 1;
        }
        /** @brief 失去窗口焦点时释放维修调角，防止返回后继续拖动。 @param focused 是否有焦点 */
        private void OnApplicationFocus(bool focused)
        {
            RecordTelemetry(focused ? "窗口获得焦点" : "窗口失去焦点");
            if (!focused) PlaytestRecording.Recorder?.Flush(Time.realtimeSinceStartupAsDouble);
            if (!focused) _repairDrag = -1;
        }
    }
}
