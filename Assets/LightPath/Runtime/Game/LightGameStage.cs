using UnityEngine;
using UnityEngine.EventSystems;

namespace DelayNoMore.LightPath
{
    /** @brief 将四视角舞台接入现有游玩状态、输入和安全检查。 */
    public sealed partial class LightGame
    {
        /** @brief 用户手动组装的舞台；为空时兼容旧演示。 */
        [SerializeField, InspectorName("四视角舞台"), Tooltip("绑定手动拼装的LightStage。启用后左键进入关注区，Tab返回3D总览。")] private LightStage _stage;
        /** @brief 是否启用四视角模式。 */
        public bool StageMode => _stage;
        /** @brief 是否显示鼠标视角入口。 */
        public bool ShowViewButtons => _ready && _started && !_paused && !_transitioning && _stage && _stage.View != StageView.World;
        /** @brief 当前关注视角。 */
        public StageView CurrentView => _stage ? _stage.View : StageView.World;
        /** @brief 从鼠标按钮请求关注视角，复用原入口验证。 @param view 目标视角 */
        public void ChooseStageView(StageView view)
        {
            if (!ShowViewButtons || _stage.Moving)
                return;
            if (view == StageView.World && !Noclip)
            {
                if (!Room.Powered) { Notify(GameText.Get("LightGameStage.EditStage.009")); return; }
                Room.Surface.Rebuild();
                if (Room.Overlaps(_body.bounds, _color, false))
                {
                    Notify(GameText.Get("LightGameStage.EditStage.010"));
                    return;
                }
            }
            _repairDrag = -1;
            SetStageView(view);
        }
        /** @brief 本游戏是否拥有舞台资源，防止其他实例初始化失败后释放占用者资源。 */
        private bool _ownsStage;
        /** @brief 总览当前悬停入口提示。 */
        private string _stageHover = GameText.Get("LightGameStage._stageHover.001");
        /** @brief 初始化手动拼装的舞台并检查所有关卡的光源。@return 是否成功 */
        private bool InitializeStage()
        {
            if (!_stage)
                return true;
            if (_repair == null)
            {
                Debug.LogError(GameText.Get("LightGameStage.InitializeStage.002"), this);
                return false;
            }
            if (!_stage.Initialize(_camera, out string error))
            {
                Debug.LogError(error, this);
                return false;
            }
            _ownsStage = true;
            foreach (LightRoom room in _rooms)
                _stage.ConfigureProjection(room.Surface);
            UpdateCamera();
            _stage.Sync(Room);
            _stage.SetView(StageView.World, true);
            return true;
        }
        /** @brief 进入一个关注状态；调用者已完成玩法条件验证。@param view 目标视角 @param snap 是否立即定位 */
        private void SetStageView(StageView view, bool snap = false)
        {
            _pointer.Cancel();
            _preview = false;
            _jumpUntil = 0;
            if (_keyboardProjectionDirty)
            {
                Room.Surface.Rebuild();
                _keyboardProjectionDirty = false;
            }
            _editing = view != StageView.World;
            _repairFocus = view == StageView.Repair;
            UpdateCamera();
            _stage.Sync(Room);
            _stage.SetView(view, snap);
            ApplyState();
        }
        /** @brief 总览点击只切换视角，维修点击只操作顶面，调整状态沿用已解锁的物件动作。 */
        private void EditStage()
        {
            if (_stage.Moving)
                return;
            if (_stage.View == StageView.Projection)
            {
                EditProjectionKeyboard();
                return;
            }
            if (_repairDrag < 0 && EventSystem.current && EventSystem.current.IsPointerOverGameObject())
            {
                _stageHover = GameText.Get("LightGameStage.EditStage.003");
                return;
            }
            Vector2 point = Action("Pointer").ReadValue<Vector2>();
            if (_stage.View == StageView.Repair)
            {
                bool valid = _stage.TryRepairPoint(_repairLayout, point, out Vector2 boardPoint);
                OperateRepair(_stage.PickRepair(_repairLayout, point, _repair), boardPoint, valid);
                return;
            }
            bool hit = _stage.TryChoose(point, Room, out StageView view);
            _stageHover = !hit ? GameText.Get("LightGameStage.EditStage.005") : view == StageView.World ? GameText.Get("LightGameStage.EditStage.006") : view == StageView.Repair ? GameText.Get("LightGameStage.EditStage.007") : GameText.Get("LightGameStage.EditStage.008");
            if (!hit || !Action("Drag").WasPressedThisFrame())
                return;
            if (view == StageView.World && !Noclip)
            {
                if (!Room.Powered) { Notify(GameText.Get("LightGameStage.EditStage.009")); return; }
                Room.Surface.Rebuild();
                if (Room.Overlaps(_body.bounds, _color, false))
                {
                    Notify(GameText.Get("LightGameStage.EditStage.010"));
                    return;
                }
            }
            SetStageView(view);
        }
        /** @brief 四视角的模式与操作提示。@return 当前操作说明 */
        private string StageHelp()
        {
            if (_stage.Moving)
                return GameText.Get("LightGameStage.StageHelp.011");
            if (_stage.View == StageView.Overview)
                return GameText.Get("LightGameStage.StageHelp.012", Label("Drag"), Label("Pause"));
            if (_stage.View == StageView.Repair)
                return GameText.Get("LightGameStage.StageHelp.013", Label("Drag"), Label("Mode"));
            if (_stage.View == StageView.Projection)
                return GameText.Get("LightGameStage.StageHelp.014", Label("Drag"), Label("Mode"), AbilityHelp(ProjectionAbility.MoveX, "DeviceHorizontal"), AbilityHelp(ProjectionAbility.MoveY, "DeviceVertical"), AbilityHelp(ProjectionAbility.MoveZ, "DeviceDepth"), AbilityHelp(ProjectionAbility.RotateX, "DeviceRotationX"), AbilityHelp(ProjectionAbility.RotateY, "DeviceRotationY"), AbilityHelp(ProjectionAbility.RotateZ, "DeviceRotation"), Label("Scroll"));
            if (!Room.Powered)
                return GameText.Get("LightGameStage.StageHelp.015", Label("Mode"), Label("Pause"));
            return GameText.Get("LightGameStage.StageHelp.016", Label("Move"), Label("Jump"), Label("Palette"), Label("Mode"), Label("Interact"));
        }
        /** @brief 所有模拟操作完成后提交舞台绘制；菜单或暂停时仍保留画面，计时由ApplyState管理。 */
        private void LateUpdate()
        {
            if (_ready && _playerAnimation)
                _playerAnimation.UpdateMotion(_player.linearVelocity, Grounded(), Time.timeScale > 0);
            UpdateRepairPanel();
            if (!_ready || !_stage || !_stage.Ready)
                return;
            if (!_stage.Sync(Room))
            {
                Debug.LogError(GameText.Get("LightGameStage.LateUpdate.017"), this);
                _paused = true;
                ApplyState();
                return;
            }
            _stage.Animate(_paused ? 0 : Time.unscaledDeltaTime);
            var sources = Room.Surface.Sources;
            _stage.Draw(_repairLayout, _repair, sources != null && _selected >= 0 && _selected < sources.Count ? sources[_selected] : null);
            _stage.ShowBlackout(!Room.Powered, GameText.Get("LightGameStage.LateUpdate.018", Label("Mode")), _menu.HudFont);
            ApplyState();
        }
    }
}
