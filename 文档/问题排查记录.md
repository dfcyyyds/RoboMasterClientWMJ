# 排查记录 (Investigations)

> 从本次开始记录：发现的问题 → 可能的原因 → 验证/修改 → 反馈结果。时间均为本机真实时间。

---

## 2026-01-24 17:44:43 +08:00

- 发现的问题:
  - RawImage 仍为空白；客户端仅见“组帧完成并上抛/推送至解码”，未见“解码帧/贴图更新”。

- 可能的原因:
  - 参数集（VPS/SPS/PPS 或 SPS/PPS）未正确进入解码器，或未在 IDR 前重发，导致 ffmpeg 未进入可解码态。
  - 解码器输入格式（HEVC/H.264）判定不一致，或 ffmpeg 进程无有效输出。
  - 次要可能：输出解析（PPM/rawvideo）未触发，但一般仍会产生“解码帧”日志。

- 做了什么验证:
  1) 查看解码器运行时诊断日志：
     - 在 Log/RunLog.txt 中发现多处“[FfmpegPipeDecoder] 缓存参数集: 85 bytes”日志（示例：行 3167、3256、3345、3802、3953、10235、…）。
     - 未检索到“IDR前重发参数集”“Codec=HEVC|H264”“解码帧”等日志。
  2) 检查解码器 stderr 关键词：
     - 在 Log/** 与 Logs/** 中未检索到 “SPS/PPS/VPS 缺失”“invalid NAL”等错误关键词。
  3) 核对服务端实际编码器 vs 客户端判定 Codec：
     - 客户端日志中未发现“Codec=”相关输出，暂无法直接核对；服务端日志未在当前工作区中呈现，需要运行并采集。

- 修改了哪些文件的什么内容:
  - 本次未对代码做任何修改，仅进行日志检索与结论记录。

- 反馈结果:
  - 现有证据显示：参数集存在“已缓存”的迹象，但未见“IDR前重发参数集/解码帧/Codec=”等入场必要信号；stderr 未报常见进入条件错误。
  - 推断问题仍集中在“解码输出阶段”（参数集与 IDR 协同、或编解码格式判定与 ffmpeg 进程输出）。
  - 建议下一步：运行端到端并采集更完整日志（含 ffmpeg stderr 与客户端 Codec 判定），必要时临时切换 rawvideo 输出以排除 PPM 解析因素。

---

## 2026-01-24 18:05:29 +08:00

- 发现的问题:
  - 通过诊断脚本聚合当前日志，仍未出现“解码帧”“Codec=”入场日志；仅见大量“缓存参数集：85 bytes”。
  - ffmpeg stderr 显示持续解析 VPS/SPS/PPS（hevc），说明参数集已进入解码器。

- 可能的原因:
  - 虽然参数集进入，但未与 IDR 同步入场或解码输出链路未触发（例如 PPM 输出、尺寸/色彩管线）。

- 做了什么验证/修改:
  1) 代码修改：在 `Assets/Scripts/Framework/Video/FfmpegPipeDecoder.cs` 的 `Push()` 中，新增明确的 `Codec=...` 日志：
     - 在自动检测到编解码器时输出 `[FfmpegPipeDecoder] Codec=<detected>`。
     - 在切换输入编解码器并重启进程后输出 `[FfmpegPipeDecoder] Codec=<inputCodec>`。
  2) 新增诊断脚本：`Assets/Diagnostics/investigate.sh`，用于聚合 `Log/RunLog.txt` 与 `Log/DebugLog.txt` 中的关键诊断（参数集/IDR/Codec/解码帧）以及 ffmpeg stderr 关键词。
  3) 运行脚本采集：显示 `stderr` 含 hevc 的 VPS/SPS/PPS 解码过程，但未见“解码帧/Codec=”尾部日志（当前窗口尾部数据）。

- 修改了哪些文件的什么内容:
  - `Assets/Scripts/Framework/Video/FfmpegPipeDecoder.cs`：在编解码器自动检测与切换处新增 `Codec=` 两处日志输出。
  - `Assets/Diagnostics/investigate.sh`：新增文件，封装日志聚合与关键词检索。

- 反馈结果:
  - 现状表明：参数集已进入 ffmpeg（stderr有 VPS/SPS/PPS），但“解码帧”仍未出现；入场缺失更可能与 IDR 协同或输出链路相关。
  - 下一步建议：
    - 运行客户端以促发 `VideoStreamService` 的入场诊断（含 `Codec=` 快照），并观察是否出现 `IDR前重发参数集` 与 `解码帧`。
    - 如仍不入场，临时切换 `FfmpegPipeDecoder` 为 `rawvideo` 输出（指定宽高），以排除 PPM 解析因素并快速验证数据面。

---

## 2026-01-24 18:07:45 +08:00

- 发现的问题:
  - 需要进一步验证数据面是否正常，排除 PPM 解析导致的未入场。

- 修改了哪些文件的什么内容:
  - `Assets/Scripts/Framework/Video/VideoStreamService.cs`
    - 将解码器实例切换为 `new FfmpegPipeDecoder(decoderCodec, useRawVideo: true, outputWidth: 1280, outputHeight: 720)`。
    - 初始化日志增加“输出=rawvideo 1280x720”。

- 做了什么验证/计划:
  - 计划运行客户端，观察是否产生 `解码帧` 与纹理更新；随后用 `Assets/Diagnostics/investigate.sh` 聚合日志，确认 `Codec=`、`IDR前重发参数集` 与 `解码帧` 是否出现。

- 反馈结果:
  - 待运行采集；切换为 rawvideo 旨在快速验证数据通路并排除 PPM 解析问题。

---

## 2026-01-24 18:28:57 +08:00

- 发现的问题:
  - 场景中已有一个空物体挂载 `VideoStreamService`，担心是否存在多实例导致竞争。

- 代码检查结论:
  - `Assets/Scripts/Framework/Video/VideoStreamService.cs` 的 `Awake()` 顶部有防重复逻辑：若 `Instance != null` 则 `Destroy(gameObject)` 并 `return`，确保运行时只有一个实例完成初始化。
  - `Assets/Scripts/Framework/Network/NetworkManager.cs` 在 `Awake()` 中若检测到 `VideoStreamService.Instance == null` 会自动创建一个 `GameObject("VideoStreamService")` 并 `AddComponent<VideoStreamService>()`，用于保障缺省场景下也能自动启动；若场景已挂载实例，则其 `Awake()` 会因 `Instance` 非空而自销毁，最终仍是单实例。

- 修改了哪些文件的什么内容:
  - `Assets/Scripts/Framework/Video/VideoStreamService.cs`：在检测到重复实例时新增日志 `[VideoStreamService] 检测到重复实例，销毁自身`，便于确认是否出现过重复实例及其自清理过程。

- 结合日志的现状分析:
  - 在 [Log/RunLog.txt](../Log/RunLog.txt) 中可见大量 `[VideoStreamService] 推送至解码`，以及 `[FfmpegPipeDecoder] 重启ffmpeg进程: 进程已退出` 后紧接 `[FfmpegPipeDecoder] 已启动 ffmpeg 管道解码`，属于同一解码器的重启行为并不能直接证明多实例。
  - 未检出 `NetworkManager` 的“自动创建 VideoStreamService”日志或“初始化完成”重复日志；结合代码逻辑，运行时维持单实例的可能性更高。

- 反馈结果:
  - 结论：当前项目结构通过 `Instance` 单例防线与 `NetworkManager` 的兜底创建机制，正常情况下不会出现多个视频接收流同时竞争；新增的重复销毁日志将帮助后续运行时确认是否曾发生重复实例。