using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 定义可复用模型的光学性质、操作范围和投影交互。 */
    [CreateAssetMenu(menuName = "Light Path/Projection Definition")]
    public sealed class ProjectionDefinition : ScriptableObject
    {
        /** @brief 是否允许透光并参与混色。 */
        [SerializeField, Tooltip("透光模型按 RGB 权重混色；不透光模型形成黑色实体影子")] private bool _transmissive;
        /** @brief 是否仅生成传送触发区域。 */
        [SerializeField, Tooltip("传送门整体仅提供触发区域")] private bool _portal;
        /** @brief 是否允许编辑模型。 */
        [SerializeField, Tooltip("允许在装置模式调整位置和姿态")] private bool _movable = true;
        /** @brief 预设色索引，负数使用自定义色。 */
        [SerializeField, Range(-1, 3), Tooltip("0–3 引用全局色表；-1 使用自定义色。正红色为存档点专用")] private int _paletteIndex = -1;
        /** @brief 自定义透射色。 */
        [SerializeField, Tooltip("自定义透射色")] private Color _color = Color.white;
        /** @brief 连续混色权重。 */
        [SerializeField, Tooltip("混色权重，大于零"), Min(.01f)] private float _weight = 1;
        /** @brief 相对初始位置的移动范围。 */
        [SerializeField, Tooltip("相对初始位置的 XYZ 最大移动量，米")] private Vector3 _travel = new Vector3(3, 2, 2);
        /** @brief 是否必须匹配一种预设色。 */
        [SerializeField, Tooltip("策划工具对无法匹配预设色的结果给出提示")] private bool _requiresMatch;
        /** @brief 获取透光状态。 */
        public bool Transmissive => _transmissive;
        /** @brief 获取传送类型。 */
        public bool Portal => _portal;
        /** @brief 获取可编辑状态。 */
        public bool Movable => _movable;
        /** @brief 每个物件允许的局部旋转轴，教学机关可单独限制。 */
        [SerializeField] private Vector3Int _rotationAxes = Vector3Int.one;
        /** @brief 判断物件是否允许指定局部轴旋转。@param axis X/Y/Z编号 @return 是否允许 */
        public bool AllowsRotation(int axis) => axis >= 0 && axis < 3 && _rotationAxes[axis] != 0;
        /** @brief 获取有效权重。 */
        public float Weight => Mathf.Max(.01f, _weight);
        /** @brief 获取移动范围。 */
        public Vector3 Travel => _travel;
        /** @brief 获取消隐检查标记。 */
        public bool RequiresMatch => _requiresMatch;
        /** @brief 获取透射色。 @param config 全局配置 @return 连续 RGB 颜色 */
        public Color GetColor(LightGameConfig config)
        {
            Color color = _paletteIndex >= 0 ? config.GetColor(_paletteIndex) : _color;
            return LightGameConfig.IsPureRed(color) ? Color.gray : color;
        }
        /** @brief 清理旧投影色号和自定义纯红配置，避免占用存档点标识色。 */
        private void OnValidate()
        {
            if (_paletteIndex == LightGameConfig.RedColorIndex)
            {
                _paletteIndex = -1;
                _color = Color.gray;
            }
            if (LightGameConfig.IsPureRed(_color))
                _color = Color.gray;
        }
        /** @brief 配置生成资产。 @param transparent 透光 @param portal 传送 @param palette 色号 @param movable 可操作 @param custom 自定义色 */
        public void Configure(bool transparent, bool portal, int palette, bool movable, Color custom)
        {
            _transmissive = transparent;
            _portal = portal;
            _paletteIndex = palette;
            _movable = movable;
            _color = custom;
            OnValidate();
            _requiresMatch = transparent;
        }
    }
}
