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
            ApplyPointLayout(healthGO, "HealthBar", 0.5f, 0.06f);

            // 受击视效（全屏效果，不需要布局定位）
            var vignetteGO = new GameObject("DamageVignette");
            vignetteGO.transform.SetParent(root, false);
            damageVignette = vignetteGO.AddComponent<DamageVignetteHUD>();

            // 准星环
            var crosshairGO = new GameObject("CrosshairRing");
            crosshairGO.transform.SetParent(root, false);
            crosshairRing = crosshairGO.AddComponent<CrosshairRingHUD>();
            ApplyPointLayout(crosshairGO, "CrosshairRing", 0.5f, 0.5f);

            // 通知
            var notifGO = new GameObject("Notifications");
            notifGO.transform.SetParent(root, false);
            notifications = notifGO.AddComponent<NotificationHUD>();
            ApplyStretchLayout(notifGO, "Notifications", 0.5f, 0.88f, 0.20f, 0.065f);

            // 开镜（无视觉布局）
            var zoomGO = new GameObject("AimZoom");
            zoomGO.transform.SetParent(root, false);
            aimZoom = zoomGO.AddComponent<AimZoomHUD>();

            // BUFF 状态栏
            var buffGO = new GameObject("BuffStatus");
            buffGO.transform.SetParent(root, false);
            buffStatus = buffGO.AddComponent<BuffStatusHUD>();
            ApplyBuffLayout(buffGO, "BuffStatus", 0.08f, 0.5f);
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
                // buff_type=0 表示当前无buff，跳过（不发给BuffStatusHUD）
                if (bt != 0 && (bt != lastBuffType || blt != lastBuffLeftTime))
                {
                    buffStatus.UpdateBuff(bt, buffVM.BuffLevel,
                        buffVM.BuffMaxTime, blt);
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

        /// <summary>
        /// 销毁并重建 HUD，用于设置更改后的实时预览
        /// </summary>
        public void RebuildHUD()
        {
            if (!isInitialized) return;
            // 销毁旧画布
            if (hudCanvas != null) Destroy(hudCanvas.gameObject);
            hudCanvas = null; healthBar = null; damageVignette = null;
            crosshairRing = null; notifications = null; aimZoom = null;
            buffStatus = null;
            // 不调用 Load()：内存中的 _data 已是最新（Save 已写入磁盘），
            // 重新 Load 会替换 _data 引用导致设置面板滑块 lambda 指向孤儿对象
            BuildHUD();
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
