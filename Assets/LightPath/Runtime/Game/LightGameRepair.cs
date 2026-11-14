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
        /** @brief 当前指针绕节点的拖动状态。 */
        private RepairLensDrag _lensDrag;
        /** @brief 是否正在显示整局维修视图。 */
        private bool RepairVisible => _ready && _started && !_paused && !_transitioning && _editing && _repairFocus && _repair != null;
        /** @brief 验证配置并建立全新整局维修状态；未绑定时停止初始化。@return 是否初始化成功 */
        private bool InitializeRepair()
        {
            _repair = null;
            _repairDrag = -1;
            if (!_repairLayout)
            {
                Debug.LogError(GameText.Get("LightGameStage.InitializeStage.002"), this);
                return false;
            }
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
        }
        /** @brief 共用维修输入：折镜点击八向切换，透镜按住并瞄准连续角度。 */
        private void OperateRepair(int index, Vector2 point, bool valid)
        {
            bool wasPowered = _repair.Powered;
            if (Action("Drag").WasPressedThisFrame())
            {
                if (index >= 0) RecordTelemetry("维修节点操作", _repairLayout.Devices[index].Id, _repairLayout.Devices[index].Type.ToString());
                _repairDrag = index >= 0 && _repairLayout.Devices[index].Type == RepairLayout.Kind.Lens ? index : -1;
                if (_repairDrag < 0) _repair.Rotate(index);
                else _lensDrag.Begin(point, valid);
                if (!wasPowered && _repair.Powered) Notify(GameText.Get("LightGameRepair.EditRepair.002"), success: true);
                return;
            }
            if (!Action("Drag").IsPressed()) _repairDrag = -1;
            if (_repairDrag >= 0 && Action("Drag").IsPressed())
            {
                Vector2 center = _repairLayout.Devices[_repairDrag].Position;
                float angle = _lensDrag.Step(center, point, valid, _repair.Angle(_repairDrag), _repairLayout.NodeRadius * .25f, _repairLayout.LensDragSensitivity);
                float previousAngle = _repair.Angle(_repairDrag);
                _repair.SetLensAngle(_repairDrag, angle);
                TelemetryOperation(_repairLayout.Devices[_repairDrag].Id, "透镜调角", Mathf.Abs(Mathf.DeltaAngle(previousAngle, _repair.Angle(_repairDrag))));
            }
            if (!wasPowered && _repair.Powered)
                Notify(GameText.Get("LightGameRepair.EditRepair.002"), success: true);
        }
        /** @brief 生成维修状态与下一阶段预警。@return 状态说明 */
        private string RepairStatus()
        {
            if (_repair.DebugPowerForced)
                return GameText.Feedback(GameText.Get(_repair.Powered ? "Debug.PowerForcedOn" : "Debug.PowerForcedOff"), _repair.Powered);
            if (!_repair.Powered)
                return GameText.Feedback(GameText.Get("LightGameRepair.RepairStatus.003") + _repairLayout.Phases[_repair.PhaseIndex].DisplayName + GameText.Get("LightGameRepair.RepairStatus.004"), false);
            string next = _repairLayout.Phases[_repair.NextPhaseIndex].DisplayName;
            string targets = "";
            foreach (RepairLayout.Device device in _repairLayout.Devices)
                if (_repair.IsTarget(device.Id, true))
                    targets += (targets.Length == 0 ? "" : "、") + device.DisplayName;
            return GameText.Feedback((_repair.Warning ? GameText.Get("LightGameRepair.RepairStatus.005") : GameText.Get("LightGameRepair.RepairStatus.006")) + GameText.Get("LightGameRepair.RepairStatus.007", _repair.RemainingSeconds, next, targets), !_repair.Warning);
        }
        /** @brief 松开或离开维修状态时释放调角。 */
        private void ReleaseRepairDrag()
        {
            if (!RepairVisible || !Action("Drag").IsPressed()) _repairDrag = -1;
        }
    }
}
