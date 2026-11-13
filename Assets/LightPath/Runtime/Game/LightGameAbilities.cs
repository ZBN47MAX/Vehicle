using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 投影操作能力获取、显示与整局状态。 */
    public sealed partial class LightGame
    {
        /** @brief 六轴能力锁的本局状态。 */
        private readonly ProjectionAbilityState _projectionAbilities = new ProjectionAbilityState();
        /** @brief 查询已获得的投影能力。 @param ability 能力 @return 是否已解锁 */
        public bool HasProjectionAbility(ProjectionAbility ability) => (int)ability >= 0 && (int)ability < 6 && (_debugAxes[(int)ability] ?? _projectionAbilities.Has(ability));
        /** @brief 使用玩家实际能力或调试覆盖过滤轴输入。 @param ability 能力 @param value 输入 @return 有效输入 */
        private float AbilityInput(ProjectionAbility ability, float value) => HasProjectionAbility(ability) ? value : 0;
        /** @brief 收集可接触的投影操作钥匙。 */
        private void CollectProjectionAbilities()
        {
            foreach (ProjectionAbilityKey key in Room.GetComponentsInChildren<ProjectionAbilityKey>(true))
            {
                if (!key.isActiveAndEnabled || _projectionAbilities.Has(key.Ability))
                    continue;
                ProjectionSource source = key.GetComponent<ProjectionSource>();
                ColorPlatform platform = key.GetComponent<ColorPlatform>();
                bool touching = source ? Room.Surface.FindPortal(_body.bounds, _color) == source
                    : (!platform || platform.ColorIndex < 0 || platform.ColorIndex != _color) && Vector2.Distance(_player.position, key.transform.position) < _config.CollectRadius;
                if (touching && _projectionAbilities.Unlock(key.Ability))
                {
                    RecordTelemetry("能力解锁", key.Ability.ToString(), TelemetryObject(key.transform));
                    NotifyUnlock(GameText.Get("LightGameAbilities.CollectProjectionAbilities.001") + ProjectionAbilityState.Label(key.Ability) + GameText.Get("LightGameAbilities.CollectProjectionAbilities.002"));
                }
            }
            RefreshProjectionKeys(Room);
        }
        /** @brief 同步房间内已解锁能力的道具表现与投影。 @param room 房间 */
        private void RefreshProjectionKeys(LightRoom room)
        {
            bool dirty = false;
            foreach (ProjectionAbilityKey key in room.GetComponentsInChildren<ProjectionAbilityKey>(true))
                if (key.SetCollected(_projectionAbilities.Has(key.Ability)))
                {
                    ProjectionSource source = key.GetComponent<ProjectionSource>();
                    if (source)
                    {
                        room.Surface.Invalidate(source);
                        dirty = true;
                    }
                }
            if (dirty)
                room.Surface.Rebuild();
        }
        /** @brief 为HUD组合当前键位及锁定状态。 @param ability 能力 @param action 动作 @return 提示 */
        private string AbilityHelp(ProjectionAbility ability, string action)
        {
            bool unlocked = HasProjectionAbility(ability);
            string color = unlocked ? "8DE4AC" : "FFFFFF";
            return "<color=#" + color + ">" + Label(action) + " " + ProjectionAbilityState.Label(ability)
                + (unlocked ? "" : GameText.Get("LightGameAbilities.AbilityHelp.003")) + "</color>";
        }
    }
}
