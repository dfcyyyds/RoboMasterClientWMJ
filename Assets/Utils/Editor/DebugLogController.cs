#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 日志控制面板 — 统一控制 wmj.Log 运行时开关和编译宏
/// </summary>
public class DebugLogController : EditorWindow
{
    // ================================================================
    // 分类条目定义
    // ================================================================
    private struct CategoryItem
    {
        public wmj.Log.Tag tag;
        public string label;
        public string tooltip;
        public string macro;     // 对应的编译宏名
    }

    private List<CategoryItem> items;
    private bool allLogEnabled = true;

    // 编译宏列表（用于 Scripting Define Symbols 面板区域）
    private static readonly string[] LevelMacros = { "LOG_LEVEL_DEBUG", "LOG_LEVEL_INFO", "LOG_LEVEL_WARN" };
    private static readonly string[] CategoryMacros = { "LOG_ALL", "LOG_GENERAL", "LOG_NETWORK", "LOG_VIDEO", "LOG_DECODER", "LOG_TRANSPORT", "LOG_UI" };
    private int selectedLevelIdx = 0;
    private bool showMacroSection = false;

    [MenuItem("Tools/调试/日志控制面板")]
    public static void ShowWindow()
    {
        var win = GetWindow<DebugLogController>(true, "日志控制面板", true);
        win.minSize = new Vector2(440, 520);
        win.Initialize();
        win.Show();
    }

    private void Initialize()
    {
        items = new List<CategoryItem>
        {
            new CategoryItem { tag = wmj.Log.Tag.General,   label = "通用日志",     tooltip = "通用信息类输出",                 macro = "LOG_GENERAL" },
            new CategoryItem { tag = wmj.Log.Tag.Network,   label = "网络管理日志", tooltip = "NetworkManager、Handler注册/分发", macro = "LOG_NETWORK" },
            new CategoryItem { tag = wmj.Log.Tag.Video,     label = "图传日志",     tooltip = "视频流、纹理更新、统计等",        macro = "LOG_VIDEO" },
            new CategoryItem { tag = wmj.Log.Tag.Decoder,   label = "解码器日志",   tooltip = "ffmpeg、参数集、解码输出等",      macro = "LOG_DECODER" },
            new CategoryItem { tag = wmj.Log.Tag.Transport, label = "数据传输日志", tooltip = "UDP/MQTT等传输链路相关",          macro = "LOG_TRANSPORT" },
            new CategoryItem { tag = wmj.Log.Tag.UI,        label = "UI日志",       tooltip = "UI系统、兵种选择面板、弹窗管理等", macro = "LOG_UI" },
        };
        RefreshFromRuntime();
        RefreshMacroState();
    }

    // ================================================================
    // 运行时开关同步
    // ================================================================
    private void RefreshFromRuntime()
    {
        allLogEnabled = wmj.Log.AllEnabled;
    }

    private void ApplyToRuntime()
    {
        wmj.Log.AllEnabled = allLogEnabled;
        UnityEngine.Debug.Log($"[LogController] 日志运行时开关已应用 (总开关={allLogEnabled})");
    }

    // ================================================================
    // 编译宏状态检测
    // ================================================================
    private HashSet<string> currentDefines = new HashSet<string>();

    private void RefreshMacroState()
    {
        var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        string defines = PlayerSettings.GetScriptingDefineSymbols(target);
        currentDefines = new HashSet<string>(defines.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));

        // 推断当前级别
        if (currentDefines.Contains("LOG_LEVEL_DEBUG")) selectedLevelIdx = 0;
        else if (currentDefines.Contains("LOG_LEVEL_INFO")) selectedLevelIdx = 1;
        else if (currentDefines.Contains("LOG_LEVEL_WARN")) selectedLevelIdx = 2;
        else selectedLevelIdx = -1; // 仅 Error/Fatal
    }

    private void ApplyMacros()
    {
        var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        string defines = PlayerSettings.GetScriptingDefineSymbols(target);
        var set = new HashSet<string>(defines.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));

        // 移除旧宏
        foreach (var m in LevelMacros) set.Remove(m);
        foreach (var m in CategoryMacros) set.Remove(m);
        // 同时移除旧式宏
        set.Remove("DEBUG_ALL_LOG"); set.Remove("DEBUG_GENERAL_LOG"); set.Remove("DEBUG_TRANSPORT_LOG");
        set.Remove("DEBUG_VIDEO_LOG"); set.Remove("DEBUG_DECODER_LOG"); set.Remove("DEBUG_NETWORK_LOG");

        // 添加级别宏
        if (selectedLevelIdx >= 0 && selectedLevelIdx < LevelMacros.Length)
            set.Add(LevelMacros[selectedLevelIdx]);

        // 添加分类宏
        foreach (var m in CategoryMacros)
        {
            if (currentDefines.Contains(m))
                set.Add(m);
        }

        string result = string.Join(";", set);
        PlayerSettings.SetScriptingDefineSymbols(target, result);
        UnityEngine.Debug.Log($"[LogController] 编译宏已更新: {result}");
    }

    // ================================================================
    // GUI 绘制
    // ================================================================
    private Vector2 scrollPos;

    private void OnGUI()
    {
        if (items == null) Initialize();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        // ============ 运行时开关区域 ============
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("▶ 运行时日志开关", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("运行时动态控制各分类的输出。修改后点击[应用运行时]生效。\nError/Fatal 级别始终输出，不受开关影响。", MessageType.Info);
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
        var allTags = wmj.Log.GetAllTags();
        using (new EditorGUI.DisabledGroupScope(!allLogEnabled))
        {
            foreach (var item in items)
            {
                bool current = allTags.ContainsKey(item.tag) && allTags[item.tag];
                bool newVal = EditorGUILayout.ToggleLeft(new GUIContent(item.label, item.tooltip), current);
                if (newVal != current)
                {
                    wmj.Log.SetTag(item.tag, newVal);
                }
            }
        }

        EditorGUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新")) RefreshFromRuntime();
            if (GUILayout.Button("全部开启")) SetAllTags(true);
            if (GUILayout.Button("全部关闭")) SetAllTags(false);
            if (GUILayout.Button("应用运行时")) ApplyToRuntime();
        }

        // ============ 编译宏配置区域 ============
        EditorGUILayout.Space(15);
        showMacroSection = EditorGUILayout.Foldout(showMacroSection, "▶ 编译宏配置 (Scripting Define Symbols)", true, EditorStyles.foldoutHeader);
        if (showMacroSection)
        {
            EditorGUILayout.HelpBox(
                "编译宏控制哪些日志在编译时被完全移除（零运行时开销）。\n" +
                "级别宏：控制全局最低编译级别\n" +
                "分类宏：控制各模块日志是否编译进二进制\n\n" +
                "修改后需点击[应用编译宏]触发重编译。",
                MessageType.Info);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("日志级别:", EditorStyles.boldLabel);
            string[] levelLabels = { "DEBUG (最详细)", "INFO (中等)", "WARN (仅警告以上)", "仅 Error/Fatal" };
            int newLevelIdx = EditorGUILayout.Popup("最低编译级别", selectedLevelIdx < 0 ? 3 : selectedLevelIdx, levelLabels);
            selectedLevelIdx = newLevelIdx == 3 ? -1 : newLevelIdx;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("分类编译开关:", EditorStyles.boldLabel);

            // LOG_ALL 总开关
            bool logAll = currentDefines.Contains("LOG_ALL");
            bool newLogAll = EditorGUILayout.ToggleLeft(new GUIContent("LOG_ALL (开启所有分类)", "优先级最高，覆盖所有分类宏"), logAll);
            if (newLogAll != logAll)
            {
                if (newLogAll) currentDefines.Add("LOG_ALL"); else currentDefines.Remove("LOG_ALL");
            }

            using (new EditorGUI.DisabledGroupScope(newLogAll))
            {
                foreach (var item in items)
                {
                    bool on = currentDefines.Contains(item.macro);
                    bool newOn = EditorGUILayout.ToggleLeft(new GUIContent($"{item.macro} ({item.label})", item.tooltip), on);
                    if (newOn != on)
                    {
                        if (newOn) currentDefines.Add(item.macro); else currentDefines.Remove(item.macro);
                    }
                }
            }

            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新宏状态")) RefreshMacroState();
                GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
                if (GUILayout.Button("应用编译宏 (触发重编译)")) ApplyMacros();
                GUI.backgroundColor = Color.white;
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "提示：\n" +
            "• 运行时开关：即时生效，不需要重编译\n" +
            "• 编译宏：需要重编译，但能实现零开销移除\n" +
            "• Error/Fatal 始终写入 RunLog，不受任何开关影响",
            MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    private void SetAllTags(bool enabled)
    {
        foreach (var item in items)
        {
            wmj.Log.SetTag(item.tag, enabled);
        }
    }
}
#endif
