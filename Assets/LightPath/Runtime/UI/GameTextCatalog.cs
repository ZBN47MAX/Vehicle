using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace DelayNoMore.LightPath
{
    /** @brief 项目唯一游戏文案表，内容与用途、显示位置、格式参数集中维护。 */
    [CreateAssetMenu(menuName = "Light Path/Game Text Catalog")]
    public sealed class GameTextCatalog : ScriptableObject
    {
        /** @brief 单个文字位置的内容定义。 */
        [Serializable] public sealed class Entry
        {
            /** @brief 稳定标识，用于代码和场景组件绑定，不作为显示内容。 */
            public string Key;
            /** @brief 一级分类：实际出现的界面或区域。 */
            public string Screen;
            /** @brief 是否属于正常游玩会出现的文字；断光等玩法反馈也属于正常。 */
            public bool NormalPlay = true;
            /** @brief 三级分类：承载文字的组件及其UI用途。 */
            public string Component;
            /** @brief 面向内容编辑者的文字位置名称。 */
            public string Title;
            /** @brief 触发时机、用途与显示位置说明。 */
            public string Purpose;
            /** @brief 显示区域和用途。 */
            public string Location;
            /** @brief 动态参数说明；花括号编号必须保留。 */
            public string Parameters;
            /** @brief 可编辑文案。 */
            [TextArea(2, 8)] public string Value;
        }
        /** @brief 所有游戏文字使用的 TMP 字体，包含菜单、HUD、幕布和维修界面。 */
        [Tooltip("全游戏统一 TMP 字体；替换后点击下方一键应用按钮。字体必须包含需要显示的中文字形。 ")]
        public TMP_FontAsset Font;
        /** @brief 全部游戏内文字，运行时只读。 */
        public List<Entry> Entries = new List<Entry>();
        /** @brief 文案索引缓存。 */
        private Dictionary<string, Entry> _lookup;
        /** @brief 查询文字，不存在时明确显示缺失标识。@param key 稳定标识 @return 文字 */
        public string Get(string key)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, Entry>();
                foreach (Entry entry in Entries)
                    if (!string.IsNullOrEmpty(entry.Key)) _lookup[entry.Key] = entry;
            }
            return _lookup.TryGetValue(key, out Entry result) ? result.Value : "[" + key + "]";
        }
        /** @brief Inspector 修改后重建查询缓存。 */
        private void OnValidate() => _lookup = null;
    }
    /** @brief 文案读取入口；键位及数值由调用方提供，模板全部来自唯一SO。 */
    public static class GameText
    {
        /** @brief 当前只读文案表。 */
        private static GameTextCatalog _catalog;
        /** @brief 当前统一字体；不会修改共享文案资产。 */
        public static TMP_FontAsset Font => _catalog ? _catalog.Font : null;
        /** @brief 将字体和对应共享材质一起应用，避免残留旧字形图集。@param text 目标文字 */
        public static void ApplyFont(TMP_Text text)
        {
            if (!text || !Font) return;
            text.font = Font;
            text.fontSharedMaterial = Font.material;
        }
        /** @brief 场景加载前读取项目唯一文案资产。 */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load() => _catalog = Resources.Load<GameTextCatalog>("GameTextCatalog");
        /** @brief 编辑器绑定已加载的文案表，避免组件构造期间访问Unity资源API。@param catalog 唯一资产 */
        public static void Bind(GameTextCatalog catalog) => _catalog = catalog;
        /** @brief 获取并格式化文案。@param key 稳定标识 @param values 动态键位或数值 @return 显示内容 */
        public static string Get(string key, params object[] values)
        {
            string template = _catalog ? _catalog.Get(key) : "[" + key + "]";
            if (values.Length == 0) return template;
            try { return string.Format(template, values); }
            catch (FormatException) { return "[format:" + key + "]"; }
        }
    }
}
