using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 房间文案仅保存文案表键，不再保存另一份本地文本。 */
    [CreateAssetMenu(menuName = "Light Path/Room Definition")]
    public sealed class LightRoomDefinition : ScriptableObject
    {
        /** @brief 文案表中的标题标识；HUD房间编号独立按关卡顺序显示。 */
        [SerializeField, Tooltip("游戏文案表中的标题键")] private string _titleKey = "Room.Default.Title";
        /** @brief 文案表中的教学提示标识，空提示也在文案表维护。 */
        [SerializeField, Tooltip("游戏文案表中的提示键")] private string _hintKey = "Room.Default.Hint";
        /** @brief 获取标题。 */
        public string Title => GameText.Get(_titleKey);
        /** @brief 获取教学提示。 */
        public string Hint => GameText.Get(_hintKey);
        /** @brief 填写文案表键，不写入或改动共享文案表。@param titleKey 标题键 @param hintKey 提示键 */
        public void Configure(string titleKey, string hintKey)
        {
            _titleKey = titleKey;
            _hintKey = hintKey;
        }
    }
}
