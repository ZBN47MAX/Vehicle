using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 封装可独立布设的房间、机关和局内恢复入口。 */
    public sealed class LightRoom : MonoBehaviour
    {
        /** @brief 房间定义。 */
        [SerializeField, Tooltip("房间流程与提示 SO")] private LightRoomDefinition _definition;
        /** @brief 投影系统。 */
        [SerializeField, Tooltip("本房间投影平面")] private ProjectionSurface _surface;
        /** @brief 折射系统。 */
        [SerializeField, Tooltip("本房间维修装置")] private RefractionRig _rig;
        /** @brief 固定实体。 */
        [SerializeField, Tooltip("固定环境碰撞平台")] private ColorPlatform[] _platforms;
        /** @brief 出生位置。 */
        [SerializeField, Tooltip("安全存档点")] private Transform _spawn;
        /** @brief 房间出口。 */
        [SerializeField, Tooltip("进入下一房间的位置")] private Transform _exit;
        /** @brief 光片收集点。 */
        [SerializeField, Tooltip("需要收集的光片")] private Transform[] _shards;
        /** @brief 幕布材质显示。 */
        [SerializeField, Tooltip("幕布背景 Renderer")] private Renderer _screen;
        /** @brief 额外属于2D世界的装饰表现，不包含幕布和维修装置。 */
        [SerializeField, Tooltip("断光时一并隐藏的2D装饰根，不放幕布或3D设备")] private Transform[] _worldPresentation = new Transform[0];
        /** @brief 运行时间。 */
        private float _clock;
        /** @brief 本次重置后完成的维修次数。 */
        public int RepairCount => _repair != null ? _repair.RepairCount : _legacyRepairCount;
        /** @brief 未绑定整局配置时的旧维修次数。 */
        private int _legacyRepairCount;
        /** @brief 未绑定整局配置时的旧供光状态。 */
        private bool _legacyPowered = true;
        /** @brief 由游戏入口提供的共享维修实例。 */
        private RepairSession _repair;
        /** @brief 当前光路连接状态。 */
        public bool Powered => _repair != null ? _repair.Powered : _legacyPowered;
        /** @brief 获取预警状态。 */
        public bool Warning => _repair != null ? _repair.Warning : _definition.OpticsEnabled && _clock >= _definition.CycleSeconds - _definition.WarningSeconds;
        /** @brief 绑定整局维修实例；不重置该实例。@param repair 共享实例，空表示旧玩法 */
        public void BindRepair(RepairSession repair)
        {
            _repair = repair;
            if (_repair != null && _rig)
                _rig.SetVisible(false);
        }
        /** @brief 获取定义。 */
        public LightRoomDefinition Definition => _definition;
        /** @brief 获取投影。 */
        public ProjectionSurface Surface => _surface;
        /** @brief 获取折射装置。 */
        public RefractionRig Rig => _rig;
        /** @brief 获取安全出生点。 */
        public Vector2 Spawn => _spawn ? (Vector2)_spawn.position : (Vector2)transform.position + Vector2.up;
        /** @brief 是否绑定有效出生点，兼容未挂出生标记组件的旧房间。 */
        public bool HasSpawn => _spawn && _spawn.gameObject.activeInHierarchy;
        /** @brief 获取出口。 */
        public Vector2 Exit => _exit ? (Vector2)_exit.position : new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        /** @brief 获取收集点。 */
        public Transform[] Shards => _shards;
        /** @brief 获取固定平台。 */
        public ColorPlatform[] Platforms => _platforms;
        /** @brief 配置房间所有引用。 @param definition 定义 @param surface 投影 @param rig 光路 @param platforms 平台 @param spawn 出生 @param exit 出口 @param shards 光片 @param screen 幕布 */
        public void Configure(LightRoomDefinition definition, ProjectionSurface surface, RefractionRig rig, ColorPlatform[] platforms, Transform spawn, Transform exit, Transform[] shards, Renderer screen)
        {
            _definition = definition;
            _surface = surface;
            _rig = rig;
            _platforms = platforms;
            _spawn = spawn;
            _exit = exit;
            _shards = shards;
            _screen = screen;
        }
        /** @brief 重置关卡布局与光路。 */
        public void ResetRoom()
        {
            _clock = _definition.OpticsEnabled ? _definition.CycleSeconds - _definition.WarningSeconds - 2 : 0;
            _legacyRepairCount = 0;
            _legacyPowered = true;
            foreach (ProjectionSource source in _surface.Sources)
                source.ResetPose();
            _surface.Invalidate();
            _surface.Rebuild();
            if (_repair != null)
                return;
            if (_rig)
                _rig.ResetRig();
            _legacyPowered = !_definition.OpticsEnabled || (_rig && _rig.Connected);
        }
        /** @brief 推进观看时间。 @param delta 世界时间增量 */
        public void Tick(float delta)
        {
            if (_repair != null || !_definition.OpticsEnabled || !_rig || !Powered)
                return;
            _clock += delta;
            if (_clock >= _definition.CycleSeconds)
            {
                _clock = 0;
                _rig.Advance();
                _legacyPowered = _rig.Connected;
            }
            _rig.Trace(Warning);
        }
        /** @brief 检查修复结果。 @return 是否恢复光路 */
        public bool Repair()
        {
            if (_repair != null)
                return _repair.Powered;
            if (!_rig)
                return false;
            bool wasPowered = Powered;
            _rig.Recalculate();
            _legacyPowered = _rig.Connected;
            if (!wasPowered && Powered)
                _legacyRepairCount++;
            return Powered;
        }
        /** @brief 刷新背景和全部投影。 @param config 配置 @param color 当前色 @param preview 预览 @param editing 装置模式 @param selected 选中模型 */
        public void Refresh(LightGameConfig config, int color, bool preview, bool editing, int selected)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", config.DisplayColor(color < 0 ? config.DefaultBackgroundColor : config.GetColor(color)));
            _screen.SetPropertyBlock(properties);
            foreach (Transform root in _worldPresentation)
                SetPresentation(root, Powered);
            SetPresentation(_exit, Powered);
            foreach (Transform shard in _shards)
                SetPresentation(shard, Powered);
            foreach (ColorPlatform platform in _platforms)
                platform.Refresh(color, preview, Powered, config);
            _surface.Refresh(color, preview, Powered);
            for (int i = 0; i < _surface.Sources.Count; i++)
                _surface.Sources[i].SetVisible(editing, i == selected);
            if (_rig)
                _rig.SetVisible(editing && _repair == null);
            foreach (LevelExit exit in GetComponentsInChildren<LevelExit>(true))
                exit.Refresh(config, color, Powered);
            foreach (RoomCheckpoint checkpoint in GetComponentsInChildren<RoomCheckpoint>(true))
                checkpoint.Refresh(config, color, Powered);
            foreach (RoomDecoration decoration in GetComponentsInChildren<RoomDecoration>(true))
                decoration.Refresh(Powered);
            foreach (Renderer renderer in _surface.Origin.GetComponentsInChildren<Renderer>())
                renderer.enabled = editing;
        }
        /** @brief 统一控制2D表现，不改变收集对象的激活状态。 @param root 表现根 @param visible 是否显示 */
        private static void SetPresentation(Transform root, bool visible)
        {
            if (root)
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = visible;
        }
        /** @brief 检查实体重叠。 @param bounds 角色边界 @param color 当前色 @param hiddenOnly 仅隐藏实体 @return 是否冲突 */
        public bool Overlaps(Bounds bounds, int color, bool hiddenOnly)
        {
            foreach (ColorPlatform platform in _platforms)
            {
                bool hidden = platform.ColorIndex >= 0 && platform.ColorIndex == color;
                if (hidden == hiddenOnly && platform.Overlaps(bounds))
                    return true;
            }
            return _surface.Overlaps(bounds, color, hiddenOnly);
        }
    }
}
