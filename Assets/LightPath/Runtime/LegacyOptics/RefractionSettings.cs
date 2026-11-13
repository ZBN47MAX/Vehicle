// 这些字段只供历史资产反序列化，不再被玩法读取。
#pragma warning disable 0414
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 保留历史 Refraction.asset 的序列化数据；不参与玩法，禁止恢复旧维修逻辑。 */
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
    }
}

#pragma warning restore 0414
