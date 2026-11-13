using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 本次许可、退出导出和时间口径回归，只创建本用例独占的报告文件。 */
    public sealed class PlaytestRecorderTests
    {
        /** @brief 本用例独占目录，所有产物遵守统一文档路径。 */
        private string _folder;
        /** @brief 确定性启动墙钟。 */
        private readonly DateTimeOffset _launch = new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
        /** @brief 选择唯一目录，默认关闭测试前不创建它。 */
        [SetUp] public void SetUp() => _folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/Reports/TelemetryTests", Guid.NewGuid().ToString("N")));
        /** @brief 创建无副作用的新会话。 */
        private PlaytestSession Session() => new PlaytestSession(_folder, "test", _launch, 0);
        /** @brief 正常游玩快照。 */
        private static PlaytestRecorder.Snapshot State(string room, string view = "World", bool paused = false) => new PlaytestRecorder.Snapshot { Room = room, View = view, Color = "无选色", Playing = true, Paused = paused, Powered = true, Active = true };
        /** @brief 异常结束保留已刷盘线索，关闭许可则删除，正常结束产生完成标记。 */
        [Test] public void DiagnosticJournal_PreservesOnlyAuthorizedInterval()
        {
            Directory.CreateDirectory(_folder);
            string pending = Path.Combine(_folder, "test.diagnostic.pending.log");
            var journal = new PlaytestDiagnosticJournal(pending, "test GPU");
            journal.Write("状态快照", "Level3Room1");
            journal.Fault("错误", "stack");
            Assert.That(File.ReadAllText(pending), Does.Contain("Level3Room1").And.Contain("stack"));
            journal.Close(false, false);
            Assert.That(File.Exists(pending), Is.True);
            Assert.That(File.Exists(pending.Replace(".pending.log", ".log")), Is.False);
            string cancelled = Path.Combine(_folder, "cancel.diagnostic.pending.log");
            journal = new PlaytestDiagnosticJournal(cancelled, "test");
            journal.Close(true, false);
            Assert.That(File.Exists(cancelled), Is.False);
            string normal = Path.Combine(_folder, "normal.diagnostic.pending.log");
            journal = new PlaytestDiagnosticJournal(normal, "test");
            journal.Close(false, true);
            Assert.That(File.Exists(normal), Is.False);
            Assert.That(File.ReadAllText(normal.Replace(".pending.log", ".log")), Does.Contain("正常退出"));
        }
        /** @brief 多线程错误日志不破坏文件，重复错误压缩保留计数。 */
        [Test] public void DiagnosticJournal_SerializesConcurrentErrors()
        {
            Directory.CreateDirectory(_folder);
            string pending = Path.Combine(_folder, "threads.diagnostic.pending.log");
            var journal = new PlaytestDiagnosticJournal(pending, "test");
            System.Threading.Tasks.Parallel.For(0, 201, index => journal.Fault("same", "stack"));
            journal.Close(false, false);
            string text = File.ReadAllText(pending);
            Assert.That(text, Does.Contain("次数=200"));
            Assert.That(text.Split(new[] { "Unity错误或异常" }, StringSplitOptions.None).Length, Is.EqualTo(2));
        }
        /** @brief 未启用时连目录都不产生。 */
        [Test] public void NeverEnabled_ExitCreatesNothing()
        {
            var session = Session(); session.Finish(50); session.Finish(51);
            Assert.That(session.Enabled, Is.False);
            Assert.That(Directory.Exists(_folder), Is.False);
        }
        /** @brief 开启期间只有pending，退出仅生成本次一对文件，重复退出幂等。 */
        [Test] public void Enabled_ExportsOnlyOnExitOnce()
        {
            var session = Session(); session.SetEnabled(true, 5);
            var recorder = session.Recorder;
            recorder.Observe(State("Level1Room0"), 5);
            recorder.Record(6, "激活存档点", "Checkpoint"); recorder.Flush(6);
            Assert.That(File.Exists(recorder.PendingPath), Is.True);
            Assert.That(Directory.GetFiles(_folder, "*.md"), Is.Empty);
            session.Finish(10); session.Finish(11);
            Assert.That(session.Enabled, Is.False);
            Assert.That(Directory.GetFiles(_folder).Length, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(_folder, "*.pending.csv"), Is.Empty);
            string report = File.ReadAllText(Directory.GetFiles(_folder, "*.md").Single());
            Assert.That(report, Does.Contain("实际游玩：5 秒"));
            Assert.That(report, Does.Contain("激活存档点"));
        }
        /** @brief 关闭取消授权并丢弃内容，再开启不混入此前采集。 */
        [Test] public void DisableThenReenable_DiscardsOldInterval()
        {
            var session = Session(); session.SetEnabled(true, 1);
            session.Recorder.Record(2, "旧事件禁止导出");
            string oldPending = session.Recorder.PendingPath;
            session.SetEnabled(false, 3);
            Assert.That(File.Exists(oldPending), Is.False);
            session.SetEnabled(true, 4); session.Recorder.Record(5, "本次重新采集"); session.Finish(6);
            string csv = File.ReadAllText(Directory.GetFiles(_folder, "*.csv").Single());
            Assert.That(csv, Does.Not.Contain("旧事件禁止导出"));
            Assert.That(csv, Does.Contain("本次重新采集"));
        }
        /** @brief 开过但退出前关闭时没有正式导出。 */
        [Test] public void DisabledBeforeExit_ProducesNoFinalFiles()
        {
            var session = Session(); session.SetEnabled(true, 1); session.SetEnabled(false, 2); session.Finish(3);
            Assert.That(Directory.GetFiles(_folder), Is.Empty);
        }
        /** @brief 下次启动默认关闭，既不恢复上次许可，也不转换遗留pending。 */
        [Test] public void NewLaunch_NeverExportsPreviousPending()
        {
            var old = Session(); old.SetEnabled(true, 1); old.Recorder.Record(2, "上次未正常结束"); old.Recorder.Flush(2);
            string pending = old.Recorder.PendingPath; old.Abandon();
            byte[] before = File.ReadAllBytes(pending);
            var next = Session(); next.Finish(10);
            Assert.That(next.Enabled, Is.False);
            Assert.That(File.ReadAllBytes(pending), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_folder, "*.md"), Is.Empty);
            next = Session(); next.SetEnabled(true, 11); next.Recorder.Record(12, "新会话"); next.Finish(13);
            string csv = File.ReadAllText(Directory.GetFiles(_folder, "*.csv").Single(path => !path.EndsWith(".pending.csv")));
            Assert.That(csv, Does.Not.Contain("上次未正常结束"));
        }
        /** @brief 重复进入单独记录，暂停不混入游玩，视角累计包括维修和投影。 */
        [Test] public void Visits_AccountForPauseAndRepeatedRooms()
        {
            var session = Session(); session.SetEnabled(true, 5); var recorder = session.Recorder;
            recorder.Observe(State("A"), 5);
            recorder.Observe(State("A", paused: true), 15);
            recorder.Observe(State("A", "Projection"), 18);
            recorder.Transition(23, "相连边界", true);
            recorder.Observe(State("B", "Repair"), 23);
            recorder.Transition(30, "Debug跳房间");
            recorder.Observe(State("A"), 30);
            session.Finish(33);
            string report = File.ReadAllText(Directory.GetFiles(_folder, "*.md").Single());
            Assert.That(report, Does.Contain("实际游玩：25 秒"));
            Assert.That(report, Does.Contain("暂停：3 秒"));
            Assert.That(report, Does.Contain("| A | 2 | 21 | 18 | 3 | 18 | 18 |"));
            Assert.That(report, Does.Contain("| B | 1 | 7 | 7 | 0 | 7 | 未观察到通过 |"));
            Assert.That(report, Does.Contain("| A | 视角停留 | Projection | 0 | 5 |"));
            string csv = File.ReadAllText(Directory.GetFiles(_folder, "*.csv").Single());
            Assert.That(csv, Does.Contain("\"访问汇总\",\"A\",1,"));
            Assert.That(csv, Does.Contain("\"访问汇总\",\"B\",2,"));
            Assert.That(csv, Does.Contain("\"访问汇总\",\"A\",3,"));
        }
        /** @brief 按住两帧仍只计一次，松开后再次输入算第二次。 */
        [Test] public void HeldInput_IsOneOperationWithRealDistance()
        {
            var session = Session(); session.SetEnabled(true, 0); var recorder = session.Recorder;
            recorder.Observe(State("A"), 0);
            recorder.Operate(1, 1, "Gate", "移动X", .1, .2); recorder.EndFrame(1, 1);
            recorder.Operate(2, 2, "Gate", "移动X", .1, .3); recorder.EndFrame(2, 2);
            recorder.EndFrame(3, 3);
            recorder.Operate(4, 4, "Gate", "移动X", .1, .4); recorder.EndFrame(4, 4);
            session.Finish(5);
            string report = File.ReadAllText(Directory.GetFiles(_folder, "*.md").Single());
            Assert.That(report, Does.Contain("| A | 对象操作/移动X | Gate | 2 | 0.3 | 0.9 |"));
        }
        /** @brief 性能取样排除暂停帧；CSV转义对象名称，中文和引号不破坏格式。 */
        [Test] public void PerformanceAndCsv_AreBoundedAndEscaped()
        {
            var session = Session(); session.SetEnabled(true, 0); var recorder = session.Recorder;
            recorder.Observe(State("A"), 0, 16); recorder.Observe(State("A"), 1, 120);
            recorder.Observe(State("A", paused: true), 2, 999);
            recorder.Build("A", 4); recorder.Build("A", 6);
            recorder.Record(2, "名称测试", "=测试\"门\n二", "说明"); session.Finish(3);
            string report = File.ReadAllText(Directory.GetFiles(_folder, "*.md").Single());
            Assert.That(report, Does.Contain("| A | 2 | 68 | 121 | 120 | 1 | 2 | 5 | 6 |"));
            string csv = File.ReadAllText(Directory.GetFiles(_folder, "*.csv").Single());
            Assert.That(csv, Does.Contain("\"'=测试\"\"门\n二\""));
        }
        /** @brief 卸载场景结束当前访问但不导出，新场景继续同一记录。 */
        [Test] public void SceneUnload_SuspendsWithoutExport()
        {
            var session = Session(); session.SetEnabled(true, 0); var recorder = session.Recorder;
            recorder.Observe(State("A"), 0); recorder.Suspend(2, "卸载场景");
            Assert.That(Directory.GetFiles(_folder, "*.md"), Is.Empty);
            recorder.Observe(State("B"), 5); session.Finish(8);
            string report = File.ReadAllText(Directory.GetFiles(_folder, "*.md").Single());
            Assert.That(report, Does.Contain("实际游玩：5 秒"));
            Assert.That(report, Does.Contain("菜单/结算：3 秒"));
            Assert.That(report, Does.Contain("卸载场景"));
        }
        /** @brief 输出路径不可用时不误认为已经启用。 */
        [Test] public void InvalidOutput_DoesNotEnableSession()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_folder)); File.WriteAllText(_folder, "occupied");
            var session = Session();
            Assert.Throws<IOException>(() => session.SetEnabled(true, 1));
            Assert.That(session.Enabled, Is.False);
            session.Finish(2);
            Assert.That(File.ReadAllText(_folder), Is.EqualTo("occupied"));
        }
    }
}
