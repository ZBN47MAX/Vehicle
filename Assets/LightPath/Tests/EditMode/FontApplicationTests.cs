using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
namespace DelayNoMore.LightPath.Tests
{
    /** @brief 文案编辑器字体应用的幂等性与动态文案保护。 */
    public sealed class FontApplicationTests
    {
        /** @brief 使用已有UI预制体的临时编辑实例；不写回预制体。 */
        [Test] public void ApplyFont_PreservesTextLayoutAndHiddenLabels()
        {
            GameObject root = PrefabUtility.LoadPrefabContents("Assets/LightPath/Prefabs/UI/LightInterface.prefab");
            var catalog = ScriptableObject.CreateInstance<GameTextCatalog>();
            try
            {
                catalog.Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                Assert.That(catalog.Font, Is.Not.Null);
                TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true); Assert.That(labels.Length, Is.GreaterThan(0));
                string[] texts = labels.Select(t => t.text).ToArray(); Vector2[] sizes = labels.Select(t => t.rectTransform.sizeDelta).ToArray();
                MethodInfo apply = typeof(Editor.GameTextFontApply).GetMethod("ApplyRoot", BindingFlags.Static | BindingFlags.NonPublic);
                apply.Invoke(null, new object[] { root, catalog, false }); apply.Invoke(null, new object[] { root, catalog, false });
                for (int i = 0; i < labels.Length; i++)
                {
                    Assert.That(labels[i].font, Is.EqualTo(catalog.Font)); Assert.That(labels[i].fontSharedMaterial, Is.EqualTo(catalog.Font.material));
                    Assert.That(labels[i].text, Is.EqualTo(texts[i])); Assert.That(labels[i].rectTransform.sizeDelta, Is.EqualTo(sizes[i]));
                    Assert.That(labels[i].GetComponents<GameTextLabel>().Length, Is.EqualTo(1));
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); Object.DestroyImmediate(catalog); GameText.Bind(Resources.Load<GameTextCatalog>("GameTextCatalog")); }
        }
    }
}
