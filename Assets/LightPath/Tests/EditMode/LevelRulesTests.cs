using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 关卡拓扑、能力锁及轮廓碰撞规则。 */
    public sealed class LevelRulesTests
    {
        /** @brief 正红色使用精确RGB，参与匹配但不允许玩家手动选择。 */
        [Test] public void RedColor_IsExactAndExcludedFromManualPalette()
        {
            var config = ScriptableObject.CreateInstance<LightGameConfig>();
            try
            {
                Assert.That(config.GetColor(LightGameConfig.RedColorIndex), Is.EqualTo(Color.red));
                Assert.That(config.DisplayColor(Color.red), Is.EqualTo(Color.red));
                Assert.That(config.Matches(Color.red, LightGameConfig.RedColorIndex), Is.True);
                Assert.That(LightGameConfig.IsManualColor(LightGameConfig.RedColorIndex), Is.False);
                for (int i = 0; i < 4; i++) Assert.That(LightGameConfig.IsManualColor(i), Is.True);
            }
            finally { Object.DestroyImmediate(config); }
        }
        /** @brief 所有已制作存档点与模板使用相同尺寸、旋转和专属红色，其他固定物件不得占用色号4。 */
        [Test] public void AuthoredCheckpoints_UseReservedRedDiamondStyle()
        {
            int checkpoints = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/LightPath" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (RoomCheckpoint checkpoint in prefab.GetComponentsInChildren<RoomCheckpoint>(true))
                {
                    checkpoints++;
                    Assert.That(checkpoint.transform.localScale.x, Is.EqualTo(.5f).Within(.0001f), path);
                    Assert.That(checkpoint.transform.localScale.y, Is.EqualTo(.5f).Within(.0001f), path);
                    Assert.That(Quaternion.Angle(checkpoint.transform.localRotation, Quaternion.Euler(0, 0, 45)), Is.LessThan(.01f), path);
                    Assert.That(checkpoint.ColorIndex, Is.EqualTo(LightGameConfig.RedColorIndex), path);
                    Assert.That(checkpoint.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.red), path);
                }
                foreach (ColorPlatform platform in prefab.GetComponentsInChildren<ColorPlatform>(true))
                    if (!platform.GetComponent<RoomCheckpoint>())
                        Assert.That(platform.ColorIndex, Is.Not.EqualTo(LightGameConfig.RedColorIndex), path);
                foreach (LevelExit exit in prefab.GetComponentsInChildren<LevelExit>(true))
                    Assert.That(exit.ColorIndex, Is.Not.EqualTo(LightGameConfig.RedColorIndex), path);
            }
            Assert.That(checkpoints, Is.GreaterThan(1));
        }
        /** @brief 投影定义拒绝预设色号和自定义颜色两种正红色配置，保留普通四色配置。 */
        [Test] public void ProjectionColor_ReservesPureRedForCheckpoints()
        {
            var config = ScriptableObject.CreateInstance<LightGameConfig>();
            var definition = ScriptableObject.CreateInstance<ProjectionDefinition>();
            try
            {
                definition.Configure(true, false, LightGameConfig.RedColorIndex, true, Color.white);
                Assert.That(definition.GetColor(config), Is.EqualTo(Color.gray));
                definition.Configure(true, false, -1, true, Color.red);
                Assert.That(definition.GetColor(config), Is.EqualTo(Color.gray));
                for (int index = 0; index < 4; index++)
                {
                    definition.Configure(true, false, index, true, Color.white);
                    Assert.That(definition.GetColor(config), Is.EqualTo(config.GetColor(index)));
                }
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(config);
            }
        }
        /** @brief 单向抵达坐标在房间平移和负坐标下保持相对位置。 */
        [Test] public void PortalArrival_PreservesRoomLocalPosition()
        {
            Vector2 arrival = new Vector2(-3, 7);
            Assert.That(RoomPortalLink.ArrivalWorldPosition(new Vector2(25, -8), arrival), Is.EqualTo(new Vector2(22, -1)));
            Assert.That(RoomPortalLink.ArrivalWorldPosition(Vector2.zero, arrival), Is.EqualTo(arrival));
        }
        /** @brief 实际房间3、4高台入口往返保持脚底高度，并验证双向连接。@param source 起点编号 @param target 终点编号 @param direction 方向 */
        [TestCase(3, 4, -1), TestCase(4, 3, 1)]
        public void AuthoredRooms_HighPlatformEntranceRemainsClear(int source, int target, int direction)
        {
            var level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>("Assets/LightPath/Levels/Level1.asset");
            Assert.That(level.Neighbor(source, direction > 0 ? 1 : 0), Is.EqualTo(target));
            Assert.That(level.Edge(source, direction > 0 ? 1 : 0), Is.EqualTo(ProjectionLevel.EdgeBehavior.Move));
            var from = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/LightPath/Rooms/Lv1/Level1Room{source}.prefab").GetComponent<LightRoom>();
            var to = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/LightPath/Rooms/Lv1/Level1Room{target}.prefab").GetComponent<LightRoom>();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LightPath/Prefabs/Characters/Player.prefab").GetComponent<BoxCollider2D>();
            Vector2 size = Vector2.Scale(player.size, player.transform.lossyScale);
            Vector2 position = new Vector2(direction * (9 - size.x / 2), 6 + size.y / 2);
            Bounds body = new Bounds(position, size);
            Vector2 landing = LightGame.BoundaryLanding(from.Surface.Bounds, to.Surface.Bounds, position, body, Vector2.right * direction);
            body.center += (Vector3)(landing - position);
            Assert.That(body.min.y, Is.EqualTo(6).Within(.0001f));
            foreach (ColorPlatform platform in to.Platforms)
            {
                var box = platform.GetComponent<BoxCollider2D>();
                if (box && !box.isTrigger)
                    Assert.That(ColorPlatform.BoxOverlaps(body, box.transform.localToWorldMatrix, box.offset, box.size), Is.False, platform.name);
            }
        }
        /** @brief 实际房间内的能力拾取触发区不得阻止返回二维世界。@param index 房间编号 */
        [TestCase(3), TestCase(4)]
        public void AuthoredAbilityTrigger_DoesNotBlockReturnToWorld(int index)
        {
            var room = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/LightPath/Rooms/Lv1/Level1Room{index}.prefab").GetComponent<LightRoom>();
            int triggers = 0;
            foreach (ColorPlatform platform in room.Platforms)
            {
                var collider = platform.GetComponent<Collider2D>();
                if (!collider || !collider.isTrigger) continue;
                triggers++;
                Assert.That(platform.Overlaps(new Bounds(platform.transform.position, Vector3.one)), Is.False, platform.name);
            }
            Assert.That(triggers, Is.GreaterThan(0));
        }
        /** @brief 横向切房保持站立高度，往返不得把角色压进高台。@param direction 切房方向 */
        [TestCase(-1), TestCase(1)]
        public void BoundaryLanding_PreservesHeightAndColliderOffset(int direction)
        {
            Rect from = new Rect(-9, 0, 18, 8), to = new Rect(21, 8, 18, 8);
            Vector2 position = new Vector2(direction * 8.8f, 6.5f);
            var body = new Bounds((Vector3)(position + new Vector2(.1f, .2f)), new Vector3(.55f, .95f, 1));
            Vector2 landing = LightGame.BoundaryLanding(from, to, position, body, Vector2.right * direction);
            Assert.That(landing.y, Is.EqualTo(14.5f).Within(.0001f));
            body.center += (Vector3)(landing - position);
            Assert.That(direction > 0 ? body.min.x - to.xMin : to.xMax - body.max.x, Is.EqualTo(.1f).Within(.0001f));
            Vector2 returned = LightGame.BoundaryLanding(to, from, landing, body, Vector2.left * direction);
            Assert.That(returned.y, Is.EqualTo(position.y).Within(.0001f));
        }
        /** @brief 纵向切房不压缩横向坐标。 */
        [Test] public void BoundaryLanding_PreservesHorizontalPosition()
        {
            var body = new Bounds(new Vector3(7, 7.8f, 0), Vector3.one);
            Vector2 landing = LightGame.BoundaryLanding(new Rect(-9, 0, 18, 8), new Rect(-9, 8, 18, 8), body.center, body, Vector2.up);
            Assert.That(landing.x, Is.EqualTo(7));
            Assert.That(landing.y, Is.EqualTo(8.6f).Within(.0001f));
        }
        /** @brief 使用碰撞器偏移和旋转后的轮廓，排除渲染包围盒空角。 */
        [Test] public void PlatformCollision_UsesColliderGeometry()
        {
            Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 45), Vector3.one);
            Assert.That(ColorPlatform.BoxOverlaps(new Bounds(new Vector3(1, -1, 0), Vector3.one * .1f), matrix, Vector2.zero, new Vector2(4, .2f)), Is.False);
            Assert.That(ColorPlatform.BoxOverlaps(new Bounds(new Vector3(1, 1, 0), Vector3.one * .1f), matrix, Vector2.zero, new Vector2(4, .2f)), Is.True);
            Assert.That(ColorPlatform.BoxOverlaps(new Bounds(Vector3.zero, Vector3.one), Matrix4x4.identity, Vector2.right * 3, Vector2.one), Is.False);
            Assert.That(ColorPlatform.BoxOverlaps(new Bounds(Vector3.up, Vector3.one), Matrix4x4.identity, Vector2.zero, Vector2.one), Is.False);
        }
        /** @brief 六轴能力分别解锁、过滤输入并仅在新局重置。@param axis 能力编号 */
        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void Ability_UnlocksOnceAndResets(int axis)
        {
            var state = new ProjectionAbilityState(); var ability = (ProjectionAbility)axis;
            Assert.That(state.Filter(ability, .75f), Is.Zero);
            Assert.That(state.Unlock(ability), Is.True); Assert.That(state.Unlock(ability), Is.False);
            Assert.That(state.Filter(ability, -.75f), Is.EqualTo(-.75f));
            Assert.That(state.Unlock((ProjectionAbility)6), Is.False);
            state.Reset(); Assert.That(state.Has(ability), Is.False);
        }
        /** @brief 关卡布局移动、占格冲突和悬空边界修复。 */
        [Test] public void Layout_RejectsOverlapAndRepairsDanglingEdges()
        {
            var level = ScriptableObject.CreateInstance<ProjectionLevel>();
            try
            {
                level.Rooms = new[] { new ProjectionLevel.RoomConnection() }; level.InitializeLayout();
                level.SetEdge(0, 1, ProjectionLevel.EdgeBehavior.Move);
                Assert.That(level.Neighbor(0, 1), Is.EqualTo(1)); Assert.That(level.Edge(0, 1), Is.EqualTo(ProjectionLevel.EdgeBehavior.Move));
                Assert.That(level.TryMoveRoom(1, Vector2Int.zero), Is.False);
                Assert.That(level.TryMoveRoom(1, new Vector2Int(2, 0)), Is.True);
                Assert.That(level.Edge(0, 1), Is.EqualTo(ProjectionLevel.EdgeBehavior.Block));
                Assert.That(level.FreePosition(), Is.EqualTo(Vector2Int.right));
                level.SetEdge(0, 2, ProjectionLevel.EdgeBehavior.Hazard); Assert.That(level.Edge(0, 2), Is.EqualTo(ProjectionLevel.EdgeBehavior.Hazard));
                level.SetEdge(0, 3, ProjectionLevel.EdgeBehavior.Move); Assert.That(level.Edge(0, 3), Is.EqualTo(ProjectionLevel.EdgeBehavior.Block));
            }
            finally { Object.DestroyImmediate(level); }
        }
        /** @brief 重复占格不允许被解析为有效房间。 */
        [Test] public void DuplicateRoomPosition_IsAmbiguous()
        {
            var level = ScriptableObject.CreateInstance<ProjectionLevel>();
            try { level.HasLayout = true; level.Rooms = new[] { new ProjectionLevel.RoomConnection { Position = Vector2Int.zero } }; Assert.That(level.RoomAt(Vector2Int.zero), Is.EqualTo(-1)); }
            finally { Object.DestroyImmediate(level); }
        }
        /** @brief 轮廓外但包围盒内的空角不得判作实体。@param x 横坐标 @param y 纵坐标 @param overlaps 期望 */
        [TestCase(.2f, .2f, true), TestCase(.85f, .85f, false), TestCase(2f, 2f, false)]
        public void PolygonCollision_UsesActualOutline(float x, float y, bool overlaps)
        {
            var region = new ProjectionRegion(new Bounds(new Vector3(.5f, .5f, 0), Vector3.one), Color.white, true, null, true, new[] { Vector2.zero, Vector2.right, Vector2.up });
            Assert.That(region.Contains(new Bounds(new Vector3(x, y, 0), Vector3.one * .1f)), Is.EqualTo(overlaps));
        }
    }
}
