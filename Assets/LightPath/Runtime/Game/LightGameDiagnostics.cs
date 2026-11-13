using System.Collections.Generic;
using System.Text;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 仅在本次授权期间采样故障上下文，不改变对象状态。 */
    public sealed partial class LightGame
    {
        private sealed class DiagnosticObject
        {
            public GameObject Object;
            public int Room;
            public string Path;
            public bool ExpectedActive;
        }
        private readonly List<DiagnosticObject> _diagnosticObjects = new List<DiagnosticObject>();
        private readonly HashSet<string> _diagnosticWarnings = new HashSet<string>();
        private PlaytestRecorder _diagnosticRecorder;
        private double _nextDiagnostic;
        /** @brief 忽略房间根停用，只检查物件自身及中间父节点。 */
        private static bool ActiveInRoom(Transform item, Transform room)
        {
            for (Transform current = item; current && current != room; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }
        /** @brief 两秒一次状态与对象清单，异常变化即记入CSV并刷盘。 */
        private void ObserveDiagnostics(PlaytestRecorder recorder)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (_diagnosticRecorder != recorder)
            {
                _diagnosticRecorder = recorder;
                _diagnosticObjects.Clear(); _diagnosticWarnings.Clear(); _nextDiagnostic = 0;
                var found = new HashSet<GameObject>();
                for (int index = 0; index < _rooms.Length; index++)
                {
                    LightRoom room = _rooms[index];
                    foreach (Component component in room.GetComponentsInChildren<Component>(true))
                    {
                        if (!(component is ProjectionSource) && !(component is ColorPlatform) && !(component is RoomPortalLink)) continue;
                        if (!found.Add(component.gameObject)) continue;
                        _diagnosticObjects.Add(new DiagnosticObject { Object = component.gameObject, Room = index,
                            Path = TelemetryObject(component.transform), ExpectedActive = ActiveInRoom(component.transform, room.transform) });
                    }
                }
                PlaytestRecording.Diagnostic("对象基线", "已登记物件=" + _diagnosticObjects.Count + "；能力钥匙停用、同色隐藏与断光作为正常上下文记录。");
            }
            if (now < _nextDiagnostic) return;
            _nextDiagnostic = now + 2;
            var text = new StringBuilder();
            var warnings = new HashSet<string>();
            Vector3 visible = _player ? _player.transform.position : Vector3.zero;
            Vector2 physics = _player ? _player.position : Vector2.zero;
            text.Append("启动后秒=").Append(now.ToString("F2")).Append(" 帧=").Append(Time.frameCount)
                .Append(" 房间=").Append(TelemetryRoom(_roomIndex)).Append(" 视角=").Append(CurrentView)
                .Append(" 游玩=").Append(_started).Append(" 暂停=").Append(_paused).Append(" 转场=").Append(_transitioning)
                .Append(" 供光=").Append(Room.Powered).Append(" 选色=").Append(_color).Append(" 浮空=").Append(Noclip)
                .Append("\n房间active=").Append(Room.gameObject.activeInHierarchy).Append(" 根位置=").Append(Room.transform.position.ToString("R"))
                .Append(" 边界=").Append(Room.Surface.Bounds).Append(" 投影块=").Append(Room.Surface.LoadedChunks)
                .Append(" 区域=").Append(Room.Surface.Regions.Count).Append(" 源=").Append(Room.Surface.Sources.Count)
                .Append(" 最近重建ms=").Append(Room.Surface.LastBuildMilliseconds)
                .Append(" 舞台就绪=").Append(_stage && _stage.Ready)
                .Append("\n角色存在=").Append((bool)_player).Append(" 显示位置=").Append(visible.ToString("R"))
                .Append(" 刚体位置=").Append(physics.ToString("R"))
                .Append(" 托管内存MiB=").Append(System.GC.GetTotalMemory(false) / 1048576)
                .Append(" Unity已分配MiB=").Append(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576);
            if (!_player) warnings.Add("角色刚体对象丢失");
            else
            {
                bool rendered = false;
                foreach (Renderer renderer in _playerRenderers)
                    if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy) rendered = true;
                SpriteRenderer sprite = _player.GetComponentInChildren<SpriteRenderer>(true);
                text.Append(" simulated=").Append(_player.simulated).Append(" active=").Append(_player.gameObject.activeInHierarchy)
                    .Append(" rendererEnabled=").Append(rendered).Append(" sprite=").Append(sprite && sprite.sprite ? sprite.sprite.name : "空")
                    .Append(" 速度=").Append(_player.linearVelocity.ToString("R"));
                if (_started && !_transitioning)
                {
                    if ((Room.Powered || Noclip) && (!rendered || (sprite && !sprite.sprite))) warnings.Add("角色显示状态异常");
                    if ((Noclip || !_player.simulated) && Vector2.Distance(visible, physics) > .05f) warnings.Add("角色显示位置与刚体不同步");
                    if (float.IsNaN(visible.x) || float.IsNaN(visible.y) || float.IsInfinity(visible.x) || float.IsInfinity(visible.y)) warnings.Add("角色坐标非有限值");
                    if (!Room.Surface.Bounds.Contains(visible)) warnings.Add("角色位于房间边界外");
                    if (!Room.gameObject.activeInHierarchy) warnings.Add("当前房间被停用");
                }
            }
            foreach (DiagnosticObject item in _diagnosticObjects)
            {
                if (item.Room != _roomIndex) continue;
                text.Append("\n物件 ").Append(item.Path);
                if (!item.Object) { text.Append(" 已销毁"); warnings.Add("物件已销毁 " + item.Path); continue; }
                Transform pose = item.Object.transform;
                bool active = ActiveInRoom(pose, Room.transform);
                text.Append(" active=").Append(active).Append(" 位置=").Append(pose.position.ToString("R"))
                    .Append(" 旋转=").Append(pose.eulerAngles.ToString("R")).Append(" 缩放=").Append(pose.lossyScale.ToString("R"));
                bool pickup = item.Object.GetComponent<ColorAbilityKey>() || item.Object.GetComponent<ProjectionAbilityKey>();
                if (_started && !_transitioning && item.ExpectedActive && !active && !pickup) warnings.Add("物件意外停用 " + item.Path);
                ProjectionSource source = item.Object.GetComponent<ProjectionSource>();
                if (source && source.Definition)
                    text.Append(" 可操作=").Append(source.Definition.Movable).Append(" 门=").Append(source.Definition.Portal)
                        .Append(" 同色隐藏=").Append(source.Definition.Transmissive && _config.Matches(source.Definition.GetColor(_config), _color));
                foreach (Renderer renderer in item.Object.GetComponentsInChildren<Renderer>(true))
                    text.Append(" renderer=").Append(renderer.name).Append('/').Append(renderer.enabled);
                if (text.Length > 65536) { text.Append("\n物件详情达到单次采样上限"); break; }
            }
            for (int index = 0; index < Room.Surface.Sources.Count; index++)
                if (!Room.Surface.Sources[index]) warnings.Add("投影源引用为空 slot=" + index);
            PlaytestRecording.Diagnostic("状态快照", text.ToString());
            foreach (string warning in warnings)
                if (!_diagnosticWarnings.Contains(warning))
                {
                    PlaytestRecording.Diagnostic("异常线索", warning);
                    recorder.Record(now, "异常线索", TelemetryRoom(_roomIndex), warning);
                    recorder.Flush(now);
                }
            foreach (string previous in _diagnosticWarnings)
                if (!warnings.Contains(previous)) PlaytestRecording.Diagnostic("线索解除或离开该房间", previous);
            _diagnosticWarnings.Clear();
            foreach (string warning in warnings) _diagnosticWarnings.Add(warning);
        }
    }
}
