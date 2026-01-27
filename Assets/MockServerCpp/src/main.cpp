
// RoboMaster MockServerCpp
// 用于仿真测试的MQTT服务端，支持接收并打印自定义协议消息
//
// 命令行参数：
//   --host <ip>        MQTT服务器IP，默认127.0.0.1
//   --port <port>      MQTT服务器端口，默认3333
//   --topic <topic>    订阅的主题，默认robomaster/data
//   --interval <ms>    （保留参数，当前仅收消息）
//
// 示例：
//   ./mock_server --host 192.168.1.100 --port 1883 --topic test/data

#include <mqtt/async_client.h>

#include <chrono>
#include <cstring>
#include <fstream>
#include <iostream>
#include <thread>

#include "../log_monitor.h"
#include "protocol.h"
#include "server_to_client_types.h"
#include "video_sender.h"

// 主程序入口：连接MQTT服务器，订阅topic，接收并打印协议消息
int main(int argc, char* argv[]) {
  // 监控log/client.log
  LogMonitor client_log_monitor("./MockServerCpp/log/client.log");
  // 日志文件路径
  const std::string log_path = "./MockServerCpp/log/mockserver.log";
  // 日志文件最大10MB
  constexpr size_t MAX_LOG_SIZE = 10 * 1024 * 1024;
  // 日志输出流
  std::ofstream log_ofs;
  auto open_log = [&]() {
    // 检查文件大小，超限则清空
    std::ifstream fin(log_path, std::ios::binary | std::ios::ate);
    if (fin) {
      size_t sz = fin.tellg();
      fin.close();
      if (sz >= MAX_LOG_SIZE) {
        log_ofs.open(log_path, std::ios::trunc);
      } else {
        log_ofs.open(log_path, std::ios::app);
      }
    } else {
      log_ofs.open(log_path, std::ios::app);
    }
  };
  open_log();
  auto log = [&](const std::string& s) {
    if (!log_ofs.is_open()) open_log();
    log_ofs << s << std::endl;
    log_ofs.flush();
    // 检查是否超限，超限则清空重开
    if (log_ofs.tellp() >= static_cast<std::streampos>(MAX_LOG_SIZE)) {
      log_ofs.close();
      log_ofs.open(log_path, std::ios::trunc);
    }
  };
  // 默认连接官方裁判系统固定IP，方便端到端联调
  std::string host = "192.168.12.1";
  int port = 3333;
  std::string topic =
      "robomaster/data";  // 兼容旧逻辑：用于接收历史通道（保留）
  int interval_ms = 1000;
  // UDP 视频推流目标与设备
  std::string udp_ip = "127.0.0.1";
  int udp_port = 3334;
  std::string video_path = "single_20260125_1534.avi";
  std::string video_codec = "hevc";  // 默认HEVC，符合官方协议
  int video_gop = 15;                // 默认IDR周期(帧数)，缩短等待时间

  // 命令行参数解析
  // 打印参数说明
  if (argc > 1 &&
      (std::string(argv[1]) == "-h" || std::string(argv[1]) == "--help")) {
    std::cout << "RoboMaster MockServerCpp 用法：\n"
              << "  --host <ip>        MQTT服务器IP，默认127.0.0.1\n"
              << "  --port <port>      MQTT服务器端口，默认3333\n"
              << "  --topic <topic>    订阅的主题，默认robomaster/data\n"
              << "  --interval <ms>    （保留参数，当前仅收消息）\n"
              << "  --codec <hevc|h264>  视频编解码器，默认 hevc\n"
              << "  --gop <frames>       关键帧间隔（IDR周期），默认 15\n"
              << "  -h, --help         显示本帮助\n";
    return 0;
  }
  for (int i = 1; i < argc; ++i) {
    if (std::string(argv[i]) == "--host" && i + 1 < argc)
      host = argv[++i];
    else if (std::string(argv[i]) == "--port" && i + 1 < argc)
      port = std::stoi(argv[++i]);
    else if (std::string(argv[i]) == "--topic" && i + 1 < argc)
      topic = argv[++i];
    else if (std::string(argv[i]) == "--interval" && i + 1 < argc)
      interval_ms = std::stoi(argv[++i]);
    else if (std::string(argv[i]) == "--udp-ip" && i + 1 < argc)
      udp_ip = argv[++i];
    else if (std::string(argv[i]) == "--udp-port" && i + 1 < argc)
      udp_port = std::stoi(argv[++i]);
    else if (std::string(argv[i]) == "--video" && i + 1 < argc)
      video_path = argv[++i];
    else if (std::string(argv[i]) == "--codec" && i + 1 < argc)
      video_codec = argv[++i];
    else if (std::string(argv[i]) == "--gop" && i + 1 < argc)
      video_gop = std::stoi(argv[++i]);
  }
  std::string address = "tcp://" + host + ":" + std::to_string(port);
  mqtt::async_client client(address, "mock_server_cpp");
  mqtt::connect_options connOpts;

  // 设置MQTT消息接收回调，收到消息后自动解析并打印并写日志
  class callback : public virtual mqtt::callback {
   public:
    std::function<void(const std::string&)> log_func;
    void message_arrived(mqtt::const_message_ptr msg) override {
      std::vector<uint8_t> payload(msg->get_payload().begin(),
                                   msg->get_payload().end());
      print_message_from_topic_payload(msg->get_topic(), payload);
      if (log_func) {
        log_func("[Recv] topic=" + msg->get_topic() +
                 ", payloadLen=" + std::to_string(payload.size()));
      }
    }
  } cb;
  cb.log_func = log;
  client.set_callback(cb);

  try {
    client.connect(connOpts)->wait();
    std::cout << "[MockServerCpp] Connected to " << address << std::endl;
    log("[MockServerCpp] Connected to " + address);
    // 订阅客户端上行的协议化topic，严格符合官方示例（RemoteControl等）
    client.subscribe("RemoteControl", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to RemoteControl" << std::endl;
    log("[MockServerCpp] Subscribed to RemoteControl");
    client.subscribe("CustomRobotData", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to CustomRobotData" << std::endl;
    log("[MockServerCpp] Subscribed to CustomRobotData");
    client.subscribe("MapClickInfoNotify", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to MapClickInfoNotify"
              << std::endl;
    log("[MockServerCpp] Subscribed to MapClickInfoNotify");
    client.subscribe("AssemblyCommand", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to AssemblyCommand" << std::endl;
    log("[MockServerCpp] Subscribed to AssemblyCommand");
    client.subscribe("RobotPerformanceSelectionCommand", 1)->wait();
    std::cout
        << "[MockServerCpp] Subscribed to RobotPerformanceSelectionCommand"
        << std::endl;
    log("[MockServerCpp] Subscribed to RobotPerformanceSelectionCommand");
    client.subscribe("HeroDeployModeEventCommand", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to HeroDeployModeEventCommand"
              << std::endl;
    log("[MockServerCpp] Subscribed to HeroDeployModeEventCommand");
    client.subscribe("RuneActivateCommand", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to RuneActivateCommand"
              << std::endl;
    log("[MockServerCpp] Subscribed to RuneActivateCommand");
    client.subscribe("DartCommand", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to DartCommand" << std::endl;
    log("[MockServerCpp] Subscribed to DartCommand");
    client.subscribe("GuardCtrlCommand", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to GuardCtrlCommand" << std::endl;
    log("[MockServerCpp] Subscribed to GuardCtrlCommand");
    client.subscribe("AirSupportCommand", 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to AirSupportCommand" << std::endl;
    log("[MockServerCpp] Subscribed to AirSupportCommand");
    // 保持对旧通道的订阅，便于兼容
    client.subscribe(topic, 1)->wait();
    std::cout << "[MockServerCpp] Subscribed to " << topic << std::endl;
    log("[MockServerCpp] Subscribed to " + topic);
    std::cout
        << "[MockServerCpp] 等待客户端消息并主动推送随机数据... (Ctrl+C 退出)"
        << std::endl;
    log("[MockServerCpp] 等待客户端消息并主动推送随机数据... (Ctrl+C 退出)");

    // 启动视频流发送（默认本机127.0.0.1:3334，可通过参数覆盖）
    StartVideoSender(udp_ip, udp_port, video_path, video_codec, video_gop);

    // 新增：定时主动推送“服务器->自定义客户端”类型的随机协议数据到topic
    while (true) {
      client_log_monitor.CheckAndTruncate();
      for (const auto& type : server_to_client_types) {
        auto msg_pair = build_random_message(type);
        const std::string& msg_type = msg_pair.first;
        const std::vector<uint8_t>& payload = msg_pair.second;
        auto pubmsg = mqtt::make_message(
            msg_type, std::string(payload.begin(), payload.end()));
        pubmsg->set_qos(1);
        client.publish(pubmsg);
        std::string logstr =
            "[MockServerCpp] 主动推送: " + msg_type +
            ", payload size: " + std::to_string(payload.size());
        std::cout << logstr << std::endl;
        log(logstr);
      }
      std::this_thread::sleep_for(std::chrono::milliseconds(interval_ms));
    }
  } catch (const mqtt::exception& exc) {
    std::cerr << exc.what() << std::endl;
    return 1;
  }
  return 0;
}
