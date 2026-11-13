using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 将整局维修、UIS 操作和运行时面板接入游戏流程。 */
    public sealed partial class LightGame
    {
        /** @brief 所有房间共享的维修状态。 */
        private RepairSession _repair;
        /** @brief 左键按住的连续透镜，松开或离开维修时释放。 */
        private int _repairDrag = -1;
        /** @brief 仅包含运行时 UI 表现的兼容维修视图。 */
        private readonly RepairPanel _repairPanel = new RepairPanel();
        /** @brief 是否正在显示整局维修视图。 */
        private bool RepairVisible => _ready && _started && !_paused && !_transitioning && _editing && _repairFocus && _repair != null;
        /** @brief 验证配置并建立全新整局维修状态；未绑定时保留旧玩法。@return 是否初始化成功 */
        private bool InitializeRepair()
        {
            _repair = null;
            _repairDrag = -1;
            if (!_repairLayout)
                return true;
            if (!_repairLayout.TryValidate(out string error))
            {
                Debug.LogError(GameText.Get("LightGameRepair.InitializeRepair.001") + error, this);
                return false;
            }
            _repair = new RepairSession(_repairLayout);
            return true;
        }
        /** @brief 将同一会话提供给所有房间，房间不再单独推进旧维修计时。 */
        private void BindRepairRooms()
        {
            foreach (LightRoom room in _rooms)
                room.BindRepair(_repair);
        }
        /** @brief 在正常观看状态推进事件计时。@param seconds 当前观看时间，秒 */
        private void TickRepair(float seconds)
        {
            if (_repair != null)
                _repair.Tick(seconds);
            else
                Room.Tick(seconds);
        }
        /** @brief 从统一动作数据读取点击，并仅修改本局机关朝向。 */
        private void EditRepair()
        {
            Vector2 pointer = Action("Pointer").ReadValue<Vector2>();
            OperateRepair(_repairPanel.Pick(_repairLayout, pointer, _repair), _repairPanel.BoardPoint(_repairLayout, pointer), true);
        }
        /** @brief 共用维修输入：折镜点击八向切换，透镜按住并瞄准连续角度。 */
        private void OperateRepair(int index, Vector2 point, bool valid)
        {
            bool wasPowered = _repair.Powered;
            if (Action("Drag").WasPressedThisFrame())
            {
                _repairDrag = index >= 0 && _repairLayout.Devices[index].Type == RepairLayout.Kind.Lens ? index : -1;
                if (_repairDrag < 0) _repair.Rotate(index);
                if (!wasPowered && _repair.Powered) Notify(GameText.Get("LightGameRepair.EditRepair.002"));
                return;
            }
            if (!Action("Drag").IsPressed()) _repairDrag = -1;
            if (_repairDrag >= 0 && Action("Drag").IsPressed())
            {
                float delta = Action("PointerDelta").ReadValue<Vector2>().x;
                _repair.SetLensAngle(_repairDrag, _repair.Angle(_repairDrag) - delta * _repairLayout.LensDegreesPerPixel);
            }
            if (!wasPowered && _repair.Powered)
                Notify(GameText.Get("LightGameRepair.EditRepair.002"));
        }
        /** @brief 生成维修状态与下一阶段预警。@return 状态说明 */
        private string RepairStatus()
        {
            if (_repair.DebugPowerForced)
                return GameText.Get(_repair.Powered ? "Debug.PowerForcedOn" : "Debug.PowerForcedOff");
            if (!_repair.Powered)
                return GameText.Get("LightGameRepair.RepairStatus.003") + _repairLayout.Phases[_repair.PhaseIndex].DisplayName + GameText.Get("LightGameRepair.RepairStatus.004");
            string next = _repairLayout.Phases[_repair.NextPhaseIndex].DisplayName;
            string targets = "";
            foreach (RepairLayout.Device device in _repairLayout.Devices)
                if (_repair.IsTarget(device.Id, true))
                    targets += (targets.Length == 0 ? "" : "、") + device.DisplayName;
            return (_repair.Warning ? GameText.Get("LightGameRepair.RepairStatus.005") : GameText.Get("LightGameRepair.RepairStatus.006")) + GameText.Get("LightGameRepair.RepairStatus.007", _repair.RemainingSeconds, next, targets);
        }
        /** @brief 刷新兼容维修 UI；输入仍由统一 UIS 处理。 */
        private void UpdateRepairPanel()
        {
            if (!RepairVisible || !Action("Drag").IsPressed()) _repairDrag = -1;
            _repairPanel.SetVisible(!_stage && RepairVisible);
            if (!_stage && RepairVisible)
                _repairPanel.Draw(_repairLayout, _repair, GameText.Get("LightGameRepair.OnGUI.008", Label("Drag"), Label("Focus"), Label("Mode"), Label("Pause")));
        }
    }
}
