using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 将程序生命周期接到会话记录，不创建场景对象。 */
    public static class PlaytestRecording
    {
        /** @brief 本次故障日志，未授权时为空。 */
        private static PlaytestDiagnosticJournal _diagnostics;
        /** @brief 主线程诊断写入入口。 */
        public static void Diagnostic(string kind, string detail) => _diagnostics?.Write(kind, detail);
        /** @brief 错误回调可能来自非主线程，只操作线程安全文件日志。 */
        private static void CaptureLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
                _diagnostics?.Fault(type + " " + message, stack);
        }
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
            Application.logMessageReceivedThreaded -= CaptureLog;
            _diagnostics?.Close(false, false); _diagnostics = null;
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
            if (Enabled && _diagnostics == null)
            {
                try
                {
                    string environment = CaptureEnvironment();
                    Recorder.SetEnvironment(Time.realtimeSinceStartupAsDouble, environment);
                    _diagnostics = new PlaytestDiagnosticJournal(Recorder.PendingPath.Replace(".pending.csv", ".diagnostic.pending.log"), environment);
                    Application.logMessageReceivedThreaded += CaptureLog;
                    Recorder.Record(Time.realtimeSinceStartupAsDouble, "故障诊断文件", "同编号diagnostic.log", "未正常退出时保留diagnostic.pending.log，可直接读取");
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                { ReportError(exception.Message); }
            }
            if (!Enabled)
            {
                Application.logMessageReceivedThreaded -= CaptureLog;
                _diagnostics?.Close(true, false); _diagnostics = null;
                _rooms.Clear();
            }
            return Enabled;
        }
        /** @brief 在授权启用时从Unity主线程采集硬件与实际图形、显示运行参数。@return 环境快照 */
        private static string CaptureEnvironment()
        {
            Resolution display = Screen.currentResolution;
            return "版本=" + Application.version + " build=" + Application.buildGUID + " Unity=" + Application.unityVersion
                + "\n系统=" + SystemInfo.operatingSystem + " 进程位数=" + (Environment.Is64BitProcess ? 64 : 32)
                + "\nCPU=" + SystemInfo.processorType + " 逻辑处理器=" + SystemInfo.processorCount + " 频率MHz=" + SystemInfo.processorFrequency
                + " RAMMiB=" + SystemInfo.systemMemorySize
                + "\nGPU=" + SystemInfo.graphicsDeviceName + " 厂商=" + SystemInfo.graphicsDeviceVendor
                + " 设备ID=" + SystemInfo.graphicsDeviceID + " 厂商ID=" + SystemInfo.graphicsDeviceVendorID
                + " VRAMMiB=" + SystemInfo.graphicsMemorySize
                + "\n驱动=" + SystemInfo.graphicsDeviceVersion + " 后端=" + SystemInfo.graphicsDeviceType
                + " 图形多线程=" + SystemInfo.graphicsMultiThreaded + " Shader级别=" + SystemInfo.graphicsShaderLevel
                + " 最大纹理=" + SystemInfo.maxTextureSize
                + "\n显示器=" + display.width + "x" + display.height + " 刷新率Hz=" + display.refreshRateRatio.value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " 窗口=" + Screen.width + "x" + Screen.height + " 显示模式=" + Screen.fullScreenMode
                + " 焦点=" + Application.isFocused + " 后台运行=" + Application.runInBackground
                + "\n垂直同步=" + QualitySettings.vSyncCount + " 目标帧率=" + Application.targetFrameRate;
        }
        /** @brief 注册当前正式房间的性能归属。 @param surface 所属投影面 @param room 稳定房间标识 */
        public static void BindRoom(ProjectionSurface surface, string room) { if (Enabled && surface) _rooms[surface] = room; }
        /** @brief 真实重建完成时推送耗时，无重建帧不重复计数。 @param surface 所属投影面 @param milliseconds CPU耗时毫秒 */
        public static void Build(ProjectionSurface surface, double milliseconds)
        {
            if (Enabled && _rooms.TryGetValue(surface, out string room)) Recorder.Build(room, milliseconds);
        }
        /** @brief 正常进程退出或停止Play时导出，仅处理当前许可。 */
        private static void FinishSession()
        {
            Application.logMessageReceivedThreaded -= CaptureLog;
            _diagnostics?.Close(false, true); _diagnostics = null;
            _session?.Finish(Time.realtimeSinceStartupAsDouble); _rooms.Clear();
        }
        /** @brief 磁盘错误只报告一次，不中断游戏。 @param message 错误说明 */
        private static void ReportError(string message) => Debug.LogError("本次游玩记录写入失败：" + message);
    }
}
