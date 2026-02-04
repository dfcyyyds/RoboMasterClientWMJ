using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// 忽略未使用的变量，否则在编译阶段会报警告
#pragma warning disable CS0414
// 忽略null
#pragma warning disable CS8600

namespace wmj
{
    public static class DebugTools
    {
        static string Reset = "\x1b[0m";
        static string Red = "\x1b[31m";
        static string Green = "\x1b[32m";
        static string Yellow = "\x1b[33m";
        static string Blue = "\x1b[34m";
        static string Magenta = "\x1b[35m";
        static string Cyan = "\x1b[36m";
        static string White = "\x1b[37m";
        static readonly object lockObject = new object();
        static string DebugLogPath;
        static string RunLogPath;

        // 日志分类定义
        public enum LogCategory { General, Network, Video, Decoder, Transport, UI, Custom1, Custom2 }
        // 分类开关（可由参数管理器/配置界面动态设置）
        public static Dictionary<LogCategory, bool> CategorySwitch = new Dictionary<LogCategory, bool>
        {
            { LogCategory.General, true },
            { LogCategory.Network, true },
            { LogCategory.Video, true },
            { LogCategory.Decoder, true },
            { LogCategory.Transport, true },
            { LogCategory.UI, true },
            { LogCategory.Custom1, false },
            { LogCategory.Custom2, false }
        };
        // 总开关
        public static bool AllLogEnabled = true;
        // 日志缓冲区
        private static List<string> debugLogBuffer = new List<string>();
        private static int logBufferSize = 32;

        static DebugTools()
        {
            string baseDir;
#if UNITY_5_3_OR_NEWER
            baseDir = Path.Combine(Application.dataPath, "..", "Log");
#else
            baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
#endif
            DebugLogPath = Path.Combine(baseDir, "DebugLog.txt");
            RunLogPath = Path.Combine(baseDir, "RunLog.txt");
            EnsureAllLogDirectories();
            // 读取参数系统的缓冲区大小
            try { logBufferSize = ConfigLoader.config != null ? ConfigLoader.config.logBufferSize : 32; } catch { logBufferSize = 32; }
        }

        // 设置日志缓冲区大小（参数变更时可调用）
        public static void SetLogBufferSize(int size)
        {
            lock (lockObject) { logBufferSize = Math.Max(4, Math.Min(size, 1024)); }
        }

        // 设置日志分类开关（参数变更时可调用）
        public static void SetCategory(LogCategory cat, bool enabled)
        {
            lock (lockObject) { CategorySwitch[cat] = enabled; }
        }

        // 设置总开关
        public static void SetAllLogEnabled(bool enabled)
        {
            lock (lockObject) { AllLogEnabled = enabled; }
        }

        /// <summary>
        /// 确保所有日志目录都存在
        /// </summary>
        private static void EnsureAllLogDirectories()
        {
            try
            {
                // 确保调试日志目录存在
                string debugLogDir = Path.GetDirectoryName(DebugLogPath);
                if (!string.IsNullOrEmpty(debugLogDir) && !Directory.Exists(debugLogDir))
                {
#if DEBUG_MODE
                    Debug($"[DebugTools] 创建调试日志目录: {debugLogDir}");
#endif
                    Directory.CreateDirectory(debugLogDir);
                }

                // 确保运行日志目录存在
                string runLogDir = Path.GetDirectoryName(RunLogPath);
                if (!string.IsNullOrEmpty(runLogDir) && !Directory.Exists(runLogDir))
                {
#if DEBUG_MODE
                    Debug($"[DebugTools] 创建运行日志目录: {runLogDir}");
#endif
                    Directory.CreateDirectory(runLogDir);
                }

#if DEBUG_MODE
                Info("[DebugTools] 日志目录初始化完成");
#endif
            }
            catch (Exception ex)
            {
                Error($"[DebugTools] 创建日志目录失败: {ex.Message}");
                Error($"[DebugTools] 错误详情: {ex}");
            }
        }




        // 分类日志输出（支持节流与分类开关）
        public static void Debug(string str, LogCategory cat = LogCategory.General, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            string tag = $"[Debug][{cat}] ({Path.GetFileName(file)}:{line})";
            lock (lockObject)
            {
                if (!AllLogEnabled || !CategorySwitch.ContainsKey(cat) || !CategorySwitch[cat]) return;
                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag} {str}";
                debugLogBuffer.Add(log);
                if (debugLogBuffer.Count >= logBufferSize)
                {
                    FlushDebugLogBuffer();
                }
#if UNITY_5_3_OR_NEWER
                UnityEngine.Debug.Log($"{tag} {str}");
#else
                Console.WriteLine($"{White}{tag} {str}{Reset}");
#endif
            }
        }

        public static void Info(string str, LogCategory cat = LogCategory.General, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            string tag = $"[Info][{cat}] ({Path.GetFileName(file)}:{line})";
            lock (lockObject)
            {
                if (!AllLogEnabled || !CategorySwitch.ContainsKey(cat) || !CategorySwitch[cat]) return;
                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag} {str}";
                debugLogBuffer.Add(log);
                if (debugLogBuffer.Count >= logBufferSize)
                {
                    FlushDebugLogBuffer();
                }
#if UNITY_5_3_OR_NEWER
                UnityEngine.Debug.Log($"{tag} {str}");
#else
                Console.WriteLine($"{Green}{tag} {str}{Reset}");
#endif
            }
        }


        public static void Warn(string str, LogCategory cat = LogCategory.General, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            string tag = $"[Warn][{cat}] ({Path.GetFileName(file)}:{line})";
            lock (lockObject)
            {
                // WARN始终写RunLog
                WriteRunLog($"{tag} {str}", "WARN");
                if (!AllLogEnabled || !CategorySwitch.ContainsKey(cat) || !CategorySwitch[cat]) return;
                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag} {str}";
                debugLogBuffer.Add(log);
                if (debugLogBuffer.Count >= logBufferSize)
                {
                    FlushDebugLogBuffer();
                }
#if UNITY_5_3_OR_NEWER
                UnityEngine.Debug.LogWarning($"{tag} {str}");
#else
                Console.WriteLine($"{Yellow}{tag} {str}{Reset}");
#endif
            }
        }


        public static void Error(string str, LogCategory cat = LogCategory.General, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            string tag = $"[Error][{cat}] ({Path.GetFileName(file)}:{line})";
            lock (lockObject)
            {
                // ERROR始终写RunLog
                WriteRunLog($"{tag} {str}", "ERROR");
                if (!AllLogEnabled || !CategorySwitch.ContainsKey(cat) || !CategorySwitch[cat]) return;
                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag} {str}";
                debugLogBuffer.Add(log);
                if (debugLogBuffer.Count >= logBufferSize)
                {
                    FlushDebugLogBuffer();
                }
#if UNITY_5_3_OR_NEWER
                UnityEngine.Debug.LogError($"{tag} {str}");
#else
                Console.WriteLine($"{Red}{tag} {str}{Reset}");
#endif
            }
        }


        public static void Fatal(string str, LogCategory cat = LogCategory.General, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            string tag = $"[Fatal][{cat}] ({Path.GetFileName(file)}:{line})";
            lock (lockObject)
            {
                // FATAL始终写RunLog
                WriteRunLog($"{tag} {str}", "FATAL");
                if (!AllLogEnabled || !CategorySwitch.ContainsKey(cat) || !CategorySwitch[cat]) return;
                string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag} {str}";
                debugLogBuffer.Add(log);
                if (debugLogBuffer.Count >= logBufferSize)
                {
                    FlushDebugLogBuffer();
                }
#if UNITY_5_3_OR_NEWER
                UnityEngine.Debug.LogError($"{tag} {str}");
#else
                Console.WriteLine($"{Red}{tag} {str}{Reset}");
#endif
            }
        }

        // 批量写入调试日志缓冲区
        public static void FlushDebugLogBuffer()
        {
            if (debugLogBuffer.Count == 0) return;
            try
            {
                File.AppendAllLines(DebugLogPath, debugLogBuffer);
                debugLogBuffer.Clear();
            }
            catch (Exception ex)
            {
                // 若写入失败，直接丢弃并写入RunLog
                WriteRunLog($"[DebugTools] 日志缓冲写入失败: {ex.Message}", "ERROR");
                debugLogBuffer.Clear();
            }
        }

        /// <summary>
        /// 写入调试日志
        /// </summary>
        /// <param name="str">调试信息</param>
        /// <param name="level">日志等级</param>
        public static void WriteDebugLog(string str, string level)
        {
            lock (lockObject)
            {
                // 确保日志目录存在
                string dir = Path.GetDirectoryName(DebugLogPath);
                if (!Directory.Exists(dir))
                {
                    Error("调试日志目录不存在，请检查！");
                    return;
                }

                // 如果日志文件太大，进行轮转
                if (File.Exists(DebugLogPath) && new FileInfo(DebugLogPath).Length > 1024 * 1024 * 10)
                {
#if DEBUG_MODE
                    Info("调试日志文件过大，进行轮转");
#endif
                    Rotate(DebugLogPath);
                    File.WriteAllText(DebugLogPath, $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}]日志轮转" + Environment.NewLine);
                }

                string log = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{level}] {str}";
                File.AppendAllText(DebugLogPath, log + Environment.NewLine);
            }
        }

        /// <summary>
        /// 写入运行日志
        /// </summary>
        /// <param name="str">运行信息</param>
        /// <param name="level">日志等级</param>
        public static void WriteRunLog(string str, string level)
        {
            lock (lockObject)
            {
                // 确保日志目录存在
                string dir = Path.GetDirectoryName(RunLogPath);
                if (!Directory.Exists(dir))
                {
                    Error("运行日志目录不存在，请检查！");
                    return;
                }

                // 如果日志文件太大，进行轮转
                if (File.Exists(RunLogPath) && new FileInfo(RunLogPath).Length > 1024 * 1024 * 10)
                {
#if DEBUG_MODE
                    Info("运行日志文件过大，进行轮转");
#endif
                    Rotate(RunLogPath);
                    File.WriteAllText(RunLogPath, $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}]日志轮转" + Environment.NewLine);
                }

                string log = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{level}] {str}";
                File.AppendAllText(RunLogPath, log + Environment.NewLine);
            }

        }

        /// <summary>
        /// 日志轮转机制，当日志文件大小超过10MB时进行轮转
        /// </summary>
        /// <param name="path"></param>
        static void Rotate(string path)
        {
            try
            {
                // 读取所有行
                string[] allLines = File.ReadAllLines(path);

                // 获取需要保留的行
                int totalLines = allLines.Length;
                int keepLines = totalLines / 2;
                var linesToKeep = allLines.Skip(totalLines - keepLines).ToArray();

                //写回文件
                File.WriteAllLines(path, linesToKeep);
#if DEBUG_MODE
                Info($"日志轮转完成: 原始 {totalLines} 行 → 保留 {keepLines} 行");
                Info($"删除了前 {totalLines - keepLines} 行");
#endif
            }
            catch (Exception e)
            {
                Error($"日志轮转失败: {e.Message}");
            }
        }
    }
}