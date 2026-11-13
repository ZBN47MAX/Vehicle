using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 单个光学界面的规则，共享资产运行时只读。 */
    [CreateAssetMenu(menuName = "Light Path/Optical Node Definition")]
    public sealed class OpticalNodeDefinition : ScriptableObject
    {
        /** @brief 光学响应类型。 */
        public enum Response
        {
            /** @brief 根据受光面选择左右固定角度偏转。 */
            Deflect,
            /** @brief 真实平面反射。 */
            Reflect,
            /** @brief 单个有方向的介质界面折射。 */
            Refract,
            /** @brief 不透光挡板。 */
            Block
        }
        /** @brief 响应规则。 */
        [SerializeField, Tooltip("偏转、反射、折射或阻挡")] private Response _response;
        /** @brief 偏转角度。 */
        [SerializeField, Range(1, 179), Tooltip("固定偏转角，正反受光面分别左右转向")] private float _deflection = 90;
        /** @brief 界面有效半长。 */
        [SerializeField, Min(.05f), Tooltip("板面内光学线段半长，米")] private float _halfLength = 1;
        /** @brief 正面介质折射率。 */
        [SerializeField, Min(1), Tooltip("局部左侧介质的折射率")] private float _frontIndex = 1;
        /** @brief 背面介质折射率。 */
        [SerializeField, Min(1), Tooltip("局部右侧介质的折射率；玻璃出射界面需要对应的介质配置")] private float _backIndex = 1.5f;
        /** @brief 允许平移。 */
        [SerializeField, Tooltip("允许左键在维修板内拖动")] private bool _movable;
        /** @brief 获取响应。 */
        public Response Kind => _response;
        /** @brief 获取半长。 */
        public float HalfLength => Mathf.Max(.05f, _halfLength);
        /** @brief 获取角度。 */
        public float Deflection => _deflection;
        /** @brief 获取正面介质。 */
        public float FrontIndex => Mathf.Max(1, _frontIndex);
        /** @brief 获取背面介质。 */
        public float BackIndex => Mathf.Max(1, _backIndex);
        /** @brief 获取平移许可。 */
        public bool Movable => _movable;
    }
}
