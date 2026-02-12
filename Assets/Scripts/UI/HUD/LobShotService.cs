using UnityEngine;
using UnityEngine.UI;
using Google.Protobuf;
using Framework.Network;
using UI.Core;
using UI.RobotSelection;
using UI.ViewModels;

namespace UI.HUD
{
    /// <summary>
    /// 英雄吊射模式服务 — 管理模式切换、双Shift手势检测、射击输入、视频流控制
    /// 仅在英雄兵种注册时创建，非英雄不实例化（注册期划分）
    /// 
    /// 手势：同时按下左右Shift键两次 → 切换吊射模式
    /// 命令：cmd_type=103(进入), 104(退出), 105(吊射射击)
    /// </summary>
    public class LobShotService : MonoBehaviour
    {
        // ─── 状态 ───
        private bool isActive;
        private bool isHero;

        // ─── 双Shift手势检测 ───
        private int dualShiftCount;
        private float lastDualShiftTime;
        private bool wasBothShiftDown;
        private const float DUAL_SHIFT_WINDOW = 1.5f; // 两次双Shift间最大间隔
        private const int DUAL_SHIFT_REQUIRED = 2;     // 需要连续按两次

        // ─── 射击冷却(本地预判) ───
        private float fireCooldown;
        private const float FIRE_INTERVAL = 2.0f; // 与服务器 LOBSHOT_42MM_INTERVAL 一致

        // ─── 射击反馈 ───
        private float recoilTimer;
        private const float RECOIL_DURATION = 0.3f;

        // ─── 外部引用 ───
        private LobShotHUD lobShotHUD;
        private CrosshairRingHUD crosshairRing;
        private RawImage videoRawImage; // 缓存视频 RawImage 引用，用于进入吊射时隐藏

        // ─── 事件(供BattleHUD订阅) ───
        public bool IsActive => isActive;
        public float RecoilProgress => recoilTimer > 0 ? recoilTimer / RECOIL_DURATION : 0f;

        public void Initialize(RobotType robotType, CrosshairRingHUD ring)
        {
            isHero = (robotType == RobotType.Hero);
            crosshairRing = ring;
            if (!isHero)
            {
                enabled = false;
                return;
            }
            wmj.Log.I("[LobShotService] 英雄吊射服务已初始化", wmj.Log.Tag.UI);
        }

        public void SetHUD(LobShotHUD hud)
        {
            lobShotHUD = hud;
        }

        void Update()
        {
            if (!isHero) return;

            DetectDualShiftGesture();
            HandleFireInput();

            // Escape 键退出吊射模式
            if (isActive && Input.GetKeyDown(KeyCode.Escape))
                ExitDeployMode();

            if (fireCooldown > 0) fireCooldown -= Time.deltaTime;
            if (recoilTimer > 0) recoilTimer -= Time.deltaTime;
        }

        // ═══════════════════════════════ 手势检测 ═══════════════════════════════

        private void DetectDualShiftGesture()
        {
            bool bothDown = Input.GetKey(KeyCode.LeftShift) && Input.GetKey(KeyCode.RightShift);

            // 检测"按下"动作（上升沿）
            if (bothDown && !wasBothShiftDown)
            {
                float now = Time.realtimeSinceStartup;
                if (now - lastDualShiftTime > DUAL_SHIFT_WINDOW)
                    dualShiftCount = 0; // 超时重置

                dualShiftCount++;
                lastDualShiftTime = now;

                if (dualShiftCount >= DUAL_SHIFT_REQUIRED)
                {
                    dualShiftCount = 0;
                    ToggleDeployMode();
                }
            }
            wasBothShiftDown = bothDown;
        }

        private void ToggleDeployMode()
        {
            if (isActive)
                ExitDeployMode();
            else
                EnterDeployMode();
        }

        // ═══════════════════════════════ 模式切换 ═══════════════════════════════

        private void EnterDeployMode()
        {
            isActive = true;

            // 发送部署进入指令(cmd_type=103)
            SendCommand(103, 0);

            // 停止图传解码
            var videoService = VideoStreamService.Instance;
            if (videoService != null)
                videoService.enabled = false;

            // 隐藏图传画面(防止残留帧透过半透明区域)
            HideVideoDisplay(true);

            // 显示吊射HUD
            if (lobShotHUD != null)
                lobShotHUD.Show();

            wmj.Log.I("[LobShotService] 进入吊射模式", wmj.Log.Tag.UI);
        }

        private void ExitDeployMode()
        {
            isActive = false;

            // 发送部署退出指令(cmd_type=104)
            SendCommand(104, 0);

            // 恢复图传画面
            HideVideoDisplay(false);

            // 恢复图传解码
            var videoService = VideoStreamService.Instance;
            if (videoService != null)
                videoService.enabled = true;

            // 隐藏吊射HUD
            if (lobShotHUD != null)
                lobShotHUD.Hide();

            wmj.Log.I("[LobShotService] 退出吊射模式", wmj.Log.Tag.UI);
        }

        /// <summary>查找并隐藏/显示图传 RawImage</summary>
        private void HideVideoDisplay(bool hide)
        {
            // 延迟查找并缓存
            if (videoRawImage == null)
            {
                var vtv = Object.FindAnyObjectByType<VideoTextureView>();
                if (vtv != null) videoRawImage = vtv.TargetRawImage;
            }
            if (videoRawImage != null)
                videoRawImage.enabled = !hide;
        }

        // ═══════════════════════════════ 射击 ═══════════════════════════════

        private void HandleFireInput()
        {
            if (!isActive) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (fireCooldown <= 0)
                {
                    SendCommand(105, 0);
                    fireCooldown = FIRE_INTERVAL;
                    recoilTimer = RECOIL_DURATION;
                    wmj.Log.D("[LobShotService] 发射42mm", wmj.Log.Tag.UI);
                }
            }
        }

        // ═══════════════════════════════ 网络 ═══════════════════════════════

        private void SendCommand(uint cmdType, uint param)
        {
            if (NetworkManager.Instance == null) return;
            var cmd = new CommonCommand { CmdType = cmdType, Param = param };
            byte[] payload = cmd.ToByteArray();
            NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);
        }

        void OnDestroy()
        {
            if (isActive) ExitDeployMode();
        }
    }
}
