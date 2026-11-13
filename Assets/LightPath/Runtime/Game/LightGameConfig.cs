using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 保存角色与换色的共享只读参数。 */
    [CreateAssetMenu(menuName = "Light Path/Game Config")]
    public sealed class LightGameConfig : ScriptableObject
    {
        /** @brief 存档点专用正红色色号，不属于玩家手动滤镜。 */
        public const int RedColorIndex = 4;
        /** @brief 判断是否为玩家可手动选择的色号，解锁状态另外检查。@param index 色号 @return 是否允许手动选择 */
        public static bool IsManualColor(int index) => index >= 0 && index < RedColorIndex;
        /** @brief 判断颜色是否为存档点专用的纯红色。@param color 待检查颜色 @return RGB为正红时为true */
        public static bool IsPureRed(Color color) => color.r == 1 && color.g == 0 && color.b == 0;
        /** @brief 水平移动速度，单位米每秒。 */
        [SerializeField, Tooltip("水平移动速度，米每秒"), Min(0.1f)] private float _speed = 5;
        /** @brief 跳跃初速度，单位米每秒。 */
        [SerializeField, Tooltip("跳跃初速度，米每秒"), Min(0.1f)] private float _jumpSpeed = 9;
        /** @brief 换色时的世界时间倍率。 */
        [SerializeField, Tooltip("换色慢动作倍率"), Range(0.01f, 1)] private float _slowMotion = 0.15f;
        /** @brief 未启用全局滤镜时的幕布默认色。 */
        [SerializeField, ColorUsage(false), Tooltip("幕布默认背景色，显示时沿用固定明度规则")] private Color _defaultBackgroundColor = Color.white;
        /** @brief 获取幕布默认背景色。 */
        public Color DefaultBackgroundColor => _defaultBackgroundColor;
        /** @brief 四个全局预设色。 */
        [SerializeField, Tooltip("浅蓝、金黄、浅绿、玫红四种预设色")]
        private Color[] _colors = { new Color(.35f, .75f, .95f), new Color(1, .75f, .2f), new Color(.5f, .9f, .55f), new Color(.95f, .3f, .6f) };
        /** @brief 线性 RGB 匹配距离。 */
        [SerializeField, Tooltip("线性 RGB 欧氏距离匹配容差"), Range(.001f, .3f)] private float _matchTolerance = .07f;
        /** @brief 色彩显示固定明度。 */
        [SerializeField, Tooltip("色彩显示固定线性亮度 Y，滤镜层数不改变此值"), Range(.1f, .9f)] private float _brightness = .45f;
        /** @brief 投影网格精度。 */
        [SerializeField, Tooltip("投影采样格尺寸，米，越小精度越高"), Range(.1f, .5f)] private float _cellSize = .2f;
        /** @brief 模型移动速度。 */
        [SerializeField, Tooltip("模型编辑移动速度，米每秒"), Min(.1f)] private float _editSpeed = 1.5f;
        /** @brief 模型转动速度。 */
        [SerializeField, Tooltip("模型与折射节点旋转速度，度每秒"), Min(1)] private float _rotationSpeed = 40;
        /** @brief 起跳宽限时间。 */
        [SerializeField, Tooltip("离开边缘后仍允许跳跃的秒数"), Min(0)] private float _coyoteSeconds = .1f;
        /** @brief 跳跃输入缓存。 */
        [SerializeField, Tooltip("落地前跳跃输入保存秒数"), Min(0)] private float _jumpBuffer = .12f;
        /** @brief 角色重力倍率。 */
        [SerializeField, Tooltip("角色2D重力倍率"), Min(.1f)] private float _gravityScale = 2;
        /** @brief 光片收集半径。 */
        [SerializeField, Tooltip("角色中心与光片的收集距离，米"), Min(.1f)] private float _collectRadius = .85f;
        /** @brief 出口交互半径。 */
        [SerializeField, Tooltip("角色中心与出口的交互距离，米"), Min(.1f)] private float _exitRadius = 1.5f;
        /** @brief 传送冷却时间。 */
        [SerializeField, Tooltip("传送后再次使用门的间隔，世界秒"), Min(0)] private float _portalCooldown = .5f;
        /** @brief 坠落重生高度。 */
        [SerializeField, Tooltip("角色低于此世界Y坐标时从存档点重生")] private float _deathHeight = -4;
        /** @brief 获取重力倍率。 */
        public float GravityScale => _gravityScale;
        /** @brief 获取收集半径。 */
        public float CollectRadius => _collectRadius;
        /** @brief 获取出口交互半径。 */
        public float ExitRadius => _exitRadius;
        /** @brief 获取传送冷却秒数。 */
        public float PortalCooldown => _portalCooldown;
        /** @brief 获取坠落高度。 */
        public float DeathHeight => _deathHeight;
        /** @brief 鼠标旋转灵敏度。 */
        [SerializeField, Min(.01f), Tooltip("每像素鼠标移动对应旋转角度")] private float _pointerRotation = .3f;
        /** @brief 拖动滚轮深度灵敏度。 */
        [SerializeField, Min(.0001f), Tooltip("每单位UIS滚轮增量对应的深度米数")] private float _scrollDepth = .003f;
        /** @brief 节点拾取范围。 */
        [SerializeField, Min(4), Tooltip("维修节点鼠标拾取半径，屏幕像素")] private float _pickPixels = 28;
        /** @brief 镜头跟随配置。 */
        [SerializeField, Tooltip("启用连续卷轴镜头；旧演示默认固定镜头")] private bool _scrollingCamera;
        /** @brief 观看相机高度。 */
        [SerializeField, Min(1), Tooltip("正交相机半高，世界米")] private float _cameraHalfHeight = 6.2f;
        /** @brief 镜头跟随死区。 */
        [SerializeField, Tooltip("角色在镜头中心附近移动时的XY半死区")] private Vector2 _cameraDeadZone = new Vector2(2, 1);
        /** @brief 镜头中心世界边界。 */
        [SerializeField, Tooltip("卷轴镜头中心可移动的世界XY矩形")] private Rect _cameraBounds = new Rect(0, 3.7f, 100, 0);
        /** @brief 投影块边长。 */
        [SerializeField, Min(1), Tooltip("投影缓存块边长，世界米")] private float _chunkSize = 8;
        /** @brief 活动区半径。 */
        [SerializeField, Min(4), Tooltip("围绕角色的投影激活半径，需覆盖相机视野并留预加载距离")] private float _activeRadius = 24;
        /** @brief 边界细化次数。 */
        [SerializeField, Range(0, 4), Tooltip("混合区域边界递归细分次数；内部一致区域不继续细分")] private int _edgeRefinement = 2;
        /** @brief 获取鼠标角度灵敏度。 */
        public float PointerRotation => _pointerRotation;
        /** @brief 获取深度灵敏度。 */
        public float ScrollDepth => _scrollDepth;
        /** @brief 获取拾取范围。 */
        public float PickPixels => _pickPixels;
        /** @brief 获取卷轴开关。 */
        public bool ScrollingCamera => _scrollingCamera;
        /** @brief 获取相机半高。 */
        public float CameraHalfHeight => _cameraHalfHeight;
        /** @brief 获取死区。 */
        public Vector2 CameraDeadZone => _cameraDeadZone;
        /** @brief 获取相机中心边界。 */
        public Rect CameraBounds => _cameraBounds;
        /** @brief 获取块尺寸。 */
        public float ChunkSize => Mathf.Max(1, _chunkSize);
        /** @brief 获取预加载范围。 */
        public float ActiveRadius => Mathf.Max(4, _activeRadius);
        /** @brief 获取边界细化层数。 */
        public int EdgeRefinement => Mathf.Clamp(_edgeRefinement, 0, 4);
        /** @brief 获取投影分辨率。 */
        public float CellSize => Mathf.Clamp(_cellSize, .1f, .5f);
        /** @brief 获取编辑移动速度。 */
        public float EditSpeed => _editSpeed;
        /** @brief 获取旋转速度。 */
        public float RotationSpeed => _rotationSpeed;
        /** @brief 获取离地宽限。 */
        public float CoyoteSeconds => _coyoteSeconds;
        /** @brief 获取跳跃缓存。 */
        public float JumpBuffer => _jumpBuffer;
        /** @brief 判断连续色是否匹配预设。 @param color 连续色 @param index 预设编号 @return 是否匹配 */
        public bool Matches(Color color, int index)
        {
            if (index < 0 || index > RedColorIndex)
                return false;
            Color a = color.linear;
            Color b = GetColor(index).linear;
            return new Vector3(a.r - b.r, a.g - b.g, a.b - b.b).sqrMagnitude <= _matchTolerance * _matchTolerance;
        }
        /** @brief 映射到固定显示明度。 @param color 逻辑色 @return 展示颜色 */
        public Color DisplayColor(Color color)
        {
            if (IsPureRed(color))
                return Color.red;
            Color linear = color.linear;
            float luminance = .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
            if (luminance < .00001f)
                return new Color(_brightness, _brightness, _brightness).gamma;
            Vector3 chroma = new Vector3(linear.r, linear.g, linear.b) * (_brightness / luminance) - Vector3.one * _brightness;
            float amount = 1;
            for (int i = 0; i < 3; i++)
            {
                if (chroma[i] > 0)
                    amount = Mathf.Min(amount, (1 - _brightness) / chroma[i]);
                else if (chroma[i] < 0)
                    amount = Mathf.Min(amount, -_brightness / chroma[i]);
            }
            Vector3 result = Vector3.one * _brightness + chroma * amount;
            return new Color(result.x, result.y, result.z, 1).gamma;
        }
        /** @brief 获取移动速度。 */
        public float Speed => _speed;
        /** @brief 获取跳跃速度。 */
        public float JumpSpeed => _jumpSpeed;
        /** @brief 获取慢动作倍率。 */
        public float SlowMotion => _slowMotion;
        /** @brief 获取指定预设颜色。 @param index 颜色编号 @return 配置色或白色 */
        public Color GetColor(int index) => index == RedColorIndex ? Color.red : index >= 0 && index < _colors.Length ? _colors[index] : Color.white;
    }
}
