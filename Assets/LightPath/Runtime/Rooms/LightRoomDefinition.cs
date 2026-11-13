using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 房间内容、教学与光路时序配置。 */
    [CreateAssetMenu(menuName = "Light Path/Room Definition")]
    public sealed class LightRoomDefinition : ScriptableObject
    {
        /** @brief 房间标题。 */
        [SerializeField, Tooltip("关卡标题")] private string _title;
        /** @brief 教学提示。 */
        [SerializeField, TextArea, Tooltip("当前谜题提示")] private string _hint;
        /** @brief 起点出口组合的切换周期。 */
        [SerializeField, Min(10), Tooltip("观看时间累计达到此秒数后切换光路组合")] private float _cycleSeconds = 40;
        /** @brief 预警秒数。 */
        [SerializeField, Min(1), Tooltip("断光前预警时长，秒")] private float _warningSeconds = 8;
        /** @brief 是否启用光路变化。 */
        [SerializeField, Tooltip("启用周期性断光修复")] private bool _opticsEnabled;
        /** @brief 获取标题。 */
        public string Title => string.IsNullOrEmpty(_titleKey) ? _title : GameText.Get(_titleKey);
        /** @brief 获取提示。 */
        public string Hint => string.IsNullOrEmpty(_hintKey) ? _hint : GameText.Get(_hintKey);
        /** @brief 统一文案表的标题标识。 */
        [SerializeField] private string _titleKey;
        /** @brief 统一文案表的教学提示标识。 */
        [SerializeField] private string _hintKey;
        /** @brief 获取周期。 */
        public float CycleSeconds => Mathf.Max(10, _cycleSeconds);
        /** @brief 获取预警。 */
        public float WarningSeconds => Mathf.Clamp(_warningSeconds, 1, CycleSeconds - 1);
        /** @brief 获取光路时序开关。 */
        public bool OpticsEnabled => _opticsEnabled;
        /** @brief 填写关卡配置。 @param title 标题 @param hint 提示 @param optics 光路变化 */
        public void Configure(string title, string hint, bool optics)
        {
            _title = title;
            _hint = hint;
            _opticsEnabled = optics;
        }
    }
}
