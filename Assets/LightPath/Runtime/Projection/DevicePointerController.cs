using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DelayNoMore.LightPath
{
    /** @brief 仅从统一UIS动作读取鼠标，管理抓取和右键旋转，不创建场景对象。 */
    public sealed class DevicePointerController
    {
        /** @brief 捕获的模型。 */
        private ProjectionSource _source;
        /** @brief 捕获的光学板。 */
        private RefractionRig _rig;
        /** @brief 鼠标与对象抓取偏移。 */
        private Vector3 _offset;
        /** @brief 当前拖动平面。 */
        private Plane _plane;
        /** @brief 是否旋转捕获。 */
        private bool _rotating;
        /** @brief 获取当前是否捕获鼠标。 */
        public bool Captured => _source || _rig;
        /** @brief 获取选中模型编号。 */
        public int Selected
        {
            get; private set;
        }
        /** @brief 释放捕获。 */
        public void Cancel()
        {
            _source = null;
            _rig = null;
        }
        /** @brief 求维修板上的最近屏幕线段，用于没有3D碰撞器的旧镜子。 @param camera 相机 @param pointer 指针 @param rig 板 @param radius 像素容差 @return 编号 */
        private static int PickNode(Camera camera, Vector2 pointer, RefractionRig rig, float radius)
        {
            int result = -1;
            float nearest = radius;
            for (int i = 0; i < rig.Nodes.Length; i++)
            {
                Transform node = rig.Nodes[i];
                if (!node)
                    continue;
                OpticalNodeDefinition rule = node.TryGetComponent(out OpticalNode optical) ? optical.Definition : null;
                float length = rule ? rule.HalfLength : 2.5f;
                Vector3 a = camera.WorldToScreenPoint(node.position - node.up * length), b = camera.WorldToScreenPoint(node.position + node.up * length);
                if (a.z <= 0 || b.z <= 0)
                    continue;
                Vector2 edge = (Vector2)(b - a);
                float t = Mathf.Clamp01(Vector2.Dot(pointer - (Vector2)a, edge) / Mathf.Max(.001f, edge.sqrMagnitude));
                float distance = Vector2.Distance(pointer, (Vector2)a + edge * t);
                if (distance < nearest)
                {
                    nearest = distance;
                    result = i;
                }
            }
            return result;
        }
        /** @brief 将鼠标位置映射到虚拟旋转球。 @param point 相对中心像素 @param radius 球半径像素 @return 单位球面向量 */
        public static Vector3 ArcPoint(Vector2 point, float radius)
        {
            Vector2 plane = point / Mathf.Max(1, radius);
            if (plane.sqrMagnitude >= 1)
            {
                plane.Normalize();
                return new Vector3(plane.x, plane.y, 0);
            }
            return new Vector3(plane.x, plane.y, Mathf.Sqrt(1 - plane.sqrMagnitude));
        }
        /** @brief 更新鼠标编辑并返回几何是否变化。 @param actions 唯一输入资产 @param camera 相机 @param room 房间 @param repair 是否光学板 @param config 灵敏度 @return 是否修改对象 */
        public bool Tick(InputActionAsset actions, Camera camera, LightRoom room, bool repair, LightGameConfig config)
        {
            if ((_source && !_source.gameObject.activeInHierarchy) || (_rig && (!_rig.gameObject.activeInHierarchy || _rig.Nodes.Length == 0 || !_rig.Nodes[_rig.Selected])))
                Cancel();
            InputAction drag = actions.FindAction("Light/Drag", true), rotate = actions.FindAction("Light/PointerRotate", true);
            Vector2 pointer = actions.FindAction("Light/Pointer", true).ReadValue<Vector2>();
            bool released = Captured && !(_rotating ? rotate.IsPressed() : drag.IsPressed());
            if (released)
            {
                Cancel();
                return true;
            }
            if (!Captured && (drag.WasPressedThisFrame() || rotate.WasPressedThisFrame()))
            {
                if (EventSystem.current && EventSystem.current.IsPointerOverGameObject())
                    return false;
                _rotating = rotate.WasPressedThisFrame();
                Ray ray = camera.ScreenPointToRay(pointer);
                Vector3 position;
                if (repair)
                {
                    int index = PickNode(camera, pointer, room.Rig, config.PickPixels);
                    if (index < 0)
                        return false;
                    _rig = room.Rig;
                    _rig.Select(index);
                    position = _rig.Nodes[index].position;
                    _plane = new Plane(_rig.transform.forward, position);
                }
                else
                {
                    float nearest = float.PositiveInfinity;
                    for (int i = 0; i < room.Surface.Sources.Count; i++)
                    {
                        ProjectionSource source = room.Surface.Sources[i];
                        if (source && source.Definition && source.Definition.Movable && source.TryHit(ray, out float distance) && distance < nearest)
                        {
                            nearest = distance;
                            _source = source;
                            Selected = i;
                        }
                    }
                    if (!_source)
                        return false;
                    position = _source.transform.position;
                    _plane = new Plane(camera.transform.forward, position);
                }
                _offset = _plane.Raycast(ray, out float hit) ? position - ray.GetPoint(hit) : Vector3.zero;
            }
            if (!Captured)
                return false;
            if (_rotating)
            {
                Vector2 delta = actions.FindAction("Light/PointerDelta", true).ReadValue<Vector2>();
                if (delta.sqrMagnitude < .001f)
                    return false;
                if (_rig)
                    _rig.Rotate(-delta.x * config.PointerRotation);
                else
                {
                    Vector2 center = camera.WorldToScreenPoint(_source.transform.position);
                    Vector3 before = ArcPoint(pointer - delta - center, config.PickPixels * 4), after = ArcPoint(pointer - center, config.PickPixels * 4);
                    Quaternion change = Quaternion.SlerpUnclamped(Quaternion.identity, Quaternion.FromToRotation(before, after), config.PointerRotation / .3f);
                    Quaternion rotation = camera.transform.rotation * change * Quaternion.Inverse(camera.transform.rotation) * _source.transform.rotation;
                    _source.SetWorldPose(_source.transform.position, rotation);
                }
                return true;
            }
            Ray cursor = camera.ScreenPointToRay(pointer);
            if (_source)
            {
                float scroll = actions.FindAction("Light/Scroll", true).ReadValue<Vector2>().y;
                Vector3 origin = _source.transform.position + camera.transform.forward * scroll * config.ScrollDepth;
                if (Vector3.Dot(origin - camera.transform.position, camera.transform.forward) > camera.nearClipPlane + .1f)
                    _plane = new Plane(camera.transform.forward, origin);
            }
            if (!_plane.Raycast(cursor, out float amount))
                return false;
            Vector3 target = cursor.GetPoint(amount) + _offset;
            if (_rig)
            {
                Vector3 old = _rig.Nodes[_rig.Selected].position;
                _rig.MoveSelected(target);
                return old != _rig.Nodes[_rig.Selected].position;
            }
            Vector3 previous = _source.transform.position;
            if (room.Surface.ProjectionOrigin.z < room.Surface.transform.position.z)
                target.z = Mathf.Clamp(target.z, room.Surface.ProjectionOrigin.z + .1f, room.Surface.transform.position.z - .1f);
            _source.SetWorldPose(target, _source.transform.rotation);
            return previous != _source.transform.position;
        }
    }
}
