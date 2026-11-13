using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace DelayNoMore.LightPath
{
    /** @brief 单次启用区间的数据记录器；只接收运行快照，不读取或修改玩法状态。 */
    public sealed class PlaytestRecorder
    {
        /** @brief 当前运行状态，时间由调用方传入单调时钟。 */
        public struct Snapshot
        {
            /** @brief 稳定房间标识。 */
            public string Room;
            /** @brief 当前视角和背景颜色。 */
            public string View, Color;
            /** @brief 是否处于游玩、暂停、转场、实际操作、浮空、供光和调试供光。 */
            public bool Playing, Paused, Transitioning, Active, Noclip, Powered, ForcedPower;
            /** @brief 玩家世界坐标。 */
            public float X, Y, Z;
        }
        /** @brief 通用计数、时长和累计量。 */
        private sealed class Metric
        {
            /** @brief 发生次数。 */
            public long Count;
            /** @brief 持续秒数及累计量。 */
            public double Seconds, Amount;
        }
        /** @brief 单次房间访问。 */
        private sealed class Visit
        {
            /** @brief 房间、进入方式与离开原因。 */
            public string Room, Entry, Exit = "未结束";
            /** @brief 起止相对时间和各类时长。 */
            public double Start, End, Play, Pause, Active, Transition;
        }
        /** @brief 按房间汇总的有界帧耗时直方图，P95精度为1ms。 */
        private sealed class Performance
        {
            /** @brief 1ms桶，最后一桶为超长帧。 */
            public readonly long[] Histogram = new long[4096];
            /** @brief 帧数与超过100ms的帧数。 */
            public long Count, Stalls, Builds;
            /** @brief 帧与投影重建累计、最大毫秒数。 */
            public double Sum, Max, BuildSum, BuildMax;
            /** @brief 取得P95桶上界，溢出桶返回最大观察值。 */
            public double P95
            {
                get
                {
                    long target = (long)Math.Ceiling(Count * .95), total = 0;
                    if (target == 0) return 0;
                    for (int i = 0; i < Histogram.Length; i++)
                    {
                        total += Histogram[i];
                        if (total >= target) return i == Histogram.Length - 1 ? Max : i + 1;
                    }
                    return Max;
                }
            }
        }
        /** @brief 一次持续按键或透镜拖动。 */
        private sealed class Operation
        {
            /** @brief 所属房间、对象和能力。 */
            public string Room, Subject, Axis;
            /** @brief 起始时间、有效时长与实际累计变化。 */
            public double Start, Seconds, Amount;
            /** @brief 最近输入帧。 */
            public int Frame;
        }
        /** @brief 启动UTC时刻、开始采集时刻及单调时钟基准。 */
        private readonly DateTimeOffset _launchUtc;
        /** @brief 单调启动基准和采集起点。 */
        private readonly double _launchClock, _begin;
        /** @brief 本次记录的版本、目录与文件前缀。 */
        private readonly string _version, _folder, _stem;
        /** @brief 仅本记录拥有的临时CSV。 */
        private StreamWriter _writer;
        /** @brief 错误回调，不将磁盘故障传播到玩法。 */
        private readonly Action<string> _error;
        /** @brief 事件汇总，键为房间/类别/对象。 @param Room 调用参数 @param Kind 调用参数 @param Subject 调用参数 */
        private readonly Dictionary<(string Room, string Kind, string Subject), Metric> _metrics = new Dictionary<(string, string, string), Metric>();
        /** @brief 每次访问，顺序不合并。 */
        private readonly List<Visit> _visits = new List<Visit>();
        /** @brief 各房间的性能数据。 */
        private readonly Dictionary<string, Performance> _performance = new Dictionary<string, Performance>();
        /** @brief 正在持续的操作。 */
        private readonly Dictionary<string, Operation> _operations = new Dictionary<string, Operation>();
        /** @brief 当前快照和是否已接到快照。 */
        private Snapshot _state;
        /** @brief 当前访问。 */
        private Visit _visit;
        /** @brief 时钟与写盘节流。 */
        private double _last, _flushed, _blackoutAt;
        /** @brief 基线及终止标志。 */
        private bool _hasState, _ended;
        /** @brief 下次房间进入方式。 */
        private string _entry = "中途启用记录";
        /** @brief 正常游玩、暂停、菜单及转场累计秒数。 */
        private double _play, _pause, _menu, _transition;
        /** @brief 首次通过时间，只有成功穿越或关卡出口才标记。 */
        private readonly Dictionary<string, double> _firstPass = new Dictionary<string, double>();
        /** @brief 已完成的关卡，调试跳转不计入。 */
        private readonly HashSet<string> _completed = new HashSet<string>();
        /** @brief 正常通关状态。 */
        private bool _won;
        /** @brief 本记录文件标识。 */
        public string Id => _stem;
        /** @brief 临时文件绝对路径；未正常退出的文件保持pending名称。 */
        public string PendingPath => Path.Combine(_folder, _stem + ".pending.csv");
        /** @brief 最近写盘错误；报告会注明明细可能不完整。 */
        public string LastError { get; private set; }
        /** @brief 创建本次记录；目录或文件无法创建时由调用方拒绝启用。 @param folder 报告输出目录 @param version 游戏版本信息 @param launchUtc 程序启动UTC时刻 @param launchClock 启动单调时钟基准 @param now 当前单调时钟秒数 @param error 写盘错误回调 */
        public PlaytestRecorder(string folder, string version, DateTimeOffset launchUtc, double launchClock, double now, Action<string> error = null)
        {
            _folder = Path.GetFullPath(folder); _version = version; _launchUtc = launchUtc;
            _launchClock = launchClock; _begin = _last = _flushed = now; _error = error;
            _stem = launchUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            Directory.CreateDirectory(_folder);
            _writer = new StreamWriter(new FileStream(PendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(true));
            _writer.WriteLine("时间,启动后秒,采集后秒,累计游玩秒,事件,房间,访问序号,对象,说明,累计量,持续秒,X,Y,Z");
            Write(now, "会话元数据", "", version, "启动=" + launchUtc.ToString("O") + "；pending表示未正常结束，不会由下次会话自动导出", 0, 0);
            Flush(now);
        }
        /** @brief 更新状态，先把上一段时间归给上一状态；不补造启用前数据。 @param state 当前只读运行快照 @param now 当前单调时钟秒数 @param frameMilliseconds 本帧未缩放耗时毫秒，零表示不采样 */
        public void Observe(Snapshot state, double now, double frameMilliseconds = 0)
        {
            if (_ended) return;
            double elapsed = Math.Max(0, now - _last);
            if (_hasState)
            {
                if (!_state.Playing) _menu += elapsed;
                else if (_state.Paused) { _pause += elapsed; if (_visit != null) _visit.Pause += elapsed; }
                else if (_state.Transitioning) { _transition += elapsed; if (_visit != null) _visit.Transition += elapsed; }
                else
                {
                    _play += elapsed;
                    if (_visit != null) { _visit.Play += elapsed; if (_state.Active) _visit.Active += elapsed; }
                    Add(_state.Room, "视角停留", _state.View, 0, elapsed, 0);
                    Add(_state.Room, "颜色停留", _state.Color, 0, elapsed, 0);
                    Add(_state.Room, "供光停留", _state.Powered ? "供光" : "断光", 0, elapsed, 0);
                    if (_state.Noclip) Add(_state.Room, "Debug浮空", "Noclip", 0, elapsed, 0);
                }
            }
            bool changedRoom = _hasState && (_state.Room != state.Room || _state.Playing != state.Playing);
            if (changedRoom) CloseVisit(now, _entry == "中途启用记录" ? "状态切换" : _entry);
            Snapshot before = _state; bool hadState = _hasState;
            _state = state; _hasState = true; _last = now;
            if (state.Playing && _visit == null)
            {
                _visit = new Visit { Room = state.Room, Start = now, End = now, Entry = _entry };
                _visits.Add(_visit);
                Write(now, "进入房间", state.Room, "", _entry, 0, 0);
                _entry = "普通切换";
            }
            if (!hadState)
            {
                _blackoutAt = now;
                Write(now, "采集基线", state.Room, state.View, $"颜色={state.Color}；供光={state.Powered}；强制供光={state.ForcedPower}；Noclip={state.Noclip}", 0, 0);
            }
            else
            {
                if (before.Paused != state.Paused) Record(now, state.Paused ? "暂停开始" : "暂停结束");
                if (before.View != state.View) Record(now, "视角切换", state.View, before.View);
                if (before.Color != state.Color) Record(now, "颜色变化", state.Color, before.Color);
                if (before.Powered != state.Powered)
                {
                    if (!state.Powered) _blackoutAt = now;
                    Record(now, state.Powered ? "恢复供光" : "断光开始", state.ForcedPower ? "Debug强制" : "光路", "", 0, state.Powered ? now - _blackoutAt : 0);
                }
            }
            if (state.Playing && !state.Paused && !state.Transitioning && frameMilliseconds > 0)
            {
                Performance p = Perf(state.Room); p.Count++; p.Sum += frameMilliseconds; p.Max = Math.Max(p.Max, frameMilliseconds);
                p.Histogram[Math.Min(4095, (int)frameMilliseconds)]++;
                if (frameMilliseconds >= 100) p.Stalls++;
            }
            if (now - _flushed >= 5) Flush(now);
        }
        /** @brief 写入离散事件并累计；位置来自最近一次快照。 @param now 当前单调时钟秒数 @param kind 事件或指标类别 @param subject 对象或状态标识 @param detail 补充说明 @param amount 累计变化量 @param seconds 有效持续秒数 */
        public void Record(double now, string kind, string subject = "", string detail = "", double amount = 0, double seconds = 0)
        {
            if (_ended) return;
            Add(_state.Room ?? "菜单", kind, subject, 1, seconds, amount);
            Write(now, kind, _state.Room, subject, detail, amount, seconds);
            if (kind == "关卡完成") _completed.Add(subject);
            if (kind == "游戏通关") _won = true;
        }
        /** @brief 指定下次进入原因；成功通过单独标记，不将调试跳转当通关。 @param now 当前单调时钟秒数 @param reason 进入或退出原因 @param passed 是否成功通过房间 */
        public void Transition(double now, string reason, bool passed = false)
        {
            if (_ended) return;
            _entry = reason;
            if (passed && _visit != null && !_firstPass.ContainsKey(_visit.Room)) _firstPass[_visit.Room] = now;
        }
        /** @brief 同房间重生也分隔访问；调用者已完成状态恢复。 @param now 当前单调时钟秒数 @param reason 进入或退出原因 */
        public void SplitVisit(double now, string reason)
        {
            if (_ended) return;
            _entry = reason; CloseVisit(now, reason);
        }
        /** @brief 场景对象销毁后使用最后一个有效快照关闭访问，避免读取已释放Unity引用。 @param now 当前单调时钟秒数 @param reason 进入或退出原因 */
        public void Suspend(double now, string reason)
        {
            if (_ended || !_hasState) return;
            Transition(now, reason);
            Snapshot state = _state; state.Playing = false;
            Observe(state, now);
        }
        /** @brief 连续输入累计，松键、换房或暂停后结束一次操作。 @param now 当前单调时钟秒数 @param frame 当前帧号 @param subject 对象或状态标识 @param axis 操作轴或透镜类别 @param seconds 有效持续秒数 @param amount 累计变化量 */
        public void Operate(double now, int frame, string subject, string axis, double seconds, double amount)
        {
            if (_ended) return;
            string key = (_state.Room ?? "") + "\n" + subject + "\n" + axis;
            if (!_operations.TryGetValue(key, out Operation operation))
            {
                operation = new Operation { Room = _state.Room, Subject = subject, Axis = axis, Start = now };
                _operations.Add(key, operation);
                Write(now, "对象操作开始", operation.Room, subject, axis, 0, 0);
            }
            operation.Frame = frame; operation.Seconds += Math.Max(0, seconds); operation.Amount += Math.Abs(amount);
        }
        /** @brief 帧末结束已松开的操作；不把每帧算作一次操作。 @param now 当前单调时钟秒数 @param frame 当前帧号 */
        public void EndFrame(double now, int frame)
        {
            if (_operations.Count == 0) return;
            foreach (string key in _operations.Where(pair => pair.Value.Frame != frame).Select(pair => pair.Key).ToArray()) EndOperation(key, now);
        }
        /** @brief 记录一次真实脏块重建，不重复读取LastBuildMilliseconds。 @param room 稳定房间标识 @param milliseconds CPU耗时毫秒 */
        public void Build(string room, double milliseconds)
        {
            if (_ended) return;
            Performance p = Perf(room); p.Builds++; p.BuildSum += milliseconds; p.BuildMax = Math.Max(p.BuildMax, milliseconds);
        }
        /** @brief 结束连续操作并保存累计值。 @param key 指标或操作标识 @param now 当前单调时钟秒数 */
        private void EndOperation(string key, double now)
        {
            Operation operation = _operations[key];
            Add(operation.Room, "对象操作/" + operation.Axis, operation.Subject, 1, operation.Seconds, operation.Amount);
            Write(now, "对象操作结束", operation.Room, operation.Subject, operation.Axis + "；开始采集后秒=" + Number(operation.Start - _begin), operation.Amount, operation.Seconds);
            _operations.Remove(key);
        }
        /** @brief 关闭访问并保留重复进入记录。 @param now 当前单调时钟秒数 @param reason 进入或退出原因 */
        private void CloseVisit(double now, string reason)
        {
            foreach (string key in _operations.Keys.ToArray()) EndOperation(key, now);
            if (_visit == null) return;
            _visit.End = now; _visit.Exit = reason;
            Write(now, "离开房间", _visit.Room, "", reason, 0, now - _visit.Start);
            _visit = null;
        }
        /** @brief 累计通用指标。 @param room 稳定房间标识 @param kind 事件或指标类别 @param subject 对象或状态标识 @param count 发生次数 @param seconds 有效持续秒数 @param amount 累计变化量 */
        private void Add(string room, string kind, string subject, long count, double seconds, double amount)
        {
            var key = (room ?? "菜单", kind ?? "", subject ?? "");
            if (!_metrics.TryGetValue(key, out Metric metric)) _metrics[key] = metric = new Metric();
            metric.Count += count; metric.Seconds += seconds; metric.Amount += amount;
        }
        /** @brief 取得房间性能容器。 @param room 稳定房间标识 */
        private Performance Perf(string room)
        {
            room = room ?? "菜单";
            if (!_performance.TryGetValue(room, out Performance value)) _performance[room] = value = new Performance();
            return value;
        }
        /** @brief 将已接收事件刷至本次临时文件。 @param now 当前单调时钟秒数 */
        public void Flush(double now)
        {
            if (_hasState && !_ended)
            {
                Write(now, "采集心跳", _state.Room, _state.View, $"暂停={_state.Paused}；颜色={_state.Color}；供光={_state.Powered}；Noclip={_state.Noclip}", 0, now - _begin);
                foreach (Operation operation in _operations.Values)
                    Write(now, "操作进度", operation.Room, operation.Subject, operation.Axis, operation.Amount, operation.Seconds);
            }
            try { _writer?.Flush(); _flushed = now; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Fail(exception); }
        }
        /** @brief 异常生命周期清理只关闭句柄，保留pending作为未完成记录。 */
        public void Abandon()
        {
            if (_ended) return;
            _ended = true;
            try { _writer?.Dispose(); _writer = null; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Fail(exception); }
        }
        /** @brief 关闭开关即丢弃当前记录，不生成正式文件，不触碰其他会话。 */
        public void Cancel()
        {
            if (_ended) return;
            _ended = true;
            try { _writer?.Dispose(); _writer = null; if (File.Exists(PendingPath)) File.Delete(PendingPath); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Fail(exception); }
        }
        /** @brief 仅会话退出调用一次；正式报告写入成功后才改名临时CSV。 @param now 当前单调时钟秒数 */
        public void Finish(double now)
        {
            if (_ended) return;
            if (_hasState) Observe(_state, now);
            CloseVisit(now, "会话正常结束");
            Write(now, "会话正常结束", _state.Room, "", "只包含本次启用区间", 0, now - _begin);
            foreach (var pair in _metrics) Write(now, "指标汇总/" + pair.Key.Kind, pair.Key.Room, pair.Key.Subject, "次数=" + pair.Value.Count, pair.Value.Amount, pair.Value.Seconds);
            for (int i = 0; i < _visits.Count; i++)
            {
                Visit visit = _visits[i];
                string detail = visit.Entry + " → " + visit.Exit;
                Write(now, "访问汇总", visit.Room, "总停留秒", detail, 0, visit.End - visit.Start, i + 1);
                Write(now, "访问汇总", visit.Room, "游玩秒", detail, 0, visit.Play, i + 1);
                Write(now, "访问汇总", visit.Room, "暂停秒", detail, 0, visit.Pause, i + 1);
                Write(now, "访问汇总", visit.Room, "操作秒", detail, 0, visit.Active, i + 1);
                Write(now, "访问汇总", visit.Room, "转场秒", detail, 0, visit.Transition, i + 1);
            }
            foreach (var pair in _performance)
            {
                Performance p = pair.Value;
                Write(now, "性能汇总", pair.Key, "帧数", "", p.Count, 0);
                Write(now, "性能汇总", pair.Key, "帧平均ms", "", p.Count > 0 ? p.Sum / p.Count : 0, 0);
                Write(now, "性能汇总", pair.Key, "帧P95约ms", "1ms桶上界", p.P95, 0);
                Write(now, "性能汇总", pair.Key, "帧最大ms", "", p.Max, 0);
                Write(now, "性能汇总", pair.Key, "卡顿帧数", "≥100ms", p.Stalls, 0);
                Write(now, "性能汇总", pair.Key, "脏投影重建次数", "", p.Builds, 0);
                Write(now, "性能汇总", pair.Key, "平均重建ms", "", p.Builds > 0 ? p.BuildSum / p.Builds : 0, 0);
                Write(now, "性能汇总", pair.Key, "最大重建ms", "", p.BuildMax, 0);
            }
            _ended = true;
            try
            {
                _writer?.Dispose(); _writer = null;
                string report = Path.Combine(_folder, _stem + ".md");
                string temporary = report + ".tmp";
                File.WriteAllText(temporary, Report(now), new UTF8Encoding(true));
                File.Move(temporary, report);
                File.Move(PendingPath, Path.Combine(_folder, _stem + ".csv"));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Fail(exception); }
        }
        /** @brief 生成中文报告，明细保留在同名CSV。 @param now 当前单调时钟秒数 */
        private string Report(double now)
        {
            var text = new StringBuilder();
            text.AppendLine("# 本次游玩数据记录\n");
            text.AppendLine($"- 编号：{_stem}\n- 版本：{Cell(_version)}\n- 启动时间：{_launchUtc.ToLocalTime():O}\n- 开始采集：{Wall(_begin)}\n- 结束时间：{Wall(now)}\n- 退出状态：正常退出\n- 启动至退出：{Number(now - _launchClock)} 秒\n- 采集区间：{Number(now - _begin)} 秒\n- 实际游玩：{Number(_play)} 秒\n- 暂停：{Number(_pause)} 秒\n- 菜单/结算：{Number(_menu)} 秒\n- 转场：{Number(_transition)} 秒\n- 完成关卡数：{_completed.Count}\n- 本采集区间是否通关：{(_won ? "是" : "否")}\n");
            text.AppendLine("只记录本次启用后的数据；中途启用前的状态为基线，不补造历史。游玩包括投影调整、维修、断光等待；暂停与转场另计。实际操作时长仅表示存在有效输入，不等于思考时长。Debug干预保留在事件明细和指标中。重开不会重置本报告。\n");
            if (!string.IsNullOrEmpty(LastError)) text.AppendLine("**写盘曾失败，CSV可能不完整：** " + Cell(LastError) + "\n");
            text.AppendLine("故障诊断若在本次Debug授权期间启用，见同编号 `.diagnostic.log`。异常结束时直接读取 `.diagnostic.pending.log` 和 `.pending.csv`；pending不等于已确认崩溃，日志仅保留成功写入的数据。\n");
            text.AppendLine("## 房间汇总\n\n| 房间 | 访问次数 | 总停留秒 | 游玩秒 | 暂停秒 | 操作秒 | 首次进入至首次通过秒 |\n|---|---:|---:|---:|---:|---:|---:|");
            foreach (var group in _visits.GroupBy(visit => visit.Room))
                text.AppendLine($"| {Cell(group.Key)} | {group.Count()} | {Number(group.Sum(v => v.End - v.Start))} | {Number(group.Sum(v => v.Play))} | {Number(group.Sum(v => v.Pause))} | {Number(group.Sum(v => v.Active))} | {(_firstPass.TryGetValue(group.Key, out double passed) ? Number(passed - group.First().Start) : "未观察到通过")} |");
            text.AppendLine("\n首次通过指成功离开该房间或使用关卡出口，可能包含折返，不代表设计解法达成；调试跳转、重生不计通过。\n\n## 按访问顺序\n\n| 次序 | 房间 | 进入时间 | 离开时间 | 进入方式 | 离开原因 | 停留秒 | 游玩秒 | 暂停秒 | 操作秒 | 转场秒 |\n|---:|---|---|---|---|---|---:|---:|---:|---:|---:|");
            for (int i = 0; i < _visits.Count; i++)
            {
                Visit v = _visits[i];
                text.AppendLine($"| {i + 1} | {Cell(v.Room)} | {Wall(v.Start)} | {Wall(v.End)} | {Cell(v.Entry)} | {Cell(v.Exit)} | {Number(v.End - v.Start)} | {Number(v.Play)} | {Number(v.Pause)} | {Number(v.Active)} | {Number(v.Transition)} |");
            }
            text.AppendLine("\n## 行为与状态汇总\n\n| 房间 | 指标 | 对象/状态 | 次数 | 秒数 | 累计量 |\n|---|---|---|---:|---:|---:|");
            foreach (var pair in _metrics.OrderBy(p => p.Key.Room).ThenBy(p => p.Key.Kind).ThenBy(p => p.Key.Subject))
                text.AppendLine($"| {Cell(pair.Key.Room)} | {Cell(pair.Key.Kind)} | {Cell(pair.Key.Subject)} | {pair.Value.Count} | {Number(pair.Value.Seconds)} | {Number(pair.Value.Amount)} |");
            text.AppendLine("\n移动累计量为世界单位，旋转与透镜为度；连续按键/拖动一次计一次，键松开、换房或暂停会结束操作。\n\n## 性能\n\n| 房间 | 帧数 | 平均ms | P95约ms | 最大ms | ≥100ms帧数 | 脏投影重建次数 | 平均重建ms | 最大重建ms |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var pair in _performance.OrderBy(p => p.Key))
            {
                Performance p = pair.Value;
                text.AppendLine($"| {Cell(pair.Key)} | {p.Count} | {Number(p.Count > 0 ? p.Sum / p.Count : 0)} | {Number(p.P95)} | {Number(p.Max)} | {p.Stalls} | {p.Builds} | {Number(p.Builds > 0 ? p.BuildSum / p.Builds : 0)} | {Number(p.BuildMax)} |");
            }
            text.AppendLine("\n帧耗时取未缩放帧间隔，排除菜单、暂停及转场；P95使用1ms桶上界，≥4095ms桶以最大值表示。投影项只计实际处理脏块的CPU重建，含目标房间预热。未启用或无样本不等于零成本。异常退出仅留下pending.csv，不会在下次启动自动汇入或导出。\n");
            return text.ToString();
        }
        /** @brief 写入带上下文的CSV行；引号与公式前缀经过转义。 @param now 当前单调时钟秒数 @param kind 事件或指标类别 @param room 稳定房间标识 @param subject 对象或状态标识 @param detail 补充说明 @param amount 累计变化量 @param seconds 有效持续秒数 @param visitNumber 访问序号，负一使用当前访问序号 */
        private void Write(double now, string kind, string room, string subject, string detail, double amount, double seconds, int visitNumber = -1)
        {
            if (_writer == null) return;
            try
            {
                _writer.WriteLine(string.Join(",", new[] { Csv(Wall(now)), Number(now - _launchClock), Number(now - _begin), Number(_play), Csv(kind), Csv(room), (visitNumber >= 0 ? visitNumber : _visit != null ? _visits.Count : 0).ToString(CultureInfo.InvariantCulture), Csv(subject), Csv(detail), Number(amount), Number(seconds), Number(_state.X), Number(_state.Y), Number(_state.Z) }));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { Fail(exception); }
        }
        /** @brief 保存首个错误并只告警一次。 @param exception 写盘异常 */
        private void Fail(Exception exception)
        {
            if (LastError != null) return;
            LastError = exception.Message; _error?.Invoke(LastError);
        }
        /** @brief 使用单调经过时间推算带时区的墙钟时间。 @param now 当前单调时钟秒数 */
        private string Wall(double now) => _launchUtc.AddSeconds(now - _launchClock).ToLocalTime().ToString("O", CultureInfo.InvariantCulture);
        /** @brief 使用文化无关数字便于CSV解析。 @param value 待格式化内容 */
        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        /** @brief 转义Markdown表格内容。 @param value 待格式化内容 */
        private static string Cell(string value) => (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        /** @brief 转义CSV文本并避免表格软件执行对象名中的公式。 @param value 待格式化内容 */
        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
