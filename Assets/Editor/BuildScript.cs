using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.IO;
using System.Linq;

/// <summary>
/// 双平台自动化构建脚本
/// 
/// 使用方式:
/// 1. Unity 编辑器菜单: Build → 构建双平台 / 构建 Linux64 / 构建 Windows64
/// 2. Shell 脚本: ./build.sh [linux|windows|all]
/// 3. 命令行: Unity -batchmode -executeMethod BuildScript.CommandLineBuild -quit
/// 
/// 构建产物:
///   Builds/Linux64/RoboMasterClient
///   Builds/Windows64/RoboMasterClient.exe
///   Builds/RoboMasterClient_{平台}_{日期}.tar.gz
/// </summary>
public static class BuildScript
{
    private const string BuildRoot = "Builds";
    private const string ProductName = "RoboMasterClient";

    // ═══════════════════ 获取场景列表 ═══════════════════

    private static string[] GetScenes()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            // 回退：自动查找 MainScene
            var mainScene = AssetDatabase.FindAssets("t:Scene MainScene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(mainScene))
            {
                Log($"Build Settings 为空，自动使用 {mainScene}");
                scenes = new[] { mainScene };
            }
        }
        return scenes;
    }

    // ═══════════════════ 编辑器菜单入口 ═══════════════════

    [MenuItem("Build/构建 Linux64 %&l", false, 100)]
    public static void BuildLinux64()
    {
        DoBuild(BuildTarget.StandaloneLinux64, "Linux64", ProductName);
    }

    [MenuItem("Build/构建 Windows64 %&w", false, 101)]
    public static void BuildWindows64()
    {
        DoBuild(BuildTarget.StandaloneWindows64, "Windows64", ProductName + ".exe");
    }

    [MenuItem("Build/构建双平台（Linux64 + Windows64） %&b", false, 200)]
    public static void BuildAll()
    {
        Log("═══════════════════════════════════════");
        Log("开始双平台构建");
        Log("═══════════════════════════════════════");

        var startTime = DateTime.Now;
        bool linuxOk = DoBuild(BuildTarget.StandaloneLinux64, "Linux64", ProductName);
        bool winOk = DoBuild(BuildTarget.StandaloneWindows64, "Windows64", ProductName + ".exe");
        var elapsed = DateTime.Now - startTime;

        Log("═══════════════════════════════════════");
        Log($"双平台构建完成  Linux: {(linuxOk ? "✅" : "❌")}  Windows: {(winOk ? "✅" : "❌")}  总耗时: {elapsed.TotalSeconds:F1}s");
        Log("═══════════════════════════════════════");

        if (Application.isBatchMode && (!linuxOk || !winOk))
            EditorApplication.Exit(1);
    }

    // ═══════════════════ 命令行入口 ═══════════════════

    /// <summary>
    /// 命令行调用入口，支持参数:
    ///   -buildTarget linux|windows|all (默认 all)
    ///   -development                   (开发构建，含调试符号)
    ///   -cleanBuild                    (构建前清理旧文件)
    /// </summary>
    public static void CommandLineBuild()
    {
        string[] args = Environment.GetCommandLineArgs();
        string targetStr = "all";
        bool development = false;
        bool clean = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "-customtarget" when i + 1 < args.Length:
                    targetStr = args[++i].ToLower();
                    break;
                case "-development":
                    development = true;
                    break;
                case "-cleanbuild":
                    clean = true;
                    break;
            }
        }

        Log($"命令行构建: target={targetStr}, development={development}, clean={clean}");

        bool buildLinux = targetStr == "all" || targetStr.Contains("linux");
        bool buildWindows = targetStr == "all" || targetStr.Contains("win");
        bool anyFailed = false;

        BuildOptions extraOpts = BuildOptions.None;
        if (development)
            extraOpts |= BuildOptions.Development | BuildOptions.AllowDebugging;

        if (buildLinux)
        {
            if (clean) CleanBuildFolder("Linux64");
            if (!DoBuild(BuildTarget.StandaloneLinux64, "Linux64", ProductName, extraOpts))
                anyFailed = true;
        }

        if (buildWindows)
        {
            if (clean) CleanBuildFolder("Windows64");
            if (!DoBuild(BuildTarget.StandaloneWindows64, "Windows64", ProductName + ".exe", extraOpts))
                anyFailed = true;
        }

        if (anyFailed)
        {
            LogError("构建存在失败项，退出码 1");
            EditorApplication.Exit(1);
        }
        else
        {
            Log("所有构建成功完成");
        }
    }

    // ═══════════════════ 核心构建逻辑 ═══════════════════

    private static bool DoBuild(BuildTarget target, string folderName, string executableName,
        BuildOptions extraOptions = BuildOptions.None)
    {
        string[] scenes = GetScenes();
        if (scenes.Length == 0)
        {
            LogError("没有可构建的场景！请在 Build Settings 中添加场景，或确保 MainScene.unity 存在。");
            return false;
        }

        string outputDir = Path.Combine(BuildRoot, folderName);
        string outputPath = Path.Combine(outputDir, executableName);

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        Log($"┌─ 构建 {target}");
        Log($"│  场景: {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");
        Log($"│  输出: {outputPath}");
        if (extraOptions != BuildOptions.None)
            Log($"│  选项: {extraOptions}");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            options = extraOptions
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        stopwatch.Stop();
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            long sizeMB = (long)(summary.totalSize / (1024 * 1024));
            Log($"│  ✅ 成功! 大小: {sizeMB}MB, 耗时: {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (target == BuildTarget.StandaloneLinux64)
                SetLinuxExecutable(outputPath);

            string tarPath = CreateTarGz(folderName);
            if (tarPath != null)
                Log($"│  📦 已打包: {Path.GetFileName(tarPath)}");

            Log($"└─ {target} 完成");
            return true;
        }
        else
        {
            LogError($"│  ❌ 失败: {summary.result}");
            LogError($"│  错误: {summary.totalErrors}, 警告: {summary.totalWarnings}");

            var errors = report.steps
                .SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Error)
                .Take(10);
            foreach (var msg in errors)
                LogError($"│    {msg.content}");

            Log($"└─ {target} 失败");
            return false;
        }
    }

    // ═══════════════════ 工具方法 ═══════════════════

    private static void CleanBuildFolder(string folderName)
    {
        string dir = Path.Combine(BuildRoot, folderName);
        if (Directory.Exists(dir))
        {
            Log($"清理构建目录: {dir}");
            Directory.Delete(dir, true);
        }
    }

    private static void SetLinuxExecutable(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
        }
        catch { /* 非 Linux 环境忽略 */ }
    }

    private static string CreateTarGz(string folderName)
    {
        string date = DateTime.Now.ToString("yyyyMMdd");
        string tarName = $"{ProductName}_{folderName}_{date}.tar.gz";
        string tarPath = Path.Combine(BuildRoot, tarName);

        try
        {
            if (File.Exists(tarPath)) File.Delete(tarPath);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-czf \"{tarPath}\" -C \"{BuildRoot}\" \"{folderName}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(120000);
                if (proc.ExitCode == 0)
                    return tarPath;
                LogWarning($"tar 打包失败: {proc.StandardError.ReadToEnd()}");
            }
        }
        catch (Exception e)
        {
            LogWarning($"打包跳过: {e.Message}");
        }
        return null;
    }

    private static void Log(string msg) => Debug.Log($"[BuildScript] {msg}");
    private static void LogWarning(string msg) => Debug.LogWarning($"[BuildScript] {msg}");
    private static void LogError(string msg) => Debug.LogError($"[BuildScript] {msg}");
}
