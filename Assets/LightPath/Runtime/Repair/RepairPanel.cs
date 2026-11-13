using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 运行时维修平面视图；仅创建维修 UI 表现，不读取物理按键。 */
    public sealed class RepairPanel
    {
        /** @brief 节点类型显示名。 */
        private static string[] _names => new[] { GameText.Get("RepairPanel._names.001"), GameText.Get("RepairPanel._names.002"), GameText.Get("RepairPanel._names.003"), GameText.Get("RepairPanel._names.004"), GameText.Get("RepairPanel._names.005") };
        /** @brief 兼容维修界面的独立 UI 画布。 */
        private GameObject _root;
        /** @brief 线条和背景父节点，位于文字下方。 */
        private Transform _geometry;
        /** @brief TMP 标签父节点。 */
        private Transform _texts;
        /** @brief 可复用图形池。 */
        private readonly List<Image> _images = new List<Image>();
        /** @brief 可复用 TMP 标签池。 */
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        /** @brief 本帧图形数量。 */
        private int _imageCount;
        /** @brief 本帧文字数量。 */
        private int _labelCount;
        /** @brief 控制兼容界面可见性，返回父视角或暂停后不残留文字。@param visible 是否可见 */
        public void SetVisible(bool visible)
        {
            if (_root) _root.SetActive(visible);
        }
        /** @brief 销毁仅用于表现的运行时画布。 */
        public void Dispose()
        {
            if (_root) Object.Destroy(_root);
            _images.Clear();
            _labels.Clear();
        }
        /** @brief 首次显示时创建 UI，保持图形在文字下方。 */
        private void EnsureCanvas()
        {
            if (_root) return;
            _root = new GameObject("Repair TMP Overlay", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            _geometry = new GameObject("Geometry", typeof(RectTransform)).transform;
            _texts = new GameObject("TMP Text", typeof(RectTransform)).transform;
            foreach (Transform child in new[] { _geometry, _texts })
            {
                child.SetParent(_root.transform, false);
                RectTransform rect = (RectTransform)child;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
        }
        /** @brief 把左上原点像素矩形映射到 UI。@param target UI 矩形 @param rect 屏幕矩形 */
        private static void Place(RectTransform target, Rect rect)
        {
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(0, 1);
            target.anchoredPosition = new Vector2(rect.x, -rect.y);
            target.sizeDelta = rect.size;
            target.localRotation = Quaternion.identity;
        }
        /** @brief 更新一个 TMP 标签，统一采用文案 SO 字体。@param rect 区域 @param text 文案 @param size 字号 */
        private void Label(Rect rect, string text, int size)
        {
            if (_labelCount == _labels.Count)
            {
                GameObject item = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                item.transform.SetParent(_texts, false);
                TextMeshProUGUI label = item.GetComponent<TextMeshProUGUI>();
                label.raycastTarget = false;
                label.alignment = TextAlignmentOptions.TopLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                _labels.Add(label);
            }
            TextMeshProUGUI current = _labels[_labelCount++];
            current.gameObject.SetActive(true);
            Place(current.rectTransform, rect);
            GameText.ApplyFont(current);
            current.fontSize = size;
            current.text = text;
        }
        /** @brief 主面板区域，避开现有顶部与底部 HUD。 */
        private static Rect PanelRect => new Rect(Screen.width * .06f, Screen.height * .22f, Screen.width * .88f, Screen.height * .62f);
        /** @brief 维修光路绘制区域。 */
        private static Rect BoardRect
        {
            get
            {
                Rect panel = PanelRect;
                return new Rect(panel.x + 30, panel.y + 65, Mathf.Max(1, panel.width - 60), Mathf.Max(1, panel.height - 110));
            }
        }
        /** @brief 将配置坐标等比映射到画布，保持上下方向。@param layout 布局 @param point 配置点 @return GUI 坐标 */
        private static Vector2 Point(RepairLayout layout, Vector2 point)
        {
            Rect board = BoardRect;
            float scale = Mathf.Min(board.width / layout.Area.width, board.height / layout.Area.height);
            Vector2 delta = point - layout.Area.center;
            return board.center + new Vector2(delta.x, -delta.y) * scale;
        }
        /** @brief 从统一 UIS 提供的屏幕位置选择最近可旋转机关。@param layout 布局 @param pointer 左下原点屏幕坐标 @return 机关编号或负一 */
        public int Pick(RepairLayout layout, Vector2 pointer)
        {
            Vector2 guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);
            if (!PanelRect.Contains(guiPoint))
                return -1;
            float nearest = 22;
            int selected = -1;
            for (int i = 0; i < layout.Devices.Length; i++)
            {
                RepairLayout.Kind type = layout.Devices[i].Type;
                if (type == RepairLayout.Kind.Source || type == RepairLayout.Kind.Exit)
                    continue;
                float distance = Vector2.Distance(guiPoint, Point(layout, layout.Devices[i].Position));
                if (distance < nearest)
                {
                    nearest = distance;
                    selected = i;
                }
            }
            return selected;
        }
        /** @brief 兼容面板的指针逆映射，和绘制共用等比缩放。 */
        public Vector2 BoardPoint(RepairLayout layout, Vector2 pointer)
        {
            float scale = Mathf.Min(BoardRect.width / layout.Area.width, BoardRect.height / layout.Area.height);
            Vector2 delta = (new Vector2(pointer.x, Screen.height - pointer.y) - BoardRect.center) / scale;
            return layout.Area.center + new Vector2(delta.x, -delta.y);
        }
        /** @brief 绘制布局与当前光路，调用者负责限制为维修模式。@param layout 布局 @param session 本局状态 @param help 来自统一动作资产的操作提示 */
        public void Draw(RepairLayout layout, RepairSession session, string help)
        {
            EnsureCanvas();
            SetVisible(true);
            _imageCount = _labelCount = 0;
            Rect panel = PanelRect;
            Fill(panel, new Color(.045f, .065f, .09f, .98f));
            string state = session.Powered ? GameText.Get("RepairPanel.Draw.006") : GameText.Get("RepairPanel.Draw.007");
            Label(new Rect(panel.x + 18, panel.y + 10, panel.width - 36, 30), GameText.Get("RepairPanel.Draw.008") + state, 18);
            Label(new Rect(panel.x + 18, panel.y + 36, panel.width - 36, 24), GameText.Get("RepairPanel.Draw.009") + layout.Phases[session.PhaseIndex].DisplayName + GameText.Get("RepairPanel.Draw.010"), 14);
            foreach (RepairPreviewSolver.Segment segment in session.Result.Segments)
                Line(Point(layout, segment.From), Point(layout, segment.To), new Color(1, .84f, .25f), 3);
            for (int i = 0; i < layout.Devices.Length; i++)
            {
                RepairLayout.Device device = layout.Devices[i];
                Vector2 point = Point(layout, device.Position);
                bool target = session.IsTarget(device.Id);
                bool lit = session.Result.Lit.Contains(device.Id);
                if (target)
                    Fill(new Rect(point.x - 15, point.y - 15, 30, 30), new Color(1, .55f, .15f));
                Color color = device.Type == RepairLayout.Kind.Source ? Color.yellow : lit ? Color.green : new Color(.25f, .65f, .85f);
                Fill(new Rect(point.x - 11, point.y - 11, 22, 22), color);
                float angle = session.Angle(i) * Mathf.Deg2Rad;
                if (device.Type != RepairLayout.Kind.Exit)
                    Line(point, point + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * 20, Color.white, 2);
                string label = device.DisplayName + " · " + _names[(int)device.Type];
                if (device.Type != RepairLayout.Kind.Exit && device.Type != RepairLayout.Kind.Source)
                    label += " " + session.Angle(i).ToString("0.0") + "°";
                float width = Mathf.Min(230, panel.width - 36);
                float x = Mathf.Clamp(point.x + 18, panel.x + 18, panel.xMax - width - 18);
                Label(new Rect(x, point.y - 12, width, 24), label, 14);
            }
            Label(new Rect(panel.x + 18, panel.yMax - 32, panel.width - 36, 28), help, 14);
            for (int i = _imageCount; i < _images.Count; i++) _images[i].gameObject.SetActive(false);
            for (int i = _labelCount; i < _labels.Count; i++) _labels[i].gameObject.SetActive(false);
        }
        /** @brief 从对象池绘制一个 UI 色块。@param rect 区域 @param color 颜色 @return 生成的矩形 */
        private RectTransform Fill(Rect rect, Color color)
        {
            if (_imageCount == _images.Count)
            {
                GameObject item = new GameObject("Shape", typeof(RectTransform), typeof(Image));
                item.transform.SetParent(_geometry, false);
                Image image = item.GetComponent<Image>();
                image.raycastTarget = false;
                _images.Add(image);
            }
            Image current = _images[_imageCount++];
            current.gameObject.SetActive(true);
            current.color = color;
            Place(current.rectTransform, rect);
            return current.rectTransform;
        }
        /** @brief 用旋转色块绘制光线，不使用旧版文字或 IMGUI。@param from 起点 @param to 终点 @param color 颜色 @param width 线宽 */
        private void Line(Vector2 from, Vector2 to, Color color, float width)
        {
            Vector2 delta = to - from;
            RectTransform rect = Fill(new Rect(from.x, from.y, delta.magnitude, width), color);
            rect.pivot = new Vector2(0, .5f);
            rect.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }
}
