using UnityEngine;
using UnityEngine.Rendering;
using Framework.Boot;

// 运行时基础调优：确保Update频率充足、在后台也保持刷新
// 放置于任意场景，或由NetworkManager检测并自动创建
// 跨平台适配：Windows/Ubuntu 22.04/24.04, NVIDIA/Intel/AMD GPU

[DefaultExecutionOrder(-1000)]
public class RuntimeTuner : MonoBehaviour
{
    private float diagLast; // 帧间隔
    private int diagFrames; // 帧数
    private float snapshotLast; // 环境快照时间戳
    private float adaptiveCheckLast; // 自适应性能检查时间戳
    private int lowFpsCount; // 连续低帧率计数

    void Awake()
    {
        Application.runInBackground = true; // 后台仍刷新

        // 使用 HardwareCapabilityDetector 进行完整硬件探测
        var detection = HardwareCapabilityDetector.Detect();

        // 根据 GPU 厂商和能力等级决定 VSync 策略
        switch (detection.Vendor)
        {
            case HardwareCapabilityDetector.GpuVendor.Nvidia:
                if (detection.HasDedicatedGpu)
                {
                    QualitySettings.vSyncCount = 0;     // NVIDIA 独显：关闭VSync追求低延迟
                    wmj.Log.I("[RuntimeTuner] NVIDIA 独显 (" + detection.GpuName + ")，关闭 VSync", wmj.Log.Tag.General);
                }
                else
                {
                    QualitySettings.vSyncCount = 1;
                    wmj.Log.I("[RuntimeTuner] NVIDIA 集显，开启 VSync", wmj.Log.Tag.General);
                }
                break;

            case HardwareCapabilityDetector.GpuVendor.Amd:
                if (detection.HasDedicatedGpu && detection.GpuMemoryMB >= 4096)
                {
                    QualitySettings.vSyncCount = 0;     // AMD 高端独显：关闭VSync
                    wmj.Log.I("[RuntimeTuner] AMD 独显 (" + detection.GpuName + ", " + detection.GpuMemoryMB + "MB)，关闭 VSync", wmj.Log.Tag.General);
                }
                else
                {
                    QualitySettings.vSyncCount = 1;     // AMD 集显/低端：开启VSync防撕裂
                    wmj.Log.I("[RuntimeTuner] AMD (" + detection.GpuName + ")，开启 VSync 防撕裂", wmj.Log.Tag.General);
                }
                break;

            case HardwareCapabilityDetector.GpuVendor.Intel:
                QualitySettings.vSyncCount = 1;     // Intel 集显：始终开启VSync
                wmj.Log.I("[RuntimeTuner] Intel 集显 (" + detection.GpuName + ")，开启 VSync 防撕裂", wmj.Log.Tag.General);
                break;

            default:
                QualitySettings.vSyncCount = 1;     // 未知 GPU：默认开启VSync
                wmj.Log.I("[RuntimeTuner] GPU: " + detection.GpuName + " (" + detection.Vendor + ")，默认开启 VSync", wmj.Log.Tag.General);
                break;
        }

        // 根据能力等级自动选择 Quality Level
        // Unity QualitySettings: 0=Mobile(低配), 1=PC(标准)
        int qualityLevel;
        switch (detection.Level)
        {
            case HardwareCapabilityDetector.CapabilityLevel.Low:
                qualityLevel = 0; // Mobile 质量等级
                break;
            case HardwareCapabilityDetector.CapabilityLevel.Mid:
            case HardwareCapabilityDetector.CapabilityLevel.High:
            default:
                qualityLevel = 1; // PC 质量等级
                break;
        }
        if (QualitySettings.GetQualityLevel() != qualityLevel)
        {
            QualitySettings.SetQualityLevel(qualityLevel, true);
            wmj.Log.I($"[RuntimeTuner] 自动设置 Quality Level: {qualityLevel} (硬件等级: {detection.Level})", wmj.Log.Tag.General);
        }

        // 从配置文件中读取目标帧率，默认使用推荐值
        int targetFps = ConfigLoader.config != null && ConfigLoader.config.targetFrameRate > 0
            ? ConfigLoader.config.targetFrameRate
            : detection.RecommendedTargetFps;

        // 低配设备帧率硬上限，避免 GPU 过载
        if (detection.Level == HardwareCapabilityDetector.CapabilityLevel.Low && targetFps > 60)
        {
            targetFps = 60;
            wmj.Log.W("[RuntimeTuner] 低配设备，帧率上限限制为 60fps", wmj.Log.Tag.General);
        }

        Application.targetFrameRate = targetFps;
        // 强制每帧渲染一次，避免渲染帧间隔导致的低刷新
        OnDemandRendering.renderFrameInterval = 1;
        diagLast = Time.realtimeSinceStartup;
        diagFrames = 0;
        snapshotLast = diagLast;
        adaptiveCheckLast = diagLast;
        lowFpsCount = 0;

        // 平台信息日志
        wmj.Log.I($"[RuntimeTuner] 平台: {Application.platform}, 图形API: {SystemInfo.graphicsDeviceType}, " +
                   $"GPU: {detection.GpuName} ({detection.Vendor}, {detection.GpuMemoryMB}MB), " +
                   $"CPU: {detection.CpuCoreCount}核, RAM: {detection.SystemMemoryMB}MB, " +
                   $"等级: {detection.Level}, 加速: {detection.Accel}, 目标帧率: {targetFps}", wmj.Log.Tag.General);
    }

    void Update()
    {
        #region 诊断输出：每秒输出一次当前目标帧率和估算FPS，每5秒输出一次环境快照(开发模式下)
        diagFrames++; // 记录帧数用于诊断
        float now = Time.realtimeSinceStartup;

        // 自适应性能降级：连续 5 秒低帧率则自动降低目标帧率
        if (now - adaptiveCheckLast >= 1f)
        {
            float fps = diagFrames / Mathf.Max(0.001f, now - adaptiveCheckLast);
            int currentTarget = Application.targetFrameRate;

            // 如果实际帧率 < 目标帧率的 60%，认为性能不足
            if (currentTarget > 30 && fps < currentTarget * 0.6f)
            {
                lowFpsCount++;
                if (lowFpsCount >= 5)
                {
                    int newTarget = Mathf.Max(30, currentTarget / 2);
                    if (newTarget != currentTarget)
                    {
                        Application.targetFrameRate = newTarget;
                        wmj.Log.W($"[RuntimeTuner] 自适应降级: 帧率 {fps:F0} < 目标 {currentTarget}*60%，降至 {newTarget}fps", wmj.Log.Tag.General);
                        lowFpsCount = 0;
                    }
                }
            }
            else
            {
                lowFpsCount = Mathf.Max(0, lowFpsCount - 1); // 逐渐恢复
            }
        }

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
        #endregion
    }
}
