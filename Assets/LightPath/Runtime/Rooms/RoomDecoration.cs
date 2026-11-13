using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 房间内纯表现图片或TMP文字；无碰撞、投影或交互能力。正文保存在房间自身。 */
    [DisallowMultipleComponent]
    public sealed class RoomDecoration : MonoBehaviour
    {
        /** @brief 图片或文字，仅用于编辑器分类。 */
        public enum Kind { Image, Text }
        /** @brief 当前表现类型。 */
        public Kind Type;
        /** @brief 跟随房间供光显示，无物理副作用。 */
        public void Refresh(bool powered)
        {
            if (TryGetComponent(out Canvas canvas)) canvas.enabled = powered;
        }
    }
}
