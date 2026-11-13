using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 独立投影关卡资产，绑定房间模板和专属配置。 */
    [CreateAssetMenu(menuName = "Light Path/Projection Level")]
    public sealed class ProjectionLevel : ScriptableObject
    {
        /** @brief 关卡房间模板。 */
        public LightRoom RoomPrefab;
        /** @brief 附加房间，主房间编号0，附加房间从1开始。 */
        public RoomConnection[] Rooms = new RoomConnection[0];
        /** @brief 主房间左、右、下、上连接，-1表示无连接。 */
        [HideInInspector] public Vector4 MainNeighbors = new Vector4(-1, -1, -1, -1);
        /** @brief 镜头滑动转场秒数。 */
        [Min(.05f)] public float SlideSeconds = .45f;
        /** @brief 房间模板与相邻连接。 */
        [System.Serializable]
        public sealed class RoomConnection
        {
            /** @brief 房间预制体。 */
            public LightRoom Prefab;
            /** @brief 关卡布局格坐标，与房间内部米制网格无关。 */
            public Vector2Int Position;
            /** @brief 左右下上边界行为。 */
            public EdgeBehavior[] Edges = new EdgeBehavior[4];
            /** @brief 左右下上目标房间编号。 */
            [HideInInspector] public Vector4 Neighbors = new Vector4(-1, -1, -1, -1);
        }
        /** @brief 四向边界行为。 */
        public enum EdgeBehavior
        {
            /** @brief 阻止继续向外运动。 */
            Block,
            /** @brief 接触后死亡并复位。 */
            Hazard,
            /** @brief 前往相邻房间。 */
            Move
        }
        /** @brief 是否已采用独立关卡布局。 */
        [HideInInspector] public bool HasLayout;
        /** @brief 主房间所在关卡格。 */
        public Vector2Int MainPosition;
        /** @brief 主房间左右下上行为。 */
        public EdgeBehavior[] MainEdges = new EdgeBehavior[4];
        /** @brief 房间总数。 */
        public int RoomCount => 1 + (Rooms?.Length ?? 0);
        /** @brief 获取房间预制体。 @param index 房间编号 @return 房间模板 */
        public LightRoom Prefab(int index) => index == 0 ? RoomPrefab : Rooms[index - 1]?.Prefab;
        /** @brief 获取独立布局坐标，旧数据采用水平排列。 @param index 房间编号 @return 网格坐标 */
        public Vector2Int Position(int index) => !HasLayout ? new Vector2Int(index, 0) : index == 0 ? MainPosition : Rooms[index - 1].Position;
        /** @brief 四向单位格。 @param direction 左右下上编号 @return 格偏移 */
        public static Vector2Int Offset(int direction) => direction == 0 ? Vector2Int.left : direction == 1 ? Vector2Int.right : direction == 2 ? Vector2Int.down : Vector2Int.up;
        /** @brief 查找格内房间，重复占格视为无效。 @param position 格坐标 @return 唯一房间编号或-1 */
        public int RoomAt(Vector2Int position)
        {
            int result = -1;
            for (int i = 0; i < RoomCount; i++)
                if (Position(i) == position)
                {
                    if (result >= 0)
                        return -1;
                    result = i;
                }
            return result;
        }
        /** @brief 查询几何相邻房间。 @param index 源房间 @param direction 左右下上 @return 目标编号或-1 */
        public int Neighbor(int index, int direction) => RoomAt(Position(index) + Offset(direction));
        /** @brief 获取有效边界行为，缺邻居的移动按阻挡处理。 @param index 房间 @param direction 左右下上 @return 行为 */
        public EdgeBehavior Edge(int index, int direction)
        {
            int neighbor = Neighbor(index, direction);
            if (!HasLayout)
            {
                Vector4 old = index == 0 ? MainNeighbors : Rooms[index - 1].Neighbors;
                return neighbor >= 0 && Mathf.RoundToInt(old[direction]) == neighbor ? EdgeBehavior.Move : EdgeBehavior.Block;
            }
            EdgeBehavior[] edges = index == 0 ? MainEdges : Rooms[index - 1].Edges;
            EdgeBehavior value = edges != null && edges.Length == 4 ? edges[direction] : EdgeBehavior.Block;
            return value == EdgeBehavior.Move && neighbor < 0 ? EdgeBehavior.Block : value;
        }
        /** @brief 编辑器初始化布局；运行时不调用、不修改共享资产。 */
        public void InitializeLayout()
        {
            if (HasLayout)
                return;
            MainPosition = Vector2Int.zero;
            MainEdges = new EdgeBehavior[4];
            for (int i = 0; i < RoomCount; i++)
            {
                EdgeBehavior[] edges = new EdgeBehavior[4];
                for (int d = 0; d < 4; d++)
                    edges[d] = Edge(i, d);
                if (i == 0)
                    MainEdges = edges;
                else
                {
                    Rooms[i - 1].Position = new Vector2Int(i, 0);
                    Rooms[i - 1].Edges = edges;
                }
            }
            HasLayout = true;
        }
        /** @brief 编辑器设置边界；禁止无邻居的移动。 @param index 房间 @param direction 方向 @param value 行为 */
        public void SetEdge(int index, int direction, EdgeBehavior value)
        {
            InitializeLayout();
            if (value == EdgeBehavior.Move && Neighbor(index, direction) < 0)
                value = EdgeBehavior.Block;
            if (index == 0)
            {
                if (MainEdges == null || MainEdges.Length != 4)
                    MainEdges = new EdgeBehavior[4];
                MainEdges[direction] = value;
            }
            else
            {
                if (Rooms[index - 1].Edges == null || Rooms[index - 1].Edges.Length != 4)
                    Rooms[index - 1].Edges = new EdgeBehavior[4];
                Rooms[index - 1].Edges[direction] = value;
            }
        }
        /** @brief 编辑器移动房间并修复悬空移动边，不改变房间内容。 @param index 房间 @param position 新格位 @return 是否成功 */
        public bool TryMoveRoom(int index, Vector2Int position)
        {
            for (int i = 0; i < RoomCount; i++)
                if (i != index && Position(i) == position)
                    return false;
            InitializeLayout();
            if (index == 0)
                MainPosition = position;
            else
                Rooms[index - 1].Position = position;
            for (int i = 0; i < RoomCount; i++)
                for (int d = 0; d < 4; d++)
                    SetEdge(i, d, Edge(i, d));
            return true;
        }
        /** @brief 新房间默认放入未占用格。 @return 空闲格坐标 */
        public Vector2Int FreePosition()
        {
            int x = 0;
            while (RoomAt(new Vector2Int(x, 0)) >= 0)
                x++;
            return new Vector2Int(x, 0);
        }
        /** @brief 关卡专属参数。 */
        public LightGameConfig Config;
    }
}
