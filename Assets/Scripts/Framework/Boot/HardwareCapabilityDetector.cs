using UnityEngine;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Framework.Boot
{
    /// <summary>
    /// 硬件能力探测器 - 检测 GPU、CPU、硬解能力，自动选择最优配置档位
    /// </summary>
    public static class HardwareCapabilityDetector
    {
        #region 能力等级定义

        /// <summary>
        /// 硬件能力等级
        /// </summary>
        public enum CapabilityLevel
        {
            Low,    // i3 + 集显 → 720p 30fps
            Mid,    // i5/i7 集显 或 低端独显 → 1080p 60fps
            High    // 中高端独显 → 1080p/2K 60-120fps
        }

        /// <summary>
        /// GPU 厂商类型
        /// </summary>
        public enum GpuVendor
        {
            Unknown,
            Intel,
            Nvidia,
            Amd,
            Apple,
            Other
        }

        /// <summary>
        /// 推荐的解码加速模式
        /// </summary>
        public enum RecommendedAccel
        {
            NvdecCuda,  // NVIDIA NVDEC/CUDA
            Vaapi,      // Intel/AMD VAAPI (Linux)
            Dxva,       // Windows D3D11VA/DXVA2
            VideoToolbox, // macOS
            Software    // 纯软解
        }

        #endregion

        #region 探测结果

        /// <summary>
        /// 硬件探测结果
        /// </summary>
        public class DetectionResult
        {
            // GPU名称
            public string GpuName { get; set; }
            // GPU供应商名称
            public string GpuVendorName { get; set; }
            // GPU供应商类型
            public GpuVendor Vendor { get; set; }
            // GPU显存大小(MB)
            public int GpuMemoryMB { get; set; }
            // CPU核心数量
            public int CpuCoreCount { get; set; }
            // CPU内存大小(MB)
            public int SystemMemoryMB { get; set; }
            // 硬件能力等级
            public CapabilityLevel Level { get; set; }
            // 推荐的解码加速模式
            public RecommendedAccel Accel { get; set; }
            // 是否有独立显卡
            public bool HasDedicatedGpu { get; set; }
            // 是否支持VAAPI
            public bool VaapiAvailable { get; set; }

            // 推荐配置
            public int RecommendedWidth { get; set; }
            public int RecommendedHeight { get; set; }
            public int RecommendedTargetFps { get; set; }

            #region 软解相关推荐参数
            // 推荐的解码队列大小（针对软解）
            public int RecommendedDecoderQueueSize { get; set; }
            // 推荐的软解的每帧解码数量
            public int RecommendedMaxDrainPerUpdate { get; set; }
            #endregion

            /// <summary>
            /// 重写输出字符串格式化方法
            /// </summary>
            /// <returns>格式化字符串</returns>
            public override string ToString()
            {
                return $"[HardwareDetection] GPU={GpuName}, Vendor={Vendor}, VRAM={GpuMemoryMB}MB, " +
                       $"CPU Cores={CpuCoreCount}, RAM={SystemMemoryMB}MB, " +
                       $"Level={Level}, Accel={Accel}, " +
                       $"Recommended={RecommendedWidth}x{RecommendedHeight}@{RecommendedTargetFps}fps";
            }
        }

        // 检测结果
        private static DetectionResult _cachedResult;

        #endregion

        #region 公开接口

        /// <summary>
        /// 执行硬件探测（结果会缓存）
        /// </summary>
        public static DetectionResult Detect(bool forceRefresh = false)
        {
            // 如果已经有配置缓存，则直接返回缓存参数（除非强制刷新）
            if (_cachedResult != null && !forceRefresh)
            {
                return _cachedResult;
            }

            // 检查是否有覆盖配置（用于模拟低配环境）
            var overrideResult = TryLoadOverrideConfig();
            if (overrideResult != null)
            {
                _cachedResult = overrideResult;
                wmj.Log.W("[HardwareDetection] 使用覆盖配置: " + overrideResult.ToString(), wmj.Log.Tag.General);
                return overrideResult;
            }

            var result = new DetectionResult();

            // 1. 获取 GPU 信息
            // SystemInfo 类是一个Unity 提供的类，用于获取硬件信息，跨平台兼容
            result.GpuName = SystemInfo.graphicsDeviceName ?? "Unknown";
            result.GpuVendorName = SystemInfo.graphicsDeviceVendor ?? "Unknown";
            result.GpuMemoryMB = SystemInfo.graphicsMemorySize;
            result.Vendor = DetectGpuVendor(result.GpuVendorName, result.GpuName);
            result.HasDedicatedGpu = DetectDedicatedGpu(result.Vendor, result.GpuMemoryMB);

            // 2. 获取 CPU 和内存信息
            result.CpuCoreCount = SystemInfo.processorCount;
            result.SystemMemoryMB = SystemInfo.systemMemorySize;

            // 3. 检测硬解可用性
            result.VaapiAvailable = CheckVaapiAvailable();

            // 4. 确定推荐的加速模式
            result.Accel = DetermineAccelMode(result);

            // 5. 确定能力等级
            result.Level = DetermineCapabilityLevel(result);

            // 6. 根据等级设置推荐配置
            ApplyRecommendedConfig(result);

            _cachedResult = result;

            // 输出探测结果
            wmj.Log.I(result.ToString(), wmj.Log.Tag.General);

            return result;
        }

        /// <summary>
        /// 获取缓存的探测结果（如果未探测则返回 null）
        /// </summary>
        public static DetectionResult GetCachedResult() => _cachedResult;

        /// <summary>
        /// 检查是否为低配设备
        /// </summary>
        public static bool IsLowSpec()
        {
            var result = Detect();
            return result.Level == CapabilityLevel.Low;
        }

        /// <summary>
        /// 检查是否应使用 VAAPI
        /// </summary>
        public static bool ShouldUseVaapi()
        {
            var result = Detect();
            return result.Accel == RecommendedAccel.Vaapi;
        }

        /// <summary>
        /// 检查是否应使用 NVDEC/CUDA
        /// </summary>
        public static bool ShouldUseNvdec()
        {
            var result = Detect();
            return result.Accel == RecommendedAccel.NvdecCuda;
        }

        /// <summary>
        /// 检查是否应使用软解
        /// </summary>
        public static bool ShouldUseSoftware()
        {
            var result = Detect();
            return result.Accel == RecommendedAccel.Software;
        }

        #endregion

        #region 内部探测逻辑

        private static GpuVendor DetectGpuVendor(string vendorName, string gpuName)
        {
            string combined = (vendorName + " " + gpuName).ToLowerInvariant();

            if (combined.Contains("nvidia") || combined.Contains("geforce") || combined.Contains("quadro") || combined.Contains("rtx") || combined.Contains("gtx"))
                return GpuVendor.Nvidia;

            if (combined.Contains("intel") || combined.Contains("iris") || combined.Contains("uhd graphics") || combined.Contains("hd graphics"))
                return GpuVendor.Intel;

            if (combined.Contains("amd") || combined.Contains("radeon") || combined.Contains("vega"))
                return GpuVendor.Amd;

            if (combined.Contains("apple") || combined.Contains("m1") || combined.Contains("m2") || combined.Contains("m3"))
                return GpuVendor.Apple;

            return GpuVendor.Unknown;
        }

        private static bool DetectDedicatedGpu(GpuVendor vendor, int vramMB)
        {
            // NVIDIA 基本都是独显
            if (vendor == GpuVendor.Nvidia)
                return true;

            // Intel 基本都是集显
            if (vendor == GpuVendor.Intel)
                return false;

            // AMD 根据显存判断（集显通常 < 1GB 专用显存）
            if (vendor == GpuVendor.Amd)
                return vramMB > 1024;

            // Apple Silicon 是统一内存，算作"集成"
            if (vendor == GpuVendor.Apple)
                return false;

            // 未知情况根据显存猜测
            return vramMB > 2048;
        }

        /// <summary>
        /// 检测到的 VAAPI 设备路径（如 /dev/dri/renderD128）
        /// </summary>
        public static string VaapiDevicePath { get; private set; } = "/dev/dri/renderD128";

        private static bool CheckVaapiAvailable()
        {
            // 仅 Linux 支持 VAAPI
            if (Application.platform != RuntimePlatform.LinuxPlayer &&
                Application.platform != RuntimePlatform.LinuxEditor)
                return false;

            // 动态扫描 /dev/dri/renderD* 设备，兼容多 GPU 或非标准编号
            try
            {
                string driDir = "/dev/dri";
                if (!Directory.Exists(driDir))
                    return false;

                // 优先检查 renderD128（最常见），再依次检查 renderD129-135
                for (int i = 128; i <= 135; i++)
                {
                    string devPath = Path.Combine(driDir, "renderD" + i);
                    if (File.Exists(devPath))
                    {
                        VaapiDevicePath = devPath;
                        wmj.Log.I("[HardwareDetection] VAAPI 设备: " + devPath, wmj.Log.Tag.General);
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static RecommendedAccel DetermineAccelMode(DetectionResult result)
        {
            // macOS 优先 VideoToolbox
            if (Application.platform == RuntimePlatform.OSXPlayer ||
                Application.platform == RuntimePlatform.OSXEditor)
            {
                return RecommendedAccel.VideoToolbox;
            }

            // Windows
            if (Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor)
            {
                // NVIDIA 独显优先 NVDEC
                if (result.Vendor == GpuVendor.Nvidia && result.HasDedicatedGpu)
                    return RecommendedAccel.NvdecCuda;

                // Intel/AMD 使用 DXVA
                if (result.Vendor == GpuVendor.Intel || result.Vendor == GpuVendor.Amd)
                    return RecommendedAccel.Dxva;

                return RecommendedAccel.Software;
            }

            // Linux
            if (Application.platform == RuntimePlatform.LinuxPlayer ||
                Application.platform == RuntimePlatform.LinuxEditor)
            {
                // NVIDIA 独显优先 NVDEC
                if (result.Vendor == GpuVendor.Nvidia && result.HasDedicatedGpu)
                    return RecommendedAccel.NvdecCuda;

                // Intel/AMD 且 VAAPI 可用
                if ((result.Vendor == GpuVendor.Intel || result.Vendor == GpuVendor.Amd) && result.VaapiAvailable)
                    return RecommendedAccel.Vaapi;

                return RecommendedAccel.Software;
            }

            return RecommendedAccel.Software;
        }

        private static CapabilityLevel DetermineCapabilityLevel(DetectionResult result)
        {
            // 高端：NVIDIA 独显 + 4GB+ 显存
            if (result.Vendor == GpuVendor.Nvidia && result.HasDedicatedGpu && result.GpuMemoryMB >= 4096)
                return CapabilityLevel.High;

            // 高端：AMD 独显 + 4GB+ 显存
            if (result.Vendor == GpuVendor.Amd && result.HasDedicatedGpu && result.GpuMemoryMB >= 4096)
                return CapabilityLevel.High;

            // 中端：NVIDIA 低端独显 或 高端集显
            if (result.Vendor == GpuVendor.Nvidia && result.HasDedicatedGpu)
                return CapabilityLevel.Mid;

            // 中端：AMD 独显 < 4GB
            if (result.Vendor == GpuVendor.Amd && result.HasDedicatedGpu)
                return CapabilityLevel.Mid;

            // 中端：Intel 新款集显（Iris Xe 等）+ 8 核以上
            if (result.Vendor == GpuVendor.Intel && result.CpuCoreCount >= 8)
            {
                string gpuLower = result.GpuName.ToLowerInvariant();
                if (gpuLower.Contains("iris") || gpuLower.Contains("xe"))
                    return CapabilityLevel.Mid;
            }

            // 中端：Apple Silicon
            if (result.Vendor == GpuVendor.Apple)
                return CapabilityLevel.Mid;

            // 低端：Intel 集显 + 4 核以下
            if (result.Vendor == GpuVendor.Intel && result.CpuCoreCount <= 4)
                return CapabilityLevel.Low;

            // 低端：系统内存 < 8GB
            if (result.SystemMemoryMB < 8192)
                return CapabilityLevel.Low;

            // 默认中端
            return CapabilityLevel.Mid;
        }

        private static void ApplyRecommendedConfig(DetectionResult result)
        {
            switch (result.Level)
            {
                case CapabilityLevel.Low:
                    result.RecommendedWidth = 1280;
                    result.RecommendedHeight = 720;
                    result.RecommendedTargetFps = 30;
                    result.RecommendedDecoderQueueSize = 4;
                    result.RecommendedMaxDrainPerUpdate = 1;
                    break;

                case CapabilityLevel.Mid:
                    result.RecommendedWidth = 1920;
                    result.RecommendedHeight = 1080;
                    result.RecommendedTargetFps = 60;
                    result.RecommendedDecoderQueueSize = 6;
                    result.RecommendedMaxDrainPerUpdate = 2;
                    break;

                case CapabilityLevel.High:
                default:
                    result.RecommendedWidth = 1920;
                    result.RecommendedHeight = 1080;
                    result.RecommendedTargetFps = 120;
                    result.RecommendedDecoderQueueSize = 8;
                    result.RecommendedMaxDrainPerUpdate = 3;
                    break;
            }
        }

        #endregion

        #region 覆盖配置支持 (用于模拟低配环境) ,仅测试期间使用

        /// <summary>
        /// 覆盖配置数据结构
        /// </summary>
        [Serializable]
        private class HardwareOverrideConfig
        {
            public bool enabled = false;
            public string forceLevel = "";
            public string forceAccel = "";
            public string simulatedGpuName = "";
            public string simulatedGpuVendor = "";
            public int simulatedVramMB = 0;
            public int simulatedCpuCores = 0;
            public int recommendedWidth = 0;
            public int recommendedHeight = 0;
            public int recommendedFps = 0;
            public int recommendedQueueSize = 0;
            public int recommendedDrainPerUpdate = 0;
        }

        /// <summary>
        /// 尝试加载覆盖配置文件
        /// 这个函数只有在测试期间才会被调用，正式发布版本中不会使用这个函数
        /// </summary>
        private static DetectionResult TryLoadOverrideConfig()
        {
            try
            {
                string overridePath = Path.Combine(Application.streamingAssetsPath, "Config/hardware_override.json");

                if (!File.Exists(overridePath))
                    return null;

                string json = File.ReadAllText(overridePath);
                if (string.IsNullOrEmpty(json))
                    return null;

                // 移除 BOM
                if (json[0] == '\uFEFF')
                    json = json.Substring(1);

                var config = JsonUtility.FromJson<HardwareOverrideConfig>(json);

                if (config == null || !config.enabled)
                    return null;

                wmj.Log.W("[HardwareDetection] 检测到硬件覆盖配置，将模拟: " + config.forceLevel + " 等级", wmj.Log.Tag.General);

                var result = new DetectionResult();

                // 设置模拟的硬件信息
                result.GpuName = !string.IsNullOrEmpty(config.simulatedGpuName) ? config.simulatedGpuName : "Simulated GPU";
                result.GpuVendorName = !string.IsNullOrEmpty(config.simulatedGpuVendor) ? config.simulatedGpuVendor : "Simulated";
                result.GpuMemoryMB = config.simulatedVramMB > 0 ? config.simulatedVramMB : 1024;
                result.CpuCoreCount = config.simulatedCpuCores > 0 ? config.simulatedCpuCores : 4;
                result.SystemMemoryMB = SystemInfo.systemMemorySize; // 保持真实内存

                // 解析强制等级
                result.Level = ParseCapabilityLevel(config.forceLevel);

                // 解析强制加速模式
                result.Accel = ParseAccelMode(config.forceAccel);

                // 根据加速模式推断厂商
                result.Vendor = InferVendorFromAccel(result.Accel);
                result.HasDedicatedGpu = (result.Vendor == GpuVendor.Nvidia);

                // VAAPI 模拟
                result.VaapiAvailable = (result.Accel == RecommendedAccel.Vaapi);

                // 设置推荐配置
                result.RecommendedWidth = config.recommendedWidth > 0 ? config.recommendedWidth : 1280;
                result.RecommendedHeight = config.recommendedHeight > 0 ? config.recommendedHeight : 720;
                result.RecommendedTargetFps = config.recommendedFps > 0 ? config.recommendedFps : 30;
                result.RecommendedDecoderQueueSize = config.recommendedQueueSize > 0 ? config.recommendedQueueSize : 4;
                result.RecommendedMaxDrainPerUpdate = config.recommendedDrainPerUpdate > 0 ? config.recommendedDrainPerUpdate : 1;

                return result;
            }
            catch (Exception ex)
            {
                wmj.Log.E("[HardwareDetection] 加载覆盖配置失败: " + ex.Message, wmj.Log.Tag.General);
                return null;
            }
        }

        private static CapabilityLevel ParseCapabilityLevel(string levelStr)
        {
            if (string.IsNullOrEmpty(levelStr))
                return CapabilityLevel.Mid;

            switch (levelStr.ToLowerInvariant())
            {
                case "low": return CapabilityLevel.Low;
                case "mid": case "medium": return CapabilityLevel.Mid;
                case "high": return CapabilityLevel.High;
                default: return CapabilityLevel.Mid;
            }
        }

        private static RecommendedAccel ParseAccelMode(string accelStr)
        {
            if (string.IsNullOrEmpty(accelStr))
                return RecommendedAccel.Software;

            switch (accelStr.ToLowerInvariant())
            {
                case "nvdeccuda": case "cuda": case "nvdec": return RecommendedAccel.NvdecCuda;
                case "vaapi": return RecommendedAccel.Vaapi;
                case "dxva": case "d3d11va": return RecommendedAccel.Dxva;
                case "videotoolbox": case "vt": return RecommendedAccel.VideoToolbox;
                case "software": case "soft": default: return RecommendedAccel.Software;
            }
        }

        private static GpuVendor InferVendorFromAccel(RecommendedAccel accel)
        {
            switch (accel)
            {
                case RecommendedAccel.NvdecCuda: return GpuVendor.Nvidia;
                case RecommendedAccel.Vaapi: return GpuVendor.Intel;
                case RecommendedAccel.VideoToolbox: return GpuVendor.Apple;
                case RecommendedAccel.Dxva: return GpuVendor.Intel; // 或 AMD
                default: return GpuVendor.Unknown;
            }
        }

        /// <summary>
        /// 检查是否正在使用覆盖配置
        /// </summary>
        public static bool IsUsingOverrideConfig()
        {
            string overridePath = Path.Combine(Application.streamingAssetsPath, "Config/hardware_override.json");
            return File.Exists(overridePath);
        }

        /// <summary>
        /// 获取覆盖配置路径
        /// </summary>
        public static string GetOverrideConfigPath()
        {
            return Path.Combine(Application.streamingAssetsPath, "Config/hardware_override.json");
        }

        #endregion
    }
}
