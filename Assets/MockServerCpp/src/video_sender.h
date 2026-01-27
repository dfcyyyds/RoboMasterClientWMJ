#ifndef MOCKSERVERCPP_VIDEO_SENDER_H_
#define MOCKSERVERCPP_VIDEO_SENDER_H_

#include <string>

// 启动视频发送线程
// port: 目标UDP端口 (默认3334)
// device: 摄像头设备路径 (默认/dev/video0 或 lavfi:testsrc=...)
// codec: "hevc" 或 "h264"，默认 "hevc"
// gop:   关键帧间隔（IDR周期），默认 15（约0.5秒@30fps）
void StartVideoSender(const std::string& target_ip, int target_port,
                      const std::string& device_path, const std::string& codec,
                      int gop);

// 停止视频发送
void StopVideoSender();

#endif  // MOCKSERVERCPP_VIDEO_SENDER_H_
