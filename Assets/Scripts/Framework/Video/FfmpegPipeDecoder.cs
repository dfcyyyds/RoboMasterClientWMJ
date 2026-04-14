using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Framework.Boot;

namespace Framework.Video
{
    // 解码器状态快照，用于入场诊断
    public struct DecoderStats
    {
        public bool HasParameterSets { get; set; }
        public int PushedFrames { get; set; }
        public int IdrsSeen { get; set; }
        public string Codec { get; set; }
    }

    /// 基于 ffmpeg 的解码器：stdin 输入 HEVC AnnexB，stdout 输出 PPM(P6)图像流
    /// 需系统已安装 ffmpeg 可执行文件
    public class FfmpegPipeDecoder : IVideoDecoder
    {
        private readonly ConcurrentQueue<DecodedFrame> frameQueue = new ConcurrentQueue<DecodedFrame>();
        private Process proc;
        private Stream stdin;
        private Stream stdout;
        private CancellationTokenSource cts;
        private Task readerTask;
        private Task stderrTask;
        private readonly object sync = new object();
        private DateTime lastRestartTime = DateTime.MinValue;
        private string inputCodec; // "hevc" or "h264"，可根据流自动切换
        private string detectedCodec = null; // 自动检测到的编解码器
        private readonly bool useRawVideo; // 输出是否改为 rawvideo
        private bool useHardwareDecode;
        private readonly bool verboseFrameLogs;
        private readonly int outputWidth;
        private readonly int outputHeight;
        private readonly bool enableStderrLog;
        private readonly bool forceHevc; // 强制HEVC模式，不允许切换到其他codec
        // 解码队列限长与计数（增大队列以减少丢帧，允许更多缓冲）
        private int queueCount = 0;
        private int maxQueueSize = 8;

        private enum AccelMode { AutoCuda, Vaapi, Dxva, VideoToolbox, Software }
        private AccelMode accelMode = AccelMode.AutoCuda;
        private DateTime lastFrameTime = DateTime.UtcNow;
        private readonly TimeSpan noFrameTimeout = TimeSpan.FromSeconds(5); // 加长超时，避免首包前误判
        private Task watchdogTask;
        private volatile bool firstPacketSeen = false;
        private volatile bool hasReceivedIdr = false;
        private volatile bool needsIdrSync = false;

        // 参数集缓存（VPS/SPS/PPS）
        private byte[] parameterSetCache = null;
        private bool parameterSetsSent = false;
        private int pushedFrames = 0;
        private int idrsSeen = 0;

        // CUDA 连续崩溃计数：超过阈值后自动回退到软件解码
        private volatile int consecutiveCrashesWithoutFrame = 0;
        private const int MAX_CRASHES_BEFORE_SOFTWARE_FALLBACK = 1; // 快速回退，避免启动延迟

        // ffmpeg 可用性缓存：IsFfmpegAvailable() 只在首次调用时执行同步检测，之后直接返回缓存值
        private static bool? ffmpegAvailableCache = null;

        // 异步重启请求：Push() 等主线程方法不再同步调用 RestartProcess，
        // 而是设置此标志让后台 WatchdogLoop 执行重启，避免主线程阻塞
        private volatile bool restartRequested = false;
        private volatile string restartReason = null;

        // 看门狗连续重启计数：超过上限后停止自动重启，避免无数据时的无限重启循环
        private volatile int consecutiveWatchdogRestarts = 0;
        private const int MAX_WATCHDOG_RESTARTS = 5; // 连续 5 次无帧重启后停止

        // 释放标志
        private volatile bool disposed = false;

        // ─── 异步写入队列（避免主线程阻塞在管道写入上）───
        // Push() 将数据入队，后台 WriteLoop 线程负责实际的 stdin 管道写入
        private readonly ConcurrentQueue<byte[]> writeQueue = new ConcurrentQueue<byte[]>();
        private volatile int writeQueueBytes = 0;
        private const int MAX_WRITE_QUEUE_BYTES = 128 * 1024; // 128KB 背压上限
        private Task writerTask;

        public FfmpegPipeDecoder(string inputCodec = "hevc", bool useRawVideo = false, int outputWidth = 0, int outputHeight = 0, bool verboseFrameLogs = false, bool enableStderrLog = false, bool useHardwareDecode = true, bool forceHevc = false)
        {
            this.inputCodec = (inputCodec == "h264") ? "h264" : "hevc";
            this.useRawVideo = useRawVideo;
            this.outputWidth = outputWidth;
            this.outputHeight = outputHeight;
            this.verboseFrameLogs = verboseFrameLogs;
            this.enableStderrLog = enableStderrLog;
            this.useHardwareDecode = useHardwareDecode;
            this.forceHevc = forceHevc;
            accelMode = useHardwareDecode ? SelectAccelMode() : AccelMode.Software;
            StartProcess();
        }

        private AccelMode SelectAccelMode()
        {
            var detection = HardwareCapabilityDetector.Detect();
            switch (detection.Accel)
            {
                case HardwareCapabilityDetector.RecommendedAccel.NvdecCuda:
                    return AccelMode.AutoCuda;
                case HardwareCapabilityDetector.RecommendedAccel.Vaapi:
                    return AccelMode.Vaapi;
                case HardwareCapabilityDetector.RecommendedAccel.Dxva:
                    return AccelMode.Dxva;
                case HardwareCapabilityDetector.RecommendedAccel.VideoToolbox:
                    return AccelMode.VideoToolbox;
                case HardwareCapabilityDetector.RecommendedAccel.Software:
                default:
                    return AccelMode.Software;
            }
        }

        /// <summary>检查系统是否安装了 ffmpeg（结果缓存，只检测一次）</summary>
        public static bool IsFfmpegAvailable()
        {
            // 缓存命中：直接返回，避免每次重启都同步启动进程等待 3 秒
            if (ffmpegAvailableCache.HasValue)
                return ffmpegAvailableCache.Value;

            try
            {
                var checkProc = new Process();
                checkProc.StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                checkProc.Start();
                string output = checkProc.StandardOutput.ReadLine() ?? "";
                checkProc.WaitForExit(3000);
                if (!checkProc.HasExited) checkProc.Kill();
                bool available = output.Contains("ffmpeg");
                ffmpegAvailableCache = available;
                return available;
            }
            catch
            {
                ffmpegAvailableCache = false;
                return false;
            }
        }

        // 暴露队列长度与上限，便于诊断与调参
        public int GetQueueCount() => Volatile.Read(ref queueCount);
        public int MaxQueueSize
        {
            get => maxQueueSize;
            set => maxQueueSize = Math.Max(1, value);
        }
        private void StartProcess(AccelMode? overrideMode = null, bool fallbackToSoftware = false)
        {
            try
            {
                // 前置检查：ffmpeg 是否可用
                if (!IsFfmpegAvailable())
                {
                    string platform = Environment.OSVersion.Platform.ToString();
                    string hint;
                    if (platform.Contains("Unix"))
                        hint = "Ubuntu/Debian: sudo apt install ffmpeg\nArch: sudo pacman -S ffmpeg\nFedora: sudo dnf install ffmpeg";
                    else if (platform.Contains("Win"))
                        hint = "请从 https://www.gyan.dev/ffmpeg/builds/ 下载 ffmpeg，解压后将 ffmpeg.exe 所在目录加入系统 PATH 环境变量";
                    else
                        hint = "请安装 ffmpeg 并确保其在 PATH 中可用";
                    wmj.Log.F($"[FfmpegPipeDecoder] ❌ 未检测到 ffmpeg！视频解码需要 ffmpeg。\n安装方式:\n{hint}", wmj.Log.Tag.Decoder);
                    return;
                }
                if (overrideMode.HasValue)
                    accelMode = overrideMode.Value;
                if (fallbackToSoftware)
                    accelMode = AccelMode.Software;

                cts = new CancellationTokenSource();
                proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    // 提升日志级别便于诊断；输出 PPM(P6) 并使用 RGB24
                    Arguments = BuildFfmpegArgs(),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                proc.Start();
                if (proc.HasExited)
                {
                    throw new InvalidOperationException("ffmpeg启动后立即退出，疑似硬件解码不可用");
                }
                stdin = proc.StandardInput.BaseStream;
                stdout = proc.StandardOutput.BaseStream;
                wmj.Log.I("[FfmpegPipeDecoder] 启动 ffmpeg 进程: " + proc.StartInfo.Arguments, wmj.Log.Tag.Decoder);
                wmj.Log.I("[FfmpegPipeDecoder] 解码加速模式: " + accelMode, wmj.Log.Tag.Decoder);
                // 异步读取stdout，根据输出格式解析帧
                readerTask = Task.Factory.StartNew(() =>
            {
                // 降低线程优先级，避免解码线程抢占主线程 CPU
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                if (useRawVideo && outputWidth > 0 && outputHeight > 0)
                    ReadRawVideoLoop(cts.Token, outputWidth, outputHeight);
                else
                    ReadPpmLoop(cts.Token);
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                // 异步读取stderr，收集错误信息
                stderrTask = Task.Factory.StartNew(() =>
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                ReadStderrLoop(cts.Token);
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                // 看门狗监测无输出/进程退出
                if (watchdogTask == null || watchdogTask.IsCompleted)
                    watchdogTask = Task.Run(() => WatchdogLoop(cts.Token));
                // 异步写入线程：负责管道写入，避免主线程阻塞
                // 清空旧的写入队列（新进程需要从参数集重新开始）
                while (writeQueue.TryDequeue(out _)) { }
                Interlocked.Exchange(ref writeQueueBytes, 0);
                writerTask = Task.Factory.StartNew(() =>
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                WriteLoop(cts.Token);
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                // 重置参数集发送状态，确保新进程能同步
                parameterSetsSent = false;
                detectedCodec = null; // 重置编解码器检测，避免误检结果被永久保留
                lastFrameTime = DateTime.UtcNow;
                hasReceivedIdr = false;
                needsIdrSync = true; // 重启后等待 IDR 再输出帧
            }
            catch (Exception ex)
            {
                wmj.Log.E("[FfmpegPipeDecoder] ffmpeg启动异常: " + ex.Message, wmj.Log.Tag.Decoder);
                // 只有在硬件路径确实启动失败时才回退软解
                if (!fallbackToSoftware && accelMode != AccelMode.Software)
                {
                    wmj.Log.W("[FfmpegPipeDecoder] 硬件解码不可用，尝试回退软解", wmj.Log.Tag.Decoder);
                    StartProcess(AccelMode.Software, fallbackToSoftware: true);
                }
            }
        }

        private void RestartProcess(string reason, bool fallbackToSoftware = false)
        {
            // 简单的节流，避免频繁重启
            if ((DateTime.UtcNow - lastRestartTime).TotalSeconds < 0.3)
            {
                return;
            }
            lastRestartTime = DateTime.UtcNow;

            // 连续崩溃检测：硬件模式下累计崩溃次数，达阈值自动回退软解
            if (!fallbackToSoftware && accelMode != AccelMode.Software)
            {
                consecutiveCrashesWithoutFrame++;
                if (consecutiveCrashesWithoutFrame >= MAX_CRASHES_BEFORE_SOFTWARE_FALLBACK)
                {
                    wmj.Log.W($"[FfmpegPipeDecoder] 硬件解码连续 {consecutiveCrashesWithoutFrame} 次崩溃无帧输出，自动回退到软件解码", wmj.Log.Tag.Decoder);
                    fallbackToSoftware = true;
                }
            }

            wmj.Log.W("[FfmpegPipeDecoder] 重启ffmpeg进程: " + reason, wmj.Log.Tag.Decoder);
            try
            {
                try { cts?.Cancel(); } catch { }
                try { stdin?.Dispose(); } catch { }
                try { stdout?.Dispose(); } catch { }
                if (proc != null)
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                    try { proc.Dispose(); } catch { }
                }
            }
            catch { }
            finally
            {
                // 若指定则回退软解
                StartProcess(accelMode, fallbackToSoftware: fallbackToSoftware);
            }
        }

        private string BuildFfmpegArgs()
        {
            // 下采样以降低CPU负载；色彩空间按 BT.709；输出 rawvideo 可选，默认 PPM
            string vf;
            if (useRawVideo && outputWidth > 0 && outputHeight > 0)
            {
                // 使用更简单、安全的过滤链，避免外部库(zimg)导致的过滤错误
                if (useHardwareDecode && accelMode != AccelMode.Software)
                {
                    if (accelMode == AccelMode.AutoCuda)
                    {
                        // CUDA/NVDEC 路径：在 GPU 侧缩放并保持 NV12，下载后再做 CPU 侧 RGB 转换，避免 scale_cuda 直接输出 rgb24 不被支持
                        vf = $"scale_cuda={outputWidth}:{outputHeight}:format=nv12,hwdownload,format=nv12,format=rgb24";
                        // 提升解码线程数至4以平滑瞬时抖动
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel cuda -hwaccel_output_format cuda -extra_hw_frames 8 -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -pix_fmt rgb24 -f rawvideo pipe:1";
                    }
                    if (accelMode == AccelMode.Vaapi)
                    {
                        // VAAPI 硬解：使用动态检测的设备路径，添加 extra_hw_frames 以改善缓冲减少卡顿
                        string vaapiDev = HardwareCapabilityDetector.VaapiDevicePath;
                        vf = $"scale_vaapi={outputWidth}:{outputHeight}:format=nv12,hwdownload,format=nv12,format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel vaapi -hwaccel_output_format vaapi -vaapi_device {vaapiDev} -extra_hw_frames 8 -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -pix_fmt rgb24 -f rawvideo pipe:1";
                    }
                    if (accelMode == AccelMode.Dxva)
                    {
                        vf = $"scale={outputWidth}:{outputHeight},format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel d3d11va -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -pix_fmt rgb24 -f rawvideo pipe:1";
                    }
                    if (accelMode == AccelMode.VideoToolbox)
                    {
                        vf = $"scale={outputWidth}:{outputHeight},format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel videotoolbox -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -pix_fmt rgb24 -f rawvideo pipe:1";
                    }
                }
                vf = $"scale={outputWidth}:{outputHeight},format=rgb24";
                // 软解路径：小分辨率（如 360×540 吊射）只用 2 线程，减少 CPU 争抢
                int swThreads = (outputWidth <= 640 && outputHeight <= 640) ? 2 : 4;
                return $"-loglevel warning -nostdin -hide_banner -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads {swThreads} -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -pix_fmt rgb24 -f rawvideo pipe:1";
            }
            else
            {
                // PPM 路径同样移除 zscale，保持稳定
                if (useHardwareDecode && accelMode != AccelMode.Software)
                {
                    if (accelMode == AccelMode.AutoCuda)
                    {
                        vf = "scale_cuda=1280:-2:format=nv12,hwdownload,format=nv12,format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel cuda -hwaccel_output_format cuda -extra_hw_frames 8 -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -f image2pipe -vcodec ppm -pix_fmt rgb24 pipe:1";
                    }
                    if (accelMode == AccelMode.Vaapi)
                    {
                        // VAAPI 硬解：使用动态检测的设备路径
                        string vaapiDev = HardwareCapabilityDetector.VaapiDevicePath;
                        vf = "scale_vaapi=1280:-2:format=nv12,hwdownload,format=nv12,format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel vaapi -hwaccel_output_format vaapi -vaapi_device {vaapiDev} -extra_hw_frames 8 -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -f image2pipe -vcodec ppm -pix_fmt rgb24 pipe:1";
                    }
                    if (accelMode == AccelMode.Dxva)
                    {
                        vf = "scale=1280:-2,format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel d3d11va -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -f image2pipe -vcodec ppm -pix_fmt rgb24 pipe:1";
                    }
                    if (accelMode == AccelMode.VideoToolbox)
                    {
                        vf = "scale=1280:-2,format=rgb24";
                        return $"-loglevel warning -nostdin -hide_banner -hwaccel videotoolbox -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads 4 -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -f image2pipe -vcodec ppm -pix_fmt rgb24 pipe:1";
                    }
                }
                vf = "scale=1280:-2,format=rgb24";
                int swThreadsPpm = (outputWidth <= 640 && outputHeight <= 640) ? 2 : 4;
                return $"-loglevel warning -nostdin -hide_banner -probesize 32 -analyzeduration 0 -fflags +nobuffer -flags low_delay -threads {swThreadsPpm} -an -sn -vsync passthrough -f {inputCodec} -i - -vf {vf} -f image2pipe -vcodec ppm -pix_fmt rgb24 pipe:1";
            }
        }

        public void Push(byte[] annexBBytes)
        {
            Push(annexBBytes, annexBBytes?.Length ?? 0);
        }

        /// <summary>
        /// 推送 H.264/HEVC AnnexB 数据（支持 ArrayPool 租借的大数组，仅使用前 length 字节）
        /// 内部会在需要时复制到精确大小的数组，调用方可安全归还原数组到 ArrayPool
        /// </summary>
        public void Push(byte[] annexBBytes, int length)
        {
            try
            {
                if (disposed || annexBBytes == null || length <= 0) return;

                // 如果传入的数组大于实际数据长度（ArrayPool 场景），截取精确副本
                byte[] data = annexBBytes;
                if (annexBBytes.Length > length)
                {
                    data = new byte[length];
                    System.Buffer.BlockCopy(annexBBytes, 0, data, 0, length);
                }
                firstPacketSeen = true;
                // 进程健康检查 — 不在调用线程（可能是主线程）执行重启，
                // 而是设置标志让后台 WatchdogLoop 处理，避免阻塞主线程
                if (proc == null || proc.HasExited || stdin == null)
                {
                    restartReason = proc == null ? "proc为空" : (proc.HasExited ? "进程已退出" : "stdin空");
                    restartRequested = true;
                    consecutiveWatchdogRestarts = 0; // 有新数据到来，重置重启计数
                    // 若看门狗已因达到重启上限而退出，需要启动新的看门狗来执行重启
                    if (watchdogTask == null || watchdogTask.IsCompleted)
                    {
                        var ct = cts?.Token ?? CancellationToken.None;
                        if (!ct.IsCancellationRequested)
                            watchdogTask = Task.Run(() => WatchdogLoop(ct));
                    }
                    return;
                }

                // 自动检测编解码器（HEVC/H264），必要时切换 ffmpeg 输入格式
                var detected = DetectCodec(data);
                if (forceHevc)
                {
                    // 强制HEVC模式：只记录检测结果，不切换到H264，避免软解回退
                    if (detected != null && detectedCodec == null)
                        detectedCodec = detected;
                    if (detected != null && detected != "hevc")
                    {
                        wmj.Log.W("[FfmpegPipeDecoder] 强制HEVC模式收到非HEVC NAL，保持hevc解码路径", wmj.Log.Tag.Decoder);
                    }
                }
                else
                {
                    if (detected != null && detectedCodec == null)
                    {
                        detectedCodec = detected;
                        wmj.Log.I("[FfmpegPipeDecoder] 自动检测到编解码器: " + detectedCodec, wmj.Log.Tag.Decoder);
                    }
                    if (detectedCodec != null && detectedCodec != inputCodec)
                    {
                        inputCodec = detectedCodec; // 更新输入格式
                        restartReason = "切换输入编解码器为: " + inputCodec;
                        restartRequested = true;
                        wmj.Log.I("[FfmpegPipeDecoder] Codec=" + inputCodec, wmj.Log.Tag.Decoder);
                        return; // 等待后台重启
                    }
                }

                // 提取并缓存参数集（VPS/SPS/PPS），识别NAL类型：VPS=32, SPS=33, PPS=34
                ExtractAndCacheParameterSets(data);

                // 侦测是否包含 HEVC IDR（IDR_W_RADL=19, IDR_N_LP=20）
                bool containsIdr = inputCodec == "h264" ? ContainsH264Idr(data) : ContainsHevcIdr(data);
                if (containsIdr)
                {
                    idrsSeen++;
                    hasReceivedIdr = true;
                    needsIdrSync = false; // 已见到 IDR，可以开始输出帧
                }

                // 若首次获得参数集，则先发送一次参数集，确保解码器同步
                if (!parameterSetsSent && parameterSetCache != null && parameterSetCache.Length > 0)
                {
                    EnqueueWrite(parameterSetCache);
                    parameterSetsSent = true;
                    wmj.Log.D("[FfmpegPipeDecoder] 首次发送参数集: " + parameterSetCache.Length + " bytes", wmj.Log.Tag.Decoder);
                }

                // 若检测到IDR帧，且参数集尚未发送过，则发送参数集
                if (containsIdr && !parameterSetsSent && parameterSetCache != null && parameterSetCache.Length > 0)
                {
                    EnqueueWrite(parameterSetCache);
                    parameterSetsSent = true;
                    wmj.Log.D("[FfmpegPipeDecoder] IDR前发送参数集: " + parameterSetCache.Length + " bytes", wmj.Log.Tag.Decoder);
                }

                // 帧数据入队（由后台 WriteLoop 异步写入 ffmpeg stdin）
                EnqueueWrite(data);
                if (verboseFrameLogs)
                    wmj.Log.D("[FfmpegPipeDecoder] 写入帧: " + data.Length + " bytes" + (containsIdr ? " (IDR)" : ""), wmj.Log.Tag.Decoder);
                pushedFrames++;
            }
            catch (Exception ex)
            {
                wmj.Log.E("[FfmpegPipeDecoder] Push异常: " + ex.Message, wmj.Log.Tag.Decoder);
                restartReason = "Push异常: " + ex.Message;
                restartRequested = true;
            }
        }

        private bool ContainsHevcIdr(byte[] annexBBytes)
        {
            for (int i = 0; i < annexBBytes.Length - 4; i++)
            {
                int startCodeLen = 0;
                if (i + 3 < annexBBytes.Length && annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x01)
                    startCodeLen = 3;
                else if (i + 4 < annexBBytes.Length && annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x00 && annexBBytes[i + 3] == 0x01)
                    startCodeLen = 4;

                if (startCodeLen > 0 && i + startCodeLen < annexBBytes.Length)
                {
                    byte nalHeader = annexBBytes[i + startCodeLen];
                    int nalType = (nalHeader >> 1) & 0x3F; // HEVC
                    if (nalType == 19 || nalType == 20)
                        return true;
                }
            }
            return false;
        }

        private bool ContainsH264Idr(byte[] annexBBytes)
        {
            for (int i = 0; i < annexBBytes.Length - 4; i++)
            {
                int startCodeLen = 0;
                if (i + 3 < annexBBytes.Length && annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x01) startCodeLen = 3;
                else if (i + 4 < annexBBytes.Length && annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x00 && annexBBytes[i + 3] == 0x01) startCodeLen = 4;
                if (startCodeLen > 0 && i + startCodeLen < annexBBytes.Length)
                {
                    byte nalHeader = annexBBytes[i + startCodeLen];
                    int nalType = nalHeader & 0x1F; // H264
                    if (nalType == 5) return true; // IDR
                }
            }
            return false;
        }

        private void ExtractAndCacheParameterSets(byte[] annexBBytes)
        {
            // 从 annexBBytes 中提取参数集 NAL 单元并缓存
            // HEVC: VPS(32), SPS(33), PPS(34)
            // H264: SPS(7), PPS(8)
            // 根据当前 inputCodec 判断使用哪种解析方式
            bool isHevcCodec = (inputCodec == "hevc");
            var paramSets = new System.Collections.Generic.List<byte>();

            for (int i = 0; i < annexBBytes.Length - 4; i++)
            {
                // 寻找起始码 00 00 01 或 00 00 00 01
                int startCodeLen = 0;
                if (i + 3 < annexBBytes.Length &&
                    annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x01)
                {
                    startCodeLen = 3;
                }
                else if (i + 4 < annexBBytes.Length &&
                    annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 &&
                    annexBBytes[i + 2] == 0x00 && annexBBytes[i + 3] == 0x01)
                {
                    startCodeLen = 4;
                }

                if (startCodeLen > 0)
                {
                    // 读取NAL header
                    if (i + startCodeLen < annexBBytes.Length)
                    {
                        byte nalHeader = annexBBytes[i + startCodeLen];

                        // 根据 codec 类型判断是否为参数集
                        bool isParamSet = false;
                        if (isHevcCodec)
                        {
                            // HEVC: VPS(32)/SPS(33)/PPS(34)
                            int hevcType = (nalHeader >> 1) & 0x3F;
                            isParamSet = (hevcType == 32 || hevcType == 33 || hevcType == 34);
                        }
                        else
                        {
                            // H264: SPS(7)/PPS(8)
                            int h264Type = nalHeader & 0x1F;
                            isParamSet = (h264Type == 7 || h264Type == 8);
                        }

                        if (isParamSet)
                        {
                            // 找到参数集，加入正确的起始码 + NAL内容
                            // 起始码应完整复制 00 00 01 或 00 00 00 01
                            paramSets.Add(annexBBytes[i]);       // 0x00
                            paramSets.Add(annexBBytes[i + 1]);   // 0x00
                            if (startCodeLen == 4)
                                paramSets.Add(annexBBytes[i + 2]); // 0x00
                            // 对于3字节起始码，第三字节为 0x01；对于4字节起始码，第四字节为 0x01
                            paramSets.Add(annexBBytes[i + (startCodeLen == 4 ? 3 : 2)]); // 0x01

                            // 追加 NAL 单元（从 NAL header 开始），直到遇到下一个起始码
                            int j = i + startCodeLen;
                            while (j < annexBBytes.Length && !(j + 3 < annexBBytes.Length &&
                                ((annexBBytes[j] == 0x00 && annexBBytes[j + 1] == 0x00 && annexBBytes[j + 2] == 0x01) ||
                                 (annexBBytes[j] == 0x00 && annexBBytes[j + 1] == 0x00 && annexBBytes[j + 2] == 0x00 && annexBBytes[j + 3] == 0x01))))
                            {
                                paramSets.Add(annexBBytes[j]);
                                j++;
                            }
                            i = j - 1;
                        }
                    }
                }
            }

            if (paramSets.Count > 0)
            {
                parameterSetCache = paramSets.ToArray();
                wmj.Log.D("[FfmpegPipeDecoder] 缓存参数集: " + parameterSetCache.Length + " bytes", wmj.Log.Tag.Decoder);
            }
        }

        private string DetectCodec(byte[] annexBBytes)
        {
            // 强证据策略，避免误判：
            // - H264：SPS(7) 或 PPS(8) 或 IDR(5) → 任一即可判定 h264（H.264 解析不会对 HEVC 产生假阳性）
            // - HEVC：需要更强证据 —— (VPS(32) + SPS(33)) 或 (SPS(33) + PPS(34)) 或 IDR(19/20)
            //   （单独的 VPS 可能是 H.264 字节的假阳性，如 P-slice 0x41 → hevcType=32）
            bool h264Sps = false, h264Pps = false, h264Idr = false;
            bool hevcVps = false, hevcSps = false, hevcPps = false, hevcIdr = false;

            for (int i = 0; i < annexBBytes.Length - 4; i++)
            {
                int startCodeLen = 0;
                if (i + 3 < annexBBytes.Length && annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x01) startCodeLen = 3;
                else if (i + 4 < annexBBytes.Length && annexBBytes[i] == 0x00 && annexBBytes[i + 1] == 0x00 && annexBBytes[i + 2] == 0x00 && annexBBytes[i + 3] == 0x01) startCodeLen = 4;
                if (startCodeLen == 0) continue;
                int idx = i + startCodeLen;
                if (idx >= annexBBytes.Length) continue;
                byte hdr = annexBBytes[idx];

                int h264Type = hdr & 0x1F;
                if (h264Type == 7) h264Sps = true;
                else if (h264Type == 8) h264Pps = true;
                else if (h264Type == 5) h264Idr = true;

                int hevcType = (hdr >> 1) & 0x3F;
                if (hevcType == 32) hevcVps = true;
                else if (hevcType == 33) hevcSps = true;
                else if (hevcType == 34) hevcPps = true;
                else if (hevcType == 19 || hevcType == 20) hevcIdr = true;
            }

            // H.264 优先且门槛更低：SPS/PPS/IDR 任一即可判定
            // （H.264 NAL type 从低 5 位提取，不会对 HEVC 字节产生假阳性）
            if (h264Idr || h264Sps || h264Pps) return "h264";

            // HEVC 需要更强证据：避免 H.264 字节的假阳性（如 0x41 → hevcType=32=VPS）
            // 要求：(VPS+SPS) 或 (SPS+PPS) 或 真正的 HEVC IDR
            if (hevcIdr || (hevcVps && hevcSps) || (hevcSps && hevcPps)) return "hevc";
            return null;
        }

        public bool TryGetFrame(out DecodedFrame frame)
        {
            if (frameQueue.TryDequeue(out frame))
            {
                Interlocked.Decrement(ref queueCount);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            disposed = true;
            try
            {
                cts?.Cancel();
                // 先关闭流，让读写线程自然退出
                try { stdin?.Dispose(); } catch { }
                try { stdout?.Dispose(); } catch { }
                if (proc != null && !proc.HasExited)
                {
                    try { proc.Kill(); } catch { }
                }
                proc?.Dispose();
                // 非阻塞等待：每个 Task 最多等 50ms（原 200ms × 3 = 600ms 可能卡主线程）
                // 如果超时也不阻塞，后台线程会自行结束
                try { readerTask?.Wait(50); } catch { }
                try { stderrTask?.Wait(50); } catch { }
                try { writerTask?.Wait(50); } catch { }
            }
            catch { }
        }

        private void ReadPpmLoop(CancellationToken token)
        {
            var reader = new BinaryReader(stdout, Encoding.ASCII);
            wmj.Log.D("[FfmpegPipeDecoder] PPM读取线程启动", wmj.Log.Tag.Decoder);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 解析PPM(P6)帧：格式为 ASCII header + 二进制像素
                    // Header: "P6\n" + (可含注释行)#...\n + "<w> <h>\n" + "255\n"
                    if (!ReadMagic(reader)) { Thread.Sleep(1); continue; }
                    // 允许注释与可变空白出现在任意位置
                    SkipComments(reader);
                    int width = ReadIntToken(reader);
                    SkipComments(reader);
                    int height = ReadIntToken(reader);
                    SkipComments(reader);
                    int maxval = ReadIntToken(reader); // 通常255
                    if (maxval <= 0 || width <= 0 || height <= 0)
                        continue;
                    // 跳过头部与像素之间的任意空白（通常为单个换行，但也可能包含 \r 或空格）
                    SkipHeaderWhitespace(reader);
                    int dataSize = width * height * 3;
                    var pixels = reader.ReadBytes(dataSize);
                    if (pixels.Length != dataSize) break;
                    bool dropped = false;
                    while (queueCount >= maxQueueSize)
                    {
                        if (frameQueue.TryDequeue(out _))
                        {
                            Interlocked.Decrement(ref queueCount);
                            dropped = true;
                        }
                        else break;
                    }
                    if (!needsIdrSync && hasReceivedIdr)
                    {
                        frameQueue.Enqueue(new DecodedFrame { Width = width, Height = height, Pixels = pixels });
                        int q = Interlocked.Increment(ref queueCount);
                        lastFrameTime = DateTime.UtcNow;
                        consecutiveCrashesWithoutFrame = 0; // 成功解码帧，重置崩溃计数
                        consecutiveWatchdogRestarts = 0; // 成功解码，重置看门狗重启计数
                        wmj.Log.D($"[FfmpegPipeDecoder] 解码帧: {width}x{height}, queue={q}", wmj.Log.Tag.Decoder);
                    }
                    if (dropped && verboseFrameLogs)
                        wmj.Log.W("[FfmpegPipeDecoder] 队列过长，已丢弃旧帧", wmj.Log.Tag.Decoder);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    wmj.Log.W("[FfmpegPipeDecoder] 读帧异常: " + ex.Message, wmj.Log.Tag.Decoder);
                }
            }
        }

        private void ReadRawVideoLoop(CancellationToken token, int width, int height)
        {
            wmj.Log.D("[FfmpegPipeDecoder] RAW读取线程启动", wmj.Log.Tag.Decoder);
            int dataSize = width * height * 3;
            // 使用ArrayPool减少GC压力，每帧仍需独立缓冲区避免数据覆盖
            var pool = System.Buffers.ArrayPool<byte>.Shared;
            byte[] readBuffer = new byte[dataSize];
            var s = stdout; // 直接用底层流读取到预分配缓冲
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int read = 0;
                    while (read < dataSize)
                    {
                        int n = s.Read(readBuffer, read, dataSize - read);
                        if (n <= 0) throw new EndOfStreamException();
                        read += n;
                    }
                    bool dropped = false;
                    while (queueCount >= maxQueueSize)
                    {
                        if (frameQueue.TryDequeue(out _))
                        {
                            Interlocked.Decrement(ref queueCount);
                            dropped = true;
                        }
                        else break;
                    }
                    if (!needsIdrSync && hasReceivedIdr)
                    {
                        // 使用ArrayPool租用缓冲区减少GC压力（消费端需要归还）
                        byte[] framePixels = pool.Rent(dataSize);
                        Buffer.BlockCopy(readBuffer, 0, framePixels, 0, dataSize);
                        frameQueue.Enqueue(new DecodedFrame { Width = width, Height = height, Pixels = framePixels, PixelArraySize = dataSize, IsPooled = true });
                        int q = Interlocked.Increment(ref queueCount);
                        lastFrameTime = DateTime.UtcNow;
                        consecutiveCrashesWithoutFrame = 0; // 成功解码帧，重置崩溃计数
                        consecutiveWatchdogRestarts = 0; // 成功解码，重置看门狗重启计数
                        wmj.Log.D($"[FfmpegPipeDecoder] 解码帧: {width}x{height}, queue={q}", wmj.Log.Tag.Decoder);
                    }
                    if (dropped && verboseFrameLogs)
                        wmj.Log.W("[FfmpegPipeDecoder] 队列过长，已丢弃旧帧", wmj.Log.Tag.Decoder);
                }
                catch (EndOfStreamException) { break; }
                catch (Exception ex)
                {
                    wmj.Log.W("[FfmpegPipeDecoder] 读RAW帧异常: " + ex.Message, wmj.Log.Tag.Decoder);
                }
            }
        }

        private void ReadStderrLoop(CancellationToken token)
        {
            try
            {
                using (var sr = proc.StandardError)
                {
                    string line;
                    int lineCount = 0;
                    int suppressed = 0;
                    while (!token.IsCancellationRequested && (line = sr.ReadLine()) != null)
                    {
                        lineCount++;

                        // 默认不打印 stderr，避免日志洪泛导致主线程卡顿/磁盘 IO 激增
                        if (!enableStderrLog)
                            continue;

                        // 仅打印前 20 行，之后每 100 行打印 1 行，控制日志量
                        bool shouldLog = lineCount <= 20 || lineCount % 100 == 0;
                        if (!shouldLog)
                        {
                            suppressed++;
                            continue;
                        }

                        wmj.Log.W("[FfmpegPipeDecoder][stderr] " + line, wmj.Log.Tag.Decoder);
                        if (suppressed > 0)
                        {
                            wmj.Log.W($"[FfmpegPipeDecoder][stderr] 已抑制 {suppressed} 行重复日志", wmj.Log.Tag.Decoder);
                            suppressed = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                wmj.Log.W("[FfmpegPipeDecoder] 读取stderr异常: " + ex.Message, wmj.Log.Tag.Decoder);
            }
        }

        private void WatchdogLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // ─── 检查 Push() 等方法请求的异步重启 ───
                    if (restartRequested)
                    {
                        restartRequested = false;
                        string reason = restartReason ?? "异步重启请求";
                        restartReason = null;
                        wmj.Log.W("[FfmpegPipeDecoder] 看门狗执行异步重启: " + reason, wmj.Log.Tag.Decoder);
                        RestartProcess(reason, fallbackToSoftware: false);
                        return;
                    }

                    if (proc == null || proc.HasExited)
                    {
                        wmj.Log.W("[FfmpegPipeDecoder] 看门狗检测到ffmpeg退出，硬解重启", wmj.Log.Tag.Decoder);
                        RestartProcess("ffmpeg进程退出", fallbackToSoftware: false);
                        return;
                    }

                    // 未收到首包/参数集前不做无帧超时判断，避免误判
                    if (!firstPacketSeen)
                    {
                        Thread.Sleep(200);
                        continue;
                    }

                    if ((DateTime.UtcNow - lastFrameTime) > noFrameTimeout)
                    {
                        consecutiveWatchdogRestarts++;
                        if (consecutiveWatchdogRestarts > MAX_WATCHDOG_RESTARTS)
                        {
                            // 连续多次无帧重启，停止自动重启避免资源浪费
                            // 下次有新数据 Push() 时会检测到进程退出并请求重启
                            wmj.Log.W($"[FfmpegPipeDecoder] 连续 {consecutiveWatchdogRestarts} 次无帧重启，停止自动重启（等待新数据）", wmj.Log.Tag.Decoder);
                            return;
                        }
                        wmj.Log.W($"[FfmpegPipeDecoder] 看门狗检测到超时无帧输出({consecutiveWatchdogRestarts}/{MAX_WATCHDOG_RESTARTS})，保持硬解重启", wmj.Log.Tag.Decoder);
                        // 重启但保持当前硬件模式，避免轻易回退软解
                        RestartProcess("无帧输出超时", fallbackToSoftware: false);
                        // 清空队列，等待下一 IDR 同步
                        while (frameQueue.TryDequeue(out _)) { }
                        Interlocked.Exchange(ref queueCount, 0);
                        hasReceivedIdr = false;
                        needsIdrSync = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    wmj.Log.W("[FfmpegPipeDecoder] 看门狗异常: " + ex.Message, wmj.Log.Tag.Decoder);
                }

                Thread.Sleep(500);
            }
        }

        private bool ReadMagic(BinaryReader r)
        {
            // 寻找 'P','6','\n'
            int state = 0;
            while (true)
            {
                int b = r.Read();
                if (b < 0) return false;
                if (state == 0 && b == 'P') state = 1;
                else if (state == 1 && b == '6') state = 2;
                else if (state == 2 && (b == '\n' || b == '\r')) return true;
                else state = (b == 'P') ? 1 : 0;
            }
        }

        private void SkipComments(BinaryReader r)
        {
            // 注释以#开始，至行尾
            while (true)
            {
                int b = r.PeekChar();
                if (b == '#')
                {
                    // 丢弃至换行
                    while (true)
                    {
                        int c = r.Read();
                        if (c < 0 || c == '\n') break;
                    }
                }
                else break;
            }
        }

        private int ReadIntToken(BinaryReader r)
        {
            // 跳过空白
            int b;
            do { b = r.Read(); if (b < 0) return 0; } while (char.IsWhiteSpace((char)b));
            // 读数字
            int val = 0;
            while (b >= '0' && b <= '9')
            {
                val = val * 10 + (b - '0');
                b = r.Read();
                if (b < 0) break;
            }
            return val;
        }

        public void ResendParameterSets()
        {
            if (parameterSetCache != null && parameterSetCache.Length > 0)
            {
                EnqueueWrite(parameterSetCache);
                wmj.Log.W("[FfmpegPipeDecoder] 看门狗重发参数集: " + parameterSetCache.Length + " bytes", wmj.Log.Tag.Decoder);
            }
        }

        // ═══════════════════════════════ 异步写入管道 ═══════════════════════════════

        /// <summary>
        /// 将数据入队等待后台写入（主线程安全，永不阻塞）
        /// 超过 128KB 背压上限时自动丢弃最旧数据，防止内存无限增长
        /// </summary>
        private void EnqueueWrite(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            writeQueue.Enqueue(data);
            int total = Interlocked.Add(ref writeQueueBytes, data.Length);
            // 背压：队列超限时丢弃旧数据
            while (total > MAX_WRITE_QUEUE_BYTES && writeQueue.TryDequeue(out var dropped))
            {
                total = Interlocked.Add(ref writeQueueBytes, -dropped.Length);
            }
        }

        /// <summary>
        /// 后台写入线程：从 writeQueue 取出数据写入 ffmpeg stdin 管道
        /// 管道写入可能阻塞（ffmpeg 处理慢时），此线程承担阻塞代价，
        /// 保证 Unity 主线程的 Push() 调用永远不会卡顿
        /// </summary>
        private void WriteLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (writeQueue.TryDequeue(out var data))
                    {
                        Interlocked.Add(ref writeQueueBytes, -data.Length);
                        var s = stdin;
                        if (s != null)
                        {
                            // 使用异步写入 + 超时保护，避免管道满时永久阻塞
                            var writeTask = s.WriteAsync(data, 0, data.Length, token);
                            if (!writeTask.Wait(2000)) // 2 秒写入超时
                            {
                                wmj.Log.W("[FfmpegPipeDecoder] 管道写入超时(2s)，请求重启", wmj.Log.Tag.Decoder);
                                restartReason = "管道写入超时";
                                restartRequested = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(1); // 无数据时让出 CPU
                    }
                }
                catch (ObjectDisposedException) { break; } // stdin 已关闭
                catch (OperationCanceledException) { break; }
                catch (AggregateException ae) when (ae.InnerException is ObjectDisposedException || ae.InnerException is OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        wmj.Log.W("[FfmpegPipeDecoder] 写入线程异常: " + ex.Message, wmj.Log.Tag.Decoder);
                    Thread.Sleep(10);
                }
            }
        }

        public DecoderStats GetStats()
        {
            return new DecoderStats
            {
                HasParameterSets = parameterSetCache != null && parameterSetCache.Length > 0,
                PushedFrames = pushedFrames,
                IdrsSeen = idrsSeen,
                Codec = inputCodec
            };
        }

        private void SkipHeaderWhitespace(BinaryReader r)
        {
            // 消费 header 与像素数据之间的一个或多个空白字符
            while (true)
            {
                int b = r.PeekChar();
                if (b < 0) break;
                if (!char.IsWhiteSpace((char)b)) break;
                r.Read();
            }
        }
    }
}
