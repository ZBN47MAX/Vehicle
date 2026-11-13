using System;
using System.IO;
using System.Text;
namespace DelayNoMore.LightPath
{
    /** @brief 本次授权记录的故障日志，逐条刷盘供未正常退出时直接读取。 */
    public sealed class PlaytestDiagnosticJournal
    {
        private readonly object _gate = new object();
        private readonly string _pending;
        private bool _closed;
        private long _bytes;
        private bool _limited;
        private string _lastFault;
        private int _repeated;
        /** @brief 最近一次写盘错误，不在日志回调中再次输出Unity日志。 */
        public string LastError { get; private set; }
        /** @brief 创建本次诊断文件。@param pending 仅本次文件路径 @param environment 硬件与版本基线 */
        public PlaytestDiagnosticJournal(string pending, string environment)
        {
            _pending = pending;
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            Write("记录开始", "pending 表示未写入正常退出标记，可能由崩溃、强制结束、断电或写盘失败导致。硬崩溃堆栈仍需结合Player.log与原生转储。\n" + environment);
        }
        /** @brief 可从日志线程调用，每条记录立即刷至操作系统。@param kind 记录类别 @param detail 状态或堆栈 */
        public void Write(string kind, string detail)
        {
            lock (_gate)
            {
                if (_closed) return;
                string line = DateTimeOffset.UtcNow.ToString("O") + " [" + kind + "] " + detail;
                if (_bytes >= 16 * 1024 * 1024)
                {
                    if (_limited) return;
                    _limited = true;
                    line = "诊断日志达到16MiB预算，后续明细停止。此前数据保留。";
                }
                try { File.AppendAllText(_pending, line + Environment.NewLine, new UTF8Encoding(true)); _bytes += Encoding.UTF8.GetByteCount(line) + 2; }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                { LastError = error.Message; }
            }
        }
        /** @brief 合并连续重复报错，保留首次完整堆栈并周期记录重复次数。 */
        public void Fault(string message, string stack)
        {
            lock (_gate)
            {
                string fault = message + "\n" + stack;
                if (_lastFault == fault)
                {
                    _repeated++;
                    if (_repeated % 100 == 0) Write("重复错误", "次数=" + _repeated + " " + message);
                    return;
                }
                _lastFault = fault;
                _repeated = 0;
                Write("Unity错误或异常", fault);
            }
        }
        /** @brief 正常退出、关闭授权、子系统重置三种结束方式。@param discard 是否丢弃 @param normal 是否正常结束 */
        public void Close(bool discard, bool normal)
        {
            lock (_gate)
            {
                if (_closed) return;
                if (normal) Write("记录结束", "正常退出");
                try
                {
                    _closed = true;
                    if (discard) File.Delete(_pending);
                    else if (normal) File.Move(_pending, _pending.Replace(".diagnostic.pending.log", ".diagnostic.log"));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                { LastError = error.Message; _closed = true; }
            }
        }
    }
}
