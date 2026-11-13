using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
        /** @brief 设置初始隐藏状态并防止遮挡玩法点击。 */
        private void Awake()
        {
            if (!_group || !_text || !_actions || !_game) { enabled = false; return; }
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _text.raycastTarget = false;
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
            Visible = visible;
            if (_group) _group.alpha = visible ? 1 : 0;
            if (visible && _text && _game) Refresh();
        }
        /** @brief 刷新字体与快照，动作提示随实际绑定变化。 */
        private void Refresh()
        {
            GameText.ApplyFont(_text);
            InputAction toggle = _actions.FindAction("Light/Debug", false);
            _text.text = GameText.Get("Debug.Header", toggle != null ? toggle.GetBindingDisplayString() : "") + "\n" + _game.DebugSummary();
            _nextRefresh = Time.unscaledTime + .2f;
        }
    }
}
