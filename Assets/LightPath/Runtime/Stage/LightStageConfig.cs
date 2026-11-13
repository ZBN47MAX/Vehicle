using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 四视角演示场景的共享只读表现参数，不包含输入绑定。 */
    [CreateAssetMenu(menuName = "Light Path/Stage Config")]
    public sealed class LightStageConfig : ScriptableObject
    {
        /** @brief 二维画面纹理宽度，像素。 */
        [Min(256)] public int TextureWidth = 1920;
        /** @brief 二维画面纹理高度，像素。 */
        [Min(256)] public int TextureHeight = 1080;
        /** @brief 显示屏有效画面世界宽高，必须匹配纹理宽高比。 */
        public Vector2 ScreenSize = new Vector2(50, 28.125f);
        /** @brief 顶面维修绘制区域世界宽高。 */
        public Vector2 RepairSize = new Vector2(1.8f, 1.4f);
        /** @brief 演示场景专用层索引；不与原游戏使用的9号物理层混用。 */
        [Range(0, 31)] public int DisplayLayer = 8;
        /** @brief 有效2D显示Quad专用层，关注2D时仅渲染该层，防止物件遮挡画面。 */
        [Range(0, 31)] public int ScreenLayer = 11;
        /** @brief 总览与物件调整透视垂直视场角。 */
        [Range(15, 100)] public float FieldOfView = 55;
        /** @brief 四视角平滑过渡秒数。 */
        [Min(.01f)] public float TransitionSeconds = .5f;
        /** @brief 正交取景边缘余量倍率。 */
        [Min(1)] public float FramePadding = 1.08f;
        /** @brief 聚焦屏幕时相机距平面的世界距离。 */
        [Min(.2f)] public float ScreenDistance = 2;
        /** @brief 聚焦顶面时相机距平面的世界距离。 */
        [Min(.2f)] public float RepairDistance = 3;
        /** @brief 布局坐标中的光线宽度。 */
        [Min(.001f)] public float BeamWidth = .08f;
        /** @brief 两个世界共用的选中描边颜色。 */
        public Color SelectionColor = new Color(1, .75f, .05f, 1);
        /** @brief 舞台世界中的描边宽度，二维按映射等比例换算。 */
        [Min(.001f)] public float SelectionWidth = .12f;
        /** @brief 可移动物件与门的细线标识宽度，独立于选中描边。 */
        [Min(.001f)] public float MarkerWidth = .018f;
        /** @brief 验证表现配置。@param error 首个错误 @return 是否有效 */
        public bool TryValidate(out string error)
        {
            error = "";
            if (TextureWidth < 256 || TextureHeight < 256 || TextureWidth > 8192 || TextureHeight > 8192)
                error = GameText.Get("LightStageConfig.TryValidate.001");
            else if (!Positive(ScreenSize.x) || !Positive(ScreenSize.y) || !Positive(RepairSize.x) || !Positive(RepairSize.y))
                error = GameText.Get("LightStageConfig.TryValidate.002");
            else if (Mathf.Abs(ScreenSize.x / ScreenSize.y - (float)TextureWidth / TextureHeight) > .01f)
                error = GameText.Get("LightStageConfig.TryValidate.003");
            else if (DisplayLayer < 6 || DisplayLayer > 31 || DisplayLayer == 9)
                error = GameText.Get("LightStageConfig.TryValidate.004");
            else if (ScreenLayer < 6 || ScreenLayer > 31 || ScreenLayer == 9 || ScreenLayer == DisplayLayer)
                error = GameText.Get("LightStageConfig.TryValidate.005");
            else if (!Positive(FieldOfView) || FieldOfView < 15 || FieldOfView > 100 || !Positive(TransitionSeconds)
                || !Positive(FramePadding) || FramePadding < 1 || !Positive(ScreenDistance) || !Positive(RepairDistance)
                || !Positive(BeamWidth) || !Positive(SelectionWidth) || !Positive(MarkerWidth))
                error = GameText.Get("LightStageConfig.TryValidate.006");
            return error.Length == 0;
        }
        /** @brief 检查有限正数。@param value 数值 @return 是否有效 */
        private static bool Positive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
