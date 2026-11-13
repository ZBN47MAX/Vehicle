using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 将静态模型真实三角面纳入投影，Collider仅用于拾取及无模型逻辑源。 */
    public sealed class ProjectionSource : MonoBehaviour
    {
        /** @brief 光学和操作配置。 */
        [SerializeField, Tooltip("模型投影配置 SO")] private ProjectionDefinition _definition;
        /** @brief 投影采样使用的所有模型碰撞。 */
        private Collider[] _shapes;
        /** @brief 与编辑器共用的真实模型投影几何。 */
        private ProjectionMeshGeometry _geometry;
        /** @brief 获取当前模型几何，采样前由Surface刷新。 */
        public ProjectionMeshGeometry Geometry => _geometry ??= new ProjectionMeshGeometry(this);
        /** @brief 更新子模型变换及网格替换，通知Surface重建旧、新范围。@return 是否改变 */
        public bool RefreshGeometry() => Geometry.Refresh();
        /** @brief 程序修改同一Mesh的顶点后显式清除几何缓存，再调用Surface.Invalidate。 */
        public void InvalidateGeometry() => _geometry = null;
        /** @brief 模型表现缓存。 */
        private Renderer[] _renderers;
        /** @brief 材质属性缓存。 */
        private MaterialPropertyBlock _properties;
        /** @brief 表现状态是否初始化。 */
        private bool _presentationReady;
        /** @brief 上次可见状态。 */
        private bool _lastVisible;
        /** @brief 上次选择状态。 */
        private bool _lastSelected;
        /** @brief 初始位置。 */
        private Vector3 _initialPosition;
        /** @brief 初始旋转。 */
        private Quaternion _initialRotation;
        /** @brief 是否保存初始姿态。 */
        private bool _initialized;
        /** @brief 获取定义。 */
        public ProjectionDefinition Definition => _definition;
        /** @brief 查询鼠标射线的最近模型表面。 @param ray 鼠标射线 @param distance 最近距离 @return 是否命中 */
        public bool TryHit(Ray ray, out float distance)
        {
            if (_shapes == null)
                Initialize();
            distance = float.PositiveInfinity;
            foreach (Collider shape in _shapes)
                if (shape && shape.enabled && shape.Raycast(ray, out RaycastHit hit, float.MaxValue))
                    distance = Mathf.Min(distance, hit.distance);
            return !float.IsPositiveInfinity(distance);
        }
        /** @brief 使用世界位姿编辑，夹紧到SO许可范围。 @param position 新世界位置 @param rotation 新世界旋转 */
        public void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            Initialize();
            if (!_definition || !_definition.Movable)
                return;
            Vector3 local = transform.parent ? transform.parent.InverseTransformPoint(position) : position;
            Vector3 offset = local - _initialPosition, range = _definition.Travel;
            transform.localPosition = _initialPosition + new Vector3(Mathf.Clamp(offset.x, -range.x, range.x), Mathf.Clamp(offset.y, -range.y, range.y), Mathf.Clamp(offset.z, -range.z, range.z));
            transform.rotation = rotation;
        }
        /** @brief 初始化配置。 @param definition 定义 */
        public void Configure(ProjectionDefinition definition)
        {
            _definition = definition;
        }
        /** @brief 缓存形状和重置状态。 */
        public void Initialize()
        {
            if (_shapes == null)
                _shapes = GetComponentsInChildren<Collider>(true);
            if (_initialized)
                return;
            _initialPosition = transform.localPosition;
            _initialRotation = transform.localRotation;
            _initialized = true;
        }
        /** @brief 检查光线穿过本模型。 @param ray 世界光线 @param distance 最大距离 @return 是否相交 */
        public bool Intersects(Ray ray, float distance)
        {
            if (Geometry.HasMeshes)
                return Geometry.Intersects(ray, distance);
            if (_shapes == null)
                Initialize();
            foreach (Collider shape in _shapes)
            {
                if (shape && shape.enabled && shape.Raycast(ray, out _, distance))
                    return true;
            }
            return false;
        }
        /** @brief 移动并旋转模型。 @param movement 本次位移 @param angle 本次旋转角度 @param axis 旋转轴，0 为 X，1 为 Y，2 为 Z */
        public void Adjust(Vector3 movement, float angle, int axis = 2)
        {
            Initialize();
            if (!_definition || !_definition.Movable)
                return;
            Vector3 offset = transform.localPosition + movement - _initialPosition;
            Vector3 range = _definition.Travel;
            offset = new Vector3(Mathf.Clamp(offset.x, -range.x, range.x), Mathf.Clamp(offset.y, -range.y, range.y), Mathf.Clamp(offset.z, -range.z, range.z));
            transform.localPosition = _initialPosition + offset;
            Vector3 rotation = Vector3.zero;
            int rotationAxis = Mathf.Clamp(axis, 0, 2);
            rotation[rotationAxis] = _definition.AllowsRotation(rotationAxis) ? angle : 0;
            transform.Rotate(rotation, Space.Self);
        }
        /** @brief 恢复策划初始姿态。 */
        public void ResetPose()
        {
            Initialize();
            transform.localPosition = _initialPosition;
            transform.localRotation = _initialRotation;
        }
        /** @brief 控制幕后模型显示。 @param visible 是否显示 @param selected 是否选中 */
        public void SetVisible(bool visible, bool selected)
        {
            if (_presentationReady && _lastVisible == visible && _lastSelected == selected)
                return;
            _presentationReady = true;
            _lastVisible = visible;
            _lastSelected = selected;
            if (_renderers == null)
                _renderers = GetComponentsInChildren<Renderer>(true);
            if (_properties == null)
                _properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in _renderers)
            {
                renderer.enabled = visible;
                if (!renderer.sharedMaterial)
                    continue;
                Color color = renderer.sharedMaterial.GetColor("_BaseColor");
                _properties.SetColor("_BaseColor", selected ? Color.Lerp(color, Color.white, .4f) : color);
                renderer.SetPropertyBlock(_properties);
            }
        }
        /** @brief 绘制可操作范围。 */
        private void OnDrawGizmosSelected()
        {
            if (!_definition)
                return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, _definition.Travel * 2);
        }
    }
}
