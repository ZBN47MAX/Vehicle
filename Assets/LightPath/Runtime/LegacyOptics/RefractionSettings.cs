using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 配置折射界面与端点时序。 */
    [CreateAssetMenu(menuName = "Light Path/Refraction Settings")]
    public sealed class RefractionSettings : ScriptableObject
    {
        /** @brief 每处界面的出射介质折射率。 */
        [SerializeField, Tooltip("入射介质按 1 计算，出射介质折射率"), Range(1.01f, 2.5f)] private float _index = 1.5f;
        /** @brief 接收器容差。 */
        [SerializeField, Tooltip("出口捕获半径，米"), Range(.05f, .8f)] private float _tolerance = .25f;
        /** @brief 界面半长度。 */
        [SerializeField, Tooltip("节点有效半长度，米"), Min(1)] private float _aperture = 2.5f;
        /** @brief 端点组合对应的目标参考角。 */
        [SerializeField, Tooltip("用于生成各组出口位置的参考角度")] private float[] _angles = { 0, 40, -40, 55 };
        /** @brief 各组合起点高度。 */
        [SerializeField, Tooltip("各组起点高度，米")] private float[] _heights = { 0, .6f, -.6f, .3f };
        /** @brief 自定义端点位置，空数组兼容旧演示布局。 */
        [SerializeField, Tooltip("新维修板各阶段起点，板面本地XY坐标；与出口一一对应")] private Vector2[] _starts = new Vector2[0];
        /** @brief 自定义端点出口。 */
        [SerializeField, Tooltip("固定出口坐标，不随旋转镜子改变")] private Vector2[] _exits = new Vector2[0];
        /** @brief 各阶段入射方向。 */
        [SerializeField, Tooltip("每阶段初始方向；缺项时向右")] private Vector2[] _directions = new Vector2[0];
        /** @brief 最大追踪段数。 */
        [SerializeField, Min(1), Tooltip("反复反射的最大光段数")] private int _maxSegments = 32;
        /** @brief 路径总长限制。 */
        [SerializeField, Min(1), Tooltip("光路总长度上限，板面米")] private float _maxDistance = 100;
        /** @brief 节点移动范围。 */
        [SerializeField, Tooltip("允许移动节点的板面矩形范围")] private Rect _boardBounds = new Rect(-8, -4, 16, 8);
        /** @brief 是否使用策划端点。 */
        public bool HasAuthoredEndpoints => _starts != null && _exits != null && _starts.Length > 0 && _starts.Length == _exits.Length;
        /** @brief 获取段数预算。 */
        public int MaxSegments => Mathf.Clamp(_maxSegments, 1, 256);
        /** @brief 获取长度预算。 */
        public float MaxDistance => Mathf.Max(1, _maxDistance);
        /** @brief 获取板面范围。 */
        public Rect BoardBounds => _boardBounds;
        /** @brief 读取阶段起点。 @param phase 阶段 @return 坐标 */
        public Vector2 GetStart(int phase) => HasAuthoredEndpoints ? _starts[phase % PhaseCount] : new Vector2(-6, GetHeight(phase));
        /** @brief 读取阶段出口。 @param phase 阶段 @return 坐标 */
        public Vector2 GetExit(int phase) => _exits[phase % PhaseCount];
        /** @brief 读取入射方向。 @param phase 阶段 @return 单位方向 */
        public Vector2 GetDirection(int phase) => _directions != null && phase < _directions.Length && _directions[phase].sqrMagnitude > .001f ? _directions[phase].normalized : Vector2.right;
        /** @brief 获取介质折射率。 */
        public float Index => Mathf.Clamp(_index, 1.01f, 2.5f);
        /** @brief 获取出口容差。 */
        public float Tolerance => Mathf.Max(.05f, _tolerance);
        /** @brief 获取界面半长度。 */
        public float Aperture => _aperture;
        /** @brief 获取组合数量。 */
        public int PhaseCount => HasAuthoredEndpoints ? _starts.Length : Mathf.Max(1, Mathf.Min(_angles?.Length ?? 0, _heights?.Length ?? 0));
        /** @brief 读取参考角。 @param phase 组合编号 @return 角度 */
        public float GetAngle(int phase) => _angles == null || _angles.Length == 0 ? 0 : _angles[phase % PhaseCount];
        /** @brief 读取起点高度。 @param phase 组合编号 @return 高度 */
        public float GetHeight(int phase) => _heights == null || _heights.Length == 0 ? 0 : _heights[phase % PhaseCount];
    }
}
