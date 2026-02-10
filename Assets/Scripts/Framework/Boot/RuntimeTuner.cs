using UnityEngine;
using UnityEngine.Rendering;

// 运行时基础调优：确保Update频率充足、在后台也保持刷新
// 放置于任意场景，或由NetworkManager检测并自动创建
[DefaultExecutionOrder(-1000)]
public class RuntimeTuner : MonoBehaviour
{
    private float diagLast;
    private int diagFrames;
    private float snapshotLast;

    void Awake()
    {
        Application.runInBackground = true; // 后台仍刷新

        // 根据 GPU 类型决定 VSync 策略：
        // - NVIDIA 高性能 GPU：关闭 VSync 追求低延迟
        // - Intel/AMD 集显：开启 VSync 避免画面撕裂
        string gpuName = SystemInfo.graphicsDeviceName.ToLowerInvariant();
        bool isNvidia = gpuName.Contains("nvidia") || gpuName.Contains("geforce") || gpuName.Contains("rtx") || gpuName.Contains("gtx");
        bool isIntelIntegrated = gpuName.Contains("intel") || gpuName.Contains("uhd") || gpuName.Contains("iris");

        if (isNvidia)
        {
            QualitySettings.vSyncCount = 0;     // NVIDIA：关闭VSync以允许更高帧率和更低延迟
            wmj.Log.I("[RuntimeTuner] 检测到 NVIDIA GPU，关闭 VSync", wmj.Log.Tag.General);
        }
        else if (isIntelIntegrated)
        {
            QualitySettings.vSyncCount = 1;     // Intel 集显：开启VSync避免画面撕裂
            wmj.Log.I("[RuntimeTuner] 检测到 Intel 集显，开启 VSync 防撕裂", wmj.Log.Tag.General);
        }
        else
        {
            QualitySettings.vSyncCount = 1;     // 其他 GPU：默认开启VSync
            wmj.Log.I("[RuntimeTuner] 未知 GPU (" + gpuName + ")，默认开启 VSync", wmj.Log.Tag.General);
        }

        int targetFps = ConfigLoader.config != null && ConfigLoader.config.targetFrameRate > 0
            ? ConfigLoader.config.targetFrameRate
            : 120;
        Application.targetFrameRate = targetFps;  // 明确目标帧率
        // 强制每帧渲染一次，避免渲染帧间隔导致的低刷新
        OnDemandRendering.renderFrameInterval = 1;
        diagLast = Time.realtimeSinceStartup;
        diagFrames = 0;
        snapshotLast = diagLast;
    }

    void Update()
    {
        diagFrames++;
        float now = Time.realtimeSinceStartup;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 每秒输出一次目标帧率与估算FPS
        if (now - diagLast >= 1f)
        {
            float fps = diagFrames / (now - diagLast);
            wmj.Log.I("[RuntimeTuner] FPS诊断: target=" + Application.targetFrameRate + ", fps≈" + fps.ToString("F1"), wmj.Log.Tag.General);
            diagFrames = 0;
            diagLast = now;
        }
        // 每5秒输出一次环境快照（全局刷新相关状态）
        if (now - snapshotLast >= 5f)
        {
            string focus = Application.isFocused ? "Focused" : "Unfocused";
            string bg = Application.runInBackground ? "BG=On" : "BG=Off";
            int vSync = QualitySettings.vSyncCount;
            int renderInterval = OnDemandRendering.renderFrameInterval;
            var res = Screen.currentResolution;
#if UNITY_2022_1_OR_NEWER
            string rr = res.refreshRateRatio.value.ToString("F1") + "Hz";
#else
            string rr = res.refreshRate + "Hz";
#endif
            wmj.Log.I(
                "[RuntimeTuner] 环境快照: " + focus + ", " + bg + ", vSync=" + vSync +
                ", target=" + Application.targetFrameRate + ", renderInterval=" + renderInterval +
                ", screen=" + res.width + "x" + res.height + "@" + rr,
                wmj.Log.Tag.General);
            snapshotLast = now;
        }
#endif
    }
}
