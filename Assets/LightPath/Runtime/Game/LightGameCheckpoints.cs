using System.Collections.Generic;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 保存当前关卡唯一激活的存档点，并协调跨房间重生。 */
    public sealed partial class LightGame
    {
        /** @brief 当前关卡最新激活的存档点，离开所属房间后继续有效。 */
        private RoomCheckpoint _checkpoint;
        /** @brief 存档点所属的运行房间编号。 */
        private int _checkpointRoom = -1;
        /** @brief 已接触的标记，避免持续重叠时反复激活多个存档点。 */
        private readonly HashSet<RoomCheckpoint> _checkpointContacts = new HashSet<RoomCheckpoint>();
        /** @brief 获取当前关卡唯一激活的存档点。 */
        public RoomCheckpoint ActiveCheckpoint => _checkpoint;
        /** @brief 检查当前房间的存档点，只在供光和正常游玩期间调用。 */
        private void CollectCheckpoints()
        {
            foreach (RoomCheckpoint checkpoint in Room.GetComponentsInChildren<RoomCheckpoint>())
                if (checkpoint.Touches(_body.bounds))
                {
                    if (_checkpointContacts.Add(checkpoint) && _checkpoint != checkpoint)
                    {
                        if (_checkpoint) _checkpoint.SetActivated(false);
                        RecordTelemetry("激活存档点", TelemetryObject(checkpoint.transform));
                        _checkpoint = checkpoint;
                        _checkpointRoom = _roomIndex;
                        checkpoint.SetActivated(true);
                        Notify(GameText.Get("LightGameCheckpoints.CollectCheckpoints.001"), success: true);
                    }
                }
                else
                    _checkpointContacts.Remove(checkpoint);
        }

        /** @brief 清除当前存档状态；用于重新开始和进入下一关。 */
        private void ClearCheckpoint()
        {
            if (_checkpoint) _checkpoint.SetActivated(false);
            _checkpoint = null;
            _checkpointRoom = -1;
            _checkpointContacts.Clear();
        }

        /** @brief 判断存档是否属于当前关卡，忽略换房导致的房间根节点停用。@return 是否可跨房间恢复 */
        private bool HasValidCheckpoint() => _checkpoint && _checkpointRoom >= 0 && _checkpointRoom < _rooms.Length
            && _roomLevels[_checkpointRoom] == _roomLevels[_roomIndex]
            && _checkpoint.AvailableForRespawn(_rooms[_checkpointRoom]);

        /** @brief 在死亡或重试时切回存档所属房间，无存档时回本关主房间。 */
        private void RestoreRespawnRoom()
        {
            int target = _levelFirst[_roomLevels[_roomIndex]];
            if (HasValidCheckpoint())
                target = _checkpointRoom;
            else
                ClearCheckpoint();
            if (_transitioning) StopAllCoroutines();
            _transitioning = false;
            _roomIndex = target;
            for (int i = 0; i < _rooms.Length; i++)
                _rooms[i].gameObject.SetActive(i == target);
            _checkpointContacts.Clear();
            _roomTravelUntil = Time.unscaledTime + .3f;
        }

        /** @brief 获取当前激活存档的安全落点，缺失或被作者停用时使用房间出生点。@return 世界复活位置 */
        private Vector2 RespawnPosition() => HasValidCheckpoint() ? _checkpoint.Spawn : Room.Spawn;
    }
}
