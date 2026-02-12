using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Google.Protobuf;
using UI.Core;
using UI.RobotSelection;
using UI.ViewModels;
using Framework.Network;

namespace UI.HUD
{
    /// <summary>
    /// 战斗 HUD 主控 — 管理所有 HUD 子元素的生命周期
    /// 在兵种选择完成后自动初始化
    /// 支持：根据兵种能力隐藏射击相关UI、顶部对局信息显示
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class BattleHUD : MonoBehaviour
    {
        public static BattleHUD Instance { get; private set; }

        // ─── 子组件 ───
        private Canvas hudCanvas;
        private HealthBarHUD healthBar;
        private DamageVignetteHUD damageVignette;
        private CrosshairRingHUD crosshairRing;
        private NotificationHUD notifications;
        private AimZoomHUD aimZoom;
        private BuffStatusHUD buffStatus;
        private MatchInfoHUD matchInfo;
        private DeathOverlayHUD deathOverlay;

        // ─── 仿真服务 ───
        private SimulationInputService simInput;
        private AutoResupplyService autoResupply;
        private AmmoPurchaseInputService ammoPurchase;

        // ─── 弹药购买弹窗 ───
        private HexPopupHUD hexPopup;

        // ─── 吊射模式(仅英雄) ───
        private LobShotService lobShotService;
        private LobShotHUD lobShotHUD;
        private bool wasDeployMode;

        // ─── 数据源 ───
        private RobotDynamicStatusViewModel dynamicVM;
        private RobotStaticStatusViewModel staticVM;
        private BuffViewModel buffVM;
        private GameStatusViewModel gameStatusVM;

        // ─── 状态 ───
        private uint lastHealth;
        private bool wasOutOfCombat;
        private bool isInitialized;
        private bool canShoot = true; // 当前兵种是否具备射击能力

        // ─── BUFF 追踪 ───
        private uint lastBuffType;
        private uint lastBuffLeftTime;
        private int lastBuffLevel;
        private uint lastBuffMaxTime;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            RobotSelectionBootstrap.OnSelectionCompleted += OnSelectionCompleted;
            if (RobotSelectionBootstrap.IsSelectionCompleted)
                OnSelectionCompleted(RobotSelectionBootstrap.CurrentSelection);
        }

        void OnDestroy()
        {
            RobotSelectionBootstrap.OnSelectionCompleted -= OnSelectionCompleted;
            dynamicVM?.Dispose();
            staticVM?.Dispose();
            buffVM?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void OnSelectionCompleted(RobotSelectionResult result)
        {
            if (isInitialized) return;
            isInitialized = true;

            // 根据兵种能力决定是否具备射击能力
            canShoot = RobotCapabilities.CanShoot(result.Robot);

            dynamicVM = new RobotDynamicStatusViewModel();
            dynamicVM.Initialize();
            staticVM = new RobotStaticStatusViewModel();
            staticVM.Initialize();
            buffVM = new BuffViewModel();
            buffVM.Initialize();

            BuildHUD();

            // 创建仿真服务
            CreateSimulationServices(result.Robot);

            wmj.Log.I($"[BattleHUD] HUD 已初始化 | 兵种={result.Robot} | 射击UI={canShoot}",
                wmj.Log.Tag.UI);
        }

        private void BuildHUD()
        {
            hudCanvas = UIFactory.CreateCanvas("BattleHUDCanvas", 10000);
            hudCanvas.transform.SetParent(transform, false);
            var root = hudCanvas.transform;

            // 血条
            var healthGO = new GameObject("HealthBar");
            healthGO.transform.SetParent(root, false);
            healthBar = healthGO.AddComponent<HealthBarHUD>();
            ApplyPointLayout(healthGO, "HealthBar", 0.5f, 0.06f);

            // 受击视效（全屏效果，不需要布局定位）
            var vignetteGO = new GameObject("DamageVignette");
            vignetteGO.transform.SetParent(root, false);
            damageVignette = vignetteGO.AddComponent<DamageVignetteHUD>();

            // 准星环 — 仅在兵种具备射击能力时创建
            if (canShoot)
            {
                var crosshairGO = new GameObject("CrosshairRing");
                crosshairGO.transform.SetParent(root, false);
                crosshairRing = crosshairGO.AddComponent<CrosshairRingHUD>();
                ApplyPointLayout(crosshairGO, "CrosshairRing", 0.5f, 0.5f);

                // 开镜（仅有射击能力时需要）
                var zoomGO = new GameObject("AimZoom");
                zoomGO.transform.SetParent(root, false);
                aimZoom = zoomGO.AddComponent<AimZoomHUD>();
            }
            else
            {
                crosshairRing = null;
                aimZoom = null;
                wmj.Log.I("[BattleHUD] 非射击兵种，已隐藏准星/热量/弹药/开镜 UI", wmj.Log.Tag.UI);
            }

            // 通知
            var notifGO = new GameObject("Notifications");
            notifGO.transform.SetParent(root, false);
            notifications = notifGO.AddComponent<NotificationHUD>();
            ApplyStretchLayout(notifGO, "Notifications", 0.5f, 0.88f, 0.20f, 0.065f);

            // BUFF 状态栏
            var buffGO = new GameObject("BuffStatus");
            buffGO.transform.SetParent(root, false);
            buffStatus = buffGO.AddComponent<BuffStatusHUD>();
            ApplyBuffLayout(buffGO, "BuffStatus", 0.08f, 0.5f);
            buffStatus.SetNotificationHUD(notifications);

            // 对局信息 HUD（顶部显示）
            var matchGO = new GameObject("MatchInfo");
            matchGO.transform.SetParent(root, false);
            matchInfo = matchGO.AddComponent<MatchInfoHUD>();

            // 死亡覆盖层（灰屏 + 复活读条 + 买活按钮）
            var deathGO = new GameObject("DeathOverlay");
            deathGO.transform.SetParent(transform, false);
            deathOverlay = deathGO.AddComponent<DeathOverlayHUD>();
            deathOverlay.Initialize();

            // 弹药购买六边形弹窗
            var hexGO = new GameObject("HexPopup");
            hexGO.transform.SetParent(transform, false);
            hexPopup = hexGO.AddComponent<HexPopupHUD>();
            hexPopup.Initialize();

            // 吊射HUD（仅英雄，注册时划分）
            if (RobotSelectionBootstrap.CurrentSelection != null
                && RobotSelectionBootstrap.CurrentSelection.Robot == RobotType.Hero)
            {
                var lobGO = new GameObject("LobShotHUD");
                lobGO.transform.SetParent(transform, false);
                lobShotHUD = lobGO.AddComponent<LobShotHUD>();
                lobShotHUD.Initialize();
            }
        }

        /// <summary>
        /// 创建仿真服务（射击按键 + 自动补给）
        /// </summary>
        private void CreateSimulationServices(RobotType robotType)
        {
            // 编辑器仿真按键服务
            var simGO = new GameObject("SimulationInput");
            simGO.transform.SetParent(transform, false);
            simInput = simGO.AddComponent<SimulationInputService>();
            simInput.Initialize(robotType);

            // 自动补给服务
            var resupplyGO = new GameObject("AutoResupply");
            resupplyGO.transform.SetParent(transform, false);
            autoResupply = resupplyGO.AddComponent<AutoResupplyService>();
            autoResupply.Initialize(robotType);

            // 弹药快捷购买服务
            var ammoPurchaseGO = new GameObject("AmmoPurchase");
            ammoPurchaseGO.transform.SetParent(transform, false);
            ammoPurchase = ammoPurchaseGO.AddComponent<AmmoPurchaseInputService>();
            ammoPurchase.Initialize(robotType, hexPopup);

            // 吊射服务（仅英雄，注册时划分）
            if (robotType == RobotType.Hero)
            {
                var lobSvcGO = new GameObject("LobShotService");
                lobSvcGO.transform.SetParent(transform, false);
                lobShotService = lobSvcGO.AddComponent<LobShotService>();
                lobShotService.Initialize(robotType, crosshairRing);
                if (lobShotHUD != null)
                    lobShotService.SetHUD(lobShotHUD);
            }

            wmj.Log.I("[BattleHUD] 仿真服务已创建", wmj.Log.Tag.UI);
        }

        void Update()
        {
            if (!isInitialized || dynamicVM == null) return;

            uint curHealth = dynamicVM.CurrentHealth;
            uint maxHealth = staticVM?.MaxHealth ?? 600;
            float healthPct = maxHealth > 0 ? (float)curHealth / maxHealth : 1f;
            float heatPct = staticVM?.MaxHeat > 0
                ? dynamicVM.CurrentHeat / staticVM.MaxHeat : 0f;
            bool outOfCombat = dynamicVM.IsOutOfCombat;

            // 血条
            if (healthBar) healthBar.UpdateHealth(healthPct, curHealth, maxHealth);

            // 受击检测
            if (curHealth < lastHealth)
            {
                float dmgPct = (float)(lastHealth - curHealth) / Mathf.Max(maxHealth, 1);
                bool firstHit = wasOutOfCombat && !outOfCombat;
                if (damageVignette) damageVignette.OnDamage(dmgPct, healthPct, firstHit);
            }
            if (damageVignette) damageVignette.UpdateLowHealth(healthPct);

            // 准星：弹药 + 热量
            if (crosshairRing)
            {
                // 弹药环视觉上限：42mm(英雄)全队上限100, 17mm视觉参考500
                bool isHeroType = RobotSelectionBootstrap.CurrentSelection != null
                    && RobotSelectionBootstrap.CurrentSelection.Robot == RobotType.Hero;
                uint maxAmmo = isHeroType ? 100u : 500u;
                crosshairRing.UpdateAmmo(dynamicVM.RemainingAmmo, maxAmmo);
                crosshairRing.UpdateHeat(heatPct);
            }

            // BUFF 数据更新
            if (buffVM != null && buffStatus != null)
            {
                // 快照当前 ViewModel 值（可能从后台线程更新）
                uint bt = buffVM.BuffType;
                uint blt = buffVM.BuffLeftTime;
                int blv = buffVM.BuffLevel;
                uint bmt = buffVM.BuffMaxTime;

                // 任意字段变化时都触发更新
                if (bt != lastBuffType || blt != lastBuffLeftTime
                    || blv != lastBuffLevel || bmt != lastBuffMaxTime)
                {
                    if (bt != 0 && blt > 0 && bmt > 0)
                    {
                        buffStatus.UpdateBuff(bt, blv, bmt, blt);
                        wmj.Log.D($"[BattleHUD] BUFF更新: type={bt} lv={blv} max={bmt} left={blt}",
                            wmj.Log.Tag.UI);
                    }
                    else if (bt != 0 && (blt == 0 || bmt == 0))
                    {
                        // 可能是线程撕裂读取，跳过本帧但不更新 last 值，下帧重试
                        wmj.Log.D($"[BattleHUD] BUFF数据不完整(可能线程竞争): type={bt} left={blt} max={bmt}",
                            wmj.Log.Tag.UI);
                        // 不更新 lastXxx，让下一帧重新检测
                        goto skipBuffUpdate;
                    }
                    lastBuffType = bt;
                    lastBuffLeftTime = blt;
                    lastBuffLevel = blv;
                    lastBuffMaxTime = bmt;
                }
            skipBuffUpdate:;
            }

            // 开镜 — 检测射击行为，自动开镜
            if (canShoot && aimZoom != null)
            {
                aimZoom.NotifyShooting(dynamicVM.TotalProjectilesFired);
            }

            lastHealth = curHealth;
            wasOutOfCombat = outOfCombat;

            // ─── 吊射模式切换(英雄专属) ───
            if (lobShotService != null)
            {
                bool isDeployed = lobShotService.IsActive;
                if (isDeployed != wasDeployMode)
                {
                    wasDeployMode = isDeployed;
                    // 准星环：提升渲染层级 + 隐藏敌人面板（保留热量/弹药环）
                    if (crosshairRing != null)
                        crosshairRing.SetDeployMode(isDeployed);
                    // 屏蔽吊射无关的HUD元素（保留血条供战场感知）
                    if (buffStatus != null) buffStatus.gameObject.SetActive(!isDeployed);
                    if (notifications != null) notifications.gameObject.SetActive(!isDeployed);
                    if (matchInfo != null) matchInfo.gameObject.SetActive(!isDeployed);
                    if (aimZoom != null) aimZoom.gameObject.SetActive(!isDeployed);
                    if (damageVignette != null) damageVignette.gameObject.SetActive(!isDeployed);

                    wmj.Log.I($"[BattleHUD] 吊射模式切换: {isDeployed}", wmj.Log.Tag.UI);
                }

                // 射击反馈 → 准星环后坐力
                if (isDeployed && lobShotService.RecoilProgress > 0.95f)
                {
                    if (crosshairRing != null)
                        crosshairRing.TriggerRecoil();
                }
            }

#if UNITY_EDITOR
            // ─── 编辑器自动回血(HP=0时自动恢复) ───
            if (curHealth == 0 && maxHealth > 0)
            {
                // 通过CommonCommand发送回血指令(复用买活满血机制：cmd_type=102, param=1)
                var healCmd = new CommonCommand { CmdType = 102, Param = 1 };
                byte[] healPayload = healCmd.ToByteArray();
                if (NetworkManager.Instance != null)
                    NetworkManager.Instance.SendMqttMessage("CommonCommand", healPayload);
                wmj.Log.I("[BattleHUD] 编辑器自动买活(HP=0)", wmj.Log.Tag.UI);
            }
#endif
        }

        // ─── 公开接口 ───

        public void Shutdown()
        {
            isInitialized = false;
            dynamicVM?.Dispose(); staticVM?.Dispose(); buffVM?.Dispose();
            dynamicVM = null; staticVM = null; buffVM = null;
            if (hudCanvas != null) Destroy(hudCanvas.gameObject);
            if (simInput != null) Destroy(simInput.gameObject);
            if (autoResupply != null) Destroy(autoResupply.gameObject);
            if (ammoPurchase != null) Destroy(ammoPurchase.gameObject);
            if (deathOverlay != null) { deathOverlay.Shutdown(); Destroy(deathOverlay.gameObject); }
            if (hexPopup != null) { hexPopup.Shutdown(); Destroy(hexPopup.gameObject); }
            if (lobShotService != null) Destroy(lobShotService.gameObject);
            if (lobShotHUD != null) { lobShotHUD.Shutdown(); Destroy(lobShotHUD.gameObject); }
            hudCanvas = null; healthBar = null; damageVignette = null;
            crosshairRing = null; notifications = null; aimZoom = null;
            buffStatus = null; matchInfo = null; deathOverlay = null;
            simInput = null; autoResupply = null; ammoPurchase = null; hexPopup = null;
            lobShotService = null; lobShotHUD = null; wasDeployMode = false;
        }

        /// <summary>
        /// 销毁并重建 HUD，用于设置更改后的实时预览
        /// </summary>
        public void RebuildHUD()
        {
            if (!isInitialized) return;
            // 销毁旧画布
            if (hudCanvas != null) Destroy(hudCanvas.gameObject);
            if (simInput != null) Destroy(simInput.gameObject);
            if (autoResupply != null) Destroy(autoResupply.gameObject);
            if (ammoPurchase != null) Destroy(ammoPurchase.gameObject);
            if (deathOverlay != null) { deathOverlay.Shutdown(); Destroy(deathOverlay.gameObject); }
            if (hexPopup != null) { hexPopup.Shutdown(); Destroy(hexPopup.gameObject); }
            if (lobShotService != null) Destroy(lobShotService.gameObject);
            if (lobShotHUD != null) { lobShotHUD.Shutdown(); Destroy(lobShotHUD.gameObject); }
            hudCanvas = null; healthBar = null; damageVignette = null;
            crosshairRing = null; notifications = null; aimZoom = null;
            buffStatus = null; matchInfo = null; deathOverlay = null;
            simInput = null; autoResupply = null; ammoPurchase = null; hexPopup = null;
            lobShotService = null; lobShotHUD = null; wasDeployMode = false;
            // 不调用 Load()：内存中的 _data 已是最新（Save 已写入磁盘），
            // 重新 Load 会替换 _data 引用导致设置面板滑块 lambda 指向孤儿对象
            BuildHUD();
            // 重建仿真服务
            if (RobotSelectionBootstrap.CurrentSelection != null)
                CreateSimulationServices(RobotSelectionBootstrap.CurrentSelection.Robot);
            wmj.Log.I("[BattleHUD] HUD 已重建（设置预览）", wmj.Log.Tag.UI);
        }

        public void PushNotification(string text, Color color, float duration = -1)
        {
            if (notifications) notifications.Push(text, color, duration);
        }

        public void SetAiming(bool aiming)
        {
            if (aimZoom) aimZoom.SetAiming(aiming);
        }

        public void UpdateEnemyInfo(uint enemyHealth, uint enemyMaxHealth, int bulletsToKill)
        {
            if (crosshairRing) crosshairRing.UpdateEnemyInfo(enemyHealth, enemyMaxHealth, bulletsToKill);
        }

        // 提供给事件通知服务访问的 NotificationHUD 引用
        public NotificationHUD NotificationHUD => notifications;

        /// <summary>当前兵种是否具备射击能力</summary>
        public bool CanShoot => canShoot;

        // ─── 布局应用辅助方法 ───

        /// <summary>点锚点布局：元素中心对齐到布局位置</summary>
        private void ApplyPointLayout(GameObject go, string id, float defX, float defY)
        {
            var layout = UILayoutManager.GetElement(id, defX, defY);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(layout.anchorX, layout.anchorY);
            rt.anchorMax = new Vector2(layout.anchorX, layout.anchorY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            // sizeDelta 保留组件 Awake() 中设定的值
        }

        /// <summary>拉伸锚点布局：以布局中心 ± 半宽/半高展开</summary>
        private void ApplyStretchLayout(GameObject go, string id,
            float defX, float defY, float halfW, float halfH)
        {
            var layout = UILayoutManager.GetElement(id, defX, defY);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            float cx = Mathf.Clamp(layout.anchorX, halfW, 1f - halfW);
            float cy = Mathf.Clamp(layout.anchorY, halfH, 1f - halfH);
            rt.anchorMin = new Vector2(cx - halfW, cy - halfH);
            rt.anchorMax = new Vector2(cx + halfW, cy + halfH);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>BUFF 栏布局：水平位置可调，垂直范围保持拉伸</summary>
        private void ApplyBuffLayout(GameObject go, string id, float defX, float defY)
        {
            var layout = UILayoutManager.GetElement(id, defX, defY);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            // layout.anchorX 表示 BUFF 区域中心，计算左边缘锚点
            float totalW = UILayoutManager.Settings.buffColumnWidth * 2f + 12f;
            float halfNorm = totalW / 1920f / 2f;
            float leftAnchor = Mathf.Max(0f, layout.anchorX - halfNorm);
            rt.anchorMin = new Vector2(leftAnchor, rt.anchorMin.y);
            rt.anchorMax = new Vector2(leftAnchor, rt.anchorMax.y);
        }
    }
}
