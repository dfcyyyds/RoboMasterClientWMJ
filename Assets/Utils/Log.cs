// ============================================================================
// Log.cs — 统一日志框架
// 
// 设计目标：
//   1. 零阻塞：所有文件 IO 由后台线程异步批量写入
//   2. 编译期裁剪：通过 [Conditional] 宏组合控制，Release 下 Debug/Info 级别零开销
//   3. 精细控制：分类宏 × 级别宏 双维度，任意组合启停
//   4. 节流去重：高频重复日志自动合并，避免日志洪泛
//   5. 统一入口：杜绝野生 Debug.Log / WriteRunLog，全部收口到 Log.X()
//
// 宏定义说明（在 Unity PlayerSettings > Scripting Define Symbols 中配置）：
//
//   级别宏（控制全局最低输出级别）：
//     LOG_LEVEL_DEBUG   — 输出 Debug 及以上（默认，最详细）
//     LOG_LEVEL_INFO    — 输出 Info 及以上
//     LOG_LEVEL_WARN    — 仅输出 Warn 及以上
//     （不定义任何级别宏 = 仅 Error/Fatal 始终输出）
//
//   分类宏（控制各模块日志是否编译）：
//     LOG_ALL           — 开启所有分类（优先级最高）
//     LOG_GENERAL       — 通用日志
//     LOG_NETWORK       — 网络管理日志
//     LOG_VIDEO         — 视频流日志
//     LOG_DECODER       — 解码器日志
//     LOG_TRANSPORT     — 数据传输日志
//     LOG_UI            — UI 日志
//
// 使用示例：
//   Log.D("详细调试信息", Log.Tag.Video);        // 需 LOG_LEVEL_DEBUG + (LOG_VIDEO|LOG_ALL)
//   Log.I("连接成功", Log.Tag.Network);           // 需 LOG_LEVEL_INFO  + (LOG_NETWORK|LOG_ALL)
//   Log.W("丢包警告", Log.Tag.Transport);         // 需 LOG_LEVEL_WARN  + (LOG_TRANSPORT|LOG_ALL)
//   Log.E("致命错误", Log.Tag.General);           // 始终输出 + 始终写文件
//   Log.F("崩溃", Log.Tag.General);              // 始终输出 + 始终写文件 + 立即刷盘
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace wmj
{
    /// <summary>
    /// 统一日志框架 — 全局静态入口
    /// </summary>
    public static class Log
    {
        // ====================================================================
        // 分类标签
        // ====================================================================
        public enum Tag
        {
            General,
            Network,
            Video,
            Decoder,
            Transport,
            UI,
        }

        // ====================================================================
        // 日志级别
        // ====================================================================
        public enum Level
        {
            Debug = 0,
            Info = 1,
            Warn = 2,
            Error = 3,
            Fatal = 4,
        }

        // ====================================================================
        // 内部常量
        // ====================================================================
        private const int FlushIntervalMs = 500;        // 后台写入间隔（毫秒）
        private const int FlushThreshold = 64;           // 缓冲区条目阈值
        private const int MaxQueueSize = 8192;           // 队列上限，防止内存爆炸
        private const long MaxFileSize = 10L * 1024 * 1024; // 10MB 轮转阈值
        private const int ThrottleWindowMs = 1000;       // 节流窗口（毫秒）
        private const int ThrottleMaxRepeat = 3;         // 窗口内相同消息最大输出次数

        // ====================================================================
        // 后台写入基础设施
        // ====================================================================
        private static readonly ConcurrentQueue<LogEntry> _queue = new ConcurrentQueue<LogEntry>();
        private static readonly Thread _writerThread;
        private static volatile bool _running = true;
        private static readonly ManualResetEventSlim _flushSignal = new ManualResetEventSlim(false);

        private static string _debugLogPath;
        private static string _runLogPath;
        private static StreamWriter _debugWriter;
        private static StreamWriter _runWriter;
        private static readonly object _fileLock = new object();

        // ====================================================================
        // 节流器
        // ====================================================================
        private static readonly ConcurrentDictionary<int, ThrottleState> _throttleMap
            = new ConcurrentDictionary<int, ThrottleState>();

        // ====================================================================
        // 运行时分类开关（运行时可动态切换，不影响编译期裁剪）
        // ====================================================================
        private static readonly Dictionary<Tag, bool> _categorySwitch = new Dictionary<Tag, bool>
        {
            { Tag.General, true },
            { Tag.Network, true },
            { Tag.Video, true },
            { Tag.Decoder, true },
            { Tag.Transport, true },
            { Tag.UI, true },
        };
        private static volatile bool _allEnabled = true;
        private static readonly object _switchLock = new object();

        // ====================================================================
        // 结构体定义
        // ====================================================================
        private struct LogEntry
        {
            public string Formatted;   // 已格式化的完整行
            public Level Lvl;
            public bool ForceFlush;    // Fatal 级别立即刷盘
        }

        private class ThrottleState
        {
            public long LastTickMs;
            public int Count;
        }

        // ====================================================================
        // 静态构造 — 初始化路径、启动后台线程
        // ====================================================================
        static Log()
        {
            InitPaths();
            EnsureDirectories();
            OpenWriters();

            _writerThread = new Thread(WriterLoop)
            {
                Name = "LogWriter",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal,
            };
            _writerThread.Start();

#if UNITY_5_3_OR_NEWER
            UnityEngine.Application.quitting += Shutdown;
#endif
        }

        // ====================================================================
        //  公开 API — 编译期 [Conditional] 控制
        // ====================================================================

        // ---- Debug 级别 ----
        // 需要 LOG_LEVEL_DEBUG + 对应分类宏

        [Conditional("LOG_LEVEL_DEBUG")]
        public static void D(string msg, Tag tag = Tag.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            Emit(Level.Debug, tag, msg, file, line);
        }

        // ---- Info 级别 ----
        // 需要 LOG_LEVEL_DEBUG 或 LOG_LEVEL_INFO + 对应分类宏

        [Conditional("LOG_LEVEL_DEBUG")]
        [Conditional("LOG_LEVEL_INFO")]
        public static void I(string msg, Tag tag = Tag.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            Emit(Level.Info, tag, msg, file, line);
        }

        // ---- Warn 级别 ----
        // 需要 LOG_LEVEL_DEBUG 或 LOG_LEVEL_INFO 或 LOG_LEVEL_WARN + 对应分类宏

        [Conditional("LOG_LEVEL_DEBUG")]
        [Conditional("LOG_LEVEL_INFO")]
        [Conditional("LOG_LEVEL_WARN")]
        public static void W(string msg, Tag tag = Tag.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            Emit(Level.Warn, tag, msg, file, line);
        }

        // ---- Error 级别 — 始终编译，无条件输出 ----
        public static void E(string msg, Tag tag = Tag.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            Emit(Level.Error, tag, msg, file, line);
        }

        // ---- Fatal 级别 — 始终编译，无条件输出 + 立即刷盘 ----
        public static void F(string msg, Tag tag = Tag.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            Emit(Level.Fatal, tag, msg, file, line, forceFlush: true);
        }

        // ====================================================================
        //  运行时开关 API
        // ====================================================================

        /// <summary>总开关</summary>
        public static bool AllEnabled
        {
            get => _allEnabled;
            set => _allEnabled = value;
        }

        /// <summary>设置分类开关</summary>
        public static void SetTag(Tag tag, bool enabled)
        {
            lock (_switchLock) { _categorySwitch[tag] = enabled; }
        }

        /// <summary>查询分类开关</summary>
        public static bool GetTag(Tag tag)
        {
            lock (_switchLock)
            {
                return _categorySwitch.TryGetValue(tag, out var v) && v;
            }
        }

        /// <summary>获取所有分类开关（Editor 面板用）</summary>
        public static Dictionary<Tag, bool> GetAllTags()
        {
            lock (_switchLock) { return new Dictionary<Tag, bool>(_categorySwitch); }
        }

        /// <summary>手动刷盘</summary>
        public static void Flush()
        {
            _flushSignal.Set();
        }

        // ====================================================================
        //  兼容旧 API（过渡期保留，后续可标记 [Obsolete]）
        // ====================================================================

        /// <summary>兼容旧 WriteRunLog —— 已废弃，请改用 Log.I/W/E</summary>
        [System.Obsolete("请使用 Log.I/W/E 代替 WriteRunLog")]
        public static void WriteRunLog(string str, string level)
        {
            Level lvl;
            switch (level?.ToUpper())
            {
                case "DEBUG": lvl = Level.Debug; break;
                case "INFO": lvl = Level.Info; break;
                case "WARN": lvl = Level.Warn; break;
                case "ERROR": lvl = Level.Error; break;
                case "FATAL": lvl = Level.Fatal; break;
                default: lvl = Level.Info; break;
            }
            Emit(lvl, Tag.General, str, "", 0, forceFlush: lvl >= Level.Fatal);
        }

        /// <summary>兼容旧 WriteDebugLog —— 已废弃，请改用 Log.D/I</summary>
        [System.Obsolete("请使用 Log.D/I 代替 WriteDebugLog")]
        public static void WriteDebugLog(string str, string level)
        {
            Level lvl;
            switch (level?.ToUpper())
            {
                case "DEBUG": lvl = Level.Debug; break;
                case "INFO": lvl = Level.Info; break;
                case "WARN": lvl = Level.Warn; break;
                case "ERROR": lvl = Level.Error; break;
                default: lvl = Level.Info; break;
            }
            Emit(lvl, Tag.General, str, "", 0);
        }

        // ====================================================================
        //  内部实现
        // ====================================================================

        private static void Emit(Level lvl, Tag tag, string msg, string file, int line,
                                  bool forceFlush = false)
        {
            // 运行时开关：Error/Fatal 绕过开关始终输出
            if (lvl < Level.Error)
            {
                if (!_allEnabled) return;
                lock (_switchLock)
                {
                    if (_categorySwitch.TryGetValue(tag, out var on) && !on) return;
                }
            }

            // 节流：对 Debug/Info 级别启用（高频日志场景）
            if (lvl <= Level.Info && !PassThrottle(msg))
                return;

            // 格式化
            string fileName = string.IsNullOrEmpty(file) ? "" : Path.GetFileName(file);
            string src = string.IsNullOrEmpty(fileName) ? "" : $" ({fileName}:{line})";
            string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{LevelLabel(lvl)}] [{tag}]{src} {msg}";

            // 入队（无锁）
            if (_queue.Count < MaxQueueSize)
            {
                _queue.Enqueue(new LogEntry
                {
                    Formatted = formatted,
                    Lvl = lvl,
                    ForceFlush = forceFlush,
                });
            }

            // 达到阈值时唤醒写入线程
            if (_queue.Count >= FlushThreshold || forceFlush)
                _flushSignal.Set();

            // Unity Console 输出（仅编辑器/开发版有用）
#if UNITY_5_3_OR_NEWER
            string consoleMsg = $"[{tag}]{src} {msg}";
            switch (lvl)
            {
                case Level.Debug:
                case Level.Info:
                    UnityEngine.Debug.Log(consoleMsg);
                    break;
                case Level.Warn:
                    UnityEngine.Debug.LogWarning(consoleMsg);
                    break;
                case Level.Error:
                case Level.Fatal:
                    UnityEngine.Debug.LogError(consoleMsg);
                    break;
            }
#else
            Console.WriteLine(formatted);
#endif
        }

        // ---- 节流判定 ----
        private static bool PassThrottle(string msg)
        {
            int key = msg?.GetHashCode() ?? 0;
            long nowMs = Environment.TickCount;

            var state = _throttleMap.GetOrAdd(key, _ => new ThrottleState { LastTickMs = nowMs, Count = 0 });

            // 窗口过期，重置
            if (nowMs - state.LastTickMs > ThrottleWindowMs)
            {
                state.LastTickMs = nowMs;
                state.Count = 1;
                return true;
            }

            state.Count++;
            if (state.Count <= ThrottleMaxRepeat)
                return true;

            // 超过阈值，首次抑制时输出提示
            if (state.Count == ThrottleMaxRepeat + 1)
            {
                // 内联一条节流通知（不递归）
                string notice = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [WARN] [Log] 日志节流：后续相同消息在本窗口内被抑制 (hash={key})";
                if (_queue.Count < MaxQueueSize)
                    _queue.Enqueue(new LogEntry { Formatted = notice, Lvl = Level.Warn });
            }
            return false;
        }

        // ---- 后台写入循环 ----
        private static void WriterLoop()
        {
            var batch = new List<LogEntry>(FlushThreshold * 2);

            while (_running || !_queue.IsEmpty)
            {
                // 等待信号或超时
                _flushSignal.Wait(FlushIntervalMs);
                _flushSignal.Reset();

                // 批量出队
                batch.Clear();
                while (batch.Count < FlushThreshold * 4 && _queue.TryDequeue(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count == 0) continue;

                // 批量写入文件
                bool needForceFlush = false;
                try
                {
                    lock (_fileLock)
                    {
                        EnsureWriters();
                        for (int i = 0; i < batch.Count; i++)
                        {
                            var e = batch[i];
                            // Debug/Info 只写 DebugLog
                            if (e.Lvl <= Level.Info)
                            {
                                _debugWriter?.WriteLine(e.Formatted);
                            }
                            else
                            {
                                // Warn/Error/Fatal 同时写 RunLog 和 DebugLog
                                _debugWriter?.WriteLine(e.Formatted);
                                _runWriter?.WriteLine(e.Formatted);
                            }
                            if (e.ForceFlush) needForceFlush = true;
                        }

                        if (needForceFlush)
                        {
                            _debugWriter?.Flush();
                            _runWriter?.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 写入失败时降级到 stderr，避免递归日志
                    try { Console.Error.WriteLine("[Log] 写入异常: " + ex.Message); } catch { }
                }

                // 定期检查轮转
                CheckRotation();
            }

            // 退出前最终刷盘
            FinalFlush();
        }

        // ---- 文件路径初始化 ----
        private static void InitPaths()
        {
            string baseDir;
#if UNITY_5_3_OR_NEWER
            baseDir = Path.Combine(UnityEngine.Application.dataPath, "..", "Log");
#else
            baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
#endif
            _debugLogPath = Path.Combine(baseDir, "DebugLog.txt");
            _runLogPath = Path.Combine(baseDir, "RunLog.txt");
        }

        private static void EnsureDirectories()
        {
            try
            {
                string dir = Path.GetDirectoryName(_debugLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                dir = Path.GetDirectoryName(_runLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        private static void OpenWriters()
        {
            try
            {
                _debugWriter = new StreamWriter(_debugLogPath, append: true, encoding: Encoding.UTF8, bufferSize: 8192)
                {
                    AutoFlush = false,
                };
                _runWriter = new StreamWriter(_runLogPath, append: true, encoding: Encoding.UTF8, bufferSize: 4096)
                {
                    AutoFlush = false,
                };
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("[Log] 无法打开日志文件: " + ex.Message); } catch { }
            }
        }

        private static void EnsureWriters()
        {
            if (_debugWriter == null || _runWriter == null)
            {
                CloseWriters();
                OpenWriters();
            }
        }

        private static void CloseWriters()
        {
            try { _debugWriter?.Flush(); _debugWriter?.Dispose(); } catch { }
            try { _runWriter?.Flush(); _runWriter?.Dispose(); } catch { }
            _debugWriter = null;
            _runWriter = null;
        }

        // ---- 轮转检查 ----
        private static long _lastRotateCheckTick;
        private static void CheckRotation()
        {
            long now = Environment.TickCount;
            if (now - _lastRotateCheckTick < 30000) return; // 每 30 秒检查一次
            _lastRotateCheckTick = now;

            lock (_fileLock)
            {
                try
                {
                    RotateIfNeeded(_debugLogPath, ref _debugWriter);
                    RotateIfNeeded(_runLogPath, ref _runWriter);
                }
                catch { }
            }
        }

        private static void RotateIfNeeded(string path, ref StreamWriter writer)
        {
            try
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                if (fi.Length <= MaxFileSize) return;

                // 关闭当前写入器
                writer?.Flush();
                writer?.Dispose();
                writer = null;

                // 重命名旧文件
                string backup = path + ".1";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(path, backup);

                // 重新打开
                writer = new StreamWriter(path, append: false, encoding: Encoding.UTF8, bufferSize: 4096)
                {
                    AutoFlush = false,
                };
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] [Log] 日志轮转完成，旧文件: {Path.GetFileName(backup)}");
            }
            catch { }
        }

        // ---- 级别标签 ----
        private static string LevelLabel(Level lvl)
        {
            switch (lvl)
            {
                case Level.Debug: return "DEBUG";
                case Level.Info: return "INFO";
                case Level.Warn: return "WARN";
                case Level.Error: return "ERROR";
                case Level.Fatal: return "FATAL";
                default: return "?";
            }
        }

        // ---- 关闭 ----
        private static void Shutdown()
        {
            _running = false;
            _flushSignal.Set();
            // 等后台线程写完，最长1秒
            if (_writerThread != null && _writerThread.IsAlive)
                _writerThread.Join(1000);
            FinalFlush();
        }

        private static void FinalFlush()
        {
            // 把队列剩余全部写出
            lock (_fileLock)
            {
                try
                {
                    EnsureWriters();
                    while (_queue.TryDequeue(out var e))
                    {
                        if (e.Lvl <= Level.Info)
                        {
                            _debugWriter?.WriteLine(e.Formatted);
                        }
                        else
                        {
                            _debugWriter?.WriteLine(e.Formatted);
                            _runWriter?.WriteLine(e.Formatted);
                        }
                    }
                }
                catch { }
                CloseWriters();
            }
        }
    }
}
