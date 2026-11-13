using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 投影演示的相似变换，保持物件角度和长宽深比例。 */
    public static class LightStageMath
    {
        /** @brief 构建模拟空间到演示空间的等比映射，不迁就旧光源位置。@param center 模拟屏幕画面中心 @param size 模拟画面宽高 @param frame 演示屏幕无缩放坐标系 @param displaySize 演示有效宽高 @return 只有平移、旋转和等比缩放的矩阵 */
        public static Matrix4x4 Mapping(Vector3 center, Vector2 size, Matrix4x4 frame, Vector2 displaySize)
        {
            float scale = displaySize.y / size.y;
            Quaternion rotation = Quaternion.LookRotation(frame.MultiplyVector(Vector3.forward), frame.MultiplyVector(Vector3.up));
            return Matrix4x4.TRS(frame.MultiplyPoint3x4(Vector3.zero), rotation, Vector3.one * scale)
                * Matrix4x4.Translate(-center);
        }
        /** @brief 逆变换指针射线；方向重新归一化后供模拟Collider查询。@param mapping 正向映射 @param ray 演示射线 @return 模拟射线 */
        public static Ray InverseRay(Matrix4x4 mapping, Ray ray)
        {
            Matrix4x4 inverse = mapping.inverse;
            return new Ray(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction).normalized);
        }
        /** @brief 求固定镜头朝向下容纳一个点所需的后退距离。@param localPoint 相机坐标点 @param fov 垂直视场角 @param aspect 宽高比 @param near 最近深度 @return 非负后退距离 */
        public static float Retreat(Vector3 localPoint, float fov, float aspect, float near)
        {
            float tangent = Mathf.Tan(fov * Mathf.Deg2Rad / 2);
            return Mathf.Max(0, near - localPoint.z, Mathf.Abs(localPoint.x) / (tangent * aspect) - localPoint.z,
                Mathf.Abs(localPoint.y) / tangent - localPoint.z);
        }
    }
}
