using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// C# 与原生 NVDEC 插件的桥接层。
/// 支持两种渲染路径:
/// - Vulkan: CUDA-Vulkan 互操作 (零拷贝，最优性能)
/// - OpenGL: CUDA-GL 互操作 (备用)
/// </summary>
public static class NativeVideoBridge
{
    private const string DllName = "NativeVideoPlugin";

    [DllImport(DllName, EntryPoint = "nvp_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvp_init(int width, int height);

    [DllImport(DllName, EntryPoint = "nvp_push_udp", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvp_push_udp(IntPtr data, int length);

    [DllImport(DllName, EntryPoint = "nvp_get_latest_texture", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvp_get_latest_texture();

    [DllImport(DllName, EntryPoint = "nvp_shutdown", CallingConvention = CallingConvention.Cdecl)]
    private static extern void nvp_shutdown();

    [DllImport(DllName, EntryPoint = "nvp_get_vulkan_image", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr nvp_get_vulkan_image();

    [DllImport(DllName, EntryPoint = "nvp_is_vulkan_enabled", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvp_is_vulkan_enabled();

    [StructLayout(LayoutKind.Sequential)]
    public struct Stats
    {
        public int width;
        public int height;
        public int tex;
        public int pbo;
        public int cuPbo;
        public int framesDecoded;
        public int framesDisplayed;
        public int glReady;
        public int glFailed;
        public int vulkanEnabled;
    }

    [DllImport(DllName, EntryPoint = "nvp_get_stats", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvp_get_stats(out Stats stats);

    public static bool Available { get; private set; } = true;

    public static int Init(int width, int height)
    {
        if (!Available) return -1;
        try
        {
            return nvp_init(width, height);
        }
        catch (DllNotFoundException ex)
        {
            Available = false;
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] DllNotFound: " + ex.Message, "WARN");
            return -2;
        }
        catch (EntryPointNotFoundException ex)
        {
            Available = false;
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] EntryPointNotFound: " + ex.Message, "WARN");
            return -3;
        }
        catch (Exception ex)
        {
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] Init异常: " + ex.Message, "ERROR");
            return -4;
        }
    }

    public static int Push(byte[] data, int length)
    {
        if (!Available) return -1;
        if (data == null || length <= 0 || length > data.Length) return -1;
        try
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                return nvp_push_udp(ptr, length);
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception ex)
        {
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] Push异常: " + ex.Message, "ERROR");
            return -2;
        }
    }

    public static int GetLatestTextureId()
    {
        if (!Available) return 0;
        try
        {
            return nvp_get_latest_texture();
        }
        catch (Exception ex)
        {
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] GetLatestTextureId异常: " + ex.Message, "ERROR");
            return 0;
        }
    }

    public static bool TryGetStats(out Stats stats)
    {
        stats = default;
        if (!Available) return false;
        try
        {
            return nvp_get_stats(out stats) == 0;
        }
        catch (Exception ex)
        {
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] GetStats异常: " + ex.Message, "ERROR");
            return false;
        }
    }

    public static void Shutdown()
    {
        if (!Available) return;
        try
        {
            nvp_shutdown();
        }
        catch (Exception ex)
        {
            wmj.DebugTools.WriteRunLog("[NativeVideoBridge] Shutdown异常: " + ex.Message, "ERROR");
        }
    }

    /// <summary>
    /// 检查是否使用 Vulkan 渲染路径
    /// </summary>
    public static bool IsVulkanEnabled()
    {
        if (!Available) return false;
        try
        {
            return nvp_is_vulkan_enabled() != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取 Vulkan 图像句柄 (用于创建外部纹理)
    /// </summary>
    public static IntPtr GetVulkanImage()
    {
        if (!Available) return IntPtr.Zero;
        try
        {
            return nvp_get_vulkan_image();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
