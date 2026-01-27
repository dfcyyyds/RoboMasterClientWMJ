using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

using System.Collections.Generic;

public class LogViewerWindow : EditorWindow
{
    private string logContent = "";
    private Vector2 scrollPos;
    private Vector2 dirScrollPos;
    private int selectedLogIndex = 0;
    private List<string> logPaths = new List<string>();
    private string newLogPath = "";
    private string deleteKeyword = ""; // 新增：用于存储删除关键字
    private const string LogPrefsKey = "LogViewerWindow.LogPaths";

    [MenuItem("Tools/日志查看器")]
    public static void ShowWindow()
    {
        GetWindow<LogViewerWindow>("日志查看器");
    }

    private void OnEnable()
    {
        LoadLogPaths();
        if (logPaths.Count == 0)
        {
            // 默认添加常用日志
            logPaths.Add(Path.Combine(Application.dataPath, "../Log/RunLog.txt"));
            logPaths.Add(Path.Combine(Application.dataPath, "../Log/DebugLog.txt"));
            SaveLogPaths();
        }
        selectedLogIndex = Mathf.Clamp(selectedLogIndex, 0, logPaths.Count - 1);
        ReadLog();
    }

    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        // 左侧目录栏
        GUILayout.BeginVertical(GUILayout.Width(220));
        GUILayout.Label("日志文件列表", EditorStyles.boldLabel);
        dirScrollPos = EditorGUILayout.BeginScrollView(dirScrollPos, GUILayout.Height(200));
        for (int i = 0; i < logPaths.Count; i++)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedLogIndex == i, Path.GetFileName(logPaths[i]), "Button"))
            {
                if (selectedLogIndex != i)
                {
                    selectedLogIndex = i;
                    ReadLog();
                }
            }
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                logPaths.RemoveAt(i);
                if (selectedLogIndex >= logPaths.Count) selectedLogIndex = logPaths.Count - 1;
                SaveLogPaths();
                ReadLog();
                break;
            }
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("添加/导入日志路径:");
        GUILayout.BeginHorizontal();
        newLogPath = EditorGUILayout.TextField(newLogPath);
        if (GUILayout.Button("添加", GUILayout.Width(50)))
        {
            if (!string.IsNullOrEmpty(newLogPath) && !logPaths.Contains(newLogPath))
            {
                logPaths.Add(newLogPath);
                selectedLogIndex = logPaths.Count - 1;
                SaveLogPaths();
                ReadLog();
                newLogPath = "";
            }
        }
        if (GUILayout.Button("浏览", GUILayout.Width(50)))
        {
            string path = EditorUtility.OpenFilePanel("选择日志文件", Application.dataPath + "/../Log", "txt");
            if (!string.IsNullOrEmpty(path) && !logPaths.Contains(path))
            {
                logPaths.Add(path);
                selectedLogIndex = logPaths.Count - 1;
                SaveLogPaths();
                ReadLog();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("按关键字删除日志条目:");
        GUILayout.BeginHorizontal();
        deleteKeyword = EditorGUILayout.TextField(deleteKeyword);
        if (GUILayout.Button("删除包含关键字的日志", GUILayout.Width(140)))
        {
            DeleteLogEntriesByKeyword(deleteKeyword);
        }
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();

        // 右侧内容区
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新日志", GUILayout.Width(100)))
        {
            ReadLog();
        }
        if (GUILayout.Button("清空日志", GUILayout.Width(100)))
        {
            ClearLog();
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("日志内容：", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        EditorGUILayout.TextArea(logContent, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }
    // 新增：按关键字删除日志条目
    private void DeleteLogEntriesByKeyword(string keyword)
    {
        if (string.IsNullOrEmpty(keyword) || logPaths.Count == 0 || selectedLogIndex < 0 || selectedLogIndex >= logPaths.Count)
            return;
        string path = logPaths[selectedLogIndex];
        if (!File.Exists(path))
            return;
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var filtered = new List<string>();
        foreach (var line in lines)
        {
            if (!line.Contains(keyword))
                filtered.Add(line);
        }
        File.WriteAllLines(path, filtered, Encoding.UTF8);
        ReadLog();
    }

    private void ReadLog()
    {
        if (logPaths.Count == 0 || selectedLogIndex < 0 || selectedLogIndex >= logPaths.Count)
        {
            logContent = "未选择日志文件。";
            return;
        }
        string path = logPaths[selectedLogIndex];
        if (File.Exists(path))
        {
            logContent = File.ReadAllText(path, Encoding.UTF8);
        }
        else
        {
            logContent = "日志文件不存在：" + path;
        }
    }

    private void ClearLog()
    {
        if (logPaths.Count == 0 || selectedLogIndex < 0 || selectedLogIndex >= logPaths.Count)
            return;
        string path = logPaths[selectedLogIndex];
        if (File.Exists(path))
        {
            File.WriteAllText(path, "");
            logContent = "";
        }
    }

    private void SaveLogPaths()
    {
        string joined = string.Join("|", logPaths);
        EditorPrefs.SetString(LogPrefsKey, joined);
    }

    private void LoadLogPaths()
    {
        logPaths.Clear();
        if (EditorPrefs.HasKey(LogPrefsKey))
        {
            string joined = EditorPrefs.GetString(LogPrefsKey);
            if (!string.IsNullOrEmpty(joined))
            {
                logPaths.AddRange(joined.Split('|'));
            }
        }
    }
}