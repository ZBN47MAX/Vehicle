using TMPro;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 初始化静态文案并统一 TMP 字体；动态 HUD 内容继续由玩法更新。 */
    [DisallowMultipleComponent]
    public sealed class GameTextLabel : MonoBehaviour
    {
        /** @brief 文案标识；为空时仅绑定字体，不覆盖内容。 */
        public string Key;
        /** @brief 首次加载文案，保留动态 HUD 的后续更新。 */
        private void Awake()
        {
            if (!TryGetComponent(out TMP_Text text)) return;
            GameText.ApplyFont(text);
            if (!string.IsNullOrEmpty(Key)) text.text = GameText.Get(Key);
        }
        /** @brief 未激活界面再次显示时使用统一字体，不重置动态文字。 */
        private void OnEnable() => GameText.ApplyFont(GetComponent<TMP_Text>());
    }
}
