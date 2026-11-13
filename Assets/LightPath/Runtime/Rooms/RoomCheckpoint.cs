using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 接触后成为本关唯一复活标记；激活操作由游戏会话管理。 */
    public sealed class RoomCheckpoint : MonoBehaviour
    {
        /** @brief 自身色号，默认使用存档点专用正红色。 */
        [SerializeField, Range(-1, 4)] private int _colorIndex = LightGameConfig.RedColorIndex;
        /** @brief 当前节点色号。 */
        public int ColorIndex => TryGetComponent(out ColorPlatform platform) ? platform.ColorIndex : _colorIndex;
        /** @brief 刷新表现和触发区；隐藏后不产生新的存档，已有存档保留。 */
        public void Refresh(LightGameConfig config, int color, bool powered)
        {
            bool visible = powered && (ColorIndex < 0 || ColorIndex != color);
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", config.DisplayColor(config.GetColor(ColorIndex)));
            properties.SetColor("_Color", config.DisplayColor(config.GetColor(ColorIndex)));
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = visible;
                renderer.SetPropertyBlock(properties);
            }
            if (TryGetComponent(out Collider2D area)) area.enabled = visible;
        }
        /** @brief 相对标记的安全落点，可在预制体或关卡实例中编辑。 */
        [SerializeField] private Vector2 _spawnOffset = Vector2.up;
        /** @brief 获取安全复活位置。 */
        public Vector2 Spawn => (Vector2)transform.position + _spawnOffset;
        /** @brief 是否为本关当前激活的唯一存档点，换房停用根节点时保留。 */
        public bool Activated { get; private set; }
        /** @brief 由游戏会话设置当前标记的激活状态。@param activated 是否成为当前存档点 */
        internal void SetActivated(bool activated) => Activated = activated;
        /** @brief 检查标记是否可恢复，允许所属房间因换房而停用。@param room 存档所属房间 @return 标记及其房间内父节点均未被作者停用时为 true */
        internal bool AvailableForRespawn(LightRoom room)
        {
            if (!enabled || !room) return false;
            Transform node = transform;
            while (node && node != room.transform)
            {
                if (!node.gameObject.activeSelf) return false;
                node = node.parent;
            }
            return node == room.transform;
        }
        /** @brief 查询玩家是否接触存档点。@param body 玩家边界 @return 是否接触 */
        public bool Touches(Bounds body) => isActiveAndEnabled && TryGetComponent(out Collider2D area) && area.enabled && area.bounds.Intersects(body);
    }
}
