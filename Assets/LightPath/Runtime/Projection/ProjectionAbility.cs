namespace DelayNoMore.LightPath
{
    /** @brief 六种独立投影操作能力，编号也是运行时状态位。 */
    public enum ProjectionAbility
    {
        /** @brief 左右平移。 */
        [UnityEngine.InspectorName("X轴移动")]
        MoveX,
        /** @brief 上下平移。 */
        [UnityEngine.InspectorName("Y轴移动")]
        MoveY,
        /** @brief 深度平移。 */
        [UnityEngine.InspectorName("Z轴移动")]
        MoveZ,
        /** @brief 绕世界X轴旋转。 */
        [UnityEngine.InspectorName("X轴旋转")]
        RotateX,
        /** @brief 绕世界Y轴旋转。 */
        [UnityEngine.InspectorName("Y轴旋转")]
        RotateY,
        /** @brief 绕世界Z轴旋转。 */
        [UnityEngine.InspectorName("Z轴旋转")]
        RotateZ
    }
}
