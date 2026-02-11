using UnityEngine;

namespace UI.RobotSelection
{
    /// <summary>
    /// 兵种选择启动器 - 在应用启动时自动显示选择界面
    /// 在 ConfigLoader 之前运行，将 RobotID 写入配置
    /// 支持体系选择流程：兵种确认 → 体系选择 → HUD 初始化
    /// </summary>
    [DefaultExecutionOrder(-2000)] // 在 RuntimeTuner (-1000) 和 ConfigLoader 之前运行
    public class RobotSelectionBootstrap : MonoBehaviour
    {
        [Tooltip("是否在启动时自动显示选择界面")]
        [SerializeField] private bool autoShowOnStart = true;

        [Tooltip("跳过选择界面（调试用）")]
        [SerializeField] private bool skipSelection = false;

        [Tooltip("调试模式下的默认阵营")]
        [SerializeField] private TeamColor debugTeam = TeamColor.Red;

        [Tooltip("调试模式下的默认兵种")]
        [SerializeField] private RobotType debugRobot = RobotType.Infantry3;

        /// <summary>
        /// 当前选择结果（选择完成后可访问）
        /// </summary>
        public static RobotSelectionResult CurrentSelection { get; private set; }

        /// <summary>
        /// 当前性能体系选择结果（可能为 null 表示不支持体系选择）
        /// </summary>
        public static PerformanceSelectionPanel.PerformanceResult CurrentPerformance { get; private set; }

        /// <summary>
        /// 是否已完成选择
        /// </summary>
        public static bool IsSelectionCompleted { get; private set; }

        /// <summary>
        /// 选择完成事件（全局可订阅）
        /// </summary>
        public static event System.Action<RobotSelectionResult> OnSelectionCompleted;

        private void Start()
        {
            if (!autoShowOnStart)
            {
                return;
            }

            if (skipSelection)
            {
                // 调试模式：直接使用默认值
                var result = new RobotSelectionResult
                {
                    Team = debugTeam,
                    Robot = debugRobot
                };
                CompleteSelection(result, null);
                return;
            }

            // 显示选择界面
            ShowSelectionPanel();
        }

        /// <summary>
        /// 手动显示选择界面
        /// </summary>
        public void ShowSelectionPanel()
        {
            RobotSelectionPanel.Show(OnPanelComplete);
        }

        private void OnPanelComplete(RobotSelectionResult result)
        {
            // 检查该兵种是否需要体系选择
            if (RobotCapabilities.NeedsPerformanceSelection(result.Robot))
            {
                var profile = RobotCapabilities.GetProfile(result.Robot);
                PerformanceSelectionPanel.Show(profile, perfResult =>
                {
                    CompleteSelection(result, perfResult);

                    // 通过 MQTT 发送体系选择命令
                    SendPerformanceCommand(perfResult);
                });
            }
            else
            {
                CompleteSelection(result, null);
            }
        }

        /// <summary>通过 MQTT 发送 RobotPerformanceSelectionCommand</summary>
        private static void SendPerformanceCommand(PerformanceSelectionPanel.PerformanceResult perf)
        {
            if (perf == null) return;
            try
            {
                var cmd = new RobotPerformanceSelectionCommand
                {
                    Shooter = perf.Shooter,
                    Chassis = perf.Chassis,
                    SentryControl = perf.SentryControl
                };
                byte[] payload = Google.Protobuf.MessageExtensions.ToByteArray(cmd);
                if (NetworkManager.Instance != null)
                {
                    NetworkManager.Instance.SendMqttMessage("RobotPerformanceSelectionCommand", payload);
                    wmj.Log.I($"[RobotSelection] 已发送体系选择: Shooter={perf.Shooter}, " +
                        $"Chassis={perf.Chassis}, SentryCtrl={perf.SentryControl}", wmj.Log.Tag.Network);
                }
            }
            catch (System.Exception ex)
            {
                wmj.Log.W($"[RobotSelection] 发送体系选择失败: {ex.Message}", wmj.Log.Tag.Network);
            }
        }

        private static void CompleteSelection(RobotSelectionResult result,
            PerformanceSelectionPanel.PerformanceResult perf)
        {
            CurrentSelection = result;
            CurrentPerformance = perf;
            IsSelectionCompleted = true;

            // 将选择结果写入 ConfigLoader
            ApplyToConfig(result);

            // 确保 HUD 系统已创建
            EnsureHUD();

            wmj.Log.I($"[RobotSelection] 选择完成: {result}" +
                (perf != null ? $" | 体系: S={perf.Shooter} C={perf.Chassis}" : " | 无体系选择"),
                wmj.Log.Tag.UI);

            OnSelectionCompleted?.Invoke(result);
        }

        /// <summary>确保 HUD 启动器存在</summary>
        private static void EnsureHUD()
        {
            if (UnityEngine.Object.FindAnyObjectByType<UI.HUD.HUDBoot>() == null)
            {
                var go = new GameObject("[HUDBoot]");
                go.AddComponent<UI.HUD.HUDBoot>();
            }
        }

        /// <summary>
        /// 将选择结果应用到 ConfigLoader
        /// </summary>
        private static void ApplyToConfig(RobotSelectionResult result)
        {
            // 确保 ConfigLoader 已初始化
            if (!ConfigLoader.IsLoaded)
            {
                ConfigLoader.LoadConfig();
            }

            if (ConfigLoader.config != null)
            {
                ConfigLoader.config.RobotID = result.RobotId;
                wmj.Log.I($"[RobotSelection] 已将 RobotID={result.RobotId} 写入 ConfigLoader", wmj.Log.Tag.UI);
            }
            else
            {
                wmj.Log.W("[RobotSelection] ConfigLoader.config 为空，无法写入 RobotID", wmj.Log.Tag.UI);
            }
        }

        /// <summary>
        /// 重置选择状态（用于重新选择）
        /// </summary>
        public static void ResetSelection()
        {
            CurrentSelection = null;
            IsSelectionCompleted = false;
        }

        /// <summary>
        /// 从外部（如设置面板）应用新的选择结果。
        /// 等待选择完成后才调用此方法。
        /// </summary>
        public static void ApplySelection(RobotSelectionResult result)
        {
            CompleteSelection(result, null);
        }
    }
}
