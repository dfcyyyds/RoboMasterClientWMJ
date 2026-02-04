using UnityEngine;
using UnityEditor;
using TMPro;
using System.IO;

namespace UI.RobotSelection.Editor
{
    /// <summary>
    /// 中文字体创建工具 - 在 Unity Editor 中创建 TMP 字体资源
    /// </summary>
    public class ChineseFontCreator : EditorWindow
    {
        private Font sourceFont;
        private string outputPath = "Assets/Resources/Fonts/ChineseFont SDF.asset";

        // 常用中文字符集 (包含兵种选择界面需要的所有字符)
        private static readonly string ChineseCharacters =
            // 基础字符
            "0123456789" +
            // 兵种选择界面用到的中文
            "兵种选择红方蓝方英雄工程号步空中机器人哨飞镖雷达站未知确认已请先阵营" +
            // 常用标点
            "，。！？、：；（）【】" +
            // 其他可能用到的字符
            "的是不了在有个这上们来我到他她它会时出能都你我" +
            "比赛开始结束暂停继续返回退出设置选项状态信息提示警告错误成功失败";

        [MenuItem("Tools/创建中文字体资源")]
        public static void ShowWindow()
        {
            GetWindow<ChineseFontCreator>("创建中文字体");
        }

        void OnGUI()
        {
            GUILayout.Label("TMP 中文字体创建工具", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            sourceFont = (Font)EditorGUILayout.ObjectField("源字体文件 (TTF)", sourceFont, typeof(Font), false);
            outputPath = EditorGUILayout.TextField("输出路径", outputPath);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "使用方法:\n" +
                "1. 将 TTF 字体文件拖入 源字体文件 (TTF) 字段\n" +
                "2. 点击 '创建字体资源' 按钮\n" +
                "3. 字体资源将创建在 Resources/Fonts/ 目录下",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("自动查找中文字体"))
            {
                FindChineseFont();
            }

            EditorGUILayout.Space();

            GUI.enabled = sourceFont != null;
            if (GUILayout.Button("Create Font Asset", GUILayout.Height(40)))
            {
                CreateFontAsset();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("将包含的字符:", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(ChineseCharacters, GUILayout.Height(100));
        }

        private void FindChineseFont()
        {
            // 尝试查找项目中的中文字体
            string[] guids = AssetDatabase.FindAssets("t:Font");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Chinese") || path.Contains("chinese") ||
                    path.Contains("6729be42a0ce0sj7py70xm7198"))
                {
                    sourceFont = AssetDatabase.LoadAssetAtPath<Font>(path);
                    Debug.Log($"[ChineseFontCreator] Found font: {path}");
                    break;
                }
            }

            if (sourceFont == null)
            {
                // 尝试在 Resources 目录下查找
                sourceFont = Resources.Load<Font>("Fonts/ChineseFont");
            }

            if (sourceFont == null)
            {
                EditorUtility.DisplayDialog("提示", "未找到中文字体文件，请手动指定", "确定");
            }
        }

        private void CreateFontAsset()
        {
            if (sourceFont == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择源字体文件", "确定");
                return;
            }

            // 确保输出目录存在
            string directory = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 创建 TMP Font Asset
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);

            if (fontAsset == null)
            {
                EditorUtility.DisplayDialog("错误", "创建字体资源失败", "确定");
                return;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(outputPath);

            // 保存资源
            AssetDatabase.CreateAsset(fontAsset, outputPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("成功", $"字体资源已创建: {outputPath}\n\n请在 TMP Font Asset Creator 中添加中文字符。", "确定");

            // 选中创建的资源
            Selection.activeObject = fontAsset;
            EditorGUIUtility.PingObject(fontAsset);

            Debug.Log($"[ChineseFontCreator] Font asset created: {outputPath}");
        }
    }
}
