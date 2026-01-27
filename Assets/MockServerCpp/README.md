# RoboMaster 虚拟服务器（C++版）

本工程用于模拟RoboMaster自定义客户端协议，动态生成并发送测试数据，便于客户端联调。

## 主要特性
- 发布“服务器→自定义客户端”方向的协议Topic（如 `GameStatus`、`GlobalUnitStatus` 等）
- 动态生成protobuf消息并通过MQTT发布
- 可选UDP推送HEVC/H.264码流（AnnexB，前8字节小端：帧号/分片号/总字节数），默认HEVC，符合官方协议。
- 可配置MQTT地址、UDP推送目标与视频设备

## 目录结构
- src/           # 源码目录
- CMakeLists.txt # 构建脚本
- README.md      # 说明文档

## 依赖
- protobuf
- paho.mqtt.cpp
- paho.mqtt.c
- FFmpeg（用于视频推流，可选）
- CMake >= 3.10

## 编译与运行
1. 安装依赖（以Ubuntu为例）：
   ```bash
   sudo apt install libprotobuf-dev protobuf-compiler
   sudo apt install libpaho-mqttpp3-dev libpaho-mqtt3as-dev
   ```
2. 生成协议C++文件（如需从最新 `.proto` 生成）：
   ```bash
   protoc --cpp_out=src ../Assets/Proto/RoboMasterClientMessage.proto
   ```
3. 编译：
   ```bash
   mkdir build && cd build
   cmake ..
   make
   ```
4. 运行（默认连接裁判系统服务器IP `192.168.12.1:3333`，并向本机UDP `127.0.0.1:3334` 推视频；默认HEVC、GOP=15，支持快速入场）：
   ```bash
   ./mock_server --host 192.168.12.1 --port 3333 --udp-ip 127.0.0.1 --udp-port 3334 --video /dev/video0 --codec hevc --gop 15
   ```

### 一键启动/停止
- 启动：在项目根的 MockServerCpp 目录下执行 `./start_mock.sh`
- 停止：在同目录执行 `./stop_mock.sh`
- 说明：
   - 若本机安装了 `mosquitto`，脚本会在 `:3333` 启动本地broker；否则默认连接 `192.168.12.1:3333`。
   - 进程PID保存于 `run/pids.env`，日志位于 `run/*.log`。
   - 需要可执行权限：`chmod +x start_mock.sh stop_mock.sh`

### 可用参数
- `--host <ip>`：MQTT服务器IP（默认 `192.168.12.1`）
- `--port <port>`：MQTT端口（默认 `3333`）
- `--udp-ip <ip>`：UDP视频推送目标IP（默认 `127.0.0.1`）
- `--udp-port <port>`：UDP视频推送目标端口（默认 `3334`）
- `--video <path>`：视频设备路径（默认 `/dev/video0`）；若无设备，可改为其他管道来源
- `--codec <hevc|h264>`：选择视频编码，默认 `hevc`
- `--gop <frames>`：关键帧(IDR)间隔，默认 `15`（≈0.5秒@30fps），更小的值可更快入场
- `--interval <ms>`：主动推送协议数据的时间间隔（默认 `1000`ms）

### 注意
- 服务器→客户端Topic列表见 `src/server_to_client_types.h`；MockServer只会发布该列表中的Topic。
- UDP包前8字节遵循小端序，后续为AnnexB NALU数据，色域为 BT.709 SDR。
- 编码参数包含 `repeat-headers=1` 与 `aud=1`（H.264: nal=9；HEVC: nal=35），确保后启动客户端在下一次 IDR 抵达时即可入场。

如需适配协议字段，请补充 src/protocol.cpp/h。
