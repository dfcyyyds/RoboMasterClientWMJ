#ifndef MOCKSERVERCPP_PROTOCOL_H_
#define MOCKSERVERCPP_PROTOCOL_H_
#include <cstdint>
#include <string>
#include <vector>

// 这里假设你有RoboMasterClientMessage.pb.h
#include "RoboMasterClientMessage.pb.h"

// 初始化比赛仿真器（可指定当前客户端对应的机器人索引 0-4，fast_mode默认开启）
void init_simulator(int self_robot_index, bool fast_mode = true);

// 推进仿真 dt 秒（会先处理所有待处理的客户端指令）
void tick_simulator(float dt);

// 生成指定类型的仿真协议消息
std::pair<std::string, std::vector<uint8_t>> build_simulated_message(
    const std::string& type);

// 根据topic和payload尝试反序列化为协议消息，并打印内容摘要
void print_message_from_topic_payload(const std::string& topic,
                                      const std::vector<uint8_t>& payload);

// 处理客户端上行指令（线程安全，可从 MQTT 回调线程调用）
// 支持的 CommonCommand cmd_type:
//   100 = 射击指令（发射一发弹丸）
//   101 = 弹药购买指令（param = 购买批次数）
void handle_incoming_command(const std::string& topic,
                             const std::vector<uint8_t>& payload);

#endif  // MOCKSERVERCPP_PROTOCOL_H_
