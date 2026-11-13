using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 将程序生命周期接到会话记录，不创建场景对象。 */
    public static class PlaytestRecording
    {
        /** @brief 每次启动独立的许可与数据。 */
        private static PlaytestSession _session;
        /** @brief 投影面到稳定房间名，启用时由游戏注册。 */
        private static readonly Dictionary<ProjectionSurface, string> _rooms = new Dictionary<ProjectionSurface, string>();
        /** @brief 是否处于本次采集。 */
        public static bool Enabled => _session != null && _session.Enabled;
        /** @brief 当前记录实例，关闭时为空。 */
        public static PlaytestRecorder Recorder => Enabled ? _session.Recorder : null;
        /** @brief 每次Play也重置静态状态，兼容关闭Domain Reload。 */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginSession()
        {
            _session?.Abandon(); _rooms.Clear();
            double now = Time.realtimeSinceStartupAsDouble;
            _session = new PlaytestSession(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PlaytestReports"),
                Application.version + " / " + Application.buildGUID + " / Unity " + Application.unityVersion + " / " + Application.platform,
                DateTimeOffset.UtcNow.AddSeconds(-now), 0);
            Application.quitting -= FinishSession;
            Application.quitting += FinishSession;
        }
        /** @brief 只由Debug UI授权开启，写盘失败则保持关闭。@return 是否启用成功 @param enabled 是否允许本次会话导出 */
        public static bool SetEnabled(bool enabled)
        {
            if (_session == null) BeginSession();
            try { _session.SetEnabled(enabled, Time.realtimeSinceStartupAsDouble, ReportError); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { ReportError(exception.Message); }
            if (!Enabled) _rooms.Clear();
            return Enabled;
        }
        /** @brief 注册当前正式房间的性能归属。 @param surface 所属投影面 @param room 稳定房间标识 */
        public static void BindRoom(ProjectionSurface surface, string room) { if (Enabled && surface) _rooms[surface] = room; }
        /** @brief 真实重建完成时推送耗时，无重建帧不重复计数。 @param surface 所属投影面 @param milliseconds CPU耗时毫秒 */
        public static void Build(ProjectionSurface surface, double milliseconds)
        {
            if (Enabled && _rooms.TryGetValue(surface, out string room)) Recorder.Build(room, milliseconds);
        }
        /** @brief 正常进程退出或停止Play时导出，仅处理当前许可。 */
        private static void FinishSession() { _session?.Finish(Time.realtimeSinceStartupAsDouble); _rooms.Clear(); }
        /** @brief 磁盘错误只报告一次，不中断游戏。 @param message 错误说明 */
        private static void ReportError(string message) => Debug.LogError("本次游玩记录写入失败：" + message);
    }
}
