# RoboMaster 自定义客户端开发流程框架

**环境**: Ubuntu 24.04 | VSCode | Unity
**技术栈**: Loxodon Framework (MVVM) | MQTT | Protobuf | UDP (图传)

---

## 1. 环境配置与依赖管理 (Infrastructure)

在 Ubuntu 下，优先使用命令行工具配置编译环境。

### A. 系统级工具
安装 Protobuf 编译器，用于将 `.proto` 文件编译为 C# 代码。
```bash
sudo apt update
sudo apt install -y protobuf-compiler
# 验证安装
protoc --version
```

### B. Unity 项目依赖
在 Unity 项目中引入以下核心库（推荐使用 NuGet for Unity 或手动下载 DLL）：

1.  **Loxodon Framework**: MVVM 架构基础。
2.  **Google.Protobuf**: 序列化支持 (需 `.dll`，通常为 `netstandard2.0` 版本)。
3.  **M2Mqtt** (或 MQTTnet): MQTT 通信客户端。

---

## 2. 项目目录结构规范 (Project Structure)

采用 **功能(Module)** + **分层(Layer)** 的方式组织。

```text
Assets/
├── Plugins/                # 外部 DLL (Google.Protobuf.dll, M2Mqtt.dll)
├── Proto/                  # 原始 .proto 文件 (源文件)
├── Scripts/
│   ├── Generated/          # [自动生成] protoc 生成的 C# 类
│   ├── Framework/          # 基础架构
│   │   ├── AppContext.cs   # Loxodon 入口、IOC 容器配置
│   │   ├── Network/        # 网络层
│   │   │   ├── MqttService.cs   # 管理 MQTT 连接 (192.168.12.1:3333)
│   │   │   ├── UdpService.cs    # 管理 UDP 图传监听 (端口 3334)
│   │   │   └── PacketParser.cs  # 消息与事件分发
│   ├── Modules/            # 业务模块 (MVVM)
│   │   ├── GameState/      # 比赛状态模块
│   │   │   ├── GameStateViewModel.cs
│   │   │   └── GameStateView.cs
│   │   ├── RobotControl/   # 控制模块
│   │   │   ├── ControlViewModel.cs
│   │   │   └── InputMonitor.cs
│   └── Utils/              # 工具类 (CRC 校验等)
├── Resources/              # UI 预制体 (Views)
└── Scenes/
```

---

## 3. 核心开发工作流 (Development Workflow)

### 第一步：协议定义 (Data Layer)
在 `Assets/Proto/` 编写 `robomaster.proto`。
使用脚本 `gen_proto.sh` 一键生成 C# 代码：

```bash
#!/bin/bash
# gen_proto.sh
protoc --csharp_out=./Assets/Scripts/Generated --proto_path=./Assets/Proto ./Assets/Proto/*.proto
echo "Code generated."
```

### 第二步：网络服务层 (Service Layer)
实现单例服务 `MqttService`：
1.  连接 Broker。
2.  订阅 Topic。
3.  接收 `byte[]` -> Protobuf 反序列化 -> 抛出 C# 事件。

### 第三步：ViewModel 设计 (Logic Layer)
ViewModel 只关心数据，不引用 UnityEngine.UI。

```csharp
public class GameStatusViewModel : ViewModelBase {
    private MqttService _mqttService;
    private int _redScore;
    
    // UI 绑定的属性
    public int RedScore {
        get => _redScore;
        set => Set(ref _redScore, value);
    }

    /* 在构造函数中订阅 Service 事件，收到数据后更新 RedScore */
}
```

### 第四步：UI 绑定 (View Layer)
1.  创建 View 脚本继承 `Window` 或 `UIView`。
2.  挂载 `VariableBinding` 组件。
3.  在代码中建立绑定关系：
    ```csharp
    bindingSet.Bind(this.scoreText).For(v => v.text).To(vm => vm.RedScore).OneWay();
    ```

---

## 4. 关键逻辑职责

| 模块            | 职责                                                                     |
| :-------------- | :----------------------------------------------------------------------- |
| **MqttService** | **数据源**：负责 TCP 连接、断线重连、原始数据收发。                      |
| **AppContext**  | **胶水层**：初始化各 Service，注入到 ViewModel 中。                      |
| **ViewModel**   | **状态机**：持有 UI 状态数据，处理业务逻辑（如点击事件转换为发送指令）。 |
| **View**        | **呈现层**：仅通过 Binding 响应数据变化，不写业务逻辑。                  |

## 5. 建议实施路线

1.  **Day 1**: 搭建 Unity + Loxodon 环境，编写并生成第一版 `.proto` 代码。
2.  **Day 2**: 跑通 MQTT 链路 (连接 -> 接收 -> 反序列化日志)。
3.  **Day 3**: 完成第一个 MVVM 模块 (如 GameStatus 计分板)。
4.  **Day 4**: 攻克 UDP 图传显示与反向控制逻辑。

---

## 6. 图传帧率过低原因（当前诊断）

**现象**：日志中 `slices` 很高，但 `assembled`/`decoded`/`applied` 长期只有个位数到十几帧；`Q` 多为 0，说明主线程并未积压帧。

**主要原因**：

1. **组帧阶段丢帧**：UDP 切片量大、单帧体积大，组帧超时较短时容易在尚未收齐就被清理，导致 `assembled` 偏低，从源头限制了 `decoded/applied`。
2. **解码吞吐不足**：每秒 `decoded` 数明显低于切片到达量，解码器输出帧数成为上限，`applied` 随之偏低。
3. **延迟来源**：组帧等待 + 解码输出 + 纹理上传叠加，形成 1~2s 的视觉延迟；`Q≈0` 说明延迟并非主线程排队造成。

**取舍方向**：若允许一定程度画面不完整，可通过缩短组帧等待、减少缓冲、降分辨率、缩小队列，优先保最新帧，换取更高流畅度与更低延迟。
