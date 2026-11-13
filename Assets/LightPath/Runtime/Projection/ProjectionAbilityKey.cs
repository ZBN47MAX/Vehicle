using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 拾取后解锁一种投影操作，可布设为固定2D道具或3D投影道具。 */
    [DisallowMultipleComponent]
    public sealed class ProjectionAbilityKey : MonoBehaviour
    {
        /** @brief 本道具解锁的操作能力。 */
        [SerializeField, Tooltip("整局保留的独立投影操作能力")] private ProjectionAbility _ability;
        /** @brief 是否因解锁而隐藏。 */
        private bool _hidden;
        /** @brief 隐藏前的自主启用状态。 */
        private bool _wasActive;
        /** @brief 所需解锁能力。 */
        public ProjectionAbility Ability => _ability;
        /** @brief 编辑器配置能力。 @param ability 六种能力之一 */
        public void Configure(ProjectionAbility ability) => _ability = ability;
        /** @brief 根据整局状态隐藏道具，新游戏恢复原启用状态。 @param collected 是否已解锁 @return 是否发生可见性变化 */
        public bool SetCollected(bool collected)
        {
            if (collected == _hidden)
                return false;
            if (collected)
            {
                _wasActive = gameObject.activeSelf;
                gameObject.SetActive(false);
            }
            else
                gameObject.SetActive(_wasActive);
            _hidden = collected;
            return true;
        }
    }
}
