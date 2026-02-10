using UnityEngine;
using UnityEditor;
using TMPro;
using System.IO;

/// <summary>
/// 编辑器工具 — 自动从 TTF 字体生成 TMP SDF 字体资产
/// 菜单: Tools/生成中文 SDF 字体
/// </summary>
public static class FontSdfGenerator
{
    private const string TTF_PATH = "Assets/Resources/Fonts/ZhanKuGaoDuanHei.ttf";
    private const string SDF_PATH = "Assets/Resources/Fonts/ChineseFont SDF.asset";

    [MenuItem("Tools/生成中文 SDF 字体")]
    public static void Generate()
    {
        if (File.Exists(SDF_PATH))
        {
            if (!EditorUtility.DisplayDialog("字体已存在",
                "SDF 字体资产已存在，是否覆盖？", "覆盖", "取消"))
                return;
        }

        var font = AssetDatabase.LoadAssetAtPath<Font>(TTF_PATH);
        if (font == null)
        {
            EditorUtility.DisplayDialog("错误",
                $"找不到 TTF 字体: {TTF_PATH}\n请先将字体放到该路径。", "确定");
            return;
        }

        // 使用 TMP_FontAsset.CreateFontAsset 创建动态 SDF 字体
        var sdfAsset = TMP_FontAsset.CreateFontAsset(
            font,
            90,              // samplingPointSize
            9,               // atlasPadding
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
            1024,            // atlasWidth
            1024             // atlasHeight
        );

        if (sdfAsset == null)
        {
            EditorUtility.DisplayDialog("错误", "SDF 字体生成失败", "确定");
            return;
        }

        sdfAsset.name = "ChineseFont SDF";

        // 设置动态字体回退（使字体在运行时按需渲染中文字符）
        sdfAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

        // 确保目录存在
        string dir = Path.GetDirectoryName(SDF_PATH);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // 先把子对象（atlas texture、material）保存为子资产
        // 必须在 CreateAsset 之前获取引用，之后再添加
        var atlasTex = sdfAsset.atlasTexture;
        var mat = sdfAsset.material;

        AssetDatabase.CreateAsset(sdfAsset, SDF_PATH);

        if (atlasTex != null)
        {
            atlasTex.name = "ChineseFont SDF Atlas";
            AssetDatabase.AddObjectToAsset(atlasTex, sdfAsset);
        }
        if (mat != null)
        {
            mat.name = "ChineseFont SDF Material";
            AssetDatabase.AddObjectToAsset(mat, sdfAsset);
        }

        // 重新指定引用（确保序列化时引用指向已保存的子资产）
        EditorUtility.SetDirty(sdfAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完成",
            $"SDF 字体已生成: {SDF_PATH}\n" +
            $"模式: Dynamic（运行时按需渲染中文字符）", "确定");

        Debug.Log($"[FontSdfGenerator] SDF 字体已生成: {SDF_PATH}");
    }

    /// <summary>
    /// 检查 SDF 字体是否已存在
    /// </summary>
    [MenuItem("Tools/检查 SDF 字体状态")]
    public static void CheckStatus()
    {
        bool ttfExists = File.Exists(TTF_PATH);
        bool sdfExists = File.Exists(SDF_PATH);

        string msg = $"TTF 字体 ({TTF_PATH}): {(ttfExists ? "✓ 存在" : "✗ 不存在")}\n" +
                     $"SDF 字体 ({SDF_PATH}): {(sdfExists ? "✓ 存在" : "✗ 不存在")}";

        if (!sdfExists && ttfExists)
            msg += "\n\n请使用菜单 Tools → 生成中文 SDF 字体 来生成。";

        EditorUtility.DisplayDialog("字体状态", msg, "确定");
    }
}
