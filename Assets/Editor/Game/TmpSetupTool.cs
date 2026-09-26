using TMPro;
using UnityEditor;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 导入 TextMeshPro 的必需资源（TMP Settings / 默认字体 / 着色器）。
    ///
    /// 为什么必须做这一步：只要工程里没有 `TMP Settings.asset`，
    /// `TextMeshProUGUI.Awake()` 在读 `TMP_Settings.instance` 时就会抛空引用，
    /// 结果是**所有 TMP 文本一个字都不显示**，而且报错发生在 Awake，
    /// 表现上非常像「字体没配好」，容易误查很久。
    ///
    /// 正常在编辑器里是 Window > TextMeshPro > Import TMP Essential Resources，
    /// 这里包一层是为了能在批处理里执行。
    /// </summary>
    public static class TmpSetupTool
    {
        const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        [MenuItem("工具/炸弹酒馆/5b. 导入 TextMeshPro 必需资源", false, 15)]
        public static void ImportEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath) != null)
            {
                Debug.Log("[TMP] 必需资源已存在，跳过导入。");
                return;
            }

            // 签名：(importEssentials, importExamples, interactive)
            TMP_PackageResourceImporter.ImportResources(true, false, false);

            AssetDatabase.Refresh();

            bool ok = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath) != null;
            Debug.Log(ok
                ? "[TMP] 必需资源导入完成。"
                : "[TMP] 导入后仍未找到 TMP Settings，请手动执行 Window > TextMeshPro > Import TMP Essential Resources。");
        }
    }
}
