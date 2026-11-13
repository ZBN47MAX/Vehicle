namespace DelayNoMore.LightPath
{
    /** @brief 整局投影能力状态，不写回配置，不由死亡或换房重置。 */
    public sealed class ProjectionAbilityState
    {
        /** @brief 已解锁的六个能力位。 */
        private int _mask;
        /** @brief 查询能力。 @param ability 能力 @return 是否解锁 */
        public bool Has(ProjectionAbility ability) => (int)ability >= 0 && (int)ability < 6 && (_mask & (1 << (int)ability)) != 0;
        /** @brief 首次解锁能力。 @param ability 能力 @return 是否新增解锁 */
        public bool Unlock(ProjectionAbility ability)
        {
            if ((int)ability < 0 || (int)ability >= 6 || Has(ability))
                return false;
            _mask |= 1 << (int)ability;
            return true;
        }
        /** @brief 仅开始新游戏时清空。 */
        public void Reset() => _mask = 0;
        /** @brief 锁定的轴不产生输入。 @param ability 能力 @param input 动作轴值 @return 实际轴值 */
        public float Filter(ProjectionAbility ability, float input) => Has(ability) ? input : 0;
        /** @brief 能力中文名称。 @param ability 能力 @return 展示名称 */
        public static string Label(ProjectionAbility ability)
        {
            switch (ability)
            {
                case ProjectionAbility.MoveX:
                    return GameText.Get("ProjectionAbilityState.Label.001");
                case ProjectionAbility.MoveY:
                    return GameText.Get("ProjectionAbilityState.Label.002");
                case ProjectionAbility.MoveZ:
                    return GameText.Get("ProjectionAbilityState.Label.003");
                case ProjectionAbility.RotateX:
                    return GameText.Get("ProjectionAbilityState.Label.004");
                case ProjectionAbility.RotateY:
                    return GameText.Get("ProjectionAbilityState.Label.005");
                case ProjectionAbility.RotateZ:
                    return GameText.Get("ProjectionAbilityState.Label.006");
                default:
                    return GameText.Get("ProjectionAbilityState.Label.007");
            }
        }
    }
}
