using System;
namespace DelayNoMore.LightPath
{
    /** @brief 单次程序启动的导出许可；默认关闭，不使用PlayerPrefs或配置资产。 */
    public sealed class PlaytestSession
    {
        /** @brief 启动时刻与单调时钟基准。 */
        private readonly DateTimeOffset _launch;
        /** @brief 启动单调时刻。 */
        private readonly double _clock;
        /** @brief 运行版本与输出目录。 */
        private readonly string _folder, _version;
        /** @brief 是否已处理退出。 */
        private bool _finished;
        /** @brief 本次启用区间；空值表示退出不得导出。 */
        public PlaytestRecorder Recorder { get; private set; }
        /** @brief 是否允许本次退出导出。 */
        public bool Enabled => Recorder != null && !_finished;
        /** @brief 创建本次启动元数据，不创建目录或文件。 @param folder 报告输出目录 @param version 游戏版本信息 @param launch 程序启动UTC时刻 @param clock 启动单调时钟基准 */
        public PlaytestSession(string folder, string version, DateTimeOffset launch, double clock)
        { _folder = folder; _version = version; _launch = launch; _clock = clock; }
        /** @brief 开启建立新采集区间；关闭丢弃当前采集，绝不提前导出。 @param enabled 是否允许本次会话导出 @param now 当前单调时钟秒数 @param error 写盘错误回调 */
        public void SetEnabled(bool enabled, double now, Action<string> error = null)
        {
            if (_finished || enabled == Enabled) return;
            if (enabled) Recorder = new PlaytestRecorder(_folder, _version, _launch, _clock, now, error);
            else { Recorder.Cancel(); Recorder = null; }
        }
        /** @brief 退出只结束当前记录，重复调用或未启用均不写盘。 @param now 当前单调时钟秒数 */
        public void Finish(double now)
        {
            if (_finished) return;
            _finished = true;
            Recorder?.Finish(now);
            Recorder = null;
        }
        /** @brief 子系统重置时只关闭遗留文件句柄，不恢复或导出上次数据。 */
        public void Abandon() { Recorder?.Abandon(); Recorder = null; _finished = true; }
    }
}
