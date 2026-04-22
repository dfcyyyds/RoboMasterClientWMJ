using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Framework.Video
{
    /// <summary>
    /// ffmpeg 可执行文件定位器：优先使用客户端内置版本，找不到再回退系统 PATH。
    ///
    /// 内置目录布局（位于 StreamingAssets/ffmpeg/ 下）：
    ///   Windows:  StreamingAssets/ffmpeg/win64/ffmpeg.exe
    ///   Linux:    StreamingAssets/ffmpeg/linux64/ffmpeg
    ///   macOS:    StreamingAssets/ffmpeg/macos/ffmpeg
    ///
    /// 用户在没有系统 ffmpeg 的电脑上无需手工安装，开箱即用。
    /// </summary>
    public static class FfmpegLocator
    {
        // 缓存解析结果，避免每次启动 ffmpeg 进程都做一次 IO
        private static string cachedPath = null;
        private static bool cacheValid = false;
        private static readonly object lockObj = new object();

        /// <summary>
        /// 返回 ffmpeg 可执行文件路径。优先返回内置绝对路径；找不到则返回 "ffmpeg"（依赖系统 PATH）。
        /// 调用方可直接用于 ProcessStartInfo.FileName。
        /// </summary>
        public static string GetExecutablePath()
        {
            if (cacheValid) return cachedPath;
            lock (lockObj)
            {
                if (cacheValid) return cachedPath;
                cachedPath = ResolvePath();
                cacheValid = true;
                return cachedPath;
            }
        }

        /// <summary>
        /// 是否当前用的是内置 ffmpeg（绝对路径）。
        /// </summary>
        public static bool IsBundled()
        {
            var p = GetExecutablePath();
            return !string.IsNullOrEmpty(p) && Path.IsPathRooted(p);
        }

        /// <summary>清除缓存，下次调用重新解析（一般用于调试）。</summary>
        public static void InvalidateCache()
        {
            lock (lockObj) { cacheValid = false; cachedPath = null; }
        }

        private static string ResolvePath()
        {
            try
            {
                string streamingRoot = Application.streamingAssetsPath; // 主线程上下文最佳；构造期已经可用
                string subDir;
                string exeName;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                subDir = "win64";
                exeName = "ffmpeg.exe";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                subDir = "macos";
                exeName = "ffmpeg";
#else
                subDir = "linux64";
                exeName = "ffmpeg";
#endif

                string candidate = Path.Combine(streamingRoot, "ffmpeg", subDir, exeName);
                if (File.Exists(candidate))
                {
                    // Linux/macOS 需要执行位；Unity 解压 StreamingAssets 时会丢失权限
                    EnsureExecutable(candidate);
                    UnityEngine.Debug.Log($"[FfmpegLocator] 使用内置 ffmpeg: {candidate}");
                    return candidate;
                }

                UnityEngine.Debug.LogWarning($"[FfmpegLocator] 未找到内置 ffmpeg ({candidate})，尝试系统路径");

                // 显式探测常见系统安装位置（Unity 编辑器进程 PATH 可能不含 /usr/bin）
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
                string[] systemPaths = {
                    "/usr/bin/ffmpeg",
                    "/usr/local/bin/ffmpeg",
                    "/opt/homebrew/bin/ffmpeg",  // macOS Homebrew arm64/x86
                    "/snap/bin/ffmpeg",
                };
                foreach (var sp in systemPaths)
                {
                    if (File.Exists(sp))
                    {
                        EnsureExecutable(sp);
                        UnityEngine.Debug.Log($"[FfmpegLocator] 使用系统 ffmpeg: {sp}");
                        return sp;
                    }
                }
#endif
                UnityEngine.Debug.LogWarning($"[FfmpegLocator] 系统路径亦未找到 ffmpeg，回退命令名 \"ffmpeg\"");
                return "ffmpeg";
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[FfmpegLocator] 定位异常: {ex.Message}，回退系统 PATH");
                return "ffmpeg";
            }
        }

        private static void EnsureExecutable(string path)
        {
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
            try
            {
                // 调 chmod +x；不抛异常，失败则交给后续 Process.Start 报错
                var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = "+x \"" + path + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                p.Start();
                p.WaitForExit(2000);
                if (!p.HasExited) { try { p.Kill(); } catch { } }
            }
            catch { }
#endif
        }
    }
}
