using System;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 整局维修状态；跨房间共享，配置只读，初始化即按第一阶段检查严格供光。 */
    public sealed class RepairSession
    {
        /** @brief 本局内容定义。 */
        private readonly RepairLayout _layout;
        /** @brief 本局各机关朝向。 */
        private readonly int[] _directions;
        /** @brief 本局实际出射角，支持透镜连续调节。 */
        private readonly float[] _angles;
        /** @brief 间隔随机源。 */
        private readonly Func<float, float, float> _random;
        /** @brief 当前已启用的阶段，初始化固定进入索引0阶段。 */
        private int _phase;
        /** @brief 当前轮已洗牌的阶段索引。 */
        private readonly int[] _phaseOrder;
        /** @brief 当前轮位置。 */
        private int _phaseCursor;
        /** @brief 下一阶段在预警前固定，不因查询而重新随机。 */
        private int _nextPhase;
        /** @brief 剩余正常观看时间，秒。 */
        public float RemainingSeconds { get; private set; }
        /** @brief 当前是否为世界供光。 */
        public bool Powered => _debugPower ?? _powered;
        /** @brief 光路真实连接状态，与调试覆盖分开保存。 */
        private bool _powered;
        /** @brief 本会话强制供光状态，空值表示遵循光路。 */
        private bool? _debugPower;
        /** @brief 是否正在强制供光或断光。 */
        public bool DebugPowerForced => _debugPower.HasValue;
        /** @brief 覆盖供光状态，不修改角度、阶段、计时或维修次数。@param powered 供光状态，空值恢复真实光路 */
        public void SetDebugPower(bool? powered) => _debugPower = powered;
        /** @brief 实际由断光恢复的次数。 */
        public int RepairCount { get; private set; }
        /** @brief 最近一次光路计算结果。 */
        public RepairPreviewSolver.Result Result { get; private set; }
        /** @brief 当前是否处于事件预警。 */
        public bool Warning => !_debugPower.HasValue && Powered && RemainingSeconds <= _layout.WarningSeconds;
        /** @brief 当前生效阶段。 */
        public int PhaseIndex => _phase;
        /** @brief 下一事件阶段，由本局已确定的阶段顺序提供。 */
        public int NextPhaseIndex => _nextPhase;
        /** @brief 创建全新会话。@param layout 已配置的布局 @param random 间隔采样函数；为空时使用 Unity 随机源 */
        public RepairSession(RepairLayout layout, Func<float, float, float> random = null)
        {
            if (!layout)
                throw new ArgumentNullException(nameof(layout));
            if (!layout.TryValidate(out string error))
                throw new ArgumentException(error, nameof(layout));
            _layout = layout;
            _random = random ?? UnityEngine.Random.Range;
            _phaseOrder = new int[layout.Phases.Length];
            ShufflePhases(-1);
            _phase = 0;
            PrepareNextPhase();
            _directions = new int[layout.Devices.Length];
            _angles = new float[layout.Devices.Length];
            for (int i = 0; i < _directions.Length; i++)
            {
                _directions[i] = layout.Devices[i].Direction;
                _angles[i] = layout.Devices[i].OutputAngle;
            }
            Recalculate();
            if (Powered) ScheduleNext();
        }
        /** @brief 只在正常供光观看期间调用；到期固定本次目标。@param seconds 本帧观看时间，秒 */
        public void Tick(float seconds)
        {
            if (_debugPower.HasValue || !Powered || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return;
            RemainingSeconds = Mathf.Max(0, RemainingSeconds - seconds);
            if (RemainingSeconds > 0)
                return;
            _phase = NextPhaseIndex;
            PrepareNextPhase();
            Recalculate();
            if (Powered)
                ScheduleNext();
        }
        /** @brief 查询本局朝向。@param index 机关索引 @return 八向朝向 */
        public int Direction(int index) => _directions[index];
        /** @brief 查询实际出射角度。@param index 节点索引 @return 角度 */
        public float Angle(int index) => _angles[index];
        /** @brief 按指针角度设置透镜，限制在自身基准方向的允许区间。@param index 节点 @param degrees 世界平面角度 @return 是否变化 */
        public bool SetLensAngle(int index, float degrees)
        {
            if (index < 0 || index >= _angles.Length || _layout.Devices[index].Type != RepairLayout.Kind.Lens
                || float.IsNaN(degrees) || float.IsInfinity(degrees)) return false;
            var device = _layout.Devices[index];
            float next = RepairPreviewSolver.ClampLensAngle(device, degrees);
            if (Mathf.Approximately(next, _angles[index])) return false;
            _angles[index] = next;
            AfterAdjustment();
            return true;
        }
        /** @brief 判断出口是否为指定阶段目标。@param id 出口标识 @param next 是否查询下个事件 @return 是否为目标 */
        public bool IsTarget(string id, bool next = false)
        {
            return Array.IndexOf(_layout.Phases[next ? NextPhaseIndex : PhaseIndex].Targets, id) >= 0;
        }
        /** @brief 单向切换一个机关，重新判定当前目标。@param index 机关索引 @return 朝向是否变化 */
        public bool Rotate(int index)
        {
            if (index < 0 || index >= _directions.Length)
                return false;
            RepairLayout.Device device = _layout.Devices[index];
            if (device.Type == RepairLayout.Kind.Source || device.Type == RepairLayout.Kind.Exit || device.Type == RepairLayout.Kind.Lens)
                return false;
            int next = RepairPreviewSolver.Next(_directions[index], device.Allowed);
            if (next == _directions[index])
                return false;
            _directions[index] = next;
            _angles[index] = next * 45f;
            AfterAdjustment();
            return true;
        }
        /** @brief 统一重算并在真正恢复供光时累计维修次数。 */
        private void AfterAdjustment()
        {
            bool wasPowered = _powered;
            Recalculate();
            if (!wasPowered && _powered)
            {
                RepairCount++;
                ScheduleNext();
            }
        }
        /** @brief 重新追踪全部光源，点亮集合必须与阶段目标完全相等。 */
        private void Recalculate()
        {
            Result = RepairPreviewSolver.TraceAngles(_layout, _angles);
            _powered = Result.Matches(_layout.Phases[_phase].Targets);
            if (!_powered) RemainingSeconds = 0;
        }
        /** @brief 为下一事件采样完整间隔；计时由外部观看状态推进。 */
        private void ScheduleNext()
        {
            RemainingSeconds = Mathf.Clamp(_random(_layout.Interval.x, _layout.Interval.y), _layout.Interval.x, _layout.Interval.y);
        }
        /** @brief 准备下一目标，每轮全部出现一次，轮界避免立即重复。 */
        private void PrepareNextPhase()
        {
            if (!_layout.RandomPhaseOrder)
            {
                _nextPhase = (_phase + 1) % _phaseOrder.Length;
                return;
            }
            _phaseCursor++;
            if (_phaseCursor >= _phaseOrder.Length) ShufflePhases(_phase);
            _nextPhase = _phaseOrder[_phaseCursor];
        }
        /** @brief 首轮保留0为首项，其余阶段Fisher-Yates洗牌，后续整轮洗牌且不写回SO。@param previous 上一轮末阶段，首次为负数 */
        private void ShufflePhases(int previous)
        {
            for (int i = 0; i < _phaseOrder.Length; i++) _phaseOrder[i] = i;
            if (_layout.RandomPhaseOrder)
            {
                int firstIndex = previous < 0 ? 1 : 0;
                for (int i = _phaseOrder.Length - 1; i > firstIndex; i--)
                {
                    float value = _random(firstIndex, i + 1);
                    int choice = float.IsNaN(value) ? firstIndex : Mathf.Clamp(Mathf.FloorToInt(value), firstIndex, i);
                    int saved = _phaseOrder[i]; _phaseOrder[i] = _phaseOrder[choice]; _phaseOrder[choice] = saved;
                }
                if (_phaseOrder.Length > 1 && _phaseOrder[0] == previous)
                {
                    int saved = _phaseOrder[0]; _phaseOrder[0] = _phaseOrder[1]; _phaseOrder[1] = saved;
                }
            }
            _phaseCursor = 0;
        }
    }
}
