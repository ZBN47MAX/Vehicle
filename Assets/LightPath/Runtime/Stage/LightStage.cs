using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DelayNoMore.LightPath
{
    /** @brief 手动拼装的演示舞台引用；负责画面输出、物件映射、表面点击和资源回收。 */
    public sealed partial class LightStage : MonoBehaviour
    {
        /** @brief 只读表现参数。 */
        [SerializeField] private LightStageConfig _config;
        /** @brief 玩家实际看到的演示相机，与二维捕获相机分离。 */
        [SerializeField] private Camera _viewCamera;
        /** @brief 可选的持久画面纹理，便于编辑器预览；运行时不销毁此资产。 */
        [SerializeField] private RenderTexture _worldTexture;
        /** @brief 当前输出纹理是否由本实例创建。 */
        private bool _ownsTexture;
        /** @brief 2D显示Quad的中心坐标系，局部XY为画面，局部负Z朝玩家。 */
        [SerializeField] private Transform _screenFrame;
        /** @brief 使用URP Unlit材质的2D显示Quad。 */
        [SerializeField] private Renderer _screenRenderer;
        /** @brief 2D平面入口碰撞器。 */
        [SerializeField] private Collider _screenHit;
        /** @brief 投影仪入口碰撞器。 */
        [SerializeField] private Collider _projectorHit;
        /** @brief 操作区入口碰撞器；进入调整后不用于物件选择。 */
        [SerializeField] private Collider _projectionHit;
        /** @brief 演示光源位置，用于保持显示物件与屏幕投影的共线关系。 */
        [SerializeField] private Transform _lightOrigin;
        /** @brief 顶面维修坐标系；XY贴合顶面，负Z指向外部观察方向。 */
        [SerializeField] private Transform _repairFrame;
        /** @brief 使用Light Path/Repair Stage着色器的材质。 */
        [SerializeField] private Material _repairMaterial;
        /** @brief 总览摄像机位置及朝向。 */
        [SerializeField] private Transform _overviewPose;
        /** @brief 投影调整摄像机位置及朝向。 */
        [SerializeField] private Transform _projectionPose;
        /** @brief 持久世界空间提示，始终贴在幕布上。 */
        [SerializeField] private TMP_Text _blackoutHint;
        /** @brief 设置幕布断光提示；键位文字由统一输入数据提供。@param visible 是否断光 @param message 提示 @param font 界面字体 */
        public void ShowBlackout(bool visible, string message, TMP_FontAsset font)
        {
            if (!_blackoutHint) return;
            _blackoutHint.gameObject.SetActive(visible);
            if (!visible) return;
            _blackoutHint.font = font;
            _blackoutHint.text = message;
        }
        /** @brief 当前舞台几何签名，供编辑器侦测未保存的坐标变化。 */
        public int ProjectionSignature => _screenFrame && _lightOrigin && _config
            ? _screenFrame.localToWorldMatrix.GetHashCode() ^ _lightOrigin.position.GetHashCode() ^ _config.ScreenSize.GetHashCode() : 0;
        /** @brief 以幕布高度为单位的真实光源偏移，不含任何物件形变。 */
        public Vector3 NormalizedLamp => Quaternion.Inverse(_screenFrame.rotation) * (_lightOrigin.position - _screenFrame.position) / _config.ScreenSize.y;
        /** @brief 获取二维捕获矩形在模拟空间的中心。@param surface 投影面 @return 中心 */
        public static Vector3 SimulationCenter(ProjectionSurface surface)
        {
            LightRoom room = surface.GetComponentInParent<LightRoom>(true);
            Vector3 center = (room ? room.transform.position : surface.transform.position) + new Vector3(0, 3.7f, 0);
            center.z = surface.transform.position.z;
            return center;
        }
        /** @brief 计算物件等比映射，同时将真实舞台光源换算给投影求解器。@param surface 关卡投影面 @return 物件映射 */
        public Matrix4x4 ConfigureProjection(ProjectionSurface surface)
        {
            Vector3 center = SimulationCenter(surface);
            float height = surface.Configuration.CameraHalfHeight * 2;
            Matrix4x4 mapping = LightStageMath.Mapping(center, new Vector2(height * _config.TextureWidth / _config.TextureHeight, height),
                Vector3.zero, Matrix4x4.TRS(_screenFrame.position, _screenFrame.rotation, Vector3.one), _config.ScreenSize, Vector3.zero);
            if (surface.SetStageOrigin(mapping.inverse.MultiplyPoint3x4(_lightOrigin.position)) && Application.isPlaying && surface.gameObject.activeInHierarchy)
                surface.Rebuild();
            return mapping;
        }
        /** @brief 二维捕获相机。 */
        private Camera _capture;
        /** @brief 原捕获目标，结束后恢复。 */
        private RenderTexture _previousTarget;
        /** @brief 原捕获裁剪层。 */
        private int _previousCaptureMask;
        /** @brief 原显示裁剪层。 */
        private int _previousViewMask;
        /** @brief 原演示相机位置。 */
        private Vector3 _previousViewPosition;
        /** @brief 原演示相机朝向。 */
        private Quaternion _previousViewRotation;
        /** @brief 原演示相机投影类型。 */
        private bool _previousOrthographic;
        /** @brief 原演示相机正交尺寸。 */
        private float _previousHalfHeight;
        /** @brief 原演示相机透视视角。 */
        private float _previousFov;
        /** @brief 原捕获宽高比。 */
        private float _previousAspect;
        /** @brief 原捕获相机排序，确保本帧先生成屏幕纹理。 */
        private float _previousCaptureDepth;
        /** @brief 本次运行拥有的输出纹理。 */
        private RenderTexture _texture;
        /** @brief 屏幕原属性。 */
        private MaterialPropertyBlock _previousScreen;
        /** @brief 屏幕与物件绘制时使用的临时属性。 */
        private MaterialPropertyBlock _properties;
        /** @brief 当前房间，用于缓存物件网格。 */
        private LightRoom _room;
        /** @brief 当前物件到舞台的映射。 */
        private Matrix4x4 _mapping;
        /** @brief 顶面绘制资源。 */
        private RepairStageMesh _repairMesh;
        /** @brief 缓存的物件绘制单元。 */
        private readonly List<Part> _parts = new List<Part>();
        /** @brief 镜头取景需要覆盖的世界点。 */
        private readonly List<Vector3> _framePoints = new List<Vector3>();
        /** @brief 一个源物件中的网格与材质引用。 */
        private sealed class Part
        {
            /** @brief 网格组件。 */
            public MeshFilter Filter;
            /** @brief 原渲染组件，仅从中读取属性。 */
            public Renderer Renderer;
            /** @brief 缓存的共享材质。 */
            public Material[] Materials;
        }
        /** @brief 是否已经拥有运行时资源。 */
        public bool Ready => _texture;
        /** @brief 当前观察状态。 */
        public StageView View { get; private set; } = StageView.World;
        /** @brief 获取实际演示相机的屏幕射线。@param pointer 屏幕位置 @return 世界射线 */
        public Ray PointerRay(Vector2 pointer) => _viewCamera.ScreenPointToRay(pointer);
        /** @brief 获得模拟空间的物件选择射线。@param pointer 屏幕位置 @return 模拟射线 */
        public Ray SimulationRay(Vector2 pointer) => LightStageMath.InverseRay(_mapping, PointerRay(pointer));
        /** @brief 检查用户拼装并分配绘制资源，不创建场景对象。@param capture 原二维相机 @param error 错误原因 @return 是否可运行 */
        public bool Initialize(Camera capture, out string error)
        {
            error = "";
            if (!_config || !_viewCamera || !capture || !_screenFrame || !_screenRenderer || !_screenHit || !_projectorHit
                || !_projectionHit || !_lightOrigin || !_repairFrame || !_repairMaterial || !_overviewPose || !_projectionPose)
                error = GameText.Get("LightStage.Initialize.001");
            else if (!_config.TryValidate(out error)) { }
            else if (_viewCamera == capture || (capture.targetTexture && capture.targetTexture != _worldTexture) || _viewCamera.targetTexture)
                error = GameText.Get("LightStage.Initialize.002");
            else if (_worldTexture && (_worldTexture.width != _config.TextureWidth || _worldTexture.height != _config.TextureHeight))
                error = GameText.Get("LightStage.Initialize.003");
            else if (!_viewCamera.enabled || !capture.enabled || !isActiveAndEnabled)
                error = GameText.Get("LightStage.Initialize.004");
            else if (_config.ScreenDistance <= _viewCamera.nearClipPlane || _config.RepairDistance <= _viewCamera.nearClipPlane)
                error = GameText.Get("LightStage.Initialize.005");
            else if (!_screenRenderer.sharedMaterial || !_screenRenderer.sharedMaterial.HasProperty("_BaseMap"))
                error = GameText.Get("LightStage.Initialize.006");
            else if (_repairMaterial.shader.name != "Light Path/Repair Stage")
                error = GameText.Get("LightStage.Initialize.007");
            else if (_screenHit == _projectorHit || _screenHit == _projectionHit || _projectorHit == _projectionHit)
                error = GameText.Get("LightStage.Initialize.008");
            else if (!_screenHit.enabled || !_projectorHit.enabled || !_projectionHit.enabled
                || (_screenHit.gameObject.layer != _config.DisplayLayer && _screenHit.gameObject.layer != _config.ScreenLayer)
                || _projectorHit.gameObject.layer != _config.DisplayLayer
                || _projectionHit.gameObject.layer != _config.DisplayLayer || _screenRenderer.gameObject.layer != _config.ScreenLayer)
                error = GameText.Get("LightStage.Initialize.009");
            else if (Vector3.Dot(_lightOrigin.position - _screenFrame.position, _screenFrame.forward) >= -.01f)
                error = GameText.Get("LightStage.Initialize.010");
            if (error.Length > 0)
                return false;
            if (Ready)
            {
                error = GameText.Get("LightStage.Initialize.011");
                return false;
            }
            _previousScreen ??= new MaterialPropertyBlock();
            _properties ??= new MaterialPropertyBlock();
            _capture = capture;
            _previousTarget = capture.targetTexture;
            _previousCaptureMask = capture.cullingMask;
            _previousViewMask = _viewCamera.cullingMask;
            _previousViewPosition = _viewCamera.transform.position;
            _previousViewRotation = _viewCamera.transform.rotation;
            _previousOrthographic = _viewCamera.orthographic;
            _previousHalfHeight = _viewCamera.orthographicSize;
            _previousFov = _viewCamera.fieldOfView;
            _previousAspect = capture.aspect;
            _previousCaptureDepth = capture.depth;
            _screenRenderer.GetPropertyBlock(_previousScreen);
            _ownsTexture = !_worldTexture;
            _texture = _worldTexture ? _worldTexture : new RenderTexture(_config.TextureWidth, _config.TextureHeight, 24) { name = "Light World Runtime" };
            if (!_texture.IsCreated() && !_texture.Create()) { Release(); error = GameText.Get("LightStage.Initialize.012"); return false; }
            capture.targetTexture = _texture;
            capture.aspect = (float)_config.TextureWidth / _config.TextureHeight;
            capture.depth = _viewCamera.depth - 1;
            capture.cullingMask &= ~((1 << _config.DisplayLayer) | (1 << _config.ScreenLayer));
            _viewCamera.cullingMask = (1 << _config.DisplayLayer) | (1 << _config.ScreenLayer);
            _screenRenderer.GetPropertyBlock(_properties);
            _properties.SetTexture("_BaseMap", _texture);
            _properties.SetTexture("_MainTex", _texture);
            _properties.SetColor("_BaseColor", Color.white);
            _screenRenderer.SetPropertyBlock(_properties);
            _repairMesh = new RepairStageMesh();
            return true;
        }
        /** @brief 同步映射、网格缓存和取景点。@param room 当前运行房间 @return 映射是否有效 */
        public bool Sync(LightRoom room)
        {
            if (!Ready || !room || !room.Surface.Origin)
                return false;
            _mapping = ConfigureProjection(room.Surface);
            if (_room != room)
            {
                _room = room;
                _parts.Clear();
                foreach (ProjectionSource source in room.Surface.Sources)
                    if (source)
                        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                            if (filter.sharedMesh && filter.TryGetComponent(out MeshRenderer renderer))
                                _parts.Add(new Part { Filter = filter, Renderer = renderer, Materials = renderer.sharedMaterials });
            }
            _framePoints.Clear();
            AddPlanePoints(_screenFrame, _config.ScreenSize);
            foreach (Part part in _parts)
                if (part.Filter && part.Filter.gameObject.activeInHierarchy)
                {
                    Bounds bounds = part.Filter.sharedMesh.bounds;
                    Matrix4x4 matrix = _mapping * part.Filter.transform.localToWorldMatrix;
                    for (int corner = 0; corner < 8; corner++)
                        _framePoints.Add(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                }
            return true;
        }
        /** @brief 绘制同一批模拟物件与顶面维修内容，仅提交给演示相机。@param layout 维修配置 @param repair 整局状态 */
        public void Draw(RepairLayout layout, RepairSession repair, ProjectionSource selected = null, LightGame game = null)
        {
            foreach (Part part in _parts)
                if (part.Filter && part.Renderer && part.Filter.gameObject.activeInHierarchy)
                {
                    part.Renderer.GetPropertyBlock(_properties);
                    Mesh mesh = part.Filter.sharedMesh;
                    for (int sub = 0; sub < mesh.subMeshCount && sub < part.Materials.Length; sub++)
                        if (part.Materials[sub])
                            Graphics.DrawMesh(mesh, _mapping * part.Filter.transform.localToWorldMatrix, part.Materials[sub],
                                _config.DisplayLayer, _viewCamera, sub, _properties, ShadowCastingMode.Off, false);
                }
            if (_room)
            {
                _objectMarkers ??= new ProjectionObjectMarkers();
                _objectMarkers.Draw(_room, _mapping, _viewCamera, _capture, _config, _repairMaterial, game);
            }
            if (layout && repair != null)
                _repairMesh.Draw(layout, repair, _config, _repairFrame, _repairMaterial, _viewCamera);
            if (View == StageView.Projection && selected && _room)
            {
                if (_selectionOutline == null) _selectionOutline = new ProjectionSelectionOutline();
                _selectionOutline.Draw(selected, _room.Surface, _mapping, _viewCamera, _capture, _config, _repairMaterial);
            }
        }
        /** @brief 双世界选中描边缓存。 */
        private ProjectionSelectionOutline _selectionOutline;
        /** @brief 常驻的可移动对象标识。 */
        private ProjectionObjectMarkers _objectMarkers;
        /** @brief 从总览选取最近可见区域或物件，物件被实体遮挡时不穿透选择。@param pointer 指针 @param room 房间 @param view 目标视角 @return 是否命中入口 */
        public bool TryChoose(Vector2 pointer, LightRoom room, out StageView view)
        {
            view = StageView.Overview;
            Ray ray = PointerRay(pointer);
            float nearest = float.PositiveInfinity;
            bool found = false;
            if (Physics.Raycast(ray, out RaycastHit hit, _viewCamera.farClipPlane, (1 << _config.DisplayLayer) | (1 << _config.ScreenLayer), QueryTriggerInteraction.Collide))
            {
                nearest = hit.distance;
                if (hit.collider == _screenHit) { view = StageView.World; found = true; }
                else if (hit.collider == _projectorHit) { view = StageView.Repair; found = true; }
                else if (hit.collider == _projectionHit) { view = StageView.Projection; found = true; }
            }
            Ray simulation = LightStageMath.InverseRay(_mapping, ray);
            foreach (ProjectionSource source in room.Surface.Sources)
                if (source && source.gameObject.activeInHierarchy && source.Definition && source.Definition.Movable && source.TryHit(simulation, out float distance))
                {
                    Vector3 point = _mapping.MultiplyPoint3x4(simulation.GetPoint(distance));
                    float stageDistance = Vector3.Dot(point - ray.origin, ray.direction);
                    if (stageDistance > 0 && stageDistance < nearest) { nearest = stageDistance; view = StageView.Projection; found = true; }
                }
            return found;
        }
        /** @brief 投影调整时按最近可见表面选择3D物件或幕布里的最终投影，绝不改变姿态。@param pointer 屏幕指针 @param room 当前房间 @param selectedColor 背景色 @return 源索引，未命中为负一 */
        public int PickProjectionObject(Vector2 pointer, LightRoom room, int selectedColor)
        {
            Ray ray = PointerRay(pointer);
            Ray simulation = LightStageMath.InverseRay(_mapping, ray);
            float nearest = float.PositiveInfinity;
            int result = -1;
            if (Physics.Raycast(ray, out RaycastHit hit, _viewCamera.farClipPlane, (1 << _config.DisplayLayer) | (1 << _config.ScreenLayer), QueryTriggerInteraction.Collide))
            {
                nearest = hit.distance;
                if (hit.collider == _screenHit)
                {
                    Vector3 local = Quaternion.Inverse(_screenFrame.rotation) * (hit.point - _screenFrame.position);
                    Vector2 uv = new Vector2(local.x / _config.ScreenSize.x + .5f, local.y / _config.ScreenSize.y + .5f);
                    Ray captureRay = _capture.ViewportPointToRay(uv);
                    Plane plane = new Plane(Vector3.forward, room.Surface.transform.position);
                    if (plane.Raycast(captureRay, out float distance))
                        result = room.Surface.PickSource(captureRay.GetPoint(distance), selectedColor);
                }
            }
            var sources = room.Surface.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                ProjectionSource source = sources[i];
                if (!source || !source.gameObject.activeInHierarchy || !source.Definition) continue;
                if (!source.TryHit(simulation, out float distance)) continue;
                float stageDistance = Vector3.Dot(_mapping.MultiplyPoint3x4(simulation.GetPoint(distance)) - ray.origin, ray.direction);
                if (stageDistance > 0 && stageDistance < nearest)
                {
                    nearest = stageDistance;
                    result = source.Definition.Movable ? i : -1;
                }
            }
            return result;
        }
        /** @brief 选择顶面机关。@param layout 布局 @param pointer 指针 @return 机关索引或负一 */
        public int PickRepair(RepairLayout layout, Vector2 pointer, RepairSession session = null) => RepairStageMesh.Pick(layout, _config, _repairFrame, PointerRay(pointer), session);
        /** @brief 将维修指针转换为配置平面坐标。 */
        public bool TryRepairPoint(RepairLayout layout, Vector2 pointer, out Vector2 point) => RepairStageMesh.TryPoint(layout, _config, _repairFrame, PointerRay(pointer), out point);
        /** @brief 添加矩形取景边界。@param frame 坐标系 @param size 世界宽高 */
        private void AddPlanePoints(Transform frame, Vector2 size)
        {
            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
                _framePoints.Add(frame.position + frame.right * corner.x * size.x / 2 + frame.up * corner.y * size.y / 2);
        }
        /** @brief 回收本实例拥有的运行资源并恢复相机与显示材质属性。 */
        public void Release()
        {
            if (_capture)
            {
                _capture.targetTexture = _previousTarget;
                _capture.cullingMask = _previousCaptureMask;
                _capture.aspect = _previousAspect;
                _capture.depth = _previousCaptureDepth;
                if (_viewCamera)
                {
                    _viewCamera.cullingMask = _previousViewMask;
                    _viewCamera.transform.SetPositionAndRotation(_previousViewPosition, _previousViewRotation);
                    _viewCamera.orthographic = _previousOrthographic;
                    _viewCamera.orthographicSize = _previousHalfHeight;
                    _viewCamera.fieldOfView = _previousFov;
                    _viewCamera.ResetProjectionMatrix();
                }
                if (_screenRenderer) _screenRenderer.SetPropertyBlock(_previousScreen);
            }
            _capture = null;
            if (_texture && _ownsTexture) { _texture.Release(); Destroy(_texture); }
            _texture = null;
            _ownsTexture = false;
            _repairMesh?.Dispose(); _repairMesh = null;
            _selectionOutline?.Dispose(); _selectionOutline = null;
            _objectMarkers?.Dispose(); _objectMarkers = null;
            _parts.Clear(); _room = null;
            Moving = false;
        }
        /** @brief 销毁时兜底释放资源。 */
        private void OnDestroy() => Release();
    }
}
