using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 向策划调试面板提供只读运行快照，不改变玩法状态或共享配置。 */
    public sealed partial class LightGame
    {
        /** @brief 会话内能力覆盖，空值表示使用真实收集进度。 */
        private readonly bool?[] _debugAxes = new bool?[6], _debugColors = new bool?[4];
        /** @brief 获取可选关卡数量。 */
        public int DebugLevelCount => _levels.Length;
        /** @brief 获取关卡房间数量。 @param level 关卡索引 @return 房间数 */
        public int DebugRoomCount(int level) => level >= 0 && level < _levels.Length ? _levels[level].RoomCount : 0;
        /** @brief 设置调试能力覆盖。 @param index 六轴后接四色 @param value 是否启用 */
        public void DebugSetAbility(int index, bool value)
        {
            if (index >= 0 && index < 6) _debugAxes[index] = value;
            else if (index >= 6 && index < 10) _debugColors[index - 6] = value;
            if (_color >= 0 && !IsUnlocked(_color)) { _color = -1; _candidate = -1; _preview = false; }
        }
        /** @brief 查询调试面板显示的有效能力。 @param index 六轴后接四色 @return 是否有效 */
        public bool DebugAbility(int index) => index < 6 ? HasProjectionAbility((ProjectionAbility)index) : IsUnlocked(index - 6);
        /** @brief 清除临时覆盖，恢复本局真实收集进度。 */
        public void DebugRestoreAbilities()
        {
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
            if (_roomLevels[_roomIndex] != level) ClearCheckpoint();
            Room.gameObject.SetActive(false);
            _roomIndex = _levelFirst[level] + room;
            Room.gameObject.SetActive(true);
            _config = _levels[level].Config;
            _menu.BindConfig(_config, this);
            if (!_visited[_roomIndex]) Room.ResetRoom();
            _visited[_roomIndex] = true;
            _selected = 0;
            _pointer.Cancel();
            _repairDrag = -1;
            _preview = false;
            _checkpointContacts.Clear();
            _player.position = Room.Spawn;
            _player.linearVelocity = Vector2.zero;
            _roomTravelUntil = Time.unscaledTime + .3f;
            UpdateCamera();
            if (_stage) _stage.ConfigureProjection(Room.Surface);
            Room.Surface.SetFocus(_player.position);
            Room.Surface.Invalidate();
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
            for (int i = 0; i < 6; i++) if (_projectionAbilities.Has((ProjectionAbility)i)) axes += (axes.Length == 0 ? "" : " / ") + ProjectionAbilityState.Label((ProjectionAbility)i);
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
