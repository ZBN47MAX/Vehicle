using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace DelayNoMore.LightPath
{
    /** @brief 持久屏幕空间UI的F1诊断面板；输入读取项目唯一动作资产，暂停时继续更新。 */
    public sealed class LightDebugPanel : MonoBehaviour
    {
        /** @brief 本场景游戏入口。 */
        [SerializeField] private LightGame _game;
        /** @brief 唯一动作资产；必须与Project-wide Actions相同。 */
        [SerializeField] private InputActionAsset _actions;
        /** @brief UI显隐组，根对象保持激活以监听切换。 */
        [SerializeField] private CanvasGroup _group;
        /** @brief 统一字体的TMP正文。 */
        [SerializeField] private TMP_Text _text;
        /** @brief 运行时可见状态，不写回资产。 */
        public bool Visible { get; private set; }
        /** @brief 下一次刷新时刻，使用非缩放时间。 */
        private float _nextRefresh;
        /** @brief 属于面板的操作区。 */
        private GameObject _tools;
        /** @brief 本面板按需补充的UI射线检测器。 */
        private GraphicRaycaster _ownedRaycaster;
        /** @brief 能力按钮文字。 */
        private readonly TMP_Text[] _abilityLabels = new TMP_Text[10];
        /** @brief 当前选择的关卡与房间。 */
        private int _level, _room;
        /** @brief 跳转目标显示。 */
        private TMP_Text _destination;
        /** @brief 浮空开关状态文字。 */
        private TMP_Text _noclipLabel;
        /** @brief 断光开关与恢复真实供光状态文字。 */
        private TMP_Text _blackoutLabel, _restorePowerLabel;
        /** @brief 创建一个鼠标操作按钮。 @param parent 父级 @param text 文案 @param x 横坐标 @param y 纵坐标 @param width 宽度 @param action 点击动作 @return 文字组件 */
        private TMP_Text ToolButton(Transform parent, string text, float x, float y, float width, UnityEngine.Events.UnityAction action)
        {
            var item = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, 42);
            item.GetComponent<Image>().color = new Color(.16f, .21f, .26f);
            Button button = item.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(rect, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.font = GameText.Font;
            label.fontSize = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }
        /** @brief 构造独立的可点击调试区。 */
        private void CreateTools()
        {
            Canvas canvas = _group.GetComponentInParent<Canvas>();
            if (!canvas.GetComponent<GraphicRaycaster>())
                _ownedRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            _tools = new GameObject("DebugTools", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_tools.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-20, -20);
            rect.sizeDelta = new Vector2(380, 550);
            _tools.GetComponent<Image>().color = new Color(.05f, .08f, .11f, .98f);
            for (int i = 0; i < 10; i++)
            {
                int index = i;
                _abilityLabels[i] = ToolButton(rect, "", 12 + i % 2 * 182, 12 + i / 2 * 48, 174,
                    () => { _game.DebugSetAbility(index, !_game.DebugAbility(index)); Refresh(); });
            }
            ToolButton(rect, GameText.Get("Debug.Tools.AllOn"), 12, 256, 112, () => { _game.DebugAllAbilities(true); Refresh(); });
            ToolButton(rect, GameText.Get("Debug.Tools.AllOff"), 134, 256, 112, () => { _game.DebugAllAbilities(false); Refresh(); });
            ToolButton(rect, GameText.Get("Debug.Tools.Restore"), 256, 256, 112, () => { _game.DebugRestoreAbilities(); Refresh(); });
            ToolButton(rect, GameText.Get("Debug.Tools.PrevLevel"), 12, 304, 112, () => SelectLevel(-1));
            _destination = ToolButton(rect, "", 134, 304, 112, () => { });
            ToolButton(rect, GameText.Get("Debug.Tools.NextLevel"), 256, 304, 112, () => SelectLevel(1));
            ToolButton(rect, GameText.Get("Debug.Tools.PrevRoom"), 12, 352, 174, () => SelectRoom(-1));
            ToolButton(rect, GameText.Get("Debug.Tools.NextRoom"), 194, 352, 174, () => SelectRoom(1));
            ToolButton(rect, GameText.Get("Debug.Tools.Jump"), 12, 400, 356, () => _game.DebugJump(_level, _room));
            _noclipLabel = ToolButton(rect, "Noclip", 12, 448, 174, () => { _game.SetNoclip(!_game.Noclip); Refresh(); });
            _blackoutLabel = ToolButton(rect, "", 194, 448, 174, () => { _game.DebugSetBlackout(!_game.DebugBlackout); Refresh(); });
            _restorePowerLabel = ToolButton(rect, GameText.Get("Debug.Tools.RestorePower"), 12, 496, 356, () => { _game.DebugRestorePower(); Refresh(); });
        }
        /** @brief 选择关卡并重置房间选择。 @param delta 方向 */
        private void SelectLevel(int delta)
        {
            _level = Mathf.Clamp(_level + delta, 0, Mathf.Max(0, _game.DebugLevelCount - 1));
            _room = 0;
            Refresh();
        }
        /** @brief 选择房间。 @param delta 方向 */
        private void SelectRoom(int delta)
        {
            _room = Mathf.Clamp(_room + delta, 0, Mathf.Max(0, _game.DebugRoomCount(_level) - 1));
            Refresh();
        }
        /** @brief 设置初始隐藏状态并防止遮挡玩法点击。 */
        private void Awake()
        {
            if (!_group || !_text || !_actions || !_game) { enabled = false; return; }
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _text.raycastTarget = false;
            CreateTools();
            SetVisible(false);
        }
        /** @brief 从UIS动作切换，显示时每秒刷新五次，隐藏时不生成诊断字符串。 */
        private void Update()
        {
            InputAction toggle = _actions.FindAction("Light/Debug", false);
            if (toggle != null && toggle.WasPressedThisFrame()) SetVisible(!Visible);
            if (Visible && Time.unscaledTime >= _nextRefresh) Refresh();
        }
        /** @brief 显示或隐藏，不改变游戏暂停、视角或鼠标状态。@param visible 是否显示 */
        public void SetVisible(bool visible)
        {
            if (visible && !Visible && _game)
            {
                _level = _game.DebugCurrentLevel;
                _room = _game.DebugCurrentRoom;
            }
            Visible = visible;
            if (_tools) _tools.SetActive(visible);
            if (_group) _group.alpha = visible ? 1 : 0;
            if (visible && _text && _game) Refresh();
        }
        /** @brief 刷新字体与快照，动作提示随实际绑定变化。 */
        private void Refresh()
        {
            for (int i = 0; i < _abilityLabels.Length; i++)
            {
                if (!_abilityLabels[i]) continue;
                string name = i < 6 ? ProjectionAbilityState.Label((ProjectionAbility)i) : GameText.Get("LightMenu._names.00" + (i - 5));
                _abilityLabels[i].text = name + (_game.DebugAbility(i) ? " ✓" : " ×");
                _abilityLabels[i].color = _game.DebugAbility(i) ? new Color(.55f, .9f, .67f) : Color.white;
            }
            if (_destination) _destination.text = $"Level{_level + 1}\nRoom{_room}";
            if (_noclipLabel) _noclipLabel.text = "Noclip " + (_game.Noclip ? "✓" : "×");
            if (_blackoutLabel) _blackoutLabel.text = GameText.Get(_game.DebugBlackout ? "Debug.Tools.BlackoutOn" : "Debug.Tools.BlackoutOff");
            if (_restorePowerLabel) _restorePowerLabel.GetComponentInParent<Button>().interactable = _game.DebugPowerForced;
            GameText.ApplyFont(_text);
            InputAction toggle = _actions.FindAction("Light/Debug", false);
            _text.text = GameText.Get("Debug.Header", toggle != null ? toggle.GetBindingDisplayString() : "") + "\n" + _game.DebugSummary();
            _nextRefresh = Time.unscaledTime + .2f;
        }
        /** @brief 释放本面板生成的UI。 */
        private void OnDestroy()
        {
            if (_tools) Destroy(_tools);
            if (_ownedRaycaster) Destroy(_ownedRaycaster);
        }
    }
}
