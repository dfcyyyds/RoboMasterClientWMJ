using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Framework.Video
{
    /// RTSP/RTP 适配骨架（示例）：
    /// - 通过 ffmpeg 进程从 rtsp:// 源读取视频
    /// - 输出为 HEVC AnnexB 到 stdout
    /// - 基于 AUD(35) 分隔组帧，触发 OnAnnexBFrame
    /// 说明：此为骨架实现，实际参数需按官方服务器规范调整（认证、超时、码率等）。
    public class RtspTransport : IVideoTransport
    {
        private readonly string rtspUrl;
        private readonly string rtspTransport; // "tcp" / "udp" / null(Auto)
        private readonly string rtspCodec;      // "hevc" / "h264" / null(Auto)
        private Process proc;
        private Stream stdout;
        private CancellationTokenSource cts;
        private Task readerTask;
        private bool started;

        public event Action<byte[]> OnAnnexBFrame;

        /// rtspTransport: null/"tcp"/"udp"; rtspCodec: null/"hevc"/"h264"
        public RtspTransport(string rtspUrl, string rtspTransport = null, string rtspCodec = null)
        {
            this.rtspUrl = rtspUrl;
            this.rtspTransport = rtspTransport;
            this.rtspCodec = rtspCodec;
        }

        public void Start()
        {
            if (started) return;
            started = true;
            cts = new CancellationTokenSource();
            proc = new Process();
            // 构建 ffmpeg 参数
            string transportArg = string.IsNullOrEmpty(rtspTransport) ? "" : $"-rtsp_transport {rtspTransport} ";
            // 复制码流并转为 AnnexB：H.265 用 hevc_mp4toannexb，H.264 用 h264_mp4toannexb
            string copyArgs;
            if (string.Equals(rtspCodec, "h264", System.StringComparison.OrdinalIgnoreCase))
                copyArgs = "-c:v copy -bsf:v h264_mp4toannexb -f h264 -";
            else
                // 默认 hevc；若实际为 h264 需在调用处传 rtspCodec=h264
                copyArgs = "-c:v copy -bsf:v hevc_mp4toannexb -f hevc -";

            string ffArgs = $"-loglevel info -fflags +nobuffer -flags low_delay {transportArg}-i {rtspUrl} {copyArgs}";

            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            proc.Start();
            stdout = proc.StandardOutput.BaseStream;
            Task.Run(() => ReadStderrLoop(cts.Token));
            readerTask = Task.Run(() => ReadAnnexBLoop(cts.Token));
#if UNITY_EDITOR
            wmj.DebugTools.Info("[RtspTransport] 启动 ffmpeg RTSP 拉流: " + rtspUrl);
            wmj.DebugTools.WriteDebugLog("[RtspTransport] 启动 ffmpeg RTSP 拉流: " + rtspUrl, "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[RtspTransport] 启动 ffmpeg RTSP 拉流", "INFO");
        }

        public void Stop()
        {
            if (!started) return;
            started = false;
            try
            {
                cts?.Cancel();
                stdout?.Dispose();
                if (proc != null && !proc.HasExited)
                {
                    try { proc.Kill(); } catch { }
                }
                proc?.Dispose();
            }
            catch { }
#if UNITY_EDITOR
            wmj.DebugTools.Info("[RtspTransport] 停止 RTSP 传输");
            wmj.DebugTools.WriteDebugLog("[RtspTransport] 停止 RTSP 传输", "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[RtspTransport] 停止 RTSP 传输", "INFO");
        }

        private void ReadAnnexBLoop(CancellationToken token)
        {
            var reader = new BinaryReader(stdout);
            var pending = new List<byte>(1024 * 256);
            var frame = new List<byte>(1024 * 256);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 读取一段数据
                    byte[] chunk = reader.ReadBytes(32 * 1024);
                    if (chunk == null || chunk.Length == 0)
                        break; // EOF
                    pending.AddRange(chunk);

                    // 搜索 AUD (NAL type 35)，分帧
                    int searchStart = 0;
                    while (pending.Count - searchStart >= 5)
                    {
                        bool foundAud = false;
                        int audPos = -1;
                        // 起始码长度不参与后续逻辑，移除未使用变量
                        for (int i = searchStart; i + 4 < pending.Count; i++)
                        {
                            // 4字节起始码 00 00 00 01
                            if (pending[i] == 0x00 && pending[i + 1] == 0x00 && pending[i + 2] == 0x00 && pending[i + 3] == 0x01)
                            {
                                int hdrIndex = i + 4;
                                int nalType = (pending[hdrIndex] >> 1) & 0x3F;
                                if (nalType == 35) { foundAud = true; audPos = i; break; }
                            }
                            // 3字节起始码 00 00 01
                            if (pending[i] == 0x00 && pending[i + 1] == 0x00 && pending[i + 2] == 0x01)
                            {
                                int hdrIndex = i + 3;
                                int nalType = (pending[hdrIndex] >> 1) & 0x3F;
                                if (nalType == 35) { foundAud = true; audPos = i; break; }
                            }
                        }

                        if (!foundAud) break;

                        // 将 AUD 之前的数据并入当前帧（作为上一帧的结束）
                        if (audPos > searchStart)
                        {
                            int len = audPos - searchStart;
                            frame.AddRange(pending.GetRange(searchStart, len));
                        }

                        // 若已有数据，说明上一帧完成，触发事件
                        if (frame.Count > 0)
                        {
                            var annexB = frame.ToArray();
                            frame.Clear();
#if UNITY_EDITOR
                            wmj.DebugTools.WriteDebugLog("[RtspTransport] 组帧完成并上抛: bytes=" + annexB.Length, "INFO");
#endif
                            wmj.DebugTools.WriteRunLog("[RtspTransport] 组帧完成并上抛: bytes=" + annexB.Length, "INFO");
                            OnAnnexBFrame?.Invoke(annexB);
                        }

                        // 从 AUD 起作为下一帧的开头
                        searchStart = audPos;
                        // 为避免重复找到同一 AUD，将其复制到下一帧
                        // 在下一次循环中，会继续推进 searchStart
                    }

                    // 移除已处理的前缀
                    if (searchStart > 0)
                    {
                        pending.RemoveRange(0, searchStart);
                    }

                    // 兜底：过大时也触发一帧（避免无 AUD 时长期堆积）
                    if (pending.Count >= 256 * 1024)
                    {
                        frame.AddRange(pending);
                        pending.Clear();
                        var annexB = frame.ToArray();
                        frame.Clear();
                        wmj.DebugTools.WriteRunLog("[RtspTransport] 兜底分帧: bytes=" + annexB.Length, "WARN");
                        OnAnnexBFrame?.Invoke(annexB);
                    }
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    wmj.DebugTools.WriteRunLog("[RtspTransport] 读取/分帧异常: " + ex.Message, "WARN");
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
                    while (!token.IsCancellationRequested && (line = sr.ReadLine()) != null)
                    {
                        wmj.DebugTools.WriteRunLog("[RtspTransport][stderr] " + line, "WARN");
                    }
                }
            }
            catch (Exception ex)
            {
                wmj.DebugTools.WriteRunLog("[RtspTransport] 读取stderr异常: " + ex.Message, "WARN");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
