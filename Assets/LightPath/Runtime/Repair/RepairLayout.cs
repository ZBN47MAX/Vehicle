using System;
using System.Collections.Generic;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 整局共用的维修布局与事件配置。 */
    [CreateAssetMenu(menuName = "Light Path/Repair Layout")]
    public sealed class RepairLayout : ScriptableObject
    {
        /** @brief 机关类型。 */
        public enum Kind { Source, Lens, Splitter, Mirror, Exit }
        /** @brief 手工布设的机关定义。 */
        [Serializable] public sealed class Device
        {
            /** @brief 稳定标识。 */
            public string Id = Guid.NewGuid().ToString();
            /** @brief 统一文案表中的机关名称标识。 */
            public string TextKey;
            /** @brief 游戏内显示名称。 */
            public string DisplayName => string.IsNullOrEmpty(TextKey) ? Name : GameText.Get(TextKey);
            /** @brief 展示名称。 */
            public string Name = GameText.Get("RepairLayout.Name.001");
            /** @brief 类型。 */
            public Kind Type;
            /** @brief 操作平面坐标。 */
            public Vector2 Position;
            /** @brief 初始八向朝向，0为右，逆时针递增。 */
            public int Direction;
            /** @brief 可用朝向位掩码。 */
            public int Allowed = 255;
            /** @brief 发射透镜相对自身基准朝向的连续偏转区间，单位度。 */
            public Vector2 AngleRange = new Vector2(-30, 30);
            /** @brief 发射透镜初始偏转角，单位度；不写入运行时修改。 */
            public float InitialAngle;
            /** @brief 初始实际出射角，单位度。 */
            public float OutputAngle => Direction * 45f + (Type == Kind.Lens ? InitialAngle : 0);
        }
        /** @brief 一次事件的目标组合。 */
        [Serializable] public sealed class Phase
        {
            /** @brief 统一文案表中的阶段名称标识。 */
            public string TextKey;
            /** @brief 游戏内显示名称。 */
            public string DisplayName => string.IsNullOrEmpty(TextKey) ? Name : GameText.Get(TextKey);
            /** @brief 阶段名称。 */
            public string Name = GameText.Get("RepairLayout.Name.002");
            /** @brief 必须同时点亮的出口标识。 */
            public string[] Targets = new string[0];
        }
        /** @brief 机关列表。 */
        public Device[] Devices = new Device[0];
        /** @brief 循环阶段。 */
        public Phase[] Phases = new Phase[0];
        /** @brief 每轮洗牌遍历所有阶段，再开始下一轮。 */
        public bool RandomPhaseOrder = true;
        /** @brief 事件间隔范围，正常供光观看时间秒数。 */
        public Vector2 Interval = new Vector2(40, 70);
        /** @brief 预警秒数。 */
        public float WarningSeconds = 8;
        /** @brief 操作空间。 */
        public Rect Area = new Rect(-10, -6, 20, 12);
        /** @brief 兼容旧调用方，命中与显示始终使用同一半宽。 */
        public float HitRadius { get => NodeRadius; set => NodeRadius = value; }
        /** @brief 节点可见方形边框的半宽，同时用于射线命中和编辑器预览。 */
        [Min(.01f)] public float NodeRadius = .45f;
        /** @brief 验证运行所需内容，不修改配置。@param error 首个错误 @return 内容可用于运行时为真；不保证谜题可解 */
        public bool TryValidate(out string error)
        {
            error = "";
            if (Devices == null || Devices.Length == 0)
                error = GameText.Get("RepairLayout.TryValidate.003");
            else if (Phases == null || Phases.Length == 0)
                error = GameText.Get("RepairLayout.TryValidate.004");
            else if (!Finite(WarningSeconds) || WarningSeconds < 0 || !Finite(Interval.x) || !Finite(Interval.y)
                || Interval.x <= WarningSeconds || Interval.y < Interval.x)
                error = GameText.Get("RepairLayout.TryValidate.005");
            else if (!Finite(Area.x) || !Finite(Area.y) || !Finite(Area.width) || !Finite(Area.height)
                || Area.width <= 0 || Area.height <= 0 || !Finite(HitRadius) || HitRadius <= 0)
                error = GameText.Get("RepairLayout.TryValidate.006");
            if (error.Length > 0)
                return false;
            var ids = new HashSet<string>();
            var exits = new HashSet<string>();
            int sources = 0;
            foreach (Device device in Devices)
            {
                if (device == null || string.IsNullOrWhiteSpace(device.Id) || !ids.Add(device.Id))
                {
                    error = GameText.Get("RepairLayout.TryValidate.007");
                    return false;
                }
                if (!Enum.IsDefined(typeof(Kind), device.Type) || device.Direction < 0 || device.Direction > 7
                    || (device.Type != Kind.Lens && ((device.Allowed & 255) == 0 || (device.Allowed & (1 << device.Direction)) == 0))
                    || (device.Type == Kind.Lens && (!Finite(device.AngleRange.x) || !Finite(device.AngleRange.y)
                        || device.AngleRange.x < -180 || device.AngleRange.y > 180 || device.AngleRange.x > device.AngleRange.y
                        || !Finite(device.InitialAngle) || device.InitialAngle < device.AngleRange.x || device.InitialAngle > device.AngleRange.y))
                    || !Finite(device.Position.x) || !Finite(device.Position.y)
                    || device.Position.x < Area.xMin || device.Position.x > Area.xMax
                    || device.Position.y < Area.yMin || device.Position.y > Area.yMax)
                {
                    error = GameText.Get("RepairLayout.TryValidate.008") + device.Name + GameText.Get("RepairLayout.TryValidate.009");
                    return false;
                }
                if (device.Type == Kind.Source)
                    sources++;
                if (device.Type == Kind.Exit)
                    exits.Add(device.Id);
            }
            if (sources == 0)
            {
                error = GameText.Get("RepairLayout.TryValidate.010");
                return false;
            }
            foreach (Phase phase in Phases)
            {
                if (phase == null || phase.Targets == null || phase.Targets.Length == 0)
                {
                    error = GameText.Get("RepairLayout.TryValidate.011");
                    return false;
                }
                foreach (string target in phase.Targets)
                    if (string.IsNullOrEmpty(target) || !exits.Contains(target))
                    {
                        error = GameText.Get("RepairLayout.TryValidate.012") + phase.Name + GameText.Get("RepairLayout.TryValidate.013");
                        return false;
                    }
            }
            return true;
        }
        /** @brief 判断数值是否有限。@param value 待检查数值 @return 是否有限 */
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
