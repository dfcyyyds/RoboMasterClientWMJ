using UnityEngine;

namespace UI.RobotSelection
{
    /// <summary>
    /// 兵种选择启动器 - 在应用启动时自动显示选择界面
    /// 在 ConfigLoader 之前运行，将 RobotID 写入配置
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
                CompleteSelection(result);
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
            CompleteSelection(result);
        }

        private static void CompleteSelection(RobotSelectionResult result)
        {
            CurrentSelection = result;
            IsSelectionCompleted = true;

            // 将选择结果写入 ConfigLoader
            ApplyToConfig(result);

            wmj.DebugTools.Info($"[RobotSelection] 选择完成: {result}", wmj.DebugTools.LogCategory.UI);

            OnSelectionCompleted?.Invoke(result);
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
                wmj.DebugTools.Info($"[RobotSelection] 已将 RobotID={result.RobotId} 写入 ConfigLoader", wmj.DebugTools.LogCategory.UI);
            }
            else
            {
                wmj.DebugTools.Warn("[RobotSelection] ConfigLoader.config 为空，无法写入 RobotID", wmj.DebugTools.LogCategory.UI);
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
    }
}
