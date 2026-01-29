# NVIDIA Video Codec SDK 安装指南

## 当前状态

✗ **系统中没有完整的 NVIDIA Video Codec SDK 头文件**

检测结果：
- ✓ 运行时库已安装：libnvidia-decode-590, libnvidia-encode-590
- ✗ 开发头文件缺失：nvcuvid.h, cuviddec.h 等
- ✓ CUDA 13.0 已安装
- ✓ 当前使用兼容 stub 可以编译但无法真正解码

## 方案一：从 NVIDIA 官网下载（推荐）

### 步骤

1. **访问 NVIDIA 开发者网站**
   
   打开浏览器访问：https://developer.nvidia.com/nvidia-video-codec-sdk
   
   或直接访问下载页面：https://developer.nvidia.com/nvidia-video-codec-sdk/download

2. **登录 NVIDIA 开发者账号**
   
   - 如果没有账号，需要先注册（免费）
   - 填写一些基本信息和使用目的

3. **下载对应版本**
   
   推荐版本：**Video Codec SDK 12.2** (最新稳定版)
   - 支持 CUDA 11.x - 13.x
   - 兼容 Ubuntu 20.04+
   - 文件名：`Video_Codec_SDK_12.2.72.zip` (约 30MB)

4. **解压并安装**

```bash
# 假设下载到 ~/Downloads/
cd ~/Downloads
unzip Video_Codec_SDK_12.2.72.zip

# 安装头文件到系统目录
cd Video_Codec_SDK_12.2.72
sudo cp -r Interface/* /usr/local/cuda/include/

# 或者安装到项目本地（推荐，避免污染系统）
cd Video_Codec_SDK_12.2.72
mkdir -p /home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin/include
cp -r Interface/* /home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin/include/
```

5. **更新 CMakeLists.txt**

如果使用项目本地安装：

```cmake
# 在 CMakeLists.txt 中添加
target_include_directories(NativeVideoPlugin PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)
```

6. **重新编译**

```bash
cd /home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin
rm -rf build
./build.sh
```

## 方案二：使用 apt 安装开发包（部分功能）

某些发行版提供了基础的开发包：

```bash
# 尝试安装
sudo apt search nvidia-cuda-toolkit | grep -i video
sudo apt install nvidia-cuda-dev  # 可能包含部分头文件

# 检查是否安装成功
find /usr -name "nvcuvid.h" 2>/dev/null
```

**注意**：这种方式可能只包含部分头文件，不如官方 SDK 完整。

## 方案三：从 GitHub 镜像获取（仅头文件）

有些开发者在 GitHub 上维护了 SDK 头文件的镜像：

```bash
cd /tmp
git clone --depth 1 https://github.com/FFmpeg/nv-codec-headers.git
cd nv-codec-headers

# 安装
make PREFIX=/usr/local
sudo make install PREFIX=/usr/local

# 或项目本地安装
make PREFIX=/home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin/include
make install PREFIX=/home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin/include
```

## 方案四：继续使用 stub（测试用）

如果只是想测试编译和基础功能，可以继续使用当前的 stub：

**优点**：
- ✓ 可以编译通过
- ✓ 插件可以加载
- ✓ API 接口正常工作

**缺点**：
- ✗ 无法真正解码视频
- ✗ 所有 CUVID 调用返回 `CUDA_ERROR_NOT_SUPPORTED`
- ✗ 只能测试 UDP 组帧等非解码功能

**适用场景**：
- 开发 UDP 组帧逻辑
- 测试 C# 和原生插件集成
- CI/CD 环境（没有 GPU）

## 验证安装

安装完成后，运行以下命令验证：

```bash
# 检查头文件
ls /usr/local/cuda/include/nvcuvid.h
# 或
ls /home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin/include/nvcuvid.h

# 检查版本信息
grep "CUDA_VERSION" /usr/local/cuda/include/nvcuvid.h

# 重新编译（应该没有 "Using nvcuvid stub" 警告）
cd /home/zby/RoboMasterClientWMJ/Assets/Plugins/NativeVideoPlugin
rm -rf build
./build.sh 2>&1 | grep -i "stub\|warning"
```

## 常见问题

### Q: 为什么不能直接用 wget/curl 下载？

A: NVIDIA 的下载需要：
1. 登录开发者账号
2. 接受许可协议
3. Cookie 认证

所以必须通过浏览器手动下载。

### Q: SDK 版本如何选择？

A: 
- **12.2.x**：最新稳定版，推荐（支持 RTX 40 系列）
- **12.1.x**：次新版本（支持 RTX 30 系列）
- **11.1.x**：旧版本（支持 GTX 10 系列）

查看你的 GPU 型号：`nvidia-smi` 然后选择对应版本。

### Q: 安装到系统目录还是项目目录？

A:
- **系统目录** (`/usr/local/cuda/include/`)：
  - 优点：所有项目共享，CMake 自动找到
  - 缺点：需要 sudo，可能影响其他项目
  
- **项目目录** (项目内 `include/`)：
  - 优点：隔离环境，不污染系统
  - 缺点：需要修改 CMakeLists.txt

推荐使用项目目录。

### Q: 下载的 ZIP 文件结构是什么？

A:
```
Video_Codec_SDK_12.2.72/
├── Interface/           # 头文件目录（这是你需要的）
│   ├── nvcuvid.h
│   ├── cuviddec.h
│   ├── nvEncodeAPI.h
│   └── ...
├── Samples/            # 示例代码
├── doc/                # 文档
└── Lib/                # Windows 库文件（Linux 不需要）
```

只需要复制 `Interface/` 目录中的头文件。

## 下一步

安装完成后：

1. 删除 `nvcuvid_stub.h`（不再需要）
2. 重新编译插件
3. 在 Unity 中测试真实解码
4. 查看 [TESTING.md](TESTING.md) 进行完整测试

## 参考链接

- [NVIDIA Video Codec SDK 主页](https://developer.nvidia.com/nvidia-video-codec-sdk)
- [SDK 文档](https://docs.nvidia.com/video-technologies/video-codec-sdk/)
- [支持的 GPU 列表](https://developer.nvidia.com/video-encode-and-decode-gpu-support-matrix-new)
- [FFmpeg nv-codec-headers](https://github.com/FFmpeg/nv-codec-headers)
