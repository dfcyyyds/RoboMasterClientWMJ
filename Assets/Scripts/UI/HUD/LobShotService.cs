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
    /// 英雄吊射模式服务 — 管理模式切换、Shift键切换、射击输入、视频流控制
    /// 仅在英雄兵种注册时创建，非英雄不实例化（注册期划分）
    /// 
    /// 手势：按一次左Shift或右Shift → 切换吊射模式（进入/退出）
    /// 命令：cmd_type=103(进入), 104(退出), 105(吊射射击)
    /// </summary>
    public class LobShotService : MonoBehaviour
    {
        // ─── 状态 ───
        private bool isActive;
        private bool isHero;

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

        // ─── 远端部署状态同步（由 DeployModeStatusSync 驱动） ───
        private volatile bool remoteDeployStatusDirty;
        private volatile uint remoteDeployStatus;

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

            Framework.Network.ProtobufManager.Instance.OnDataUpdated += OnProtoDataUpdated;
            wmj.Log.I("[LobShotService] 英雄吊射服务已初始化", wmj.Log.Tag.UI);
        }

        public void SetHUD(LobShotHUD hud)
        {
            lobShotHUD = hud;
        }

        void Update()
        {
            if (!isHero) return;

            ApplyRemoteDeployStatusIfNeeded();

            DetectShiftToggle();
            HandleFireInput();

            // Escape 键退出吊射模式
            if (isActive && Input.GetKeyDown(KeyCode.Escape))
                ExitDeployMode(sendCommand: !IsPassiveObserverMode());

            if (fireCooldown > 0) fireCooldown -= Time.deltaTime;
            if (recoilTimer > 0) recoilTimer -= Time.deltaTime;
        }

        // ═══════════════════════════════ Shift 切换检测 ═══════════════════════════════

        /// <summary>按一次左Shift或右Shift即切换吊射模式</summary>
        private void DetectShiftToggle()
        {
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                bool sendCmd = !IsPassiveObserverMode();
                if (isActive)
                    ExitDeployMode(sendCommand: sendCmd);
                else
                    EnterDeployMode(sendCommand: sendCmd);
            }
        }

        // ═══════════════════════════════ 模式切换 ═══════════════════════════════

        private void EnterDeployMode()
        {
            EnterDeployMode(sendCommand: true);
        }

        private void EnterDeployMode(bool sendCommand)
        {
            if (isActive) return;
            isActive = true;

            // 发送部署进入指令
            if (sendCommand)
            {
                if (GameParamsConfig.Get.isCompetitionMode)
                    SendDeployModeCommand(1); // 官方协议: HeroDeployModeEventCommand(mode=1)
                else
                    SendCommand(103, 0); // MockServer模式
            }

            // 停止图传解码
            var videoService = VideoStreamService.Instance;
            if (videoService != null)
                videoService.enabled = false;

            // 隐藏图传画面(防止残留帧透过半透明区域)
            HideVideoDisplay(true);

            // 显示吊射HUD
            if (lobShotHUD != null)
                lobShotHUD.Show();

            wmj.Log.I($"[LobShotService] 进入吊射模式{(sendCommand ? "" : " (仅观察)")}", wmj.Log.Tag.UI);
        }

        private void ExitDeployMode()
        {
            ExitDeployMode(sendCommand: true);
        }

        private void ExitDeployMode(bool sendCommand)
        {
            if (!isActive) return;
            isActive = false;

            // 发送部署退出指令
            if (sendCommand)
            {
                if (GameParamsConfig.Get.isCompetitionMode)
                    SendDeployModeCommand(0); // 官方协议: HeroDeployModeEventCommand(mode=0)
                else
                    SendCommand(104, 0); // MockServer模式
            }

            // 恢复图传画面
            HideVideoDisplay(false);

            // 恢复图传解码
            var videoService = VideoStreamService.Instance;
            if (videoService != null)
                videoService.enabled = true;

            // 隐藏吊射HUD
            if (lobShotHUD != null)
                lobShotHUD.Hide();

            wmj.Log.I($"[LobShotService] 退出吊射模式{(sendCommand ? "" : " (仅观察)")}", wmj.Log.Tag.UI);
        }

        private void OnProtoDataUpdated(string typeName, object data)
        {
            if (!isHero) return;
            if (typeName != "DeployModeStatusSync") return;

            var sync = data as DeployModeStatusSync;
            if (sync == null) return;

            remoteDeployStatus = sync.Status;
            remoteDeployStatusDirty = true;
        }

        /// <summary>
        /// 在主线程应用服务器下发的部署状态：
        /// status!=0 视为进入吊射，status=0 视为退出吊射。
        /// </summary>
        private void ApplyRemoteDeployStatusIfNeeded()
        {
            if (!remoteDeployStatusDirty) return;
            remoteDeployStatusDirty = false;

            bool shouldActive = remoteDeployStatus != 0;
            if (shouldActive == isActive) return;

            if (shouldActive)
                EnterDeployMode(sendCommand: false);
            else
                ExitDeployMode(sendCommand: false);
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
            if (IsPassiveObserverMode()) return;

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

        /// <summary>官方协议：通过 HeroDeployModeEventCommand 发送部署模式指令</summary>
        private void SendDeployModeCommand(uint mode)
        {
            if (NetworkManager.Instance == null) return;
            var deployCmd = new HeroDeployModeEventCommand { Mode = mode };
            byte[] payload = deployCmd.ToByteArray();
            NetworkManager.Instance.SendMqttMessage("HeroDeployModeEventCommand", payload);
        }

        private bool IsPassiveObserverMode()
        {
            return GameParamsConfig.Get.isCompetitionMode
                && GameParamsConfig.Get.competitionPassiveObserverMode;
        }

        void OnDestroy()
        {
            Framework.Network.ProtobufManager.Instance.OnDataUpdated -= OnProtoDataUpdated;
            if (isActive) ExitDeployMode();
        }
    }
}
