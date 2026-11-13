using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 同一关卡内房间连接及两种镜头转场。 */
    public sealed partial class LightGame
    {
        /** @brief 实例对应关卡索引。 */
        private int[] _roomLevels;
        /** @brief 关卡首房间实例索引。 */
        private int[] _levelFirst;
        /** @brief 房间是否已初始化。 */
        private bool[] _visited;
        /** @brief 正在转场。 */
        private bool _transitioning;
        /** @brief 防止边界连续往返。 */
        private float _roomTravelUntil;
        /** @brief 创建各关卡的房间实例。 */
        private void CreateLevelRooms()
        {
            var rooms = new List<LightRoom>();
            var levels = new List<int>();
            _levelFirst = new int[_levels.Length];
            for (int level = 0; level < _levels.Length; level++)
            {
                _levelFirst[level] = rooms.Count;
                rooms.Add(Instantiate(_levels[level].RoomPrefab, transform));
                levels.Add(level);
                foreach (ProjectionLevel.RoomConnection entry in _levels[level].Rooms)
                {
                    if (entry == null || !entry.Prefab || !entry.Prefab.Surface || !entry.Prefab.Definition)
                        throw new System.InvalidOperationException(GameText.Get("LightGameRooms.CreateLevelRooms.001"));
                    rooms.Add(Instantiate(entry.Prefab, transform));
                    levels.Add(level);
                }
            }
            _rooms = rooms.ToArray();
            _roomLevels = levels.ToArray();
            _visited = new bool[_rooms.Length];
            for (int i = 0; i < _rooms.Length; i++)
            {
                _rooms[i].Surface.BindConfig(_levels[_roomLevels[i]].Config);
                _rooms[i].gameObject.SetActive(false);
            }
        }
        /** @brief 查找同关卡目标实例。 @param local 房间编号 @return 实例索引或-1 */
        private int TargetRoom(int local)
        {
            int level = _roomLevels[_roomIndex];
            int count = 1 + _levels[level].Rooms.Length;
            return local >= 0 && local < count ? _levelFirst[level] + local : -1;
        }
        /** @brief 获取当前投影矩形。 @param room 房间 @return 世界平面范围 */
        private static Rect RoomRect(LightRoom room) => room.Surface.Bounds;
        /** @brief 在合法移动范围内修正完整投影，失败由调用者恢复姿态。 @param source 操作对象 @return 是否满足相连边留空 */
        private bool CorrectProjectionClearance(ProjectionSource source)
        {
            ProjectionLevel level = _levels[_roomLevels[_roomIndex]];
            int local = _roomIndex - _levelFirst[_roomLevels[_roomIndex]];
            Rect bounds = RoomRect(Room);
            float margin = (Noclip ? _noclipShape.size.y : _body.bounds.size.y) * _config.ConnectedEdgeClearance;
            bool left = level.Edge(local, 0) == ProjectionLevel.EdgeBehavior.Move;
            bool right = level.Edge(local, 1) == ProjectionLevel.EdgeBehavior.Move;
            bool bottom = level.Edge(local, 2) == ProjectionLevel.EdgeBehavior.Move;
            bool top = level.Edge(local, 3) == ProjectionLevel.EdgeBehavior.Move;
            if (!left && !right && !bottom && !top)
                return true;
            Vector3 light = Room.Surface.ProjectionOrigin;
            float depth = Room.Surface.transform.position.z - light.z;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Vector2 low = Vector2.one * float.NegativeInfinity;
                Vector2 high = Vector2.one * float.PositiveInfinity;
                bool found = false;
                foreach (Vector3 vertex in source.Geometry.WorldVertices())
                {
                    float fraction = (vertex.z - light.z) / depth;
                    if (fraction <= .0001f)
                        return false;
                    Vector2 projected = Room.Surface.ProjectPoint(vertex);
                    if (left) low.x = Mathf.Max(low.x, (bounds.xMin + margin - projected.x) * fraction);
                    if (right) high.x = Mathf.Min(high.x, (bounds.xMax - margin - projected.x) * fraction);
                    if (bottom) low.y = Mathf.Max(low.y, (bounds.yMin + margin - projected.y) * fraction);
                    if (top) high.y = Mathf.Min(high.y, (bounds.yMax - margin - projected.y) * fraction);
                    found = true;
                }
                if (!found || low.x > high.x || low.y > high.y)
                    return false;
                Vector3 correction = new Vector3(Mathf.Clamp(0, low.x, high.x), Mathf.Clamp(0, low.y, high.y), 0);
                if (correction.sqrMagnitude < .00000001f)
                    return true;
                source.SetWorldPose(source.transform.position + correction, source.transform.rotation);
            }
            return false;
        }
        /** @brief 自动检测四向边界连接。 @return 是否已请求换房 */
        private bool TryBoundaryTravel()
        {
            Rect bounds = RoomRect(Room);
            Bounds body = _body.bounds;
            ProjectionLevel level = _levels[_roomLevels[_roomIndex]];
            int local = _roomIndex - _levelFirst[_roomLevels[_roomIndex]];
            for (int direction = 0; direction < 4; direction++)
            {
                bool touching = direction == 0 ? body.min.x <= bounds.xMin : direction == 1 ? body.max.x >= bounds.xMax : direction == 2 ? body.min.y <= bounds.yMin : body.max.y >= bounds.yMax;
                if (!touching)
                    continue;
                ProjectionLevel.EdgeBehavior behavior = level.Edge(local, direction);
                if (behavior == ProjectionLevel.EdgeBehavior.Hazard)
                {
                    Respawn(true, "危险边界死亡");
                    return true;
                }
                if (behavior == ProjectionLevel.EdgeBehavior.Move && Time.unscaledTime >= _roomTravelUntil)
                {
                    StartCoroutine(ChangeRoom(TargetRoom(level.Neighbor(local, direction)), ProjectionLevel.Offset(direction), null));
                    if (_transitioning)
                        return true;
                }
                Vector2 position = _player.position;
                Vector2 velocity = _player.linearVelocity;
                if (direction == 0)
                {
                    position.x += bounds.xMin - body.min.x;
                    velocity.x = Mathf.Max(0, velocity.x);
                }
                if (direction == 1)
                {
                    position.x -= body.max.x - bounds.xMax;
                    velocity.x = Mathf.Min(0, velocity.x);
                }
                if (direction == 2)
                {
                    position.y += bounds.yMin - body.min.y;
                    velocity.y = Mathf.Max(0, velocity.y);
                }
                if (direction == 3)
                {
                    position.y -= body.max.y - bounds.yMax;
                    velocity.y = Mathf.Min(0, velocity.y);
                }
                _player.position = position;
                _player.linearVelocity = velocity;
                Physics2D.SyncTransforms();
                body = _body.bounds;
            }
            return false;
        }
        /** @brief 枚举已登记投影门和房间内固定二维门。@param room 房间 @return 门对象 */
        private static IEnumerable<GameObject> PortalObjects(LightRoom room)
        {
            foreach (ProjectionSource source in room.Surface.Sources)
                if (source && IsPortal(source.gameObject)) yield return source.gameObject;
            foreach (RoomPortalLink link in room.GetComponentsInChildren<RoomPortalLink>(true))
                if (!link.GetComponent<ProjectionSource>()) yield return link.gameObject;
        }
        /** @brief 检查对象是否为门。@param portal 对象 @return 是否有效类型 */
        private static bool IsPortal(GameObject portal)
        {
            var source = portal.GetComponent<ProjectionSource>();
            return source ? source.Definition && source.Definition.Portal : portal.GetComponent<RoomPortalLink>() && portal.GetComponent<Collider2D>();
        }
        /** @brief 查询合法门的单向或双向表现，包含反向连接。 @param portal 门对象 @return 是否双向 */
        public bool PortalBidirectional(GameObject portal)
        {
            RoomPortalLink link = portal.GetComponent<RoomPortalLink>();
            if (link && !link.ArrivalOnly) return !link.UseArrivalPosition && link.Bidirectional;
            int first = _levelFirst[_roomLevels[_roomIndex]];
            int local = _roomIndex - first;
            int count = _levels[_roomLevels[_roomIndex]].RoomCount;
            for (int i = first; i < first + count; i++)
                foreach (RoomPortalLink incoming in _rooms[i].GetComponentsInChildren<RoomPortalLink>(true))
                    if (!incoming.ArrivalOnly && !incoming.UseArrivalPosition && incoming.Bidirectional && incoming.TargetRoom == local && incoming.TargetPortal == portal.name)
                        return true;
            ProjectionSource source = portal.GetComponent<ProjectionSource>();
            return !link && source && source.Partner;
        }
        /** @brief 获取实际二维门位置。@param room 房间 @param portal 门 @return 位置 */
        private static Vector2 PortalPosition(LightRoom room, GameObject portal) => portal.GetComponent<ProjectionSource>() ? (Vector2)room.Surface.ProjectPoint(portal.transform.position) : (Vector2)portal.transform.position;
        /** @brief 供光、激活和颜色共同决定门是否有效。@param room 房间 @param portal 门 @return 是否可用 */
        private bool PortalAvailable(LightRoom room, GameObject portal)
        {
            if (!room.Powered || !portal.activeInHierarchy) return false;
            var source = portal.GetComponent<ProjectionSource>();
            if (source) return room.Surface.PortalActive(source, _color);
            var platform = portal.GetComponent<ColorPlatform>();
            return !platform || platform.ColorIndex < 0 || platform.ColorIndex != _color;
        }
        /** @brief 查找指定房间内唯一有效传送门。 @param room 房间 @param name 门名 @return 唯一门或空 */
        private GameObject FindRoomPortal(int room, string name)
        {
            if (room < 0 || room >= _rooms.Length)
                return null;
            GameObject found = null;
            foreach (GameObject source in PortalObjects(_rooms[room]))
                if (source && source.name == name && IsPortal(source) && !source.GetComponent<LevelExit>() && !source.GetComponent<ColorAbilityKey>() && !source.GetComponent<ProjectionAbilityKey>())
                {
                    if (found)
                        return null;
                    found = source;
                }
            return found;
        }
        /** @brief 解析显式连接或双向连接的反向入口；冲突拒绝传送。 @param portal 当前门 @return 是否由本系统处理 */
        private bool TryPortalTravel(ProjectionSource portal) => TryPortalObjectTravel(portal.gameObject);
        /** @brief 处理固定二维门和三维投影门的统一连接。@param portal 当前门 @return 是否处理 */
        private bool TryPortalObjectTravel(GameObject portal)
        {
            _telemetryPortal = TelemetryObject(portal.transform);
            RecordTelemetry("传送尝试", _telemetryPortal);
            int target = -1;
            GameObject destination = null;
            RoomPortalLink link = portal.GetComponent<RoomPortalLink>();
            if (link && !link.ArrivalOnly)
            {
                target = TargetRoom(link.TargetRoom);
                if (link.UseArrivalPosition)
                {
                    if (target < 0) { RejectPortal("LightGameRooms.TryPortalTravel.003"); return true; }
                    if (target != _roomIndex)
                        StartCoroutine(ChangeRoomTo(target, Vector2.zero, null, link.ArrivalPosition));
                    else
                    {
                        Vector2 landing = link.ResolveArrival(Room.transform.position);
                        Bounds body = _body.bounds;
                        body.center += (Vector3)(landing - _player.position);
                        Room.Surface.SetFocus(landing);
                        if (Room.Powered && Room.Surface.Bounds.Contains(body.min) && Room.Surface.Bounds.Contains(body.max) && !Room.Overlaps(body, _color, false))
                        {
                            RecordTelemetry("传送成功", _telemetryPortal, "同房间坐标落点");
                            _player.position = landing;
                            _portalUntil = Time.time + _config.PortalCooldown;
                            Physics2D.SyncTransforms();
                        }
                        else
                        {
                            Room.Surface.SetFocus(_player.position);
                            RejectPortal("LightGameRooms.TryPortalTravel.004");
                        }
                    }
                    return true;
                }
                destination = FindRoomPortal(target, link.TargetPortal);
            }
            else
            {
                int first = _levelFirst[_roomLevels[_roomIndex]];
                int count = _levels[_roomLevels[_roomIndex]].RoomCount;
                for (int i = first; i < first + count; i++)
                    foreach (RoomPortalLink incoming in _rooms[i].GetComponentsInChildren<RoomPortalLink>(true))
                        if (!incoming.ArrivalOnly && !incoming.UseArrivalPosition && incoming.Bidirectional && incoming.TargetRoom == _roomIndex - first && incoming.TargetPortal == portal.name)
                        {
                            if (destination)
                            {
                                RejectPortal("LightGameRooms.TryPortalTravel.002");
                                return true;
                            }
                            target = i;
                            destination = FindRoomPortal(i, incoming.name);
                        }
                if (!destination)
                    return false;
            }
            if (!destination || destination == portal)
            {
                RejectPortal("LightGameRooms.TryPortalTravel.003");
                return true;
            }
            if (!destination.GetComponent<ProjectionSource>())
            {
                var platform = destination.GetComponent<ColorPlatform>();
                if (!destination.activeSelf || (platform && platform.ColorIndex >= 0 && platform.ColorIndex == _color))
                {
                    RejectPortal("LightGameRooms.TryPortalTravel.004");
                    return true;
                }
            }
            if (target == _roomIndex)
            {
                Vector2 landing = PortalPosition(Room, destination);
                Bounds body = _body.bounds;
                body.center += (Vector3)(landing - _player.position);
                if (PortalAvailable(Room, destination) && !Room.Overlaps(body, _color, false))
                {
                    RecordTelemetry("传送成功", _telemetryPortal, "同房间门=" + (destination ? destination.name : "坐标落点"));
                    _player.position = landing;
                    _portalUntil = Time.time + _config.PortalCooldown;
                }
                else
                    RejectPortal("LightGameRooms.TryPortalTravel.004");
            }
            else
                StartCoroutine(ChangeRoomTo(target, Vector2.zero, destination.GetComponent<ProjectionSource>(), destination.GetComponent<ProjectionSource>() ? (Vector2?)null : (Vector2)destination.transform.position - (Vector2)_rooms[target].transform.position));
            return true;
        }
        /** @brief 明确拒绝原因同时进入事件记录，不把所有提示猜测为失败。 */
        private void RejectPortal(string key)
        {
            RecordTelemetry("传送失败", _telemetryPortal, GameText.Get(key));
            Notify(GameText.Get(key));
        }
        /** @brief 保持入口的相对位置，只在穿越方向留出角色空间。@param from 原房间 @param to 目标房间 @param position 角色位置 @param body 角色碰撞范围 @param direction 穿越方向 @return 目标角色位置 */
        public static Vector2 BoundaryLanding(Rect from, Rect to, Vector2 position, Bounds body, Vector2 direction)
        {
            Vector2 center = body.center;
            Vector2 offset = center - position;
            Vector2 landing = new Vector2(
                Mathf.Lerp(to.xMin, to.xMax, Mathf.InverseLerp(from.xMin, from.xMax, center.x)),
                Mathf.Lerp(to.yMin, to.yMax, Mathf.InverseLerp(from.yMin, from.yMax, center.y)));
            Vector2 inset = (Vector2)body.extents + Vector2.one * .1f;
            if (direction.x != 0)
            {
                landing.x = direction.x > 0 ? to.xMin + inset.x : to.xMax - inset.x;
                landing.y = Mathf.Clamp(landing.y, to.yMin + body.extents.y, to.yMax - body.extents.y);
            }
            if (direction.y != 0)
            {
                landing.y = direction.y > 0 ? to.yMin + inset.y : to.yMax - inset.y;
                landing.x = Mathf.Clamp(landing.x, to.xMin + body.extents.x, to.xMax - body.extents.x);
            }
            return landing - offset;
        }
        /** @brief 保持状态切换房间，边界平移镜头、门直接切镜。 @param target 目标索引 @param direction 边界方向 @param portal 目标门 */
        private IEnumerator ChangeRoom(int target, Vector2 direction, ProjectionSource portal)
            => ChangeRoomTo(target, direction, portal, null);
        /** @brief 使用可选房间内坐标切房，抵达点随目标房间平移。@param target 目标索引 @param direction 边界方向 @param portal 目标门 @param arrival 房间内抵达点 @return 转场协程 */
        private IEnumerator ChangeRoomTo(int target, Vector2 direction, ProjectionSource portal, Vector2? arrival)
        {
            if (_transitioning || target < 0 || target >= _rooms.Length || target == _roomIndex
                || _roomLevels[target] != _roomLevels[_roomIndex])
                yield break;
            LightRoom old = Room;
            LightRoom next = _rooms[target];
            Vector3 originalPosition = next.transform.position;
            Vector3 cameraStart = _camera.transform.position;
            float width = _camera.orthographicSize * 2 * _camera.aspect;
            float height = _camera.orthographicSize * 2;
            next.transform.position = old.transform.position + new Vector3(direction.x * width, direction.y * height, 0);
            next.gameObject.SetActive(true);
            if (_stage) _stage.ConfigureProjection(next.Surface);
            if (!_visited[target])
            {
                next.ResetRoom();
            }
            RefreshProjectionKeys(next);
            next.Surface.Invalidate();
            Rect from = RoomRect(old), to = RoomRect(next);
            Vector2 landing;
            if (arrival.HasValue)
                landing = RoomPortalLink.ArrivalWorldPosition(next.transform.position, arrival.Value);
            else if (portal)
                landing = next.Surface.ProjectPoint(portal.transform.position);
            else
            {
                landing = BoundaryLanding(from, to, _player.position, _body.bounds, direction);
            }
            next.Surface.SetFocus(landing);
            next.Surface.Rebuild();
            next.Refresh(_config, _color, false, false, 0);
            Bounds body = _body.bounds;
            body.center += (Vector3)(landing - _player.position);
            if (!next.Powered || (arrival.HasValue && (!to.Contains(body.min) || !to.Contains(body.max))) || next.Overlaps(body, _color, false) || (portal && !next.Surface.PortalActive(portal, _color)))
            {
                next.gameObject.SetActive(false);
                next.transform.position = originalPosition;
                RecordTelemetry("房间切换失败", direction == Vector2.zero ? _telemetryPortal : "房间边界", "目标=" + TelemetryRoom(target) + "；供光、落点碰撞或门状态不满足");
                Notify(GameText.Get("LightGameRooms.ChangeRoom.005"));
                yield break;
            }
            _visited[target] = true;
            _transitioning = true;
            _preview = false;
            _pointer.Cancel();
            Vector2 velocity = _player.linearVelocity;
            Time.timeScale = 0;
            Vector3 cameraEnd = cameraStart + (next.transform.position - old.transform.position);
            if (direction != Vector2.zero)
            {
                float duration = Mathf.Max(.05f, _levels[_roomLevels[_roomIndex]].SlideSeconds);
                float elapsed = 0;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    _camera.transform.position = Vector3.Lerp(cameraStart, cameraEnd, Mathf.SmoothStep(0, 1, elapsed / duration));
                    yield return null;
                }
            }
            _camera.transform.position = cameraEnd;
            RecordTelemetry(direction == Vector2.zero ? "传送成功" : "边界通行", direction == Vector2.zero ? _telemetryPortal : direction.ToString(), "目标=" + TelemetryRoom(target));
            BreakTelemetryVisit(direction == Vector2.zero ? "传送门" : "相连边界", true);
            old.gameObject.SetActive(false);
            _roomIndex = target;
            _checkpointContacts.Clear();
            _selected = 0;
            _editing = false;
            _repairFocus = false;
            _player.position = landing;
            _player.linearVelocity = velocity;
            _roomTravelUntil = Time.unscaledTime + .3f;
            _portalUntil = Time.time + _config.PortalCooldown;
            _transitioning = false;
            Physics2D.SyncTransforms();
            ApplyState();
        }
    }
}
