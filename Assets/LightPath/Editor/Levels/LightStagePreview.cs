using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 从已打开或已保存的LightGame读取真实舞台几何，不写入场景和配置。 */
    internal static class LightStagePreview
    {
        /** @brief 舞台来源场景。 */
        private const string _path = "Assets/LightPath/Scenes/LightGame.unity";
        /** @brief 已读取磁盘场景的签名。 */
        private static int _savedSignature;
        /** @brief 已读取的归一化光源。 */
        private static Vector3? _savedLamp;
        /** @brief 获取当前打开的舞台，优先采用尚未保存的位置修改。@return 舞台或空 */
        private static LightStage Loaded()
        {
            Scene scene = SceneManager.GetSceneByPath(_path);
            return scene.isLoaded ? scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LightStage>(true)).FirstOrDefault() : null;
        }
        /** @brief 预览失效签名，覆盖磁盘保存和当前场景编辑。 */
        public static int Signature => Loaded() is LightStage stage ? stage.ProjectionSignature : File.GetLastWriteTimeUtc(_path).GetHashCode();
        /** @brief 将舞台光源换算到当前房间的模拟空间，仅修改非序列化缓存。@param surface 预览投影面 */
        public static void Configure(ProjectionSurface surface)
        {
            if (!surface || !surface.Configuration) return;
            LightStage stage = Loaded();
            if (stage)
            {
                stage.ConfigureProjection(surface);
                return;
            }
            int signature = Signature;
            if (_savedLamp.HasValue && signature == _savedSignature)
            {
                surface.SetStageOrigin(LightStage.SimulationCenter(surface) + _savedLamp.Value * surface.Configuration.CameraHalfHeight * 2);
                return;
            }
            Scene preview = EditorSceneManager.OpenPreviewScene(_path);
            try
            {
                stage = preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LightStage>(true)).FirstOrDefault();
                if (stage)
                {
                    stage.ConfigureProjection(surface);
                    _savedLamp = stage.NormalizedLamp;
                    _savedSignature = signature;
                }
                else surface.SetStageOrigin(null);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
