using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 获得后在整局解锁指定全局滤镜。 */
    public sealed class ColorAbilityKey : MonoBehaviour
    {
        /** @brief 四种能力滤镜之一。 */
        [SerializeField, Range(0, 3), Tooltip("解锁的全局色号")] private int _color;
        /** @brief 配置解锁颜色。 @param color 色号 */
        public void Configure(int color) => _color = Mathf.Clamp(color, 0, 3);
        /** @brief 获取解锁色号。 */
        public int Color => Mathf.Clamp(_color, 0, 3);
    }
}
