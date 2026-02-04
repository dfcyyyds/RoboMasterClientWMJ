#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 调试日志分类开关面板
/// 直接控制运行时 DebugTools.CategorySwitch 字典
/// </summary>
public class DebugLogController : EditorWindow
{
    private struct CategoryItem
    {
        public wmj.DebugTools.LogCategory category;
        public string label;
        public string tooltip;
    }

    private List<CategoryItem> items;
    private bool allLogEnabled = true;

    [MenuItem("Tools/调试/日志分类开关")]
    public static void ShowWindow()
    {
        var win = GetWindow<DebugLogController>(true, "日志分类开关", true);
        win.minSize = new Vector2(420, 380);
        win.Initialize();
        win.Show();
    }

    private void Initialize()
    {
        items = new List<CategoryItem>
        {
            new CategoryItem { category = wmj.DebugTools.LogCategory.General, label = "通用日志", tooltip = "通用信息类输出" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.Network, label = "网络管理日志", tooltip = "NetworkManager、Handler注册/分发" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.Video, label = "图传日志", tooltip = "视频流、纹理更新、统计等" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.Decoder, label = "解码器日志", tooltip = "ffmpeg、参数集、解码输出等" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.Transport, label = "数据传输日志", tooltip = "UDP/MQTT等传输链路相关" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.UI, label = "UI日志", tooltip = "UI系统、兵种选择面板、弹窗管理等" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.Custom1, label = "自定义1", tooltip = "预留自定义分类1" },
            new CategoryItem { category = wmj.DebugTools.LogCategory.Custom2, label = "自定义2", tooltip = "预留自定义分类2" },
        };
        RefreshFromRuntime();
    }

    private void RefreshFromRuntime()
    {
        allLogEnabled = wmj.DebugTools.AllLogEnabled;
    }

    private void ApplyToRuntime()
    {
        wmj.DebugTools.SetAllLogEnabled(allLogEnabled);
        Debug.Log($"[DebugLogController] 日志开关已应用 (总开关={allLogEnabled})");
    }

    private void OnGUI()
    {
        if (items == null) Initialize();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("日志分类开关", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("控制运行时各分类日志的输出。修改后点击[应用]生效。\n注意：此设置在编辑器运行时有效，重启后恢复默认。", MessageType.Info);
        EditorGUILayout.Space(5);

        // 总开关
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("总开关", GUILayout.Width(80));
            allLogEnabled = EditorGUILayout.Toggle(allLogEnabled);
            if (!allLogEnabled)
            {
                EditorGUILayout.LabelField("(所有日志已禁用)", EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("分类开关:", EditorStyles.boldLabel);

        // 分类开关
        using (new EditorGUI.DisabledGroupScope(!allLogEnabled))
        {
            foreach (var item in items)
            {
                bool current = wmj.DebugTools.CategorySwitch.ContainsKey(item.category) 
                    && wmj.DebugTools.CategorySwitch[item.category];
                bool newVal = EditorGUILayout.ToggleLeft(new GUIContent(item.label, item.tooltip), current);
                if (newVal != current)
                {
                    wmj.DebugTools.SetCategory(item.category, newVal);
                }
            }
        }

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新")) RefreshFromRuntime();
            if (GUILayout.Button("全部开启")) SetAllCategories(true);
            if (GUILayout.Button("全部关闭")) SetAllCategories(false);
            if (GUILayout.Button("应用")) ApplyToRuntime();
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox("提示：WARN/ERROR级别日志始终写入RunLog，不受开关影响。", MessageType.None);
    }

    private void SetAllCategories(bool enabled)
    {
        foreach (var item in items)
        {
            wmj.DebugTools.SetCategory(item.category, enabled);
        }
    }
}
#endif
