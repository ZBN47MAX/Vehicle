using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 读取正式会话数据并提交记录，不负责修改玩法或保存配置。 */
    public sealed partial class LightGame
    {
        /** @brief 已注册房间的记录实例。 */
        private PlaytestRecorder _telemetryBound;
        /** @brief 房间与颜色名称只在开启时缓存，不在每帧拼接。 */
        private string[] _telemetryRoomNames, _telemetryColors;
        /** @brief 本帧存在有效物件或维修操作。 */
        private int _telemetryActiveFrame = -1;
        /** @brief 最近传送入口的稳定标识。 */
        private string _telemetryPortal = "";
        /** @brief 用于房间名与报告归属，和HUD一致。 @param index 房间或能力索引 */
        private string TelemetryRoom(int index) => _telemetryRoomNames != null && index < _telemetryRoomNames.Length ? _telemetryRoomNames[index] : $"Level{_roomLevels[index] + 1}Room{index - _levelFirst[_roomLevels[index]]}";
        /** @brief Debug面板是否允许本次退出导出。 */
        public bool DebugRecording => PlaytestRecording.Enabled;
        /** @brief 鼠标开关授权当前会话记录；关闭即丢弃。 @param enabled 是否允许本次会话导出 */
        public void DebugSetRecording(bool enabled)
        {
            if (enabled == DebugRecording) return;
            PlaytestRecording.SetEnabled(enabled);
            if (!DebugRecording)
            {
                _diagnosticObjects.Clear(); _diagnosticWarnings.Clear(); _diagnosticRecorder = null;
                _telemetryBound = null;
                return;
            }
            if (DebugRecording)
            {
                ObserveTelemetry();
                if (_ready) ObserveDiagnostics(PlaytestRecording.Recorder);
                RecordTelemetry("启用记录", "Debug面板", "仅记录当前启用区间；退出导出");
                for (int i = 0; i < 10; i++) RecordTelemetry("能力基线", i < 6 ? ((ProjectionAbility)i).ToString() : "颜色" + (i - 6), "有效=" + DebugAbility(i));
                if (_checkpoint) RecordTelemetry("存档基线", TelemetryObject(_checkpoint.transform), "开启前已激活，不计本次激活次数");
            }
        }
        /** @brief 生成只读状态。 */
        private PlaytestRecorder.Snapshot TelemetrySnapshot()
        {
            bool active = _started && !_paused && !_transitioning && (_telemetryActiveFrame == Time.frameCount ||
                (!_editing && (Mathf.Abs(Action("Move").ReadValue<float>()) > .01f || Action("Jump").IsPressed() || Action("Interact").IsPressed() || Action("Palette").IsPressed() || (Noclip && Action("DebugMove").ReadValue<Vector2>() != Vector2.zero))));
            Vector3 position = _player ? _player.transform.position : Vector3.zero;
            return new PlaytestRecorder.Snapshot
            {
                Room = TelemetryRoom(_roomIndex), View = _stage ? (_stage.View == StageView.World ? "2D关注" : _stage.View == StageView.Projection ? "投影调整" : _stage.View == StageView.Repair ? "维修光路" : "3D总览") : _editing ? (_repairFocus ? "维修光路" : "投影调整") : "2D关注",
                Color = _color < 0 ? "无选色" : _telemetryColors[_color], Playing = _started, Paused = _paused,
                Transitioning = _transitioning, Active = active, Noclip = Noclip, Powered = Room.Powered, ForcedPower = DebugPowerForced,
                X = position.x, Y = position.y, Z = position.z
            };
        }
        /** @brief 提交快照，开启时才做输入查询与字符串构建。 @param frame 当前帧号 */
        private void ObserveTelemetry(bool frame = false)
        {
            PlaytestRecorder recorder = PlaytestRecording.Recorder;
            if (recorder == null || !_ready) return;
            if (_rooms == null || _roomIndex < 0 || _roomIndex >= _rooms.Length || !_rooms[_roomIndex] || !_rooms[_roomIndex].Surface)
            {
                if (Time.realtimeSinceStartupAsDouble >= _nextDiagnostic)
                {
                    _nextDiagnostic = Time.realtimeSinceStartupAsDouble + 2;
                    PlaytestRecording.Diagnostic("异常线索", "当前房间或投影面丢失，房间索引=" + _roomIndex);
                }
                return;
            }
            if (_telemetryBound != recorder)
            {
                _telemetryRoomNames = new string[_rooms.Length];
                for (int i = 0; i < _rooms.Length; i++)
                {
                    _telemetryRoomNames[i] = $"Level{_roomLevels[i] + 1}Room{i - _levelFirst[_roomLevels[i]]}";
                    PlaytestRecording.BindRoom(_rooms[i].Surface, _telemetryRoomNames[i]);
                }
                _telemetryColors = new string[4];
                for (int i = 0; i < 4; i++) _telemetryColors[i] = GameText.Get("LightMenu._names.00" + (i + 1)) + "[" + i + "]";
                _telemetryBound = recorder;
            }
            recorder.Observe(TelemetrySnapshot(), Time.realtimeSinceStartupAsDouble, frame ? Time.unscaledDeltaTime * 1000 : 0);
            if (frame)
            {
                ObserveDiagnostics(recorder);
                recorder.EndFrame(Time.realtimeSinceStartupAsDouble, Time.frameCount);
            }
        }
        /** @brief 记录离散结果，未启用时不保留任何历史。 @param kind 事件或指标类别 @param subject 对象或状态标识 @param detail 补充说明 @param amount 累计变化量 */
        private void RecordTelemetry(string kind, string subject = "", string detail = "", double amount = 0)
        {
            if (!PlaytestRecording.Enabled || !_ready) return;
            if (kind == "对象选择" || kind == "维修节点操作" || kind == "选用颜色") _telemetryActiveFrame = Time.frameCount;
            ObserveTelemetry();
            PlaytestRecording.Recorder.Record(Time.realtimeSinceStartupAsDouble, kind, subject, detail, amount);
        }
        /** @brief 为跳转、重生、重开分隔房间访问。 @param reason 进入或退出原因 @param passed 是否成功通过房间 */
        private void BreakTelemetryVisit(string reason, bool passed = false)
        {
            if (!PlaytestRecording.Enabled || !_ready) return;
            ObserveTelemetry();
            PlaytestRecording.Recorder.Transition(Time.realtimeSinceStartupAsDouble, reason, passed);
        }
        /** @brief 场景卸载只关闭当前访问，不导出；重新进入仍属于同次程序启动。 */
        private void DetachTelemetry()
        {
            if (_telemetryBound != null && _telemetryBound == PlaytestRecording.Recorder)
            {
                _telemetryBound.Suspend(Time.realtimeSinceStartupAsDouble, "游戏对象停用/场景卸载");
                _telemetryBound.Flush(Time.realtimeSinceStartupAsDouble);
            }
        }
        /** @brief 对象路径包含同级序号，避免重名门和平台混为一项。 @param item 对象变换 */
        private string TelemetryObject(Transform item)
        {
            if (!PlaytestRecording.Enabled || !item) return "";
            LightRoom owner = item.GetComponentInParent<LightRoom>(true);
            Transform root = owner ? owner.transform : null;
            string path = "";
            for (Transform current = item; current && current != root; current = current.parent)
                path = current.name + "[" + current.GetSiblingIndex() + "]" + (path.Length > 0 ? "/" + path : "");
            int index = System.Array.IndexOf(_rooms, owner);
            return index >= 0 ? TelemetryRoom(index) + "/" + path : path;
        }
        /** @brief 记录连续操作；累计实际变化，按住时受限也保留操作时长。 @param subject 对象或状态标识 @param axis 操作轴或透镜类别 @param amount 累计变化量 */
        private void TelemetryOperation(string subject, string axis, double amount)
        {
            if (!PlaytestRecording.Enabled || !_ready) return;
            _telemetryActiveFrame = Time.frameCount;
            ObserveTelemetry();
            PlaytestRecording.Recorder.Operate(Time.realtimeSinceStartupAsDouble, Time.frameCount, subject, axis, Time.unscaledDeltaTime, amount);
        }
        /** @brief 记录键盘六轴实际变化，自动边界修正不冒充另一轴的输入。 @param source 被操作的投影源 @param move 移动轴输入 @param rotate 旋转轴输入 @param before 操作前世界位置 @param beforeRotation 操作前世界旋转 */
        private void TelemetryObjectMotion(ProjectionSource source, Vector3 move, Vector3 rotate, Vector3 before, Quaternion beforeRotation)
        {
            if (!PlaytestRecording.Enabled) return;
            Vector3 delta = source.transform.position - before;
            bool rotated = Quaternion.Angle(source.transform.rotation, beforeRotation) > .000001f;
            string subject = TelemetryObject(source.transform);
            for (int i = 0; i < 3; i++)
            {
                if (move[i] != 0) TelemetryOperation(subject, "移动" + "XYZ"[i], Mathf.Abs(delta[i]));
                if (rotate[i] != 0) TelemetryOperation(subject, "旋转" + "XYZ"[i], rotated ? Mathf.Abs(rotate[i] * _config.RotationSpeed * Time.unscaledDeltaTime) : 0);
            }
        }
    }
}
