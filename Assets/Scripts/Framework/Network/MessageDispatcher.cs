using System;
using System.Collections.Generic;

/// <summary>
/// 简单的主题-处理器分发器，适配 NetworkManager
/// </summary>
public class MessageDispatcher
{
    private readonly Dictionary<string, IMessageHandler> handlers = new Dictionary<string, IMessageHandler>();
    // 每Topic日志节流（每秒最多输出若干条）
    private class ThrottleState { public int Count; public int Suppressed; public long WindowStartMs; }
    private readonly Dictionary<string, ThrottleState> throttle = new Dictionary<string, ThrottleState>();
    private const int LogPerSecondLimit = 10; // 每个topic每秒最多记录10条分发日志
    private const int WindowMs = 1000;

    private ThrottleState GetState(string topic)
    {
        if (!throttle.TryGetValue(topic, out var s))
        {
            s = new ThrottleState { Count = 0, Suppressed = 0, WindowStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            throttle[topic] = s;
        }
        return s;
    }

    private bool ShouldLog(string topic)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var s = GetState(topic);
        if (now - s.WindowStartMs >= WindowMs)
        {
            // 窗口切换，输出上一窗口抑制摘要
            if (s.Suppressed > 0)
            {
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[MessageDispatcher] 日志节流: topic={topic}, 上一秒抑制={s.Suppressed}", wmj.DebugTools.LogCategory.Network);
                wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 日志节流: topic=" + topic + ", 上一秒抑制=" + s.Suppressed, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[MessageDispatcher] 日志节流: topic=" + topic + ", 上一秒抑制=" + s.Suppressed, "WARN");
            }
            s.WindowStartMs = now;
            s.Count = 0;
            s.Suppressed = 0;
        }
        if (s.Count < LogPerSecondLimit)
        {
            s.Count++;
            return true;
        }
        s.Suppressed++;
        return false;
    }

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    public void RegisterHandler(string topic, IMessageHandler handler)
    {
        bool replaced = handlers.ContainsKey(topic);
        handlers[topic] = handler;
#if UNITY_EDITOR
        if (replaced)
        {
            wmj.DebugTools.Info($"[MessageDispatcher] 处理器覆盖: topic={topic}, handler={handler?.GetType().Name}", wmj.DebugTools.LogCategory.Network);
            wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 处理器覆盖: topic=" + topic + ", handler=" + handler?.GetType().Name, "INFO");
        }
        else
        {
            wmj.DebugTools.Info($"[MessageDispatcher] 注册处理器: topic={topic}, handler={handler?.GetType().Name}", wmj.DebugTools.LogCategory.Network);
            wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 注册处理器: topic=" + topic + ", handler=" + handler?.GetType().Name, "INFO");
        }
#endif
        if (replaced)
            wmj.DebugTools.WriteRunLog("[MessageDispatcher] 处理器覆盖: topic=" + topic + ", handler=" + handler?.GetType().Name, "INFO");
        else
            wmj.DebugTools.WriteRunLog("[MessageDispatcher] 注册处理器: topic=" + topic + ", handler=" + handler?.GetType().Name, "INFO");
    }

    /// <summary>
    /// 注销消息处理器
    /// </summary>
    public void UnregisterHandler(string topic)
    {
        if (handlers.ContainsKey(topic))
        {
            handlers.Remove(topic);
#if UNITY_EDITOR
            wmj.DebugTools.Info($"[MessageDispatcher] 注销处理器: topic={topic}");
            wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 注销处理器: topic=" + topic, "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[MessageDispatcher] 注销处理器: topic=" + topic, "INFO");
        }
        else
        {
#if UNITY_EDITOR
            wmj.DebugTools.Warn($"[MessageDispatcher] 注销处理器失败，未找到: topic={topic}");
            wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 注销处理器失败，未找到: topic=" + topic, "WARN");
#endif
            wmj.DebugTools.WriteRunLog("[MessageDispatcher] 注销处理器失败，未找到: topic=" + topic, "WARN");
        }
    }

    /// <summary>
    /// 分发消息到对应处理器（保持兼容的 byte[] 入口）。
    /// </summary>
    public void Dispatch(string topic, byte[] payload)
    {
        var seg = payload == null ? ArraySegment<byte>.Empty : new ArraySegment<byte>(payload, 0, payload.Length);
        DispatchSegment(topic, seg);
    }

    /// <summary>
    /// 支持零拷贝分发的入口，优先调用实现 IMessageSegmentHandler 的处理器。
    /// 调用方不得在回调结束后继续持有 payload（若需要请自行复制）。
    /// </summary>
    public void DispatchSegment(string topic, ArraySegment<byte> payload)
    {
        if (handlers.TryGetValue(topic, out var handler))
        {
            if (ShouldLog(topic))
            {
#if UNITY_EDITOR
                wmj.DebugTools.Info($"[MessageDispatcher] 分发消息: topic={topic}, len={payload.Count}", wmj.DebugTools.LogCategory.Network);
                wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 分发消息: topic=" + topic + ", len=" + payload.Count, "INFO");
#endif
                wmj.DebugTools.WriteRunLog("[MessageDispatcher] 分发消息: topic=" + topic + ", len=" + payload.Count, "INFO");
            }

            if (handler is IMessageSegmentHandler segHandler)
            {
                segHandler.HandleMessage(topic, payload);
            }
            else
            {
                // 兼容旧接口：复制一份 byte[]
                var copy = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array, payload.Offset, copy, 0, payload.Count);
                handler.HandleMessage(topic, copy);
            }
        }
        else
        {
            if (ShouldLog(topic))
            {
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[MessageDispatcher] 未找到处理器，丢弃消息: topic={topic}, len={payload.Count}", wmj.DebugTools.LogCategory.Network);
                wmj.DebugTools.WriteDebugLog("[MessageDispatcher] 未找到处理器，丢弃消息: topic=" + topic + ", len=" + payload.Count, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[MessageDispatcher] 未找到处理器，丢弃消息: topic=" + topic + ", len=" + payload.Count, "WARN");
            }
        }
    }
}
