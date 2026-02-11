#ifndef MOCKSERVERCPP_PROTOCOL_H_
#define MOCKSERVERCPP_PROTOCOL_H_
#include <cstdint>
#include <string>
#include <vector>

// 这里假设你有RoboMasterClientMessage.pb.h
#include "RoboMasterClientMessage.pb.h"

// 初始化比赛仿真器（可指定当前客户端对应的机器人索引 0-4，fast_mode默认开启）
void init_simulator(int self_robot_index, bool fast_mode = true);

// 推进仿真 dt 秒
void tick_simulator(float dt);

// 生成指定类型的仿真协议消息
std::pair<std::string, std::vector<uint8_t>> build_simulated_message(
    const std::string& type);

// 根据topic和payload尝试反序列化为协议消息，并打印内容摘要
void print_message_from_topic_payload(const std::string& topic,
                                      const std::vector<uint8_t>& payload);

#endif  // MOCKSERVERCPP_PROTOCOL_H_
