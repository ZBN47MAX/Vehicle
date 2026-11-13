using System.Collections.Generic;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 管理阶段端点并使用最近命中求解可组合偏转、反射和折射光路。 */
    public sealed class RefractionRig : MonoBehaviour
    {
        /** @brief 光学节点，不要求按路径排序。 */
        [SerializeField, Tooltip("维修板节点；新节点挂OpticalNode绑定SO，旧节点按折射界面兼容")] private Transform[] _nodes;
        /** @brief 当前起点。 */
        [SerializeField, Tooltip("当前发射标识")] private Transform _startMarker;
        /** @brief 当前出口。 */
        [SerializeField, Tooltip("当前维修出口，始终保留")] private Transform _exitMarker;
        /** @brief 光段表现。 */
        [SerializeField, Tooltip("世界坐标LineRenderer")] private LineRenderer _beam;
        /** @brief 下次目标。 */
        [SerializeField, Tooltip("下一阶段预告，与当前出口分离")] private Transform _nextMarker;
        /** @brief 共享配置。 */
        [SerializeField, Tooltip("光路参数与固定端点")] private RefractionSettings _settings;
        /** @brief 当前阶段。 */
        private int _phase;
        /** @brief 选中节点。 */
        private int _selected;
        /** @brief 预告显示状态，由时序控制，旋转不修改。 */
        private bool _warning;
        /** @brief 初始位姿缓存。 */
        private Vector3[] _initialPositions;
        /** @brief 初始旋转缓存。 */
        private Quaternion[] _initialRotations;
        /** @brief 固定阶段出口缓存。 */
        private Vector2[] _targets;
        /** @brief 求解节点缓存。 */
        private readonly List<OpticalRaySolver.Node> _geometry = new List<OpticalRaySolver.Node>();
        /** @brief 本地折点缓存。 */
        private readonly List<Vector2> _points = new List<Vector2>();
        /** @brief 是否连接。 */
        public bool Connected
        {
            get; private set;
        }
        /** @brief 获取阶段。 */
        public int Phase => _phase;
        /** @brief 获取选中节点。 */
        public int Selected => _selected;
        /** @brief 获取节点。 */
        public Transform[] Nodes => _nodes;
        /** @brief 获取下一端点预告。 */
        public string NextDescription => _settings ? GameText.Get("RefractionRig.NextDescription.001", _settings.GetStart((_phase + 1) % _settings.PhaseCount), Target((_phase + 1) % _settings.PhaseCount)) : GameText.Get("RefractionRig.NextDescription.002");
        /** @brief 兼容现有装配入口。 @param nodes 节点 @param start 起点 @param exit 出口 @param next 预告 @param beam 线 @param settings 配置 */
        public void Configure(Transform[] nodes, Transform start, Transform exit, Transform next, LineRenderer beam, RefractionSettings settings)
        {
            _nodes = nodes;
            _startMarker = start;
            _exitMarker = exit;
            _nextMarker = next;
            _beam = beam;
            _settings = settings;
        }
        /** @brief 兼容纯数学折射接口。 @param direction 入射 @param normal 法线 @param ratio 折射率比 @return 方向 */
        public static Vector2 Refract(Vector2 direction, Vector2 normal, float ratio) => OpticalRaySolver.Refract(direction, normal, ratio);
        /** @brief 记录初始布局及独立于镜子操作的目标。 */
        private void Initialize()
        {
            if (_initialPositions != null || _nodes == null || !_settings)
                return;
            _initialPositions = new Vector3[_nodes.Length];
            _initialRotations = new Quaternion[_nodes.Length];
            for (int i = 0; i < _nodes.Length; i++)
                if (_nodes[i])
                {
                    _initialPositions[i] = _nodes[i].localPosition;
                    _initialRotations[i] = _nodes[i].localRotation;
                }
            _targets = new Vector2[_settings.PhaseCount];
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_settings.HasAuthoredEndpoints)
                {
                    _targets[i] = _settings.GetExit(i);
                    continue;
                }
                Vector2 normal = Quaternion.Euler(0, 0, _settings.GetAngle(i)) * Vector2.left;
                Vector2 direction = Refract(Vector2.right, normal, 1 / _settings.Index);
                float height = _settings.GetHeight(i);
                Vector2 node = _nodes.Length > 1 && _nodes[1] ? (Vector2)_nodes[1].localPosition : new Vector2(2, 0);
                float hitX = node.x - normal.y / Mathf.Max(.0001f, Mathf.Abs(normal.x)) * Mathf.Sign(normal.x) * (height - node.y);
                _targets[i] = new Vector2(6, height + direction.y / Mathf.Max(.001f, direction.x) * (6 - hitX));
            }
        }
        /** @brief 读取固定目标。 @param phase 阶段 @return 坐标 */
        private Vector2 Target(int phase)
        {
            Initialize();
            return _targets != null ? _targets[phase % _targets.Length] : Vector2.zero;
        }
        /** @brief 恢复初始布局。 */
        public void ResetRig()
        {
            Initialize();
            _phase = 0;
            _selected = 0;
            _warning = false;
            if (_initialPositions == null)
                return;
            for (int i = 0; i < _nodes.Length; i++)
                if (_nodes[i])
                {
                    _nodes[i].localPosition = _initialPositions[i];
                    _nodes[i].localRotation = _initialRotations[i];
                }
            Recalculate();
        }
        /** @brief 按时序切换阶段。 */
        public void Advance()
        {
            if (!_settings)
                return;
            _phase = (_phase + 1) % _settings.PhaseCount;
            _warning = false;
            Recalculate();
        }
        /** @brief 兼容工具的选择入口。 */
        public void NextNode()
        {
            if (_nodes.Length > 0)
                _selected = (_selected + 1) % _nodes.Length;
        }
        /** @brief 鼠标命中后选择节点。 @param index 节点编号 */
        public void Select(int index)
        {
            if (index >= 0 && index < _nodes.Length)
                _selected = index;
        }
        /** @brief 获取节点是否可移动。 @return 是否可平移 */
        public bool CanMoveSelected() => _nodes.Length > 0 && _nodes[_selected] && _nodes[_selected].TryGetComponent(out OpticalNode node) && node.Definition && node.Definition.Movable;
        /** @brief 鼠标移动节点，约束在维修板平面。 @param world 世界位置 */
        public void MoveSelected(Vector3 world)
        {
            if (!CanMoveSelected())
                return;
            Vector3 local = transform.InverseTransformPoint(world);
            Rect bounds = _settings.BoardBounds;
            _nodes[_selected].position = transform.TransformPoint(new Vector3(Mathf.Clamp(local.x, bounds.xMin, bounds.xMax), Mathf.Clamp(local.y, bounds.yMin, bounds.yMax), 0));
            Recalculate();
        }
        /** @brief 在板面内旋转节点。 @param degrees 角度增量 */
        public void Rotate(float degrees)
        {
            if (_nodes == null || _nodes.Length == 0 || !_nodes[_selected])
                return;
            _nodes[_selected].Rotate(0, 0, degrees, Space.Self);
            Recalculate();
        }
        /** @brief 时序入口，单独更新预告状态。 @param warning 显示下一阶段预告 */
        public void Trace(bool warning)
        {
            _warning = warning;
            Recalculate();
        }
        /** @brief 重算光路，保持当前目标和预告生命周期。 */
        public void Recalculate()
        {
            Initialize();
            Connected = false;
            if (!_settings || _initialPositions == null || !_beam || !_startMarker || !_exitMarker)
                return;
            _geometry.Clear();
            foreach (Transform node in _nodes)
            {
                if (!node || !node.gameObject.activeInHierarchy)
                    continue;
                OpticalNodeDefinition rule = node.TryGetComponent(out OpticalNode component) ? component.Definition : null;
                Vector3 normal = transform.InverseTransformDirection(node.TransformDirection(Vector3.left));
                _geometry.Add(new OpticalRaySolver.Node
                {
                    Center = transform.InverseTransformPoint(node.position),
                    Normal = ((Vector2)normal).normalized,
                    HalfLength = rule ? rule.HalfLength : _settings.Aperture,
                    Kind = rule ? rule.Kind : OpticalNodeDefinition.Response.Refract,
                    Angle = rule ? rule.Deflection : 90,
                    FrontIndex = rule ? rule.FrontIndex : 1,
                    BackIndex = rule ? rule.BackIndex : _settings.Index
                });
            }
            Vector2 start = _settings.GetStart(_phase), target = Target(_phase);
            _startMarker.position = transform.TransformPoint(start);
            _exitMarker.position = transform.TransformPoint(target);
            _exitMarker.gameObject.SetActive(true);
            if (_nextMarker)
            {
                _nextMarker.position = transform.TransformPoint(Target((_phase + 1) % _settings.PhaseCount));
                _nextMarker.gameObject.SetActive(_warning);
            }
            Connected = OpticalRaySolver.Trace(start, _settings.GetDirection(_phase), target, _settings.Tolerance, _geometry, _settings.MaxSegments, _settings.MaxDistance, _points);
            _beam.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++)
                _beam.SetPosition(i, transform.TransformPoint(_points[i]));
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", Connected ? new Color(.4f, 1, .7f) : new Color(1, .6f, .2f));
            _beam.SetPropertyBlock(properties);
            for (int i = 0; i < _nodes.Length; i++)
                if (_nodes[i])
                {
                    properties.SetColor("_BaseColor", i == _selected ? new Color(1, .85f, .4f) : new Color(.3f, .75f, .9f));
                    foreach (Renderer renderer in _nodes[i].GetComponentsInChildren<Renderer>())
                        renderer.SetPropertyBlock(properties);
                }
        }
        /** @brief 显示维修板。 @param visible 是否显示 */
        public void SetVisible(bool visible)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = visible;
        }
        /** @brief 策划查看实际光段。 */
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Connected ? Color.green : Color.yellow;
            for (int i = 1; i < _points.Count; i++)
                Gizmos.DrawLine(transform.TransformPoint(_points[i - 1]), transform.TransformPoint(_points[i]));
        }
    }
}
