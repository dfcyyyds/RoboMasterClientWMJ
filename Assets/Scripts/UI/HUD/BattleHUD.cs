using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;
using UI.RobotSelection;
using UI.ViewModels;
using Framework.Network;

namespace UI.HUD
{
    /// <summary>
    /// 战斗 HUD 主控 — 管理所有 HUD 子元素的生命周期
    /// 在兵种选择完成后自动初始化
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

        // ─── 数据源 ───
        private RobotDynamicStatusViewModel dynamicVM;
        private RobotStaticStatusViewModel staticVM;
        private BuffViewModel buffVM;
        private GameStatusViewModel gameStatusVM;

        // ─── 状态 ───
        private uint lastHealth;
        private bool wasOutOfCombat;
        private bool isInitialized;

        // ─── BUFF 追踪 ───
        private uint lastBuffType;
        private uint lastBuffLeftTime;

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

            dynamicVM = new RobotDynamicStatusViewModel();
            dynamicVM.Initialize();
            staticVM = new RobotStaticStatusViewModel();
            staticVM.Initialize();
            buffVM = new BuffViewModel();
            buffVM.Initialize();

            BuildHUD();
            wmj.Log.I("[BattleHUD] HUD 已初始化", wmj.Log.Tag.UI);
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

            // 受击视效
            var vignetteGO = new GameObject("DamageVignette");
            vignetteGO.transform.SetParent(root, false);
            damageVignette = vignetteGO.AddComponent<DamageVignetteHUD>();

            // 准星环
            var crosshairGO = new GameObject("CrosshairRing");
            crosshairGO.transform.SetParent(root, false);
            crosshairRing = crosshairGO.AddComponent<CrosshairRingHUD>();

            // 通知
            var notifGO = new GameObject("Notifications");
            notifGO.transform.SetParent(root, false);
            notifications = notifGO.AddComponent<NotificationHUD>();

            // 开镜
            var zoomGO = new GameObject("AimZoom");
            zoomGO.transform.SetParent(root, false);
            aimZoom = zoomGO.AddComponent<AimZoomHUD>();

            // BUFF 状态栏
            var buffGO = new GameObject("BuffStatus");
            buffGO.transform.SetParent(root, false);
            buffStatus = buffGO.AddComponent<BuffStatusHUD>();
            buffStatus.SetNotificationHUD(notifications);
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
                uint maxAmmo = 500; // 可从 staticVM 获取，暂用默认
                crosshairRing.UpdateAmmo(dynamicVM.RemainingAmmo, maxAmmo);
                crosshairRing.UpdateHeat(heatPct);
            }

            // BUFF 数据更新
            if (buffVM != null && buffStatus != null)
            {
                uint bt = buffVM.BuffType;
                uint blt = buffVM.BuffLeftTime;
                if (bt != lastBuffType || blt != lastBuffLeftTime)
                {
                    buffStatus.UpdateBuff(bt, buffVM.BuffLevel,
                        buffVM.BuffMaxTime, blt, buffVM.MsgParams);
                    lastBuffType = bt;
                    lastBuffLeftTime = blt;
                }
            }

            lastHealth = curHealth;
            wasOutOfCombat = outOfCombat;
        }

        // ─── 公开接口 ───

        public void Shutdown()
        {
            isInitialized = false;
            dynamicVM?.Dispose(); staticVM?.Dispose(); buffVM?.Dispose();
            dynamicVM = null; staticVM = null; buffVM = null;
            if (hudCanvas != null) Destroy(hudCanvas.gameObject);
            hudCanvas = null; healthBar = null; damageVignette = null;
            crosshairRing = null; notifications = null; aimZoom = null;
            buffStatus = null;
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
    }
}
