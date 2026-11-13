using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 给普通物件附加本关出口能力，支持直接显隐或投影触发。 */
    public sealed class LevelExit : MonoBehaviour
    {
        /** @brief 固定出口自身颜色。 */
        [SerializeField, Range(-1, 3)] private int _color = -1;
        /** @brief 可选投影源，使用源的颜色与触发轮廓。 */
        [SerializeField] private ProjectionSource _projection;
        /** @brief 编辑器中组装的描边渲染器，使用自身材质颜色并跟随出口显隐。 */
        [SerializeField] private Renderer[] _outlineRenderers = new Renderer[0];
        /** @brief 固定出口色号，投影出口使用投影定义的颜色。 */
        public int ColorIndex => TryGetComponent(out ColorPlatform platform) ? platform.ColorIndex : _color;
        /** @brief 配置出口外观规则。 @param source 可选投影源 @param color 固定色号 */
        public void Configure(ProjectionSource source, int color)
        {
            _projection = source;
            _color = color == LightGameConfig.RedColorIndex ? -1 : color;
        }
        /** @brief 检查当前出口是否可用。 @param surface 投影面 @param bounds 角色范围 @param color 背景色 @param radius 交互距离 @return 是否可通关 */
        public bool Available(ProjectionSurface surface, Bounds bounds, int color, float radius)
        {
            if (!isActiveAndEnabled)
                return false;
            if (_projection)
                return surface.FindPortal(bounds, color) == _projection;
            return (ColorIndex < 0 || ColorIndex != color) && Vector2.Distance(bounds.center, transform.position) <= radius;
        }
        /** @brief 刷新固定出口显隐。 @param config 色表 @param color 背景 @param powered 供光状态 */
        public void Refresh(LightGameConfig config, int color, bool powered)
        {
            if (_projection)
                return;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", config.DisplayColor(config.GetColor(ColorIndex)));
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = powered && (ColorIndex < 0 || ColorIndex != color);
                renderer.SetPropertyBlock(System.Array.IndexOf(_outlineRenderers, renderer) >= 0 ? null : properties);
            }
        }
    }
}
