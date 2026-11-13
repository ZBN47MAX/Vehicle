using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 使用正式LightGame场景的集成回归，不制作或保存测试场景。 */
    public sealed class GameFlowTests
    {
        /** @brief 本用例游戏会话。 */
        private LightGame _game;
        /** @brief 本用例舞台。 */
        private LightStage _stage;
        /** @brief 已有角色刚体。 */
        private Rigidbody2D _player;
        /** @brief 私有状态仅供集成断言和可重复触发，不改共享配置。 */
        private const BindingFlags _flags = BindingFlags.NonPublic | BindingFlags.Instance;
        /** @brief 每项重新加载正式场景，隔离运行状态。 */
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("LightGame", LoadSceneMode.Single);
            yield return null;
            _game = UnityEngine.Object.FindAnyObjectByType<LightGame>();
            _stage = UnityEngine.Object.FindAnyObjectByType<LightStage>();
            Assert.That(_game && _game.enabled && _stage && _stage.Ready, Is.True, "正式场景初始化");
            _player = Read<Rigidbody2D>(_game, "_player");
            _game.Restart(); _game.SetPaused(true);
            yield return null;
        }
        /** @brief 还原全局时间，场景交由下一用例或Test Runner恢复。 */
        [TearDown] public void TearDown() { Time.timeScale = 1; }
        /** @brief 读取已有实例状态。@param target 对象 @param name 字段 @return 值 */
        private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, _flags).GetValue(target);
        /** @brief 调用正式入口中可确定复现的内部行为。@param target 对象 @param name 方法 @param args 参数 @return 返回值 */
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, _flags).Invoke(target, args);
        /** @brief 将已有角色移动到指定位置并同步物理。@param position 世界位置 */
        private void Place(Vector2 position) { _player.position = position; _player.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
        /** @brief 使用正式第三关实例验证逐房间推进、实际拾取X/Y钥匙及终局解法。维修供光依赖仅在本用例运行实例中隔离。 */
        [UnityTest] public IEnumerator ThirdLevel_ProgressionUnlocksBothRotationsAndBuildsBridges()
        {
            var rooms = Read<LightRoom[]>(_game, "_rooms");
            int first = Array.FindIndex(rooms, room => room.name.StartsWith("Level3Room0"));
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "正式场景第三关引用");
            _game.Room.gameObject.SetActive(false);
            typeof(LightGame).GetField("_roomIndex", _flags).SetValue(_game, first);
            var unlocked = Read<bool[]>(_game, "_unlocked");
            for (int color = 0; color < 4; color++) unlocked[color] = true;
            var abilities = Read<ProjectionAbilityState>(_game, "_projectionAbilities");
            foreach (var ability in new[] { ProjectionAbility.MoveX, ProjectionAbility.MoveY, ProjectionAbility.MoveZ, ProjectionAbility.RotateZ }) abilities.Unlock(ability);
            Assert.That(abilities.Has(ProjectionAbility.RotateX), Is.False);
            Assert.That(abilities.Has(ProjectionAbility.RotateY), Is.False);
            for (int i = first; i < first + 7; i++) rooms[i].BindRepair(null);
            rooms[first].gameObject.SetActive(true); rooms[first].ResetRoom();
            Call(_game, "SetStageView", StageView.World, true);
            for (int index = 0; index < 7; index++)
            {
                Assert.That(_game.Room, Is.SameAs(rooms[first + index]));
                var room = _game.Room;
                if (index == 2 || index == 4)
                {
                    var key = room.GetComponentInChildren<ProjectionAbilityKey>(true);
                    Place(key.transform.position); Call(_game, "CollectProjectionAbilities");
                    Assert.That(abilities.Has(key.Ability), Is.True);
                }
                foreach (var source in room.Surface.Sources)
                {
                    if (source.name == "XYZ_Z_Bridge")
                    {
                        Vector3 position = Vector3.Lerp(room.Surface.ProjectionOrigin, room.transform.position + new Vector3(0, .65f, 0), .5f);
                        source.SetWorldPose(position, Quaternion.identity);
                    }
                    else if (source.name == "X_Plate" || source.name == "终局X桥") source.Adjust(Vector3.zero, 90, 0);
                    else if (source.name == "Y_Beam" || source.name == "终局Y桥") source.Adjust(Vector3.zero, 90, 1);
                    else if (source.name == "终局Z桥") source.Adjust(Vector3.zero, -90, 2);
                }
                room.Surface.Invalidate(); room.Surface.Rebuild();
                if (index == 1 || index == 2 || index == 4 || index == 6)
                {
                    for (float x = -2.5f; x <= 2.5f; x += .5f)
                    {
                        var sample = room.Surface.Sample(room.transform.position + new Vector3(x, .65f, 0));
                        Assert.That(sample.Exists, Is.True, "房间桥面 " + index + " / " + x);
                    }
                }
                if (index == 6) break;
                if (index == 5)
                {
                    var portal = room.Surface.Sources.Single();
                    Vector3 position = Vector3.Lerp(room.Surface.ProjectionOrigin, room.transform.position + new Vector3(-2, 1, 0), .6f);
                    portal.SetWorldPose(position, Quaternion.identity);
                    room.Surface.Invalidate(); room.Surface.Rebuild();
                    Place(room.Surface.ProjectPoint(portal.transform.position));
                    Assert.That((bool)Call(_game, "TryPortalObjectTravel", portal.gameObject), Is.True);
                    Assert.That(Vector2.Distance(_player.position, (Vector2)room.transform.position + new Vector2(6, 4.6f)), Is.LessThan(.001f));
                    var fixedGate = room.GetComponentsInChildren<RoomPortalLink>(true).Single(gate => !gate.GetComponent<ProjectionSource>());
                    Assert.That((bool)Call(_game, "TryPortalObjectTravel", fixedGate.gameObject), Is.True);
                    yield return null;
                    Assert.That(_game.Room, Is.SameAs(rooms[first + 6]), "固定二维门进入终局");
                    continue;
                }
                Place((Vector2)room.transform.position + new Vector2(8.6f, .6f));
                typeof(LightGame).GetField("_color", _flags).SetValue(_game, -1);
                yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoom", first + index + 1, Vector2.right, null));
            }
            Assert.That(abilities.Has(ProjectionAbility.RotateX) && abilities.Has(ProjectionAbility.RotateY), Is.True);
        }
        /** @brief 正式房间实例按指定二维坐标传送，目标被平移后仍抵达正确点，非法位置拒绝切房。 */
        [UnityTest] public IEnumerator PortalArrival_UsesRoomCoordinatesAndRejectsOutside()
        {
            var rooms = Read<LightRoom[]>(_game, "_rooms");
            if (rooms.Length < 5) Assert.Inconclusive("当前正式场景关卡未包含房间3和4，跨房间抵达点集成用例等待多房间关卡绑定。");
            _game.Room.gameObject.SetActive(false);
            typeof(LightGame).GetField("_roomIndex", _flags).SetValue(_game, 3);
            typeof(LightGame).GetField("_color", _flags).SetValue(_game, -1);
            rooms[3].BindRepair(null); rooms[4].BindRepair(null);
            rooms[3].gameObject.SetActive(true); rooms[3].ResetRoom();
            Call(_game, "SetStageView", StageView.World, true);
            Vector2 arrival = new Vector2(0, 7);
            yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoomTo", 4, Vector2.zero, null, (Vector2?)arrival));
            Assert.That(_game.Room, Is.SameAs(rooms[4]));
            Assert.That(Vector2.Distance(_player.position, (Vector2)rooms[4].transform.position + arrival), Is.LessThan(.001f));
            Vector2 position = _player.position;
            yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoomTo", 3, Vector2.zero, null, (Vector2?)new Vector2(100, 100)));
            Assert.That(_game.Room, Is.SameAs(rooms[4]));
            Assert.That(_player.position, Is.EqualTo(position));
        }
        /** @brief 正式动作资产在暂停、重新开始和断光中继续接收移动轴、Esc、Tab及F1。 */
        [UnityTest] public IEnumerator KeyboardActions_RemainAvailableAcrossRestartAndPause()
        {
            InputSettings original = InputSystem.settings; HideFlags flags = original.hideFlags;
            InputSettings settings = UnityEngine.Object.Instantiate(original);
            original.hideFlags = HideFlags.DontSave; settings.hideFlags = HideFlags.DontSave;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = settings;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    _game.Restart(); _game.SetPaused(false);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); yield return null; yield return null;
                    var actions = Read<InputActionAsset>(_game, "_actions");
                    Assert.That(actions.FindAction("Light/Move").ReadValue<float>(), Is.GreaterThan(0), "移动轴");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
                    Assert.That(Read<bool>(_game, "_paused"), Is.True, "Esc暂停");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
                    Assert.That(Read<bool>(_game, "_paused"), Is.False, "Esc继续");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab)); yield return null; yield return null;
                    Assert.That(_stage.View, Is.EqualTo(StageView.Overview), "断光中Tab仍可离开二维");
                    var panel = UnityEngine.Object.FindAnyObjectByType<LightDebugPanel>(); bool before = panel.Visible;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1)); yield return null; yield return null;
                    Assert.That(panel.Visible, Is.EqualTo(!before), "F1诊断");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                }
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard); InputSystem.settings = original;
                original.hideFlags = flags; UnityEngine.Object.Destroy(settings);
            }
        }
        /** @brief 使用正式房间实例验证高台连续往返和能力触发区安全检查；仅隔离本用例的维修供光依赖，不修改配置资产。 */
        [UnityTest] public IEnumerator RoomThreeFour_HighEntranceRoundTripsAndTriggerSafety()
        {
            var rooms = Read<LightRoom[]>(_game, "_rooms");
            Assert.That(rooms.Length, Is.GreaterThan(4));
            _game.Room.gameObject.SetActive(false);
            typeof(LightGame).GetField("_roomIndex", _flags).SetValue(_game, 3);
            typeof(LightGame).GetField("_color", _flags).SetValue(_game, -1);
            rooms[3].BindRepair(null); rooms[4].BindRepair(null);
            rooms[3].gameObject.SetActive(true);
            rooms[3].ResetRoom();
            Read<bool[]>(_game, "_visited")[3] = true;
            Call(_game, "SetStageView", StageView.World, true);
            var body = _player.GetComponent<Collider2D>();
            for (int pass = 0; pass < 6; pass++)
            {
                bool toFour = pass % 2 == 0;
                Rect bounds = _game.Room.Surface.Bounds;
                Place(new Vector2(toFour ? bounds.xMin + body.bounds.extents.x : bounds.xMax - body.bounds.extents.x,
                    bounds.yMin + 6 + body.bounds.extents.y + .02f));
                yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoom", toFour ? 4 : 3, toFour ? Vector2.left : Vector2.right, null));
                Assert.That(_game.Room, Is.SameAs(rooms[toFour ? 4 : 3]), $"往返次数 {pass}");
                Assert.That(body.bounds.min.y - _game.Room.Surface.Bounds.yMin, Is.EqualTo(6.02f).Within(.001f));
                if (toFour)
                {
                    var key = _game.Room.Platforms.First(platform => platform.name == "ZAxisMovement");
                    Place(key.transform.position);
                    Call(_game, "SetStageView", StageView.Overview, true);
                    _game.Room.Surface.Rebuild();
                    Assert.That(_game.Room.Overlaps(body.bounds, -1, false), Is.False, "能力触发物件不应阻止返回二维");
                    Call(_game, "SetStageView", StageView.World, true);
                }
            }
        }
        /** @brief 开始、暂停、继续、回主菜单使用真实UI绑定的方法。 */
        [UnityTest] public IEnumerator Menu_StartPauseResumeAndHome()
        {
            var menu = Read<LightMenu>(_game, "_menu");
            menu.Home(); Assert.That(Read<GameObject>(menu, "_start").activeSelf, Is.True);
            menu.Begin(); yield return null; Assert.That(Read<GameObject>(menu, "_start").activeSelf, Is.False);
            menu.Pause(); float before = _game.PlaySeconds; yield return new WaitForSecondsRealtime(.1f);
            Assert.That(_game.PlaySeconds, Is.EqualTo(before)); Assert.That(Read<GameObject>(menu, "_pause").activeSelf, Is.True);
            menu.Resume(); yield return null; Assert.That(Read<GameObject>(menu, "_pause").activeSelf, Is.False);
            menu.ShowEnd(65, 3, 2); Assert.That(Read<GameObject>(menu, "_end").activeSelf, Is.True);
            Assert.That(Read<TMP_Text>(menu, "_statistics").text, Is.EqualTo(GameText.Get("LightMenu.ShowEnd.008", 1, 5, 2, 3)));
        }
        /** @brief 正式计时到期断光仍保持2D视角，幕布提示可见且角色冻结。 */
        [UnityTest] public IEnumerator Blackout_DoesNotSwitchView_AndShowsScreenHint()
        {
            _game.SetPaused(false);
            var repair = Read<RepairSession>(_game, "_repair");
            // 推进正式维修计时至事件，不改配置或直接伪造供光状态。
            Call(_game, "TickRepair", repair.RemainingSeconds + .01f);
            yield return null;
            Assert.That(_game.Room.Powered, Is.False);
            Assert.That(_stage.View, Is.EqualTo(StageView.World)); Assert.That(_game.Editing, Is.False);
            var hint = Read<TMP_Text>(_stage, "_blackoutHint");
            Assert.That(hint.gameObject.activeInHierarchy, Is.True); Assert.That(hint.text, Does.Not.StartWith("["));
            Assert.That(Read<Camera>(_stage, "_viewCamera").orthographic, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Vector2 stopped = _player.position; yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Vector2.Distance(stopped, _player.position), Is.LessThan(.0001f));
        }
        /** @brief 四机位投影类型、顶面方向以及Tab语义返回总览。 */
        [UnityTest] public IEnumerator Views_ReturnToOverview_AndRepairFacesTop()
        {
            _game.SetPaused(false);
            foreach (StageView view in new[] { StageView.Overview, StageView.Repair, StageView.Projection, StageView.World })
            {
                Call(_game, "SetStageView", view, true); yield return null;
                var camera = Read<Camera>(_stage, "_viewCamera");
                Assert.That(_stage.View, Is.EqualTo(view));
                Assert.That(camera.orthographic, Is.EqualTo(view == StageView.World || view == StageView.Repair));
                if (view == StageView.Repair) Assert.That(Vector3.Dot(camera.transform.forward, Read<Transform>(_stage, "_repairFrame").forward), Is.GreaterThan(.999f));
                if (view != StageView.Overview)
                {
                    _game.ToggleMode(); yield return new WaitForSecondsRealtime(1);
                    Assert.That(_stage.View, Is.EqualTo(StageView.Overview));
                }
            }
        }
        /** @brief 已有存档点通过正式接触逻辑激活，死亡重试回该点，新局清空。 */
        [UnityTest] public IEnumerator Checkpoint_ContactRespawnAndNewGameReset()
        {
            var checkpoint = _game.Room.GetComponentInChildren<RoomCheckpoint>(); Assert.That(checkpoint, Is.Not.Null);
            Place(checkpoint.transform.position); Call(_game, "CollectCheckpoints"); Assert.That(_game.ActiveCheckpoint, Is.EqualTo(checkpoint));
            Place(_player.position + Vector2.right * 2); _game.RetryCheckpoint();
            Assert.That(Vector2.Distance(_player.position, checkpoint.Spawn), Is.LessThan(.001f));
            _game.SetPaused(true); _game.Restart(); _game.SetPaused(true);
            Assert.That(_game.ActiveCheckpoint, Is.Null); Assert.That(checkpoint.Activated, Is.False);
            Assert.That(Vector2.Distance(_player.position, _game.Room.Spawn), Is.LessThan(.001f));
            yield return null;
        }
        /** @brief 停用存档点后回退到本关出生点。 */
        [UnityTest] public IEnumerator DisabledCheckpoint_FallsBackToSpawn()
        {
            var checkpoint = _game.Room.GetComponentInChildren<RoomCheckpoint>(); Assert.That(checkpoint, Is.Not.Null);
            Place(checkpoint.transform.position); Call(_game, "CollectCheckpoints"); checkpoint.enabled = false;
            _game.Respawn(); Assert.That(_game.ActiveCheckpoint, Is.Null);
            Assert.That(Vector2.Distance(_player.position, _game.Room.Spawn), Is.LessThan(.001f));
            checkpoint.enabled = true; yield return null;
        }
        /** @brief 房间切换保存选色、共享维修计时以及跨房复活目标。 */
        [UnityTest] public IEnumerator RoomTravel_PreservesColorRepairAndCheckpoint()
        {
            var rooms = Read<LightRoom[]>(_game, "_rooms"); Assert.That(rooms.Length, Is.GreaterThanOrEqualTo(2));
            var checkpoint = rooms[0].GetComponentInChildren<RoomCheckpoint>(); Assert.That(checkpoint, Is.Not.Null);
            Place(checkpoint.transform.position); Call(_game, "CollectCheckpoints");
            typeof(LightGame).GetField("_color", _flags).SetValue(_game, 0);
            var repair = Read<RepairSession>(_game, "_repair"); float seconds = repair.RemainingSeconds;
            Place(rooms[0].Spawn);
            yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoom", 1, Vector2.right, null));
            Assert.That(_game.Room, Is.EqualTo(rooms[1])); Assert.That(_game.SelectedColor, Is.Zero);
            Assert.That(Read<RepairSession>(_game, "_repair"), Is.SameAs(repair)); Assert.That(repair.RemainingSeconds, Is.EqualTo(seconds));
            _game.Respawn(); Assert.That(_game.Room, Is.EqualTo(rooms[0])); Assert.That(_game.SelectedColor, Is.Zero);
            Assert.That(Vector2.Distance(_player.position, checkpoint.Spawn), Is.LessThan(.001f));
            Assert.That(rooms.Count(room => room.gameObject.activeSelf), Is.EqualTo(1));
        }
        /** @brief 运行投影生成实体区域，并把2D捕获画面绑定到持久幕布纹理。 */
        [UnityTest] public IEnumerator Projection_ProducesRegionsAndPersistentScreenOutput()
        {
            var rooms = Read<LightRoom[]>(_game, "_rooms");
            int target = Array.FindIndex(rooms, room => room.Surface.Sources.Count > 0);
            Assert.That(target, Is.GreaterThanOrEqualTo(0), "样例必须包含投影源房间");
            if (_game.Room != rooms[target]) yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoom", target, Vector2.right, null));
            var surface = _game.Room.Surface; surface.Rebuild();
            Assert.That(surface.Regions.Count, Is.GreaterThan(0)); Assert.That(surface.Regions.Any(r => r.Exists), Is.True);
            var capture = Read<Camera>(_game, "_camera"); var texture = Read<RenderTexture>(_stage, "_worldTexture");
            Assert.That(capture.targetTexture, Is.EqualTo(texture)); Assert.That(texture.IsCreated(), Is.True);
            var renderer = Read<Renderer>(_stage, "_screenRenderer"); var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetTexture("_BaseMap"), Is.EqualTo(texture));
            yield return null;
        }
        /** @brief 角色四种状态参数、朝向、暂停冻结和重生复位。 */
        [UnityTest] public IEnumerator Animation_ReflectsMotionAndPause()
        {
            var motion = _player.GetComponentInChildren<LightPlayerAnimation>(); Assert.That(motion, Is.Not.Null);
            var animator = motion.GetComponent<Animator>(); var sprite = motion.GetComponent<SpriteRenderer>();
            motion.UpdateMotion(Vector2.zero, true, true); Assert.That(animator.GetBool("Grounded"), Is.True); Assert.That(animator.GetBool("Moving"), Is.False);
            motion.UpdateMotion(Vector2.left, true, true); Assert.That(animator.GetBool("Moving"), Is.True); Assert.That(sprite.flipX, Is.True);
            motion.UpdateMotion(Vector2.up, true, true); Assert.That(animator.GetBool("Grounded"), Is.False); Assert.That(animator.GetBool("Rising"), Is.True);
            motion.UpdateMotion(Vector2.down, false, true); Assert.That(animator.GetBool("Rising"), Is.False);
            motion.UpdateMotion(Vector2.right, true, false); Assert.That(animator.speed, Is.Zero); Assert.That(sprite.flipX, Is.True);
            motion.ResetPose(); Assert.That(sprite.sprite, Is.Not.Null); yield return null;
        }
        /** @brief 未解锁色不能选中；能力状态死亡保留，新局清空。 */
        [UnityTest] public IEnumerator Unlocks_PersistAcrossDeathOnly()
        {
            _game.SelectColor(3); Assert.That(_game.SelectedColor, Is.EqualTo(-1));
            var abilities = Read<ProjectionAbilityState>(_game, "_projectionAbilities"); abilities.Unlock(ProjectionAbility.MoveX);
            _game.Respawn(); Assert.That(_game.HasProjectionAbility(ProjectionAbility.MoveX), Is.True);
            _game.Restart(); Assert.That(_game.HasProjectionAbility(ProjectionAbility.MoveX), Is.False);
            yield return null;
        }
        /** @brief 虚拟键盘走真实UIS链路，验证移动、起跳和松键；结束移除测试设备。 */
        [UnityTest] public IEnumerator Input_MovesAndJumpsThroughProjectActions()
        {
            InputSettings originalSettings = InputSystem.settings;
            HideFlags originalFlags = originalSettings.hideFlags;
            InputSettings settings = UnityEngine.Object.Instantiate(originalSettings);
            // InputManager替换配置时会销毁HideAndDontSave默认对象；暂时保护原对象以便还原。
            originalSettings.hideFlags = HideFlags.DontSave;
            settings.hideFlags = HideFlags.DontSave;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = settings;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                _game.SetPaused(false);
                float timeout = Time.realtimeSinceStartup + 3;
                while (!_game.Grounded() && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(_game.Grounded(), Is.True, "角色应在样例地面落地");
                float start = _player.position.x;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                timeout = Time.realtimeSinceStartup + 3;
                while (_player.position.x <= start + .01f && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(Read<InputActionAsset>(_game, "_actions").FindAction("Light/Move").ReadValue<float>(), Is.GreaterThan(0), "虚拟键盘应通过正式动作资产");
                Assert.That(_player.position.x, Is.GreaterThan(start + .01f));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                timeout = Time.realtimeSinceStartup + 2;
                while (_player.linearVelocity.y <= 0 && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(_player.linearVelocity.y, Is.GreaterThan(0), "跳跃输入应赋予正向速度");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            }
            finally { InputSystem.RemoveDevice(keyboard); InputSystem.settings = originalSettings; originalSettings.hideFlags = originalFlags; UnityEngine.Object.Destroy(settings); }
        }
        /** @brief 正常断光后顶面拾取对应可旋转节点，修好仍保持维修视角。 */
        [UnityTest] public IEnumerator RepairPicking_RestoresPowerWithoutForcedViewChange()
        {
            var session = Read<RepairSession>(_game, "_repair"); Call(_game, "TickRepair", session.RemainingSeconds + .01f);
            Call(_game, "SetStageView", StageView.Repair, true); yield return null;
            var layout = _game.RepairLayout; var config = Read<LightStageConfig>(_stage, "_config"); var frame = Read<Transform>(_stage, "_repairFrame");
            int index = Array.FindIndex(layout.Devices, d => d.Type == RepairLayout.Kind.Mirror); Assert.That(index, Is.GreaterThanOrEqualTo(0));
            float scale = Mathf.Min(config.RepairSize.x / (layout.Area.width + config.NodeRadius * 4), config.RepairSize.y / (layout.Area.height + config.NodeRadius * 4));
            Vector3 point = frame.position + frame.rotation * (Vector3)((layout.Devices[index].Position - layout.Area.center) * scale);
            Vector2 pointer = Read<Camera>(_stage, "_viewCamera").WorldToScreenPoint(point);
            Assert.That(_stage.PickRepair(layout, pointer), Is.EqualTo(index));
            for (int i = 0; i < 8 && !session.Powered; i++) session.Rotate(index);
            Assert.That(session.Powered, Is.True); Assert.That(_stage.View, Is.EqualTo(StageView.Repair));
        }
        /** @brief 使用场景已有平台验证同色隐藏同时影响碰撞，预览不恢复碰撞。 */
        [UnityTest] public IEnumerator Platform_ColorAndPowerControlCollisionAndVisuals()
        {
            var platform = _game.Room.GetComponentInChildren<ColorPlatform>(); Assert.That(platform, Is.Not.Null);
            int original = platform.ColorIndex;
            try
            {
                platform.Configure(0); var config = Read<LightGameConfig>(_game, "_config");
                platform.Refresh(0, false, true, config); Assert.That(platform.GetComponent<Collider2D>().enabled, Is.False); Assert.That(platform.GetComponent<Renderer>().enabled, Is.False);
                platform.Refresh(0, true, true, config); Assert.That(platform.GetComponent<Collider2D>().enabled, Is.False); Assert.That(platform.GetComponent<Renderer>().enabled, Is.True);
                platform.Refresh(1, false, true, config); Assert.That(platform.GetComponent<Collider2D>().enabled, Is.True);
                platform.Refresh(1, false, false, config); Assert.That(platform.GetComponent<Collider2D>().enabled, Is.False); Assert.That(platform.GetComponent<Renderer>().enabled, Is.False);
            }
            finally { platform.Configure(original); }
            yield return null;
        }
        /** @brief 正式出口的接近、距离及停用规则，不靠任意距离触发通关。 */
        [UnityTest] public IEnumerator Exit_RequiresProximityAndEnabledMarker()
        {
            var rooms = Read<LightRoom[]>(_game, "_rooms");
            int index = Array.FindIndex(rooms, room => room.GetComponentInChildren<LevelExit>(true)); Assert.That(index, Is.GreaterThanOrEqualTo(0));
            if (_game.Room != rooms[index]) yield return _game.StartCoroutine((IEnumerator)Call(_game, "ChangeRoom", index, Vector2.right, null));
            var exit = _game.Room.GetComponentInChildren<LevelExit>(); Assert.That(exit, Is.Not.Null);
            var body = _player.GetComponent<Collider2D>().bounds; body.center = exit.transform.position;
            Assert.That(exit.Available(_game.Room.Surface, body, -1, 2), Is.True);
            body.center += Vector3.right * 100; Assert.That(exit.Available(_game.Room.Surface, body, -1, 2), Is.False);
            body.center = exit.transform.position; exit.enabled = false; Assert.That(exit.Available(_game.Room.Surface, body, -1, 2), Is.False); exit.enabled = true;
        }
        /** @brief F1走真实UIS切换，暂停与断光中继续更新，幕布锚点不阻挡鼠标。 */
        [UnityTest] public IEnumerator DebugPanel_F1TogglesWhilePausedAndDuringBlackout()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<LightDebugPanel>(); Assert.That(panel, Is.Not.Null);
            Assert.That(panel.Visible, Is.False);
            var group = panel.GetComponent<CanvasGroup>(); Assert.That(group.blocksRaycasts, Is.False);
            var rect = panel.GetComponent<RectTransform>(); Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0, 1)));
            var canvas = panel.GetComponentInParent<Canvas>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.transform.parent, Is.Null);
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(24, -24)));
            InputSettings original = InputSystem.settings; HideFlags flags = original.hideFlags;
            InputSettings settings = UnityEngine.Object.Instantiate(original); original.hideFlags = HideFlags.DontSave; settings.hideFlags = HideFlags.DontSave;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = settings; Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1)); yield return null; yield return null;
                Assert.That(panel.Visible, Is.True); Assert.That(Time.timeScale, Is.Zero);
                var text = panel.GetComponentInChildren<TMP_Text>(); Assert.That(text.text, Does.Contain(GameText.Get("Debug.Paused"))); Assert.That(text.font, Is.EqualTo(GameText.Font));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                var session = Read<RepairSession>(_game, "_repair"); Call(_game, "TickRepair", session.RemainingSeconds + .01f); _game.SetPaused(false);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(text.text, Does.Contain(GameText.Get("Debug.Blackout"))); Assert.That(_stage.View, Is.EqualTo(StageView.World));
                text.ForceMeshUpdate(); Assert.That(text.isTextOverflowing, Is.False, "当前快照必须容纳于固定面板");
                if (Application.isBatchMode)
                {
                    CaptureDebugView(Read<Camera>(_stage, "_viewCamera"));
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1)); yield return null; yield return null;
                Assert.That(panel.Visible, Is.False); Assert.That(group.alpha, Is.Zero);
            }
            finally { InputSystem.RemoveDevice(keyboard); InputSystem.settings = original; original.hideFlags = flags; UnityEngine.Object.Destroy(settings); }
        }
        /** @brief 批处理下通过URP正式渲染请求保存幕布面板画面，不依赖GameView截图队列。@param camera 正式显示相机 */
        private static void CaptureDebugView(Camera camera)
        {
            int width = camera.pixelWidth, height = camera.pixelHeight;
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create(); Canvas.ForceUpdateCanvases();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../TestResults"));
                System.IO.Directory.CreateDirectory(folder); System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "DebugPanel.png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels); }
        }
    }
}
