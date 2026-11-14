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
        /** @brief 属于本界面的鼠标视角按钮组。 */
        private GameObject _viewButtons;
        /** @brief 三个关注入口。 */
        private Button[] _viewChoices;
        /** @brief 按钮对应的视角。 */
        private readonly StageView[] _views = { StageView.World, StageView.Projection, StageView.Repair };
        /** @brief 左侧移动、右侧旋转的两行三列键盘显示顺序，键名继续读取UIS。 */
        private static readonly (ProjectionAbility Ability, string Action, string Part)[] _axisKeys =
        {
            (ProjectionAbility.MoveZ, "DeviceDepth", "Negative"),
            (ProjectionAbility.MoveY, "DeviceVertical", "Positive"),
            (ProjectionAbility.MoveZ, "DeviceDepth", "Positive"),
            (ProjectionAbility.MoveX, "DeviceHorizontal", "Negative"),
            (ProjectionAbility.MoveY, "DeviceVertical", "Negative"),
            (ProjectionAbility.MoveX, "DeviceHorizontal", "Positive"),
            (ProjectionAbility.RotateZ, "DeviceRotation", "Positive"),
            (ProjectionAbility.RotateX, "DeviceRotationX", "Positive"),
            (ProjectionAbility.RotateZ, "DeviceRotation", "Negative"),
            (ProjectionAbility.RotateY, "DeviceRotationY", "Positive"),
            (ProjectionAbility.RotateX, "DeviceRotationX", "Negative"),
            (ProjectionAbility.RotateY, "DeviceRotationY", "Negative")
        };
        /** @brief 本菜单拥有的键盘示意UI。 */
        private GameObject _axisGuide;
        /** @brief 对应十二个键框和键名。 */
        private readonly Image[] _axisKeyBorders = new Image[12];
        /** @brief 十二个键位文字。 */
        private readonly TMP_Text[] _axisKeyLabels = new TMP_Text[12];
        /** @brief 返回总览的实际绑定文字。 */
        private TMP_Text _returnKeyLabel;
        /** @brief 切换物件的实际滚轮绑定文字。 */
        private TMP_Text _scrollKeyLabel;
        /** @brief 输入绑定变化后才重新生成键名。 */
        private bool _axisBindingsDirty = true;
        /** @brief 是否已应用投影调整时的底部布局。 */
        private bool _axisLayoutApplied;
        /** @brief 原底部说明的坐标。 */
        private Vector2 _statusPosition;
        /** @brief 原底部说明的尺寸。 */
        private Vector2 _statusSize;
        /** @brief 原底部说明的对齐方式。 */
        private TextAlignmentOptions _statusAlignment;
        /** @brief 创建不接收点击的键位或分组文字。@param parent 父级 @param name UI名称 @param position 顶部坐标 @param size 文字区域 @return 文字组件 */
        private TMP_Text CreateAxisLabel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.gameObject.layer = _status.gameObject.layer;
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 1);
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size;
            GameText.ApplyFont(label);
            label.fontSize = 20;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }
        /** @brief 创建非交互的线框按键。@param parent 父级 @param name UI名称 @param position 顶部坐标 @param size 按键尺寸 @param border 返回外框图像 @return 键名文字 */
        private TMP_Text CreateAxisKey(Transform parent, string name, Vector2 position, Vector2 size, out Image border)
        {
            var key = new GameObject(name, typeof(RectTransform), typeof(Image));
            key.layer = _status.gameObject.layer;
            var rect = (RectTransform)key.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            border = key.GetComponent<Image>();
            border.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.gameObject.layer = _status.gameObject.layer;
            fill.transform.SetParent(rect, false);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.one;
            fill.rectTransform.offsetMax = -Vector2.one;
            fill.color = new Color(.04f, .07f, .1f, .95f);
            fill.raycastTarget = false;
            TMP_Text label = CreateAxisLabel(rect, "Label", Vector2.zero, size);
            label.enableAutoSizing = true;
            label.fontSizeMin = 10;
            label.fontSizeMax = 20;
            return label;
        }
        /** @brief 在现有底部HUD中创建左右两组两行三列的键盘图示。 */
        private void CreateAxisGuide()
        {
            if (_axisGuide || !_status) return;
            _statusPosition = _status.rectTransform.anchoredPosition;
            _statusSize = _status.rectTransform.sizeDelta;
            _statusAlignment = _status.alignment;
            _axisGuide = new GameObject("AxisKeyboardGuide", typeof(RectTransform));
            _axisGuide.layer = _status.gameObject.layer;
            var root = (RectTransform)_axisGuide.transform;
            root.SetParent(_status.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(640, 100);
            root.anchoredPosition = new Vector2(0, -72);
            for (int group = 0; group < 2; group++)
            {
                TMP_Text title = CreateAxisLabel(root, group == 0 ? "MoveTitle" : "RotateTitle", new Vector2(160 + group * 192, 0), new Vector2(128, 22));
                title.fontSize = 18;
                title.text = GameText.Get(group == 0 ? "LightMenu.AxisGuide.Move" : "LightMenu.AxisGuide.Rotate");
            }
            for (int i = 0; i < _axisKeys.Length; i++)
            {
                _axisKeyLabels[i] = CreateAxisKey(root, "Key" + i,
                    new Vector2(160 + i / 6 * 192 + i % 3 * 44, -26 - i % 6 / 3 * 38),
                    new Vector2(40, 34), out _axisKeyBorders[i]);
            }
            TMP_Text returnTitle = CreateAxisLabel(root, "ReturnTitle", Vector2.zero, new Vector2(112, 22));
            returnTitle.fontSize = 18;
            returnTitle.text = GameText.Get("LightMenu.AxisGuide.Return");
            _returnKeyLabel = CreateAxisKey(root, "ReturnKey", new Vector2(0, -26), new Vector2(112, 34), out _);
            TMP_Text scrollTitle = CreateAxisLabel(root, "ScrollTitle", new Vector2(528, 0), new Vector2(112, 22));
            scrollTitle.fontSize = 18;
            scrollTitle.text = GameText.Get("LightMenu.AxisGuide.Select");
            _scrollKeyLabel = CreateAxisKey(root, "ScrollControl", new Vector2(528, -26), new Vector2(112, 72), out _);
            _axisBindingsDirty = true;
        }
        /** @brief 仅投影调整显示键盘图示，退出后恢复原HUD布局。 */
        private void UpdateAxisGuide()
        {
            bool visible = _game && _game.ShowViewButtons && _game.CurrentView == StageView.Projection && _status;
            if (visible) CreateAxisGuide();
            if (_axisGuide && _axisGuide.activeSelf != visible) _axisGuide.SetActive(visible);
            if (_status && visible != _axisLayoutApplied)
            {
                _status.rectTransform.anchoredPosition = visible ? _statusPosition + Vector2.up * 72 : _statusPosition;
                _status.rectTransform.sizeDelta = visible ? new Vector2(_statusSize.x, 28) : _statusSize;
                _status.alignment = visible ? TextAlignmentOptions.Center : _statusAlignment;
                _axisLayoutApplied = visible;
            }
            if (!visible) return;
            for (int i = 0; i < _axisKeys.Length; i++)
            {
                var binding = _axisKeys[i];
                Color color = _game.HasProjectionAbility(binding.Ability) ? Color.green : Color.white;
                if (_axisKeyBorders[i].color != color) _axisKeyBorders[i].color = color;
                if (_axisKeyLabels[i].color != color) _axisKeyLabels[i].color = color;
                if (_axisBindingsDirty) _axisKeyLabels[i].text = _game.ProjectionBindingLabel(binding.Action, binding.Part);
            }
            if (_axisBindingsDirty)
            {
                _returnKeyLabel.text = _game.ProjectionBindingLabel("Mode", null);
                _scrollKeyLabel.text = _game.ProjectionBindingLabel("Scroll", null);
            }
            _axisBindingsDirty = false;
        }
        /** @brief 动作重新绑定后刷新键盘文字。@param changed 变化对象 @param change 变化类型 */
        private void OnAxisBindingChanged(object changed, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged) _axisBindingsDirty = true;
        }
        /** @brief 监听输入绑定变化。 */
        private void OnEnable()
        {
            InputSystem.onActionChange += OnAxisBindingChanged;
            _axisBindingsDirty = true;
        }
        /** @brief 取消监听并恢复底部说明位置。 */
        private void OnDisable()
        {
            InputSystem.onActionChange -= OnAxisBindingChanged;
            if (_axisGuide) _axisGuide.SetActive(false);
            if (_axisLayoutApplied && _status)
            {
                _status.rectTransform.anchoredPosition = _statusPosition;
                _status.rectTransform.sizeDelta = _statusSize;
                _status.alignment = _statusAlignment;
            }
            _axisLayoutApplied = false;
        }
        /** @brief 释放本菜单拥有的键盘图示。 */
        private void OnDestroy()
        {
            if (_axisGuide) Destroy(_axisGuide);
        }
        /** @brief 创建仅属于本菜单的鼠标入口，不安装玩法组件。 */
        private void CreateViewButtons()
        {
            if (_viewButtons || !_game || !_game.StageMode)
                return;
            _viewButtons = new GameObject("ViewButtons", typeof(RectTransform));
            var rect = (RectTransform)_viewButtons.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
            rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-24, 0);
            rect.sizeDelta = new Vector2(220, 192);
            _viewChoices = new Button[3];
            string[] labels = { GameText.Get("View.World"), GameText.Get("View.Projection"), GameText.Get("View.Repair") };
            for (int i = 0; i < _views.Length; i++)
            {
                var item = new GameObject(labels[i], typeof(RectTransform), typeof(Image), typeof(Button));
                var itemRect = (RectTransform)item.transform;
                itemRect.SetParent(rect, false);
                itemRect.anchorMin = itemRect.anchorMax = new Vector2(.5f, 1);
                itemRect.pivot = new Vector2(.5f, 1);
                itemRect.anchoredPosition = new Vector2(0, -i * 64);
                itemRect.sizeDelta = new Vector2(220, 54);
                item.GetComponent<Image>().color = new Color(.12f, .17f, .21f, .96f);
                Button button = item.GetComponent<Button>();
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                StageView view = _views[i];
                button.onClick.AddListener(() => _game.ChooseStageView(view));
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.transform.SetParent(item.transform, false);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.sizeDelta = Vector2.zero;
                label.font = HudFont;
                label.fontSize = 24;
                label.alignment = TextAlignmentOptions.Center;
                label.text = labels[i];
                label.raycastTarget = false;
                _viewChoices[i] = button;
            }
        }
        /** @brief 按游戏生命周期更新入口，菜单和二维关注期间隐藏。 */
        private void LateUpdate()
        {
            UpdateAxisGuide();
            CreateViewButtons();
            if (!_viewButtons)
                return;
            _viewButtons.SetActive(_game && _game.ShowViewButtons);
            for (int i = 0; i < _viewChoices.Length; i++)
                _viewChoices[i].interactable = _game && _game.CurrentView != _views[i];
        }
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
            _wheelStatus.text = allowed ? GameText.Get("LightMenu.UpdateGame.011") : GameText.Feedback(GameText.Get("LightMenu.UpdateGame.012"), false);
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
