using UnityEngine;
using System.Collections.Generic;

namespace DelayNoMore.LightPath
{
    /** @brief 投影演示的相似变换，保持物件角度和长宽深比例。 */
    public static class LightStageMath
    {
        /** @brief 将一条轮廓边分解为虚线可见区间，用整数格索引保证有限次结束。@param phase 轮廓累计长度 @param length 当前边长 @param dash 虚线格长，调用者按整圈至少48分之一计算 @param intervals 复用的归一化区间输出 */
        public static void MarkerDashIntervals(double phase, double length, double dash, List<Vector2> intervals)
        {
            intervals.Clear();
            if (phase < 0 || length <= 0 || dash <= 0 || double.IsNaN(phase) || double.IsInfinity(phase)
                || double.IsNaN(length) || double.IsInfinity(length) || double.IsNaN(dash) || double.IsInfinity(dash)) return;
            double first = System.Math.Floor(phase / dash);
            double last = System.Math.Ceiling((phase + length) / dash);
            if (double.IsInfinity(first) || double.IsInfinity(last)) return;
            // 正常单边最多跨49格，64格保留浮点边界余量并限制异常输入的工作量。
            int count = (int)System.Math.Min(64, System.Math.Max(0, last - first));
            for (int step = 0; step < count; step++)
            {
                double cell = first + step;
                if (cell % 2 != 0) continue;
                double start = System.Math.Max(phase, cell * dash);
                double end = System.Math.Min(phase + length, (cell + 1) * dash);
                if (end > start)
                    intervals.Add(new Vector2((float)((start - phase) / length), (float)((end - phase) / length)));
            }
        }
        /** @brief 判断窗口宽高比是否可用于相机计算。@param aspect 宽高比 @return 有限且大于0时为真 */
        public static bool ValidAspect(float aspect) => aspect > 0 && !float.IsNaN(aspect) && !float.IsInfinity(aspect);
        /** @brief 窗口尺寸暂时失效时沿用上次有效宽高比。@param observed 当前采样 @param previous 上次有效值 @return 有效宽高比；两者均失效时使用1 */
        public static float ResolveAspect(float observed, float previous)
        {
            if (ValidAspect(observed)) return observed;
            if (ValidAspect(previous)) return previous;
            return 1f;
        }
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
