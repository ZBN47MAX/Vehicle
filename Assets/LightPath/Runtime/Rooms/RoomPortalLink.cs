using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 同一关卡内跨房间的投影传送门连接。 */
    public sealed class RoomPortalLink : MonoBehaviour
    {
        /** @brief 目标房间编号，主房间为0。 */
        [Min(0)] public int TargetRoom;
        /** @brief 目标ProjectionSource的唯一对象名称。 */
        public string TargetPortal;
        /** @brief 使用房间内二维坐标作为单向抵达点，无需另一扇门。 */
        public bool UseArrivalPosition;
        /** @brief 仅作为抵达端，允许从双向连接自动解析回程。 */
        public bool ArrivalOnly;
        /** @brief 相对目标房间根的二维抵达坐标。 */
        public Vector2 ArrivalPosition;
        /** @brief 将作者填写的房间坐标换算为世界位置。@param roomPosition 目标房间根位置 @return 世界抵达点 */
        public Vector2 ResolveArrival(Vector2 roomPosition) => ArrivalWorldPosition(roomPosition, ArrivalPosition);
        /** @brief 保持房间平移后的抵达位置一致。@param roomPosition 房间世界原点 @param arrival 房间内坐标 @return 世界坐标 */
        public static Vector2 ArrivalWorldPosition(Vector2 roomPosition, Vector2 arrival) => roomPosition + arrival;
        /** @brief 自动允许目标门返回此门，无需修改对侧房间资产。 */
        public bool Bidirectional;
    }
}
