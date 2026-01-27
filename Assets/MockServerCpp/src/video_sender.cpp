#include "video_sender.h"

#include <arpa/inet.h>
#include <sys/socket.h>
#include <unistd.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <thread>
#include <vector>

#include "../log_monitor.h"  // 复用它的日志或者由于是单独线程直接cout

static std::atomic<bool> g_video_running(false);
static std::thread g_video_thread;
static bool g_use_hevc = true;
static int g_gop = 15;

// Protocol Constants
const int MAX_UDP_PAYLOAD = 1400;  // Safe size under MTU 1500
const int HEADER_SIZE = 8;         // 2(FrameId) + 2(FragId) + 4(TotalBytes)
const int MAX_PACKET_SIZE = MAX_UDP_PAYLOAD + HEADER_SIZE;

// UDP发送函数
static uint64_t g_video_packets_sent = 0;
static uint64_t g_video_bytes_sent = 0;

void SendUdpPacket(int sock, const sockaddr_in& addr, uint16_t frameId,
                   uint16_t fragId, uint32_t totalBytes, const uint8_t* data,
                   int len) {
  std::vector<uint8_t> buffer(HEADER_SIZE + len);

  // Header: Little Endian (frameId, fragId, totalBytes)
  buffer[0] = frameId & 0xFF;
  buffer[1] = (frameId >> 8) & 0xFF;
  buffer[2] = fragId & 0xFF;
  buffer[3] = (fragId >> 8) & 0xFF;
  buffer[4] = totalBytes & 0xFF;
  buffer[5] = (totalBytes >> 8) & 0xFF;
  buffer[6] = (totalBytes >> 16) & 0xFF;
  buffer[7] = (totalBytes >> 24) & 0xFF;

  // Payload
  std::memcpy(buffer.data() + HEADER_SIZE, data, len);

  ssize_t sent = sendto(sock, buffer.data(), buffer.size(), 0,
                        (struct sockaddr*)&addr, sizeof(addr));
  if (sent > 0) {
    g_video_packets_sent++;
    g_video_bytes_sent += sent;
  } else {
    std::cerr << "[VideoSender] 发送失败: errno=" << errno << std::endl;
  }
}

// 视频发送主循环
void VideoSenderLoop(std::string target_ip, int target_port,
                     std::string device_path) {
  std::cout << "[VideoSender] 启动视频发送线程. 目标: " << target_ip << ":"
            << target_port << " 设备: " << device_path << std::endl;
  printf("[VideoSender] 📡 开始推送 %s AnnexB 到 %s:%d\n",
         g_use_hevc ? "HEVC(H.265)" : "H264", target_ip.c_str(), target_port);

  // 1. 创建UDP Socket
  int sock = socket(AF_INET, SOCK_DGRAM, 0);
  if (sock < 0) {
    std::cerr << "[VideoSender] 创建Socket失败" << std::endl;
    return;
  }

  // 提升UDP发送缓冲与优先级，减少阻塞与丢包
  {
    int sndbuf = 4 * 1024 * 1024;  // 4MB 发送缓冲
    setsockopt(sock, SOL_SOCKET, SO_SNDBUF, &sndbuf, sizeof(sndbuf));
#ifdef SO_PRIORITY
    int priority = 6;  // 较高优先级（0-6，越大越高）
    setsockopt(sock, SOL_SOCKET, SO_PRIORITY, &priority, sizeof(priority));
#endif
  }

  struct sockaddr_in addr;
  std::memset(&addr, 0, sizeof(addr));
  addr.sin_family = AF_INET;
  addr.sin_port = htons(target_port);
  if (inet_pton(AF_INET, target_ip.c_str(), &addr.sin_addr) <= 0) {
    std::cerr << "[VideoSender] 无效的IP地址" << std::endl;
    close(sock);
    return;
  }

  // 2. 启动FFmpeg进程
  // 支持两种输入：
  //  - v4l2 设备：/dev/video0 等
  //  - lavfi 测试源：形如 "lavfi:testsrc=size=640x360:rate=30"
  // 强化编码参数：
  //  - aud=1 保证每帧前有 AUD（H.264: nal=9；HEVC: nal=35），便于分帧
  //  - repeat-headers=1 在每个IDR重复（H.264: SPS/PPS；HEVC:
  //  VPS/SPS/PPS），便于解码器入场
  //  - keyint=g_gop, scenecut=0 稳定GOP与周期性IDR，降低首帧等待时间
  auto build_ffmpeg_cmd = [&](const std::string& input_spec) -> std::string {
    bool is_lavfi = false;
    bool is_v4l2 = false;
    bool is_file = false;
    std::string lower = input_spec;
    std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);
    if (input_spec.rfind("lavfi:", 0) == 0) {
      is_lavfi = true;
    } else if (input_spec.rfind("/dev/video", 0) == 0) {
      is_v4l2 = true;
    } else if (lower.size() > 4 && (lower.substr(lower.size() - 4) == ".avi" ||
                                    lower.substr(lower.size() - 4) == ".mp4")) {
      is_file = true;
    }
    std::string cmd;
    const std::string color_args =
        " -colorspace bt709 -color_primaries bt709 -color_trc bt709 ";
    if (is_lavfi) {
      std::string lavfi_arg = input_spec.substr(6);  // 去除前缀 "lavfi:"
      if (g_use_hevc) {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-f lavfi -i " + lavfi_arg +
              " -vf scale=1280:720,fps=30 "
              " -g " +
              std::to_string(g_gop) +
              " -c:v libx265 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x265-params "
              " repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":aud=1:scenecut=0:bframes=0:rc-lookahead=0:ref=1:open-gop=0:idr-"
              "recovery-sei=1 "
              " -f hevc pipe:1";
      } else {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-f lavfi -i " + lavfi_arg +
              " -vf scale=1280:720,fps=30 "
              " -g " +
              std::to_string(g_gop) +
              " -c:v libx264 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x264-params "
              " aud=1:repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":scenecut=0:bframes=0:rc-lookahead=0:ref=1 "
              " -f h264 pipe:1";
      }
    } else if (is_v4l2) {
      if (g_use_hevc) {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-f v4l2 -thread_queue_size 512 -framerate 30 -i " + input_spec +
              " -vf scale=1280:720,fps=30 "
              " -g " +
              std::to_string(g_gop) +
              " -c:v libx265 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x265-params "
              " repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":aud=1:scenecut=0:bframes=0:rc-lookahead=0:ref=1:open-gop=0:idr-"
              "recovery-sei=1 "
              " -f hevc pipe:1";
      } else {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-f v4l2 -thread_queue_size 512 -framerate 30 -i " + input_spec +
              " -vf scale=1280:720,fps=30 "
              " -g " +
              std::to_string(g_gop) +
              " -c:v libx264 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x264-params "
              " aud=1:repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":scenecut=0:bframes=0:rc-lookahead=0:ref=1 "
              " -f h264 pipe:1";
      }
    } else if (is_file) {
      // 针对avi/mp4等文件输入，拼接推荐命令
      if (g_use_hevc) {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-stream_loop -1 -i " + input_spec +
              " -vf scale=1280:720,fps=30 "
              " -g " +
              std::to_string(g_gop) +
              " -c:v libx265 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x265-params "
              " repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":aud=1:scenecut=0:bframes=0:rc-lookahead=0:ref=1:open-gop=0:idr-"
              "recovery-sei=1 "
              " -f hevc pipe:1";
      } else {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-stream_loop -1 -i " + input_spec +
              " -vf scale=1280:720,fps=30 "
              " -g " +
              std::to_string(g_gop) +
              " -c:v libx264 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x264-params "
              " aud=1:repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":scenecut=0:bframes=0:rc-lookahead=0:ref=1 "
              " -f h264 pipe:1";
      }
    } else {
      // 兜底：按v4l2处理
      if (g_use_hevc) {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-f v4l2 -thread_queue_size 512 -framerate 30 -i " + input_spec +
              " -vf scale=1280:720,fps=30 "
              " -c:v libx265 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x265-params "
              " repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":aud=1:scenecut=0:bframes=0:rc-lookahead=0:ref=1 "
              " -f hevc pipe:1";
      } else {
        cmd = std::string("ffmpeg -nostdin -hide_banner -loglevel warning ") +
              "-f v4l2 -thread_queue_size 512 -framerate 30 -i " + input_spec +
              " -vf scale=1280:720,fps=30 "
              " -c:v libx264 -preset ultrafast -tune zerolatency "
              " -pix_fmt yuv420p" +
              color_args +
              " -x264-params "
              " aud=1:repeat-headers=1:keyint=" +
              std::to_string(g_gop) + ":min-keyint=" + std::to_string(g_gop) +
              ":scenecut=0:bframes=0:rc-lookahead=0:ref=1 "
              " -f h264 pipe:1";
      }
    }
    return cmd;
  };

  auto cmd = build_ffmpeg_cmd(device_path);

  FILE* pipe = popen(cmd.c_str(), "r");
  if (!pipe) {
    std::cerr << "[VideoSender] 无法启动FFmpeg (" << cmd << ")" << std::endl;
    // 尝试回退到 lavfi 测试源
    std::string fallback = "lavfi:testsrc=size=640x360:rate=30";
    auto cmd2 = build_ffmpeg_cmd(fallback);
    std::cerr << "[VideoSender] 回退到测试源: " << cmd2 << std::endl;
    pipe = popen(cmd2.c_str(), "r");
    if (!pipe) {
      std::cerr << "[VideoSender] 回退FFmpeg仍失败" << std::endl;
      close(sock);
      return;
    }
  }

  std::cout << "[VideoSender] FFmpeg启动成功" << std::endl;
  printf("[VideoSender] ✅ FFmpeg 进程就绪\n");

  // 缓冲区
  const int BUF_SIZE = 256 * 1024;  // 缓冲缩小至256KB以降低首帧延迟
  std::vector<uint8_t> read_buffer(BUF_SIZE);
  std::vector<uint8_t> frame_buffer;
  frame_buffer.reserve(1024 * 512);  // 预留512KB

  uint16_t frameId = 0;
  auto last_frame_time = std::chrono::steady_clock::now();
  const auto frame_interval = std::chrono::milliseconds(33);  // 约30fps节流

  // NAL Start Code: 00 00 00 01 或 00 00 01
  // H.264 AUD 类型为 9（AnnexB 输出已包含 AUD）
  std::vector<uint8_t> pending_data;  // 未处理的数据
  uint64_t total_frames_processed = 0;
  uint32_t last_report_frameId = 0;

  while (g_video_running) {
    size_t n = fread(read_buffer.data(), 1, BUF_SIZE, pipe);
    if (n <= 0) {
      std::cerr << "[VideoSender] FFmpeg输出中断，尝试一次性回退到测试源"
                << std::endl;
      // 一次性回退到 lavfi 测试源（若当前不是lavfi）
      // 关闭当前pipe，重启为lavfi
      pclose(pipe);
      std::string cmd2 = build_ffmpeg_cmd("lavfi:testsrc=size=640x360:rate=30");
      pipe = popen(cmd2.c_str(), "r");
      if (!pipe) {
        std::cerr << "[VideoSender] 回退FFmpeg失败，退出发送线程" << std::endl;
        break;
      } else {
        std::cout << "[VideoSender] 已切换到lavfi测试源" << std::endl;
        continue;  // 继续读取新pipe
      }
    }

    // 诊断日志：每次读取前20字节后打印（仅首次）
    if (frameId == 0 && total_frames_processed == 0) {
      printf(
          "[VideoSender] 🔍 首次读取: %zu 字节, 头4字节: %02X %02X %02X %02X\n",
          n, read_buffer[0], read_buffer[1], read_buffer[2], read_buffer[3]);
    }

    // 追加到pending_data
    size_t old_size = pending_data.size();
    pending_data.resize(old_size + n);
    std::memcpy(pending_data.data() + old_size, read_buffer.data(), n);

    // 搜索Start Codes
    // 我们寻找 AUD：
    //   - H.264: (00 00 00 01 09) 或 (00 00 01 09)
    //   - HEVC:  (00 00 00 01 23) 或 (00 00 01 23) 其中 23(dec)=35 为 AUD
    // 作为分隔符，兼容3/4字节起始码。FFmpeg通过 -x264-params aud=1
    // 确保每一帧开头都有 AUD，包括首帧。
    // 当我们遇到一个新的AUD，若frame_buffer不为空，则说明frame_buffer里是完整的一帧。

    size_t processed_len = 0;
    size_t search_start = 0;

    while (true) {
      // 确保有至少5个字节 (00 00 00 01 46)
      if (pending_data.size() - search_start < 5) {
        break;
      }

      // 暴力搜索 AUD 起始码
      // 优化：可以用KMP，但这里数据流不大且特征明显
      bool found_aud = false;
      size_t aud_pos = 0;
      size_t sc_len = 0;  // start code length

      for (size_t i = search_start; i + 4 < pending_data.size(); ++i) {
        // 4字节起始码
        if (pending_data[i] == 0x00 && pending_data[i + 1] == 0x00 &&
            pending_data[i + 2] == 0x00 && pending_data[i + 3] == 0x01) {
          uint8_t hdr = pending_data[i + 4];
          bool is_aud = false;
          if (!g_use_hevc) {
            uint8_t nal_type = hdr & 0x1F;  // H.264
            is_aud = (nal_type == 9);
          } else {
            uint8_t nal_type = (hdr >> 1) & 0x3F;  // HEVC
            is_aud = (nal_type == 35);
          }
          if (is_aud) {
            found_aud = true;
            aud_pos = i;
            sc_len = 4;
            break;
          }
        }
        // 3字节起始码
        if (pending_data[i] == 0x00 && pending_data[i + 1] == 0x00 &&
            pending_data[i + 2] == 0x01) {
          uint8_t hdr = pending_data[i + 3];
          bool is_aud = false;
          if (!g_use_hevc) {
            uint8_t nal_type = hdr & 0x1F;  // H.264
            is_aud = (nal_type == 9);
          } else {
            uint8_t nal_type = (hdr >> 1) & 0x3F;  // HEVC
            is_aud = (nal_type == 35);
          }
          if (is_aud) {
            found_aud = true;
            aud_pos = i;
            sc_len = 3;
            break;
          }
        }
      }

      if (found_aud) {
        if (aud_pos > processed_len) {
          // 这一段数据属于当前帧
          size_t len = aud_pos - processed_len;
          frame_buffer.insert(frame_buffer.end(),
                              pending_data.begin() + processed_len,
                              pending_data.begin() + aud_pos);
        }

        // 如果frame_buffer非空，说明收集好了一帧（因为遇到了新的AUD）
        if (!frame_buffer.empty()) {
          // 发送当前帧（诊断：检测是否包含参数集与IDR）
          bool has_param = false;
          bool has_idr = false;
          // 简单解析 AnnexB 起始码并检查 H.264 NAL 类型
          for (size_t i = 0; i + 4 < frame_buffer.size(); ++i) {
            size_t sc = 0;
            if (frame_buffer[i] == 0x00 && frame_buffer[i + 1] == 0x00 &&
                frame_buffer[i + 2] == 0x00 && frame_buffer[i + 3] == 0x01) {
              sc = 4;
            } else if (frame_buffer[i] == 0x00 && frame_buffer[i + 1] == 0x00 &&
                       frame_buffer[i + 2] == 0x01) {
              sc = 3;
            }
            if (sc) {
              uint8_t hdr = frame_buffer[i + sc];
              if (!g_use_hevc) {
                uint8_t nal_type = hdr & 0x1F;  // H.264
                if (nal_type == 7 || nal_type == 8)
                  has_param = true;                 // SPS/PPS
                if (nal_type == 5) has_idr = true;  // IDR
              } else {
                uint8_t nal_type = (hdr >> 1) & 0x3F;  // HEVC
                if (nal_type == 32 || nal_type == 33 || nal_type == 34)
                  has_param = true;  // VPS/SPS/PPS
                if (nal_type == 19 || nal_type == 20) has_idr = true;  // IDR
              }
            }
          }
          uint32_t totalBytes = frame_buffer.size();
          uint16_t fragId = 0;
          uint32_t offset = 0;
          uint32_t pktCount = 0;

          while (offset < totalBytes) {
            uint32_t remaining = totalBytes - offset;
            uint32_t chunkSize =
                (remaining > MAX_UDP_PAYLOAD) ? MAX_UDP_PAYLOAD : remaining;

            SendUdpPacket(sock, addr, frameId, fragId, totalBytes,
                          frame_buffer.data() + offset, chunkSize);

            offset += chunkSize;
            fragId++;
            pktCount++;
            // 简单的流控，防止发太快UDP丢包（恢复到更保守的节流，降低丢包率）
            std::this_thread::sleep_for(std::chrono::microseconds(80));
          }

          total_frames_processed++;
          // 每10帧打印诊断信息
          if (frameId % 10 == 0 && frameId != last_report_frameId) {
            printf(
                "[VideoSender] 📊 帧 #%u 已发送: %u 分片, 总 %u 字节 "
                "(Codec=%s, ParamSets=%s, IDR=%s)\n",
                frameId, pktCount, totalBytes, g_use_hevc ? "HEVC" : "H264",
                has_param ? "Y" : "N", has_idr ? "Y" : "N");
            last_report_frameId = frameId;
          }
          // 帧级节流：确保整体输出不超过 ~30fps，避免UDP洪水挤占解码
          auto now_frame = std::chrono::steady_clock::now();
          auto since_last = now_frame - last_frame_time;
          if (since_last < frame_interval) {
            std::this_thread::sleep_for(frame_interval - since_last);
          }
          last_frame_time = std::chrono::steady_clock::now();
          frameId++;
          frame_buffer.clear();
        }

        // 将AUD本身也加入新的frame_buffer (作为新帧的开始)
        // AUD长度通常很短 (00 00 00 01 46 ww zz - 6~7 bytes)
        // 这里我们只知道开始。我们将从aud_pos开始作为下一段处理
        processed_len = aud_pos;
        // Move past the SC prefix to avoid finding it again immediately?
        // No, logic is: "Found AUD start".
        // We handled everything BEFORE it.
        // Now we are AT the AUD.
        // Wait... if I consume AUD start here, I need to put it into the NEXT
        // frame. But my loop logic is: find AUD -> Flush PREV frame. So the
        // data starting at aud_pos belongs to the NEW frame.

        // 为了防止无限循环，我们需要推进 processed_len
        // 但我们不能跳过数据，因为这些数据要进下一个frame。
        // 解决：我们把 processed_len 更新到 aud_pos。
        // 并且 search_start 更新到 aud_pos + 1，继续找下一个AUD。

        // 但 wait，如果我不把 aud_pos 的数据加进去，下一次循环怎么加？
        // 必须在此处把 current pending data 里这一小部分（Start
        // Code）的处理逻辑理清。

        // 正确逻辑：
        // 数据流: [Frame N data] [AUD] [Frame N+1 data] ...
        // aud_pos 指向中间那个AUD。
        // frame_buffer.insert(..., processed_len, aud_pos) -> 把 Frame N data
        // 放入 buffer。 Send Frame N. Clear buffer. processed_len = aud_pos. ->
        // 指向 AUD。 search_start = aud_pos + 5. -> 从AUD之后找下一个。

        // 跳过起始码与首字节头，继续查找下一个AUD
        search_start = aud_pos + sc_len + 1;
      } else {
        // 没找到AUD
        // 剩下的所有数据 (pending_data from processed_len to end)
        // 都是当前帧的一部分 但可能包含半个AUD，所以只需检查到最后几个字节
        // 为了简单，我们只把确定的部分放入 frame_buffer，不确定的留着
        // 或者更简单：没找到AUD，就整个buffer（除去最后可能match了一半的4字节）append到frame_buffer?
        // 不建议，因为下次 search_start 需要重置。

        // 让我们简化逻辑：
        // 我们不需要每读一次fread就立刻处理，我们可以等待buffer足够大。
        // 或者：
        // 把 pending_data[processed_len:] 留做下一次的 pending_data。
        break;  // 跳出内层循环，读取更多数据
      }
    }

    // 回退策略：若长时间未发现 AUD，但累计数据已较大，则直接按块发送，避免停滞
    // 这对下游解码器是安全的（连续AnnexB字节流），只是“帧”边界可能不与AU对齐
    const size_t FALLBACK_FLUSH_THRESHOLD =
        128 * 1024;  // 降低到128KB尽量减少停滞
    if (processed_len == 0 && pending_data.size() >= FALLBACK_FLUSH_THRESHOLD) {
      // 将当前待处理数据整体作为一帧发送
      frame_buffer.insert(frame_buffer.end(), pending_data.begin(),
                          pending_data.end());

      if (!frame_buffer.empty()) {
        uint32_t totalBytes = frame_buffer.size();
        uint16_t fragId = 0;
        uint32_t offset = 0;
        uint32_t pktCount = 0;

        while (offset < totalBytes) {
          uint32_t remaining = totalBytes - offset;
          uint32_t chunkSize =
              (remaining > MAX_UDP_PAYLOAD) ? MAX_UDP_PAYLOAD : remaining;

          SendUdpPacket(sock, addr, frameId, fragId, totalBytes,
                        frame_buffer.data() + offset, chunkSize);

          offset += chunkSize;
          fragId++;
          pktCount++;
          std::this_thread::sleep_for(std::chrono::microseconds(80));
        }

        std::cout << "[VideoSender] Fallback flush Frame " << frameId
                  << " packets=" << pktCount << " totalBytes=" << totalBytes
                  << std::endl;
        frameId++;
        frame_buffer.clear();
      }

      pending_data.clear();
    }

    // 清理 pending_data 中已处理的部分
    if (processed_len > 0) {
      // 把 processed_len 之前的数据都已经处理（放入frame_buffer并发送了）
      // 但 wait，如果我在循环里找到了AUD，processed_len = aud_pos.
      // AUD 本身还没放入 frame_buffer。
      // 此时 pending_data[processed_len] 是 0x00 (AUD start).
      // 这些数据应该保留到下一次循环，作为新帧的开头。

      // 所以 erase 0 到 processed_len
      pending_data.erase(pending_data.begin(),
                         pending_data.begin() + processed_len);
    }
  }

  pclose(pipe);
  close(sock);
  printf("[VideoSender] ✔️  线程退出. 总计发送: %lu 帧, %lu 分片, %lu 字节\n",
         total_frames_processed, g_video_packets_sent, g_video_bytes_sent);
  std::cout << "[VideoSender] 线程退出" << std::endl;
}

void StartVideoSender(const std::string& target_ip, int target_port,
                      const std::string& device_path, const std::string& codec,
                      int gop) {
  if (g_video_running) return;
  g_video_running = true;
  g_use_hevc = !(codec == "h264");
  g_gop = (gop > 0 ? gop : 15);
  g_video_thread =
      std::thread(VideoSenderLoop, target_ip, target_port, device_path);
}

void StopVideoSender() {
  g_video_running = false;
  if (g_video_thread.joinable()) {
    g_video_thread.join();
  }
}
