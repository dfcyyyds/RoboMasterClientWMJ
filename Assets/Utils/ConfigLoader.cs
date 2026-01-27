using UnityEngine;
using System.IO;
using UnityEngine.Networking;

public static class ConfigLoader
{
    // 配置文件路径
    private static string fileName = "Config/params.json";
    private static ConfigData _config;
    private static bool _isLoaded = false;
    private static string _configPath = null;
    public static bool IsLoaded => _isLoaded;
    public static ConfigData config
    {
        get
        {
            if (!_isLoaded)
                LoadConfig(); // 懒加载，只读一次
            return _config;
        }
        set
        {
            _config = value;
            _isLoaded = value != null;
        }
    }

    // 获取全局MonoBehaviour用于协程（可自定义实现）
    private static MonoBehaviour GetGlobalHost()
    {
        var go = GameObject.Find("ConfigLoaderHost");
        if (go == null)
        {
            go = new GameObject("ConfigLoaderHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
        return go.GetComponent<ConfigLoaderHost>() ?? go.AddComponent<ConfigLoaderHost>();
    }

    // 用于协程的MonoBehaviour
    private class ConfigLoaderHost : MonoBehaviour { }

    /// <summary>
    /// 只加载一次参数到内存
    /// </summary>
    public static void LoadConfig()
    {
        if (_isLoaded) return;
        if (_configPath == null)
            _configPath = GetConfigPath();
        if (!File.Exists(_configPath))
        {
            wmj.DebugTools.Error($"[ConfigLoader] 配置文件不存在: {_configPath}");
            return;
        }
        string jsonStr = File.ReadAllText(_configPath);
        if (!string.IsNullOrEmpty(jsonStr) && jsonStr[0] == '\uFEFF')
            jsonStr = jsonStr.Substring(1);
        jsonStr = RemoveJsonComments(jsonStr);
        _config = JsonUtility.FromJson<ConfigData>(jsonStr);
        _isLoaded = _config != null;
    }

    /// <summary>
    /// 保存参数到配置文件
    /// </summary>
    public static void SaveConfig()
    {
        if (_config == null)
        {
            wmj.DebugTools.Error("[ConfigLoader] 无法保存，参数未初始化");
            return;
        }
        if (_configPath == null)
            _configPath = GetConfigPath();
        string jsonStr = JsonUtility.ToJson(_config, true);
        File.WriteAllText(_configPath, jsonStr);
        wmj.DebugTools.Info($"[ConfigLoader] 参数已保存到 {_configPath}");
    }

    private static string GetConfigPath()
    {
        // 默认StreamingAssets/Config/params.json
        string path = Path.Combine(Application.streamingAssetsPath, "Config/params.json");
        return path;
    }

#if UNITY_EDITOR || UNITY_ANDROID
    public static System.Collections.IEnumerator LoadConfigCoroutine()
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
#if UNITY_EDITOR || UNITY_STANDALONE
        if (!path.StartsWith("file://"))
            path = "file://" + path;
#endif
        using (UnityWebRequest www = UnityWebRequest.Get(path))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                wmj.DebugTools.Error($"[ConfigLoader] 加载配置文件失败: {www.error}");
                wmj.DebugTools.Error($"[ConfigLoader] 加载配置文件失败: {www.error}");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                wmj.DebugTools.Error($"[ConfigLoader] 加载配置文件失败: {www.error}");
#endif
                yield break;
            }
            string jsonStr = www.downloadHandler.text;
            // 去除UTF-8 BOM
            if (!string.IsNullOrEmpty(jsonStr) && jsonStr[0] == '\uFEFF')
                jsonStr = jsonStr.Substring(1);
            if (string.IsNullOrEmpty(jsonStr))
            {
                wmj.DebugTools.Error($"[ConfigLoader] 找不到配置文件 {fileName}");
                wmj.DebugTools.Error($"[ConfigLoader] 找不到配置文件 {fileName}");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                wmj.DebugTools.Error($"[ConfigLoader] 找不到配置文件 {fileName}");
#endif
                yield break;
            }
            jsonStr = RemoveJsonComments(jsonStr);
            _config = JsonUtility.FromJson<ConfigData>(jsonStr);
            _isLoaded = true;
        }
    }
#else
    public static void LoadConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        string jsonStr = File.ReadAllText(path);
        // 去除UTF-8 BOM
        if (!string.IsNullOrEmpty(jsonStr) && jsonStr[0] == '\uFEFF')
            jsonStr = jsonStr.Substring(1);
        if (string.IsNullOrEmpty(jsonStr))
        {
            wmj.DebugTools.Error($"[ConfigLoader] 找不到配置文件 {fileName}");
            wmj.DebugTools.Error($"[ConfigLoader] 找不到配置文件 {fileName}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            wmj.DebugTools.Error($"[ConfigLoader] 找不到配置文件 {fileName}");
#endif
            return;
        }
        jsonStr = RemoveJsonComments(jsonStr);
        _config = JsonUtility.FromJson<ConfigData>(jsonStr);
        _isLoaded = true;
    }
#endif

    // 去除JSON中的注释（支持 // 和 /* */）
    private static string RemoveJsonComments(string json)
    {
        // 移除 // 行注释
        string noLineComments = System.Text.RegularExpressions.Regex.Replace(json, @"//.*", "");
        // 移除 /* ... */ 块注释
        string noBlockComments = System.Text.RegularExpressions.Regex.Replace(noLineComments, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        return noBlockComments;
    }
    // 静态类不需要生命周期方法
}
