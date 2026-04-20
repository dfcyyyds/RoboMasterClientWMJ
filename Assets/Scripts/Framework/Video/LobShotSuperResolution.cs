using System;
using UnityEngine;


namespace Framework.Video
{
    /// <summary>
    /// 吊射图传超分辨率推理模块
    ///
    /// 使用 Unity Sentis 加载 ONNX 模型，在 GPU 上执行 2× 超分辨率重建。
    /// 数据流: Texture2D(360×540) → Tensor → SR推理 → RenderTexture(720×1080)
    /// ❗ v3.2.1 起，客户端默认不使用 SR（1024×512 原生分辨率）。旧模型仅保留为兼容参考。
    /// 全 GPU 路径，无 CPU 回读。
    ///
    /// 模型: LobShot-SRGAN v3 (67K 参数, ~6ms @ RTX 5080 Laptop)
    ///   - 全局残差学习: 输出 = Bicubic(input, 2×) + 网络高频残差
    ///   - 32ch × 6 残差块, 深度可分离卷积, 无 BN
    /// </summary>
    public class LobShotSuperResolution : IDisposable
    {
        // ─── 推理引擎 ───
        private Unity.InferenceEngine.Model runtimeModel;
        private Unity.InferenceEngine.Worker worker;
        private bool isReady;
        private bool isDisposed;

        // ─── 输出纹理 ───
        private RenderTexture outputRT;

        // ─── 预分配 Tensor ───
        private Unity.InferenceEngine.Tensor<float> inputTensor;

        // ─── 尺寸 ───
        public int InputWidth { get; private set; }
        public int InputHeight { get; private set; }
        public int OutputWidth { get; private set; }
        public int OutputHeight { get; private set; }

        /// <summary>模型是否成功加载并可用</summary>
        public bool IsReady => isReady && !isDisposed;

        /// <summary>GPU 着色器 JIT 编译是否已通过异步逐层预热完成（ScheduleIterable）</summary>
        public bool IsWarmedUp => isWarmedUp;

        /// <summary>SR 输出 RenderTexture (720×1080)，可直接赋给 RawImage.texture</summary>
        public RenderTexture OutputTexture => outputRT;

        // ─── 性能统计 ───
        private int inferCount;
        private float totalInferMs;
        private float lastInferMs;
        private int inferFailCount;  // 推理失败计数（独立于成功计数，用于日志节流）

        // ─── 预热状态 ───
        private bool isWarmedUp;  // GPU Shader JIT 编译是否已通过 ScheduleIterable 完成
        private bool tcShadersWarmedUp; // TextureConverter 着色器是否已预热

        // ─── TextureConverter 预热用哑纹理 ───
        private Texture2D dummyInputTex; // 仅用于预热 TC shader，不用于实际推理

        // ─── 推理耗时过长自动熔断 ───
        private int consecutiveSlowInfers;      // 连续慢推理计数
        private const float SLOW_INFER_THRESHOLD_MS = 30f; // 超过 30ms 视为慢推理
        private const int SLOW_INFER_FUSE_COUNT = 3;       // 连续 3 次慢推理触发标记
        /// <summary>连续慢推理次数，外部可据此自动熔断 SR</summary>
        public int ConsecutiveSlowInfers => consecutiveSlowInfers;

        /// <summary>推理次数</summary>
        public int InferCount => inferCount;
        /// <summary>最近一次推理耗时 (ms)</summary>
        public float LastInferMs => lastInferMs;
        /// <summary>平均推理耗时 (ms)</summary>
        public float AvgInferMs => inferCount > 0 ? totalInferMs / inferCount : 0f;

        /// <summary>
        /// 构造并初始化超分辨率模块
        /// </summary>
        /// <param name="inputW">输入宽度 (默认 360)</param>
        /// <param name="inputH">输入高度 (默认 540)</param>
        /// <param name="modelResourcePath">模型在 Resources 文件夹下的路径 (不含扩展名)</param>
        public LobShotSuperResolution(int inputW = 360, int inputH = 540,
            string modelResourcePath = "Models/lobshot_sr")
        {
            InputWidth = inputW;
            InputHeight = inputH;
            OutputWidth = inputW * 2;
            OutputHeight = inputH * 2;

            try
            {
                LoadModel(modelResourcePath);
            }
            catch (Exception e)
            {
                isReady = false;
                wmj.Log.E($"[SR] 超分辨率模块初始化失败: {e.Message}\n{e.StackTrace}", wmj.Log.Tag.UI);
            }
        }

        private void LoadModel(string modelResourcePath)
        {
            // ─── 1. 通过 Resources.Load 加载 Unity 已导入的 ModelAsset ───
            // Unity InferenceEngine 的 ONNXModelImporter 会自动将 .onnx 文件
            // 转换为 ModelAsset (ScriptableObject)，存储在 Library 缓存中。
            // ModelLoader.Load(string path) 期望的是 Unity 私有二进制格式，
            // 不能直接加载原始 .onnx 文件，必须通过 ModelAsset 加载。
            var modelAsset = Resources.Load<Unity.InferenceEngine.ModelAsset>(modelResourcePath);
            if (modelAsset == null)
            {
                wmj.Log.E($"[SR] ModelAsset 加载失败: Resources/{modelResourcePath}\n" +
                    "请确保 .onnx 文件位于 Assets/Resources/Models/ 目录下", wmj.Log.Tag.UI);
                isReady = false;
                return;
            }

            // ─── 2. 将 ModelAsset 转换为运行时 Model ───
            runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
            wmj.Log.I($"[SR] ONNX 模型已加载: Resources/{modelResourcePath}", wmj.Log.Tag.UI);

            // ─── 3. 创建 GPU Worker ───
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);

            // ─── 4. 创建输出 RenderTexture ───
            outputRT = new RenderTexture(OutputWidth, OutputHeight, 0,
                RenderTextureFormat.ARGB32);
            outputRT.filterMode = FilterMode.Bilinear;
            outputRT.wrapMode = TextureWrapMode.Clamp;
            outputRT.Create();

            // ─── 5. 预分配输入 Tensor [1, 3, H, W] ───
            inputTensor = new Unity.InferenceEngine.Tensor<float>(
                new Unity.InferenceEngine.TensorShape(1, 3, InputHeight, InputWidth));

            isReady = true;
            isWarmedUp = false; // 尚未完成异步逐帧预热，等待 LobShotHUD 启动 SRWarmupCoroutine
            tcShadersWarmedUp = false;

            // 创建哑纹理（仅用于 TextureConverter 着色器预热，不用于实际推理）
            dummyInputTex = new Texture2D(InputWidth, InputHeight, TextureFormat.RGBA32, false);
            dummyInputTex.Apply(false, false); // 初始化为黑色，内容不重要
            wmj.Log.I($"[SR] 超分辨率模块就绪 " +
                $"({InputWidth}×{InputHeight} → {OutputWidth}×{OutputHeight}, " +
                $"GPU Compute)", wmj.Log.Tag.UI);

            // ⚠️ 注意：不在此处调用 RunGpuWarmup()！
            // 同步 Schedule() 会触发 Unity 渲染线程 GPU 同步点，导致主线程冻结 ~24 秒。
            // 预热由 LobShotHUD.SRWarmupCoroutine() 通过 ScheduleIterable() 逐帧完成，
            // 将 JIT 编译开销分摊到多帧，避免任何单帧卡顿。
        }

        /// <summary>
        /// 预热 TextureConverter 着色器（单帧消耗，建议在 SRWarmupCoroutine 结尾处调用）
        ///
        /// 原理： Infer() 中的 TextureConverter.ToTensor 和 TextureConverter.RenderToTexture
        ///   使用与 worker 模型推理完全独立的 GPU Blit Shader。
        ///   在 OpenGL/Linux 上首次调用时将触发 GPU Shader JIT 编译（与模型预热同等），
        ///   导致用户首次进入吊射模式时出现可感知的卡顿。
        ///   提前调用此方法可将该 JIT 开销转移到启动预热阶段，不影响实际操作。
        ///
        /// 注意：调用前必须将模型预热完成（worker 已通过 ScheduleIterable 预热）。
        /// </summary>
        public void WarmupTextureConverterShaders()
        {
            if (!IsReady || dummyInputTex == null || tcShadersWarmedUp) return;
            try
            {
                // 步骤 1: 预热 TextureConverter.ToTensor 着色器
                Unity.InferenceEngine.TextureConverter.ToTensor(dummyInputTex, inputTensor);

                // 步骤 2: 运行模型推理（worker shader 已由 ScheduleIterable 预热，此步应很快）
                worker.Schedule(inputTensor);

                // 步骤 3: 预热 TextureConverter.RenderToTexture 着色器
                var output = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                if (output != null)
                    Unity.InferenceEngine.TextureConverter.RenderToTexture(output, outputRT);

                tcShadersWarmedUp = true;
                wmj.Log.I("[SR] TextureConverter 着色器预热完成 (首次 Infer 不再触发 JIT)", wmj.Log.Tag.UI);
            }
            catch (Exception e)
            {
                wmj.Log.W($"[SR] TextureConverter 预热失败 (不影响正常推理): {e.Message}", wmj.Log.Tag.UI);
            }
        }

        /// <summary>
        /// 仅预热 TextureConverter 着色器，不调用 worker.Schedule()。
        ///
        /// 与 WarmupTextureConverterShaders() 的区别：
        ///   旧方法内部调用 worker.Schedule(inputTensor) 触发同步 GPU 推理（~24s 冻结的元凶）。
        ///   此方法利用 ScheduleIterable 已完成全部层迭代的事实，直接用 PeekOutput()
        ///   获取已有输出，仅预热 ToTensor 和 RenderToTexture 的 GPU Blit Shader。
        ///
        /// 调用前提：GetWarmupEnumerator() 已完整迭代完毕（worker 内部已有推理结果）。
        /// </summary>
        public void WarmupTextureConverterOnly()
        {
            if (!IsReady || dummyInputTex == null || tcShadersWarmedUp) return;
            try
            {
                // 步骤 1: 预热 TextureConverter.ToTensor 着色器
                Unity.InferenceEngine.TextureConverter.ToTensor(dummyInputTex, inputTensor);

                // 步骤 2: 直接 PeekOutput（ScheduleIterable 已完成全部层，无需再 Schedule）
                var output = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;

                // 步骤 3: 预热 TextureConverter.RenderToTexture 着色器
                if (output != null)
                    Unity.InferenceEngine.TextureConverter.RenderToTexture(output, outputRT);

                tcShadersWarmedUp = true;
                wmj.Log.I("[SR] TextureConverter 着色器预热完成（无同步 Schedule）", wmj.Log.Tag.UI);
            }
            catch (Exception e)
            {
                wmj.Log.W($"[SR] TextureConverter 预热失败 (不影响正常推理): {e.Message}", wmj.Log.Tag.UI);
            }
        }

        /// <summary>
        /// 返回逐层异步预热迭代器（供 MonoBehaviour.StartCoroutine 使用）。
        ///
        /// 原理：
        ///   worker.ScheduleIterable() 将整个模型推理拆分成逐层迭代器，
        ///   每次 MoveNext() 仅调度一层，由调用方在每帧 yield return null 以分摊开销。
        ///   这样 GPU Compute Shader JIT 编译成本被分摊到多帧而非一帧阻塞 ~24 秒。
        ///
        /// 用法（在 MonoBehaviour 协程中）：
        ///   var iter = srModule.GetWarmupEnumerator();
        ///   while (iter.MoveNext()) yield return null;
        ///   srModule.MarkWarmedUp();
        /// </summary>
        public System.Collections.IEnumerator GetWarmupEnumerator()
        {
            // inputTensor 已预分配，数据未初始化（仅用于触发 GPU Shader JIT 编译，不使用输出）
            return worker.ScheduleIterable(inputTensor);
        }

        /// <summary>标记 GPU 预热完成（由 LobShotHUD 协程在迭代结束后调用）</summary>
        public void MarkWarmedUp()
        {
            isWarmedUp = true;
        }

        /// <summary>
        /// 执行超分辨率推理（全 GPU 路径）
        ///
        /// 流程:
        ///   1. TextureConverter.ToTensor: Texture2D → Tensor[1,3,H,W] (GPU)
        ///   2. worker.Schedule: SR 推理 (GPU)
        ///   3. TextureConverter.RenderToTexture: Tensor → RenderTexture (GPU)
        ///
        /// 调用后 OutputTexture 即为 SR 结果，可直接赋给 RawImage.texture
        /// </summary>
        /// <param name="inputTex">解码后的低分辨率纹理 (360×540 RGBA32)</param>
        /// <returns>是否推理成功</returns>
        public bool Infer(Texture2D inputTex)
        {
            if (!IsReady || inputTex == null) return false;

            float t0 = Time.realtimeSinceStartup;

            try
            {
                // 1. Texture2D → Tensor (GPU)
                //    TextureConverter 自动处理:
                //    - RGBA32 → RGB (3通道)
                //    - [0,255] → [0,1] 归一化
                //    - Unity Y轴(底部=0) → 标准 Y轴(顶部=0)
                Unity.InferenceEngine.TextureConverter.ToTensor(inputTex, inputTensor);

                // 2. GPU 推理
                worker.Schedule(inputTensor);

                // 3. 输出 Tensor → RenderTexture (GPU, 无 CPU 回读)
                //    TextureConverter 自动处理 Y 轴翻转和色值 clamp
                var outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                Unity.InferenceEngine.TextureConverter.RenderToTexture(outputTensor, outputRT);

                // 统计
                float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
                lastInferMs = elapsed;
                totalInferMs += elapsed;
                inferCount++;

                // 慢推理检测：连续多次超时则标记，供外部熔断
                if (elapsed > SLOW_INFER_THRESHOLD_MS)
                {
                    consecutiveSlowInfers++;
                    if (consecutiveSlowInfers == SLOW_INFER_FUSE_COUNT)
                        wmj.Log.W($"[SR] ⚠️ 连续 {consecutiveSlowInfers} 次推理超时(最近 {elapsed:F1}ms > {SLOW_INFER_THRESHOLD_MS}ms)，" +
                            "建议外部熔断 SR", wmj.Log.Tag.UI);
                }
                else
                {
                    consecutiveSlowInfers = 0; // 正常推理重置计数
                }

                return true;
            }
            catch (Exception e)
            {
                inferFailCount++;
                // 前 3 次失败详细报告，之后每 100 次报告一次，避免日志刷屏
                if (inferFailCount <= 3 || inferFailCount % 100 == 0)
                    wmj.Log.E($"[SR] 推理失败 (第{inferFailCount}次): {e.Message}", wmj.Log.Tag.UI);
                return false;
            }
        }

        /// <summary>获取诊断信息</summary>
        public string GetDiagnostics()
        {
            if (!IsReady)
                return $"SR=OFF(未就绪, 失败{inferFailCount}次)";
            return $"SR=ON 推理={inferCount}次 失败={inferFailCount}次 " +
                $"最近={lastInferMs:F1}ms 平均={AvgInferMs:F1}ms";
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            isReady = false;

            worker?.Dispose();
            worker = null;

            inputTensor?.Dispose();
            inputTensor = null;

            if (dummyInputTex != null)
            {
                UnityEngine.Object.Destroy(dummyInputTex);
                dummyInputTex = null;
            }

            if (outputRT != null)
            {
                outputRT.Release();
                UnityEngine.Object.Destroy(outputRT);
                outputRT = null;
            }

            runtimeModel = null;
            wmj.Log.I("[SR] 超分辨率模块已释放", wmj.Log.Tag.UI);
        }
    }
}
