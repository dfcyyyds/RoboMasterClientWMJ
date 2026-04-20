using UnityEngine;
using UI.RobotSelection;

namespace UI.HUD
{
    /// <summary>
    /// HUD 启动器 — 等待兵种选择完成后才创建 BattleHUD 和 SettingsPanel
    /// </summary>
    [DefaultExecutionOrder(-1500)]
    public class HUDBoot : MonoBehaviour
    {
        private static bool _created;

        void Awake()
        {
            if (_created) { Destroy(gameObject); return; }
            _created = true;
            DontDestroyOnLoad(gameObject);

            // 如果选择已完成，立即创建
            if (RobotSelectionBootstrap.IsSelectionCompleted)
            {
                CreateHUDComponents();
            }
            else
            {
                // 否则订阅事件，等待选择完成
                RobotSelectionBootstrap.OnSelectionCompleted += OnSelectionDone;
            }
        }

        void OnDestroy()
        {
            RobotSelectionBootstrap.OnSelectionCompleted -= OnSelectionDone;
            if (_created) _created = false;
        }

        private void OnSelectionDone(RobotSelectionResult result)
        {
            RobotSelectionBootstrap.OnSelectionCompleted -= OnSelectionDone;
            CreateHUDComponents();
        }

        private void CreateHUDComponents()
        {
            if (BattleHUD.Instance == null)
            {
                var hudGO = new GameObject("[BattleHUD]");
                hudGO.AddComponent<BattleHUD>();
                wmj.Log.I("[HUDBoot] 已创建 BattleHUD", wmj.Log.Tag.UI);
            }

            if (SettingsPanel.Instance == null)
            {
                var settingsGO = new GameObject("[SettingsPanel]");
                settingsGO.AddComponent<SettingsPanel>();
                wmj.Log.I("[HUDBoot] 已创建 SettingsPanel", wmj.Log.Tag.UI);
            }

            if (QuickReferencePanel.Instance == null)
            {
                var qrGO = new GameObject("[QuickReferencePanel]");
                qrGO.AddComponent<QuickReferencePanel>();
                wmj.Log.I("[HUDBoot] 已创建 QuickReferencePanel (按 H 切换)", wmj.Log.Tag.UI);
            }
        }
    }
}
