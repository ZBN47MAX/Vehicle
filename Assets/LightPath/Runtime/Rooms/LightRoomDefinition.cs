using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 房间内容与教学配置。 */
    [CreateAssetMenu(menuName = "Light Path/Room Definition")]
    public sealed class LightRoomDefinition : ScriptableObject
    {
        /** @brief 房间标题。 */
        [SerializeField, Tooltip("关卡标题")] private string _title;
        /** @brief 教学提示。 */
        [SerializeField, TextArea, Tooltip("当前谜题提示")] private string _hint;
        /** @brief 获取标题。 */
        public string Title => string.IsNullOrEmpty(_titleKey) ? _title : GameText.Get(_titleKey);
        /** @brief 获取提示。 */
        public string Hint => string.IsNullOrEmpty(_hintKey) ? _hint : GameText.Get(_hintKey);
        /** @brief 统一文案表的标题标识。 */
        [SerializeField] private string _titleKey;
        /** @brief 统一文案表的教学提示标识。 */
        [SerializeField] private string _hintKey;
        /** @brief 填写关卡配置。 @param title 标题 @param hint 提示 */
        public void Configure(string title, string hint)
        {
            _title = title;
            _hint = hint;
        }
    }
}
