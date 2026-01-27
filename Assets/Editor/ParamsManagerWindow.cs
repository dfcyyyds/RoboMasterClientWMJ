using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System;

public class ParamsManagerWindow : EditorWindow
{
    private string jsonPath;
    private Dictionary<string, string> paramDict = new Dictionary<string, string>();
    private Vector2 scrollPos;
    private string jsonRaw = "";

    [MenuItem("Tools/参数管理器")]
    public static void ShowWindow()
    {
        GetWindow<ParamsManagerWindow>("参数管理器");
    }

    private void OnEnable()
    {
        jsonPath = Path.Combine(Application.dataPath, "StreamingAssets/Config/params.json");
        LoadParams();
    }

    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新", GUILayout.Width(80)))
        {
            LoadParams();
        }
        if (GUILayout.Button("保存", GUILayout.Width(80)))
        {
            SaveParams();
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("参数列表：", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        List<string> keys = new List<string>(paramDict.Keys);
        // 参数中文含义字典
        Dictionary<string, string> keyToChinese = new Dictionary<string, string>()
        {
            {"dataPort", "官方服务器数据端口"},
            {"decoderOutputHeight", "解码输出分辨率-高"},
            {"decoderOutputWidth", "解码输出分辨率-宽"},
            {"decoderQueueSize", "解码队列上限"},
            {"initialFileQueueSize", "消息发送队列初始大小"},
            {"ip", "官方服务器ip"},
            {"maxDrainPerUpdate", "主线程每帧最大解码帧数"},
            {"maxFileQueueSize", "消息发送队列最大大小"},
            {"mqttReconnectInterval", "MQTT重连间隔（秒）"},
            {"RobotID", "本机器人ID"},
            {"RobotNum", "场上机器人数量"},
            {"videoPort", "官方服务器视频端口"},
            {"logBufferSize", "日志缓冲区大小"}
        };
        foreach (var key in keys)
        {
            GUILayout.BeginHorizontal();
            string chinese = keyToChinese.ContainsKey(key) ? keyToChinese[key] : "";
            GUILayout.Label(key, GUILayout.Width(120));
            if (!string.IsNullOrEmpty(chinese))
                GUILayout.Label($"({chinese})", GUILayout.Width(180));
            else
                GUILayout.Label("", GUILayout.Width(180));
            string oldValue = paramDict[key];
            string newValue = EditorGUILayout.TextField(oldValue);
            if (newValue != oldValue)
                paramDict[key] = newValue;
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUILayout.Space(10);
        GUILayout.Label("原始 JSON：", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(jsonRaw, GUILayout.Height(80));
    }

    // 去除JSON中的注释（支持 // 和 /* */）
    private string RemoveJsonComments(string json)
    {
        // 移除 // 行注释
        string noLineComments = System.Text.RegularExpressions.Regex.Replace(json, @"//.*", "");
        // 移除 /* ... */ 块注释
        string noBlockComments = System.Text.RegularExpressions.Regex.Replace(noLineComments, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        return noBlockComments;
    }

    private void LoadParams()
    {
        paramDict.Clear();
        if (File.Exists(jsonPath))
        {
            jsonRaw = File.ReadAllText(jsonPath, Encoding.UTF8);
            try
            {
                string jsonNoComments = RemoveJsonComments(jsonRaw);
                var dict = MiniJSON.Json.Deserialize(jsonNoComments) as Dictionary<string, object>;
                if (dict != null)
                {
                    foreach (var kv in dict)
                        paramDict[kv.Key] = kv.Value.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("JSON 解析失败: " + e.Message);
            }
        }
        else
        {
            jsonRaw = "未找到文件：" + jsonPath;
        }
    }

    private void SaveParams()
    {
        // 使用排序，保证 key 顺序一致
        var dict = new SortedDictionary<string, object>();
        foreach (var kv in paramDict)
        {
            // 尝试自动识别数字类型
            if (int.TryParse(kv.Value, out int intVal))
                dict[kv.Key] = intVal;
            else if (float.TryParse(kv.Value, out float floatVal))
                dict[kv.Key] = floatVal;
            else
                dict[kv.Key] = kv.Value;
        }
        string json = MiniJSON.Json.Serialize(dict);
        // 简单美化：缩进2空格、换行、key排序
        string prettyJson = FormatJson(json);
        File.WriteAllText(jsonPath, prettyJson, Encoding.UTF8);
        AssetDatabase.Refresh();
        LoadParams();
    }

    // 简单 JSON 格式化工具（兼容 MiniJSON 输出）
    private string FormatJson(string json)
    {
        int indent = 0;
        bool quoted = false;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < json.Length; i++)
        {
            char ch = json[i];
            switch (ch)
            {
                case '{':
                case '[':
                    sb.Append(ch);
                    if (!quoted)
                    {
                        sb.Append('\n');
                        sb.Append(new string(' ', ++indent * 2));
                    }
                    break;
                case '}':
                case ']':
                    if (!quoted)
                    {
                        sb.Append('\n');
                        sb.Append(new string(' ', --indent * 2));
                    }
                    sb.Append(ch);
                    break;
                case ',':
                    sb.Append(ch);
                    if (!quoted)
                    {
                        sb.Append('\n');
                        sb.Append(new string(' ', indent * 2));
                    }
                    break;
                case ':':
                    sb.Append(quoted ? ch : ": ");
                    break;
                case '"':
                    sb.Append(ch);
                    bool escaped = false;
                    int index = i;
                    while (index > 0 && json[--index] == '\\') escaped = !escaped;
                    if (!escaped) quoted = !quoted;
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}
