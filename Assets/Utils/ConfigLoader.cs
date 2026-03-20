using UnityEngine;
using System.IO;
using UnityEngine.Networking;
using Framework.Boot;

public static class ConfigLoader
{
    private static ConfigData _config;
    private static bool _isLoaded = false;
    private static string _configPath = null;
    private static HardwareCapabilityDetector.CapabilityLevel _detectedLevel = HardwareCapabilityDetector.CapabilityLevel.Mid;
    public static bool IsLoaded => _isLoaded;
    public static HardwareCapabilityDetector.CapabilityLevel DetectedLevel => _detectedLevel;
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
    /// 只加载一次参数到内存，根据硬件能力自动选择配置档位
    /// </summary>
    public static void LoadConfig()
    {
        if (_isLoaded) return;

        // 执行硬件探测，确定能力等级
        var detection = HardwareCapabilityDetector.Detect();
        _detectedLevel = detection.Level;

        // 根据能力等级选择配置文件
        string configFileName = GetConfigFileNameByLevel(_detectedLevel);

        // 加载优先级: persistentDataPath (用户修改) > streamingAssetsPath (默认)
        string persistPath = Path.Combine(Application.persistentDataPath, configFileName);
        string streamPath = Path.Combine(Application.streamingAssetsPath, configFileName);

        if (File.Exists(persistPath))
        {
            _configPath = persistPath;
        }
        else if (File.Exists(streamPath))
        {
            _configPath = streamPath;
        }
        else
        {
            // 分档配置不存在，回退到默认配置
            wmj.Log.W($"[ConfigLoader] 分档配置 {configFileName} 不存在，回退到默认配置", wmj.Log.Tag.General);
            string defaultPersist = Path.Combine(Application.persistentDataPath, "Config/params.json");
            string defaultStream = Path.Combine(Application.streamingAssetsPath, "Config/params.json");
            _configPath = File.Exists(defaultPersist) ? defaultPersist : defaultStream;
        }

        if (!File.Exists(_configPath))
        {
            wmj.Log.E($"[ConfigLoader] 配置文件不存在: {_configPath}", wmj.Log.Tag.General);
            return;
        }

        string jsonStr = File.ReadAllText(_configPath);
        if (!string.IsNullOrEmpty(jsonStr) && jsonStr[0] == '\uFEFF')
            jsonStr = jsonStr.Substring(1);
        jsonStr = RemoveJsonComments(jsonStr);
        _config = JsonUtility.FromJson<ConfigData>(jsonStr);
        _isLoaded = _config != null;

        // 应用硬件探测推荐的配置（如果配置文件中的值为默认值）
        ApplyHardwareRecommendations(detection);

        wmj.Log.I($"[ConfigLoader] 已加载配置: {_configPath} (硬件等级: {_detectedLevel})", wmj.Log.Tag.General);
    }

    /// <summary>
    /// 根据硬件能力等级获取配置文件名
    /// </summary>
    private static string GetConfigFileNameByLevel(HardwareCapabilityDetector.CapabilityLevel level)
    {
        switch (level)
        {
            case HardwareCapabilityDetector.CapabilityLevel.Low:
                return "Config/params_lowspec.json";
            case HardwareCapabilityDetector.CapabilityLevel.Mid:
                return "Config/params_midspec.json";
            case HardwareCapabilityDetector.CapabilityLevel.High:
            default:
                return "Config/params.json";
        }
    }

    /// <summary>
    /// 应用硬件探测推荐的配置
    /// </summary>
    private static void ApplyHardwareRecommendations(HardwareCapabilityDetector.DetectionResult detection)
    {
        if (_config == null) return;

        // 如果配置文件中的帧率为 0 或未设置，使用推荐值
        if (_config.targetFrameRate <= 0)
            _config.targetFrameRate = detection.RecommendedTargetFps;

        // 日志输出当前生效的关键配置
        wmj.Log.I($"[ConfigLoader] 生效配置: " +
            $"分辨率={_config.decoderOutputWidth}x{_config.decoderOutputHeight}, " +
            $"目标帧率={_config.targetFrameRate}, " +
            $"解码队列={_config.decoderQueueSize}, " +
            $"推荐加速={detection.Accel}", wmj.Log.Tag.General);
    }

    /// <summary>
    /// 保存参数到配置文件
    /// </summary>
    public static void SaveConfig()
    {
        if (_config == null)
        {
            wmj.Log.E("[ConfigLoader] 无法保存，参数未初始化", wmj.Log.Tag.General);
            return;
        }
        if (_configPath == null)
            _configPath = GetConfigPath();
        string jsonStr = JsonUtility.ToJson(_config, true);

        try
        {
            File.WriteAllText(_configPath, jsonStr);
            wmj.Log.I($"[ConfigLoader] 参数已保存到 {_configPath}", wmj.Log.Tag.General);
        }
        catch (System.UnauthorizedAccessException)
        {
            // StreamingAssets 只读（打包后的 Linux），回退到 persistentDataPath
            string fallbackPath = Path.Combine(Application.persistentDataPath,
                "Config/" + Path.GetFileName(_configPath));
            string fallbackDir = Path.GetDirectoryName(fallbackPath);
            if (!Directory.Exists(fallbackDir)) Directory.CreateDirectory(fallbackDir);
            File.WriteAllText(fallbackPath, jsonStr);
            _configPath = fallbackPath;
            wmj.Log.I($"[ConfigLoader] StreamingAssets 只读，已保存到 {fallbackPath}", wmj.Log.Tag.General);
        }
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
        if (_isLoaded) yield break;

        var detection = HardwareCapabilityDetector.Detect();
        _detectedLevel = detection.Level;

        string configFileName = GetConfigFileNameByLevel(_detectedLevel);

        // 协程模式也支持 persistentDataPath 优先
        string persistPath = Path.Combine(Application.persistentDataPath, configFileName);
        string streamPath = Path.Combine(Application.streamingAssetsPath, configFileName);
        string path;

        if (File.Exists(persistPath))
        {
            path = persistPath;
        }
        else if (File.Exists(streamPath))
        {
            path = streamPath;
        }
        else
        {
            wmj.Log.W($"[ConfigLoader] 分档配置 {configFileName} 不存在，回退到默认配置", wmj.Log.Tag.General);
            string defaultPersist = Path.Combine(Application.persistentDataPath, "Config/params.json");
            string defaultStream = Path.Combine(Application.streamingAssetsPath, "Config/params.json");
            path = File.Exists(defaultPersist) ? defaultPersist : defaultStream;
        }

        if (!File.Exists(path))
        {
            wmj.Log.E($"[ConfigLoader] 配置文件不存在: {path}", wmj.Log.Tag.General);
            yield break;
        }

        _configPath = path;

#if UNITY_EDITOR || UNITY_STANDALONE
        if (!path.StartsWith("file://"))
            path = "file://" + path;
#endif
        using (UnityWebRequest www = UnityWebRequest.Get(path))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                wmj.Log.E($"[ConfigLoader] 加载配置文件失败: {www.error}", wmj.Log.Tag.General);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                yield break;
            }
            string jsonStr = www.downloadHandler.text;
            // 去除UTF-8 BOM
            if (!string.IsNullOrEmpty(jsonStr) && jsonStr[0] == '\uFEFF')
                jsonStr = jsonStr.Substring(1);
            if (string.IsNullOrEmpty(jsonStr))
            {
                wmj.Log.E($"[ConfigLoader] 找不到配置文件 {_configPath}", wmj.Log.Tag.General);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                yield break;
            }
            jsonStr = RemoveJsonComments(jsonStr);
            _config = JsonUtility.FromJson<ConfigData>(jsonStr);
            _isLoaded = true;

            ApplyHardwareRecommendations(detection);
            wmj.Log.I($"[ConfigLoader] 已加载配置: {_configPath} (硬件等级: {_detectedLevel})", wmj.Log.Tag.General);
        }
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
