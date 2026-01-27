#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System.Collections.Generic;

// 调试日志宏控制面板：切换分类脚本宏以选择性编译 Editor 下的日志输出
public class DebugLogController : EditorWindow
{
    private struct DefineItem { public string key; public string label; public string tooltip; }

    private BuildTargetGroup targetGroup;
    private NamedBuildTarget namedTarget;
    private List<DefineItem> items;
    private Dictionary<string, bool> states = new Dictionary<string, bool>();

    [MenuItem("Tools/调试/日志分类开关")]
    public static void ShowWindow()
    {
        var win = GetWindow<DebugLogController>(true, "日志分类开关", true);
        win.minSize = new Vector2(420, 320);
        win.Initialize();
        win.Show();
    }

    private void Initialize()
    {
        targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
        items = new List<DefineItem>
        {
            new DefineItem{ key = "DEBUG_ALL_LOG", label = "全部日志", tooltip = "启用所有分类的调试日志（仅Editor下使用）"},
            new DefineItem{ key = "DEBUG_GENERAL_LOG", label = "通用日志", tooltip = "通用信息类输出"},
            new DefineItem{ key = "DEBUG_TRANSPORT_LOG", label = "数据传输日志", tooltip = "UDP/MQTT等传输链路相关"},
            new DefineItem{ key = "DEBUG_VIDEO_LOG", label = "图传日志", tooltip = "视频流、纹理更新、统计等"},
            new DefineItem{ key = "DEBUG_DECODER_LOG", label = "解码器日志", tooltip = "ffmpeg、参数集、解码输出等"},
            new DefineItem{ key = "DEBUG_NETWORK_LOG", label = "网络管理日志", tooltip = "NetworkManager、Handler注册/分发"},
        };
        RefreshStates();
    }

    private void RefreshStates()
    {
        states.Clear();
        var defs = PlayerSettings.GetScriptingDefineSymbols(namedTarget) ?? string.Empty;
        var set = new HashSet<string>(string.IsNullOrEmpty(defs) ? System.Array.Empty<string>() : defs.Split(';'));
        foreach (var it in items)
        {
            states[it.key] = set.Contains(it.key);
        }
    }

    private void Apply()
    {
        var defs = PlayerSettings.GetScriptingDefineSymbols(namedTarget) ?? string.Empty;
        var current = new HashSet<string>(string.IsNullOrEmpty(defs) ? System.Array.Empty<string>() : defs.Split(';'));

        foreach (var it in items)
        {
            if (states[it.key]) current.Add(it.key);
            else current.Remove(it.key);
        }
        var newStr = string.Join(";", current);
        PlayerSettings.SetScriptingDefineSymbols(namedTarget, newStr);
        Debug.Log($"[DebugLogController] 已应用脚本宏: {newStr}");
    }

    private void PresetAll(bool enable)
    {
        foreach (var it in items)
        {
            states[it.key] = enable;
        }
    }

    private void OnGUI()
    {
        if (items == null) Initialize();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("目标平台组: ", targetGroup.ToString());
        EditorGUILayout.LabelField("NamedBuildTarget: ", namedTarget.ToString());
        EditorGUILayout.HelpBox("切换脚本宏用于选择性编译Editor下调试日志。修改后Unity将自动重新编译。", MessageType.Info);
        EditorGUILayout.Space();

        foreach (var it in items)
        {
            var old = states[it.key];
            var val = EditorGUILayout.ToggleLeft(new GUIContent(it.label, it.tooltip), old);
            if (val != old) states[it.key] = val;
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新")) RefreshStates();
            if (GUILayout.Button("全部开启")) PresetAll(true);
            if (GUILayout.Button("全部关闭")) PresetAll(false);
            if (GUILayout.Button("应用")) Apply();
        }
    }
}
#endif
