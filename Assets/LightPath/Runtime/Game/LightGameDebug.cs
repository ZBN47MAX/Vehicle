using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 向策划调试面板提供只读运行快照，不改变玩法状态或共享配置。 */
    public sealed partial class LightGame
    {
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
