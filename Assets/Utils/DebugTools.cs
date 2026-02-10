using System;
using System.Collections.Generic;
using System.IO;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// 忽略过时警告（此文件本身就是过渡兼容层）
#pragma warning disable CS0612
#pragma warning disable CS0618

namespace wmj
{
    // ====================================================================
    // DebugTools — 旧日志系统兼容层
    //
    // ⚠️ 已废弃：所有调用已迁移到 wmj.Log / wmj.LogBridge
    //    此文件仅为编译兼容保留，后续版本将删除。
    //
    // 新代码请使用：
    //   Log.I("msg", Log.Tag.Network);      // 统一日志
    //   LogBridge.Video.D("msg");            // 带编译宏控制
    // ====================================================================
    [Obsolete("请使用 wmj.Log / wmj.LogBridge 替代。此类将在后续版本移除。")]
    public static class DebugTools
    {
        // 保留枚举定义以防外部引用
        public enum LogCategory { General, Network, Video, Decoder, Transport, UI, Custom1, Custom2 }

        // 分类开关（转发到 Log 系统）
        public static Dictionary<LogCategory, bool> CategorySwitch
        {
            get
            {
                var tags = Log.GetAllTags();
                var dict = new Dictionary<LogCategory, bool>();
                foreach (LogCategory cat in Enum.GetValues(typeof(LogCategory)))
                {
                    if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                        dict[cat] = tags.ContainsKey(tag) && tags[tag];
                    else
                        dict[cat] = false;
                }
                return dict;
            }
        }

        public static bool AllLogEnabled
        {
            get => Log.AllEnabled;
            set => Log.AllEnabled = value;
        }

        public static void SetCategory(LogCategory cat, bool enabled)
        {
            if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                Log.SetTag(tag, enabled);
        }

        public static void SetAllLogEnabled(bool enabled)
        {
            Log.AllEnabled = enabled;
        }

        // ---- 转发方法 ----
        public static void Debug(string str, LogCategory cat = LogCategory.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                Log.D(str, tag, file, line);
        }

        public static void Info(string str, LogCategory cat = LogCategory.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                Log.I(str, tag, file, line);
        }

        public static void Warn(string str, LogCategory cat = LogCategory.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                Log.W(str, tag, file, line);
        }

        public static void Error(string str, LogCategory cat = LogCategory.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                Log.E(str, tag, file, line);
        }

        public static void Fatal(string str, LogCategory cat = LogCategory.General,
            [System.Runtime.CompilerServices.CallerFilePath] string file = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Enum.TryParse<Log.Tag>(cat.ToString(), out var tag))
                Log.F(str, tag, file, line);
        }

        public static void WriteRunLog(string str, string level)
        {
            // 转发到 Log 系统的兼容旧 API
#pragma warning disable CS0618
            Log.WriteRunLog(str, level);
#pragma warning restore CS0618
        }

        public static void WriteDebugLog(string str, string level)
        {
#pragma warning disable CS0618
            Log.WriteDebugLog(str, level);
#pragma warning restore CS0618
        }

        public static void FlushDebugLogBuffer()
        {
            Log.Flush();
        }

        public static void SetLogBufferSize(int size)
        {
            // 新日志系统使用固定缓冲策略，此方法已无效
        }
    }
}
