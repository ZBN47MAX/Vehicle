using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace DelayNoMore.LightPath
{
    /** @brief 完整界面及独立 UI 场景的交互控制器。 */
    public sealed class LightMenu : MonoBehaviour
    {
        /** @brief 可选玩法引用。 */
        [SerializeField, Tooltip("UI 场景允许留空")] private LightGame _game;
        /** @brief 共享颜色配置。 */
        [SerializeField, Tooltip("四预设色配置")] private LightGameConfig _config;
        /** @brief 开始面板。 */
        [SerializeField, Tooltip("开始界面")] private GameObject _start;
        /** @brief 暂停面板。 */
        [SerializeField, Tooltip("暂停界面")] private GameObject _pause;
        /** @brief 结束面板。 */
        [SerializeField, Tooltip("结束界面")] private GameObject _end;
        /** @brief 轮盘面板。 */
        [SerializeField, Tooltip("换色轮盘")] private GameObject _palette;
        /** @brief 状态文本。 */
        [SerializeField, Tooltip("模式和操作提示")] private TMP_Text _status;
        /** @brief 房间标题。 */
        [SerializeField, Tooltip("章节标题")] private TMP_Text _roomTitle;
        /** @brief 关卡提示。 */
        [SerializeField, Tooltip("玩法提示")] private TMP_Text _hint;
        /** @brief 进度文本。 */
        [SerializeField, Tooltip("进度与收集")] private TMP_Text _progress;
        /** @brief 结算文本。 */
        [SerializeField, Tooltip("结束统计")] private TMP_Text _statistics;
        /** @brief 轮盘中心说明。 */
        [SerializeField, Tooltip("换色状态")] private TMP_Text _wheelStatus;
        /** @brief 轮盘选项图像。 */
        [SerializeField, Tooltip("四个色彩选项")] private Image[] _options;
        /** @brief 轮盘标签。 */
        [SerializeField, Tooltip("四个色名标签")] private TMP_Text[] _optionLabels;
        /** @brief 当前唯一动作资产，按界面状态控制导航动作。 */
        private InputActionAsset _actions;
        /** @brief 供运行时维修视图复用现有中文 HUD 字体。 */
        public TMP_FontAsset HudFont => GameText.Font;
        /** @brief 色名说明。 */
        private static string[] _names => new[] { GameText.Get("LightMenu._names.001"), GameText.Get("LightMenu._names.002"), GameText.Get("LightMenu._names.003"), GameText.Get("LightMenu._names.004") };
        /** @brief 设置基础引用。 @param game 会话 @param start 开始 @param pause 暂停 @param end 结束 @param palette 轮盘 @param status 状态 */
        public void Configure(LightGame game, GameObject start, GameObject pause, GameObject end, GameObject palette, TMP_Text status)
        {
            _game = game;
            _start = start;
            _pause = pause;
            _end = end;
            _palette = palette;
            _status = status;
        }
        /** @brief 设置 HUD 引用。 @param roomTitle 标题 @param hint 提示 @param progress 进度 @param statistics 结算 @param wheelStatus 轮盘提示 @param options 选项 @param labels 标签 */
        public void ConfigureHud(TMP_Text roomTitle, TMP_Text hint, TMP_Text progress, TMP_Text statistics, TMP_Text wheelStatus, Image[] options, TMP_Text[] labels)
        {
            _roomTitle = roomTitle;
            _hint = hint;
            _progress = progress;
            _statistics = statistics;
            _wheelStatus = wheelStatus;
            _options = options;
            _optionLabels = labels;
        }
        /** @brief 设置配置和会话。 @param config 配置 @param game 会话 */
        public void BindConfig(LightGameConfig config, LightGame game)
        {
            _config = config;
            _game = game;
            for (int i = 0; i < 4; i++)
                _options[i].color = config.DisplayColor(config.GetColor(i));
        }
        /** @brief 从当前唯一动作资产刷新已生成开始界面的说明。 @param actions 输入资产 */
        public void BindInputHelp(UnityEngine.InputSystem.InputActionAsset actions)
        {
            _actions = actions;
            Transform entry = _start.transform.Find("Help");
            if (!entry || !entry.TryGetComponent(out TMP_Text help))
                return;
            /** @brief 读取当前绑定。 @param name 动作名 @return 绑定显示 */
            string Binding(string name) => actions.FindAction("Light/" + name, true).GetBindingDisplayString();
            help.text = GameText.Get("LightMenu.BindInputHelp.005", Binding("Move"), Binding("Jump"), Binding("Palette"), Binding("Mode"), Binding("Focus"), Binding("Drag"), Binding("DeviceHorizontal"), Binding("DeviceVertical"), Binding("DeviceDepth"), Binding("DeviceRotationX"), Binding("DeviceRotationY"), Binding("DeviceRotation"));
            if (_game && _game.StageMode)
                help.text = GameText.Get("LightMenu.BindInputHelp.006", Binding("Move"), Binding("Jump"), Binding("Palette"), Binding("Mode"), Binding("Drag"), Binding("Drag"), Binding("Drag"));
        }
        /** @brief 初始化菜单。 */
        private void Start()
        {
            if (InputSystem.actions)
                BindInputHelp(InputSystem.actions);
            ShowStart();
        }
        /** @brief 装置模式滤镜按钮入口。 */
        public void DevicePalette()
        {
            if (_game)
                _game.OpenDevicePalette();
            else
                PreviewPalette();
        }
        /** @brief 开始全新游戏或界面预览。 */
        public void Begin()
        {
            if (_game)
                _game.Restart();
            else
            {
                ShowGame();
                _hint.text = GameText.Get("LightMenu.Begin.007");
            }
        }
        /** @brief 选择面板的默认按钮。 @param panel 面板 */
        private void Focus(GameObject panel)
        {
            if (!EventSystem.current)
                return;
            Button button = panel.GetComponentInChildren<Button>();
            EventSystem.current.SetSelectedGameObject(button ? button.gameObject : null);
        }
        /** @brief 仅菜单接收键盘导航，避免 UI 复合绑定抢占角色移动；鼠标 UI 始终保留。
         * @param visible 是否显示菜单
         */
        private void SetNavigation(bool visible)
        {
            if (!_actions)
                return;
            foreach (string name in new[] { "Navigate", "Submit", "Cancel" })
            {
                InputAction action = _actions.FindAction("UI/" + name, false);
                if (action == null)
                    continue;
                if (visible)
                    action.Enable();
                else
                    action.Disable();
            }
            if (!visible && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(null);
        }
        /** @brief 显示开始界面。 */
        public void ShowStart()
        {
            _start.SetActive(true);
            _pause.SetActive(false);
            _end.SetActive(false);
            _palette.SetActive(false);
            SetNavigation(true);
            Focus(_start);
        }
        /** @brief 显示 HUD。 */
        public void ShowGame()
        {
            SetNavigation(false);
            _start.SetActive(false);
            _pause.SetActive(false);
            _end.SetActive(false);
            _palette.SetActive(false);
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(null);
        }
        /** @brief 预览结束面板。 */
        public void ShowEnd()
        {
            ShowEnd(360, 2, 24);
        }
        /** @brief 显示真实统计。 @param seconds 游玩秒数 @param deaths 重试次数 @param shards 光片数 */
        public void ShowEnd(float seconds, int deaths, int shards)
        {
            ShowGame();
            _end.SetActive(true);
            _statistics.text = GameText.Get("LightMenu.ShowEnd.008", Mathf.FloorToInt(seconds / 60), Mathf.FloorToInt(seconds % 60), shards, deaths);
            SetNavigation(true);
            Focus(_end);
        }
        /** @brief 控制暂停面板。 @param visible 是否显示 */
        public void ShowPause(bool visible)
        {
            SetNavigation(visible);
            _pause.SetActive(visible);
            if (visible)
            {
                _palette.SetActive(false);
                Focus(_pause);
            }
        }
        /** @brief 暂停或预览暂停。 */
        public void Pause()
        {
            if (_game)
                _game.SetPaused(true);
            else
                ShowPause(true);
        }
        /** @brief 继续游戏。 */
        public void Resume()
        {
            if (_game)
                _game.SetPaused(false);
            else
                ShowPause(false);
        }
        /** @brief 从当前存档重试。 */
        public void Retry()
        {
            if (_game)
                _game.RetryCheckpoint();
            else
                ShowGame();
        }
        /** @brief 返回主菜单。 */
        public void Home()
        {
            if (_game)
                _game.MainMenu();
            else
                ShowStart();
        }
        /** @brief 退出程序。 */
        public void Quit()
        {
            Application.Quit();
        }
        /** @brief 预览轮盘。 */
        public void PreviewPalette()
        {
            ShowGame();
            _palette.SetActive(true);
            _wheelStatus.text = GameText.Get("LightMenu.PreviewPalette.009");
        }
        /** @brief 从屏幕指针获取候选颜色。 @param point 屏幕位置 @return 色号或 -1 */
        public int PointedColor(Vector2 point)
        {
            RectTransform rect = (RectTransform)_palette.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, point, null, out Vector2 local) || local.magnitude < 45)
                return -1;
            float degrees = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            return ((Mathf.RoundToInt((90 - degrees) / 90) % 4) + 4) % 4;
        }
        /** @brief 刷新玩法信息和色盘状态。 @param mode 模式 @param title 房间 @param hint 提示 @param help 绑定帮助 @param preview 换色 @param allowed 允许换色 @param candidate 候选 @param selected 当前 @param shards 光片 @param room 关卡编号 @param rooms 总数 @param seconds 秒数 */
        public void UpdateGame(string mode, string title, string hint, string help, bool preview, bool allowed, int candidate, int selected, int shards, int room, int rooms, float seconds)
        {
            _roomTitle.text = title;
            _hint.text = hint;
            _status.text = mode + "  /  " + help;
            _progress.text = GameText.Get("LightMenu.UpdateGame.010", room, rooms, shards, Mathf.FloorToInt(seconds / 60), Mathf.FloorToInt(seconds % 60));
            _palette.SetActive(preview);
            if (!preview)
                return;
            _wheelStatus.text = allowed ? GameText.Get("LightMenu.UpdateGame.011") : GameText.Get("LightMenu.UpdateGame.012");
            for (int i = 0; i < 4; i++)
            {
                bool unlocked = !_game || _game.IsUnlocked(i);
                _options[i].color = unlocked ? _config.DisplayColor(_config.GetColor(i)) : new Color(.22f, .24f, .28f);
                _options[i].transform.localScale = Vector3.one * (candidate == i ? 1.16f : 1);
                _options[i].GetComponent<Button>().interactable = allowed && unlocked;
                _optionLabels[i].text = unlocked ? _names[i] + (selected == i ? " •" : "") : GameText.Get("LightMenu.UpdateGame.013");
            }
        }
    }
}
