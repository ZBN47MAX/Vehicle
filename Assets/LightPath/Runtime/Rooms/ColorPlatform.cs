using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 根据全局色切换平台显示和碰撞，预览仅影响显示。 */
    public sealed class ColorPlatform : MonoBehaviour
    {
        /** @brief 平台颜色编号，负数表示基础平台。 */
        [SerializeField, Tooltip("颜色编号 0 到 3 为手动滤镜，4 仅供存档点使用，-1 为基础平台")] private int _colorIndex = -1;
        /** @brief 检查作者配置，正红色仅保留给同对象上的存档点。 */
        private void OnValidate()
        {
            Configure(_colorIndex);
        }
        /** @brief 平台实体碰撞。 */
        private Collider2D _collider;
        /** @brief 平台可视模型。 */
        private Renderer _renderer;
        /** @brief 获取平台色号。 */
        public int ColorIndex => _colorIndex;
        /** @brief 配置平台颜色。 @param index 颜色编号 */
        public void Configure(int index)
        {
            _colorIndex = index == LightGameConfig.RedColorIndex && !GetComponent<RoomCheckpoint>() ? -1 : index;
        }
        /** @brief 获取组件引用。 */
        private void Awake()
        {
            _collider = GetComponent<Collider2D>();
            _renderer = GetComponent<Renderer>();
        }
        /** @brief 检查角色是否位于本平台实体内部。 @param body 角色边界 @return 是否发生穿入 */
        public bool Overlaps(Bounds body)
        {
            if (!isActiveAndEnabled) return false;
            Collider2D collider = GetComponent<Collider2D>();
            if (!collider || collider.isTrigger) return false;
            if (collider is BoxCollider2D box)
                return BoxOverlaps(body, box.transform.localToWorldMatrix, box.offset, box.size);
            Bounds bounds = collider.bounds;
            return body.min.x < bounds.max.x - .02f && body.max.x > bounds.min.x + .02f
                && body.min.y < bounds.max.y - .02f && body.max.y > bounds.min.y + .02f;
        }
        /** @brief 按真实矩形碰撞形状检查，保留旋转、缩放和偏移；隐藏平台也可查询。@param body 角色边界 @param matrix 平台变换 @param offset 碰撞中心 @param size 碰撞尺寸 @return 是否穿入 */
        public static bool BoxOverlaps(Bounds body, Matrix4x4 matrix, Vector2 offset, Vector2 size)
        {
            Vector2 half = size * .5f;
            Vector2[] points = { offset - half, offset + new Vector2(half.x, -half.y), offset + half, offset + new Vector2(-half.x, half.y) };
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(points[0]), Vector3.zero);
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = matrix.MultiplyPoint3x4(points[i]);
                bounds.Encapsulate(points[i]);
            }
            return new ProjectionRegion(bounds, Color.black, false, null, true, points).Contains(body);
        }
        /** @brief 更新可见与碰撞状态。 @param selected 当前颜色 @param preview 是否预览全部 */
        public void Refresh(int selected, bool preview)
        {
            bool hidden = _colorIndex >= 0 && _colorIndex == selected;
            _collider.enabled = !hidden;
            _renderer.enabled = !hidden || preview;
        }
        /** @brief 按当前光路与色表刷新固定物件。 @param selected 背景色 @param preview 预览 @param powered 光路状态 @param config 色表 */
        public void Refresh(int selected, bool preview, bool powered, LightGameConfig config)
        {
            if (!_collider)
                Awake();
            bool hidden = _colorIndex >= 0 && _colorIndex == selected;
            _collider.enabled = powered && !hidden;
            _renderer.enabled = powered && (!hidden || preview);
            var properties = new MaterialPropertyBlock();
            Color color = _colorIndex < 0 ? new Color(.06f, .07f, .09f) : config.DisplayColor(config.GetColor(_colorIndex));
            if (hidden && preview)
                color = Color.Lerp(color, Color.white, .45f);
            properties.SetColor("_BaseColor", color);
            _renderer.SetPropertyBlock(properties);
        }
    }
}
