using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 向策划调试面板提供运行快照和会话内覆盖，不修改共享配置。 */
    public sealed partial class LightGame
    {
        /** @brief 会话内能力覆盖，空值表示使用真实收集进度。 */
        private readonly bool?[] _debugAxes = new bool?[6], _debugColors = new bool?[4];
        /** @brief 调试浮空状态。 */
        public bool Noclip { get; private set; }
        /** @brief 浮空前的物理模拟状态。 */
        private bool _noclipWasSimulated;
        /** @brief 浮空前的角色形状及相对位置。 */
        private Bounds _noclipShape;
        /** @brief 尝试开启或关闭浮空，关闭前检查实体重叠。 @param enabled 是否开启 */
        public void SetNoclip(bool enabled)
        {
            if (!_ready || enabled == Noclip) return;
            if (!enabled)
            {
                Bounds body = _noclipShape;
                body.center += (Vector3)_player.position;
                Room.Surface.Rebuild();
                if (!Room.Surface.Bounds.Contains(body.min) || !Room.Surface.Bounds.Contains(body.max) || Room.Overlaps(body, _color, false))
                {
                    Notify(GameText.Get("Debug.NoclipBlocked"), success: false);
                    return;
                }
            }
            if (enabled)
            {
                _noclipShape = _body.bounds;
                _noclipShape.center -= (Vector3)_player.position;
                _noclipWasSimulated = _player.simulated;
            }
            RecordTelemetry("Debug浮空开关", "Noclip", enabled ? "开启" : "关闭");
            Noclip = enabled;
            _player.simulated = enabled ? false : _noclipWasSimulated;
            _player.linearVelocity = Vector2.zero;
            _jumpUntil = 0;
            _checkpointContacts.Clear();
            Physics2D.SyncTransforms();
            if (enabled && _started)
            {
                if (_stage && _stage.Ready) SetStageView(StageView.World, true);
                else { _editing = false; _repairFocus = false; }
                SetPaused(false);
            }
        }
        /** @brief 瞬移同步物理位置与显示位置，关闭模拟时也立即生效。@param position 世界二维落点 */
        private void SetPlayerPosition(Vector2 position)
        {
            _player.position = position;
            _player.transform.position = new Vector3(position.x, position.y, _player.transform.position.z);
        }
        /** @brief 正常游戏重开或退出时清理调试浮空。 */
        private void ResetNoclip()
        {
            if (!Noclip) return;
            Noclip = false;
            if (!_player) return;
            _player.simulated = _noclipWasSimulated;
            _player.linearVelocity = Vector2.zero;
        }
        /** @brief 在二维平面按统一UIS动作浮空；鼠标停在调试控件上不吞掉键盘输入。 */
        private void MoveNoclip()
        {
            Vector2 movement = Action("DebugMove").ReadValue<Vector2>();
            float speed = _config.NoclipSpeed * (Action("DebugBoost").IsPressed() ? _config.NoclipBoost : 1);
            Vector2 position = (Vector2)_player.transform.position + Vector2.ClampMagnitude(movement, 1) * (speed * Time.unscaledDeltaTime);
            _player.position = position;
            _player.transform.position = new Vector3(position.x, position.y, _player.transform.position.z);
            Physics2D.SyncTransforms();
        }
        /** @brief 当前有效断光状态。 */
        public bool DebugBlackout => _repair != null && !_repair.Powered;
        /** @brief 是否正在强制覆盖供光。 */
        public bool DebugPowerForced => _repair != null && _repair.DebugPowerForced;
        /** @brief 强制进入或退出断光，保持当前视角。@param blackout 是否断光 */
        public void DebugSetBlackout(bool blackout)
        {
            if (!_ready || _repair == null) return;
            RecordTelemetry("Debug强制断光", "断光", blackout ? "开启" : "关闭");
            _repair.SetDebugPower(!blackout);
            ApplyState();
        }
        /** @brief 清除调试供光覆盖，恢复当前真实光路状态。 */
        public void DebugRestorePower()
        {
            if (_repair == null) return;
            RecordTelemetry("Debug恢复供光规则");
            _repair.SetDebugPower(null);
            if (_ready) ApplyState();
        }
        /** @brief 获取可选关卡数量。 */
        public int DebugLevelCount => _levels.Length;
        /** @brief 当前关卡索引。 */
        public int DebugCurrentLevel => _ready ? _roomLevels[_roomIndex] : 0;
        /** @brief 当前关卡内房间索引。 */
        public int DebugCurrentRoom => _ready ? _roomIndex - _levelFirst[_roomLevels[_roomIndex]] : 0;
        /** @brief 获取关卡房间数量。 @param level 关卡索引 @return 房间数 */
        public int DebugRoomCount(int level) => level >= 0 && level < _levels.Length ? _levels[level].RoomCount : 0;
        /** @brief 设置调试能力覆盖。 @param index 六轴后接四色 @param value 是否启用 */
        public void DebugSetAbility(int index, bool value)
        {
            RecordTelemetry("Debug能力覆盖", index < 6 ? ((ProjectionAbility)index).ToString() : "颜色" + (index - 6), value ? "开启" : "关闭");
            if (index >= 0 && index < 6) _debugAxes[index] = value;
            else if (index >= 6 && index < 10) _debugColors[index - 6] = value;
            if (_color >= 0 && !IsUnlocked(_color)) { _color = -1; _candidate = -1; _preview = false; }
        }
        /** @brief 查询调试面板显示的有效能力。 @param index 六轴后接四色 @return 是否有效 */
        public bool DebugAbility(int index) => index < 6 ? HasProjectionAbility((ProjectionAbility)index) : IsUnlocked(index - 6);
        /** @brief 清除临时覆盖，恢复本局真实收集进度。 */
        public void DebugRestoreAbilities(bool record = true)
        {
            if (record) RecordTelemetry("Debug恢复能力进度");
            System.Array.Clear(_debugAxes, 0, _debugAxes.Length);
            System.Array.Clear(_debugColors, 0, _debugColors.Length);
            if (_color >= 0 && !IsUnlocked(_color)) { _color = -1; _candidate = -1; }
        }
        /** @brief 全部开启或关闭调试能力。 @param value 是否启用 */
        public void DebugAllAbilities(bool value)
        {
            for (int i = 0; i < 10; i++) DebugSetAbility(i, value);
        }
        /** @brief 调试跳转到指定关卡与房间，保留已获得的玩家能力。 @param level 关卡索引 @param room 房间索引 */
        public void DebugJump(int level, int room)
        {
            if (!_ready || !_started || _transitioning || level < 0 || level >= _levels.Length || room < 0 || room >= DebugRoomCount(level))
                return;
            RecordTelemetry("Debug跳房间", $"Level{level + 1}Room{room}");
            BreakTelemetryVisit("Debug跳房间");
            if (_roomLevels[_roomIndex] != level) ClearCheckpoint();
            Room.gameObject.SetActive(false);
            _roomIndex = _levelFirst[level] + room;
            Room.gameObject.SetActive(true);
            _config = _levels[level].Config;
            _menu.BindConfig(_config, this);
            _player.gravityScale = _config.GravityScale;
            SetPlayerPosition(Room.Spawn);
            _stage.ConfigureProjection(Room.Surface);
            Room.Surface.SetFocus(_player.position);
            if (!_visited[_roomIndex]) Room.ResetRoom();
            _visited[_roomIndex] = true;
            _selected = 0;
            _repairDrag = -1;
            _preview = false;
            _checkpointContacts.Clear();
            _player.linearVelocity = Vector2.zero;
            if (_playerAnimation) _playerAnimation.ResetPose();
            _jumpUntil = 0;
            _groundedAt = -10;
            _roomTravelUntil = Time.unscaledTime + .3f;
            UpdateCamera();
            Room.Surface.SetFocus(_player.position);
            Room.Surface.Invalidate();
            Room.Surface.Rebuild();
            Physics2D.SyncTransforms();
            Bounds playerBounds = Noclip ? _noclipShape : _body.bounds;
            if (Noclip) playerBounds.center += (Vector3)_player.position;
            if (!Room.Powered || !Room.HasSpawn || !Room.Surface.Bounds.Contains(playerBounds.min) || !Room.Surface.Bounds.Contains(playerBounds.max) || Room.Overlaps(playerBounds, _color, false))
            {
                SetNoclip(true);
                if (!Room.Surface.Bounds.Contains(_player.position)) SetPlayerPosition(Room.Surface.Bounds.center);
            }
            Room.Surface.SetFocus(_player.position);
            Room.Surface.Rebuild();
            Physics2D.SyncTransforms();
            if (_stage) SetStageView(StageView.World, true);
            else { _editing = false; _repairFocus = false; }
            ApplyState();
        }
        /** @brief 生成集中诊断文字；所有标签和格式模板来自文案SO。@return 当前快照 */
        public string DebugSummary()
        {
            if (!_ready) return GameText.Get("Debug.NotReady");
            string none = GameText.Get("Debug.None");
            string viewKey = !_stage || _stage.View == StageView.World ? "LightGame.Update.012" : _stage.View == StageView.Overview ? "LightGame.Update.009" : _stage.View == StageView.Repair ? "LightGame.Update.011" : "LightGame.Update.010";
            string view = _stage ? GameText.Get(viewKey) : none;
            string state = GameText.Get(!_started ? "Debug.Menu" : _paused ? "Debug.Paused" : !Room.Powered ? "Debug.Blackout" : _editing ? "Debug.Editing" : "Debug.Running");
            string[] filterKeys = { "LightMenu._names.001", "LightMenu._names.002", "LightMenu._names.003", "LightMenu._names.004" };
            string filter = _color < 0 ? none : GameText.Get(filterKeys[_color]);
            string axes = "";
            for (int i = 0; i < 6; i++) if (HasProjectionAbility((ProjectionAbility)i)) axes += (axes.Length == 0 ? "" : " / ") + ProjectionAbilityState.Label((ProjectionAbility)i);
            string selected = _stage && _stage.View == StageView.Projection && _selected >= 0 && _selected < Room.Surface.Sources.Count && Room.Surface.Sources[_selected] ? Room.Surface.Sources[_selected].name : none;
            string snapshot = GameText.Get("Debug.Snapshot", _roomIndex + 1, _rooms.Length, Room.name.Replace("(Clone)", ""), view, state,
                _player.position.x, _player.position.y, _player.linearVelocity.x, _player.linearVelocity.y,
                filter, UnlockedCount(), axes.Length == 0 ? none : axes, _checkpoint ? _checkpoint.name : none,
                Room.Surface.Sources.Count, Room.Surface.Regions.Count, Room.Surface.LoadedChunks,
                Room.Surface.LastSampleCount, Room.Surface.LastBuildMilliseconds, selected,
                CanSwitch() ? GameText.Get("Debug.Safe") : GameText.Get("Debug.Blocked"));
            if (_repair == null) return snapshot + "\n" + GameText.Get("Debug.NoRepair");
            int lit = 0;
            string targets = "";
            foreach (RepairLayout.Device device in _repairLayout.Devices)
                if (_repair.IsTarget(device.Id))
                {
                    targets += (targets.Length == 0 ? "" : " / ") + device.DisplayName;
                    if (_repair.Result.Lit.Contains(device.Id)) lit++;
                }
            return snapshot + "\n" + GameText.Get("Debug.Repair", _repairLayout.Phases[_repair.PhaseIndex].DisplayName, _repair.RemainingSeconds,
                _repair.RepairCount, lit, _repairLayout.Phases[_repair.PhaseIndex].Targets.Length, targets);
        }
    }
}
