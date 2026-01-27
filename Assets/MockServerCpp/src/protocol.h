#ifndef MOCKSERVERCPP_PROTOCOL_H_
#define MOCKSERVERCPP_PROTOCOL_H_
#include <cstdint>
#include <string>
#include <vector>

// 这里假设你有RoboMasterClientMessage.pb.h
#include "RoboMasterClientMessage.pb.h"

// 随机生成一个协议消息（33种之一），返回序列化后的数据和消息类型名
std::pair<std::string, std::vector<uint8_t>> build_random_message();
// 按类型名生成指定类型的随机协议消息
std::pair<std::string, std::vector<uint8_t>> build_random_message(
    const std::string& type);

// 根据topic和payload尝试反序列化为协议消息，并打印内容摘要
void print_message_from_topic_payload(const std::string& topic,
                                      const std::vector<uint8_t>& payload);

#endif  // MOCKSERVERCPP_PROTOCOL_H_
