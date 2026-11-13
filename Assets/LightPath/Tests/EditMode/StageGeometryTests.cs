using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DelayNoMore.LightPath.Tests
{
    /** @brief 不组装场景的四视角数学、配置与着色器检查。 */
    public sealed class StageGeometryTests
    {
        /** @brief 运行几何与配置回归，仅创建内存临时SO。 */
        [Test]
        public void Mapping_PreservesShapeRayAlignmentAndFraming()
        {
            LightStageConfig config = ScriptableObject.CreateInstance<LightStageConfig>();
            try
            {
                Check(config.TryValidate(out _), "默认舞台参数有效");
                Vector3 center = new Vector3(7, 4, 2);
                Vector2 size = new Vector2(20, 12), display = new Vector2(50, 30);
                Matrix4x4 frame = Matrix4x4.TRS(new Vector3(4, 25, 27), Quaternion.Euler(5, 12, -3), Vector3.one);
                Vector3 lamp = new Vector3(1, 6, -37);
                Matrix4x4 map = LightStageMath.Mapping(center, size, frame, display);
                Vector3 effectiveOrigin = map.inverse.MultiplyPoint3x4(lamp);
                Near(map.MultiplyPoint3x4(effectiveOrigin), lamp, "真实舞台光源逆变换对应");
                Vector3 axisX = map.MultiplyVector(Vector3.right), axisY = map.MultiplyVector(Vector3.up), axisZ = map.MultiplyVector(Vector3.forward);
                Check(Mathf.Abs(axisX.magnitude - axisZ.magnitude) < .0001f && Mathf.Abs(axisX.magnitude - axisY.magnitude) < .0001f, "三轴等比，不拉伸深度");
                Check(Mathf.Abs(Vector3.Dot(axisX, axisZ)) < .0001f && Mathf.Abs(Vector3.Dot(axisY, axisZ)) < .0001f, "无剪切形变");
                Near(map.MultiplyPoint3x4(center), frame.MultiplyPoint3x4(Vector3.zero), "屏幕中心对应");
                Near(map.MultiplyPoint3x4(center + new Vector3(10, 6, 0)), frame.MultiplyPoint3x4(new Vector3(25, 15, 0)), "画面边角对应");
                Vector3 node = new Vector3(5, 6, -4);
                Vector3 projection = effectiveOrigin + (node - effectiveOrigin) * ((center.z - effectiveOrigin.z) / (node.z - effectiveOrigin.z));
                Vector3 a = map.MultiplyPoint3x4(node) - lamp, b = map.MultiplyPoint3x4(projection) - lamp;
                Check(Vector3.Cross(a.normalized, b.normalized).magnitude < .0001f, "物件与实际屏幕投影保持共线");
                Ray stageRay = new Ray(new Vector3(30, 29, -15), (map.MultiplyPoint3x4(node) - new Vector3(30, 29, -15)).normalized);
                Ray simulation = LightStageMath.InverseRay(map, stageRay);
                Check(Vector3.Cross((node - simulation.origin).normalized, simulation.direction).magnitude < .0001f, "点击射线准确回到原模拟物件");
                foreach (float aspect in new[] { 4f / 3, 16f / 9, 9f / 16 })
                {
                    Vector3 point = new Vector3(20, -12, -1);
                    float retreat = LightStageMath.Retreat(point, 55, aspect, .3f);
                    float z = point.z + retreat;
                    float tangent = Mathf.Tan(55 * Mathf.Deg2Rad / 2);
                    Check(z >= .2999f && Mathf.Abs(point.x) <= z * tangent * aspect + .001f && Mathf.Abs(point.y) <= z * tangent + .001f, "多宽高比取景容纳边界");
                }
                config.DisplayLayer = 9;
                Check(!config.TryValidate(out _), "拒绝占用原物理层");
                config.DisplayLayer = 8;
                config.ScreenLayer = 8;
                Check(!config.TryValidate(out _), "拒绝屏幕与物件共层导致2D视野遮挡");
                config.ScreenLayer = 11;
                config.ScreenSize = new Vector2(50, 35);
                Check(!config.TryValidate(out _), "拒绝拉伸二维画面的宽高比");
                config.ScreenSize = new Vector2(50, 28.125f);
                config.TransitionSeconds = float.NaN;
                Check(!config.TryValidate(out _), "拒绝非有限过渡时间");
                Shader shader = Shader.Find("Light Path/Repair Stage");
                Check(shader && !ShaderUtil.ShaderHasError(shader), "顶面着色器导入无错误");
                Debug.Log("LIGHT_STAGE_CHECKS_PASS：光源/屏幕映射、共线投影、逆向点击、取景边界、配置校验、顶面Shader通过；场景拼装和画面验收另行执行。");
            }
            catch (Exception)
            {
                
                throw;
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }
        /** @brief 检查空间点误差。@param a 实际 @param b 预期 @param message 内容 */
        private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < .001f, message);
        /** @brief 失败时中止并定位。@param value 条件 @param message 内容 */
        private static void Check(bool value, string message)
        {
            Assert.That(value, Is.True, message);
        }
    }
}
