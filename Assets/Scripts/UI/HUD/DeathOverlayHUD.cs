using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Google.Protobuf;
using UI.Core;
using UI.ViewModels;

namespace UI.HUD
{
    /// <summary>
    /// 死亡灰屏覆盖层 — 显示半透明灰色遮罩 + 复活读条 + 金币买活按钮
    /// 
    /// 状态机：
    ///   Alive  → 正常，覆盖层隐藏
    ///   Dead   → 灰屏渐现，显示复活进度条和买活按钮
    ///   Reviving → 复活完成动画（白闪 → 隐藏）
    /// 
    /// 数据来源：
    ///   - RobotStaticStatusViewModel.AliveState (0=死亡, 1=存活)
    ///   - RobotRespawnStatusViewModel (复活进度、金币花费)
    ///   - GlobalLogisticsStatusViewModel.RemainingEconomy (当前金币)
    /// </summary>
    public class DeathOverlayHUD : MonoBehaviour
    {
        private enum State { Alive, Dead, Reviving }
        private State state = State.Alive;

        // ─── UI 元素 ───
        private Canvas overlayCanvas;
        private CanvasGroup canvasGroup;
        private Image grayOverlay;
        private Image reviveFlash;

        // 中央信息区
        private RectTransform infoPanel;
        private TextMeshProUGUI deathTitle;
        private TextMeshProUGUI timerText;
        private Image progressBg;
        private Image progressFill;
        private TextMeshProUGUI progressText;

        // 买活按钮区
        private RectTransform buybackPanel;
        private Button buybackButton;
        private TextMeshProUGUI buybackLabel;
        private TextMeshProUGUI goldInfoText;

        // ─── 数据源 ───
        private RobotRespawnStatusViewModel respawnVM;
        private GlobalLogisticsStatusViewModel logisticsVM;

        // ─── 动画 ───
        private float fadeInTimer;
        private float reviveFlashTimer;
        private const float FADE_IN_DURATION = 0.6f;
        private const float REVIVE_FLASH_DURATION = 0.5f;

        // ─── 状态 ───
        private bool buybackRequested;
        private float buybackCooldown;

        // ─── 颜色 ───
        private static readonly Color DeathGray = new Color(0.08f, 0.08f, 0.10f, 0.72f);
        private static readonly Color ProgressBgColor = new Color(0.12f, 0.12f, 0.16f, 0.85f);
        private static readonly Color ProgressFillColor = new Color(0.30f, 0.75f, 0.95f, 0.90f);
        private static readonly Color BuybackGold = new Color(0.95f, 0.80f, 0.20f, 1f);
        private static readonly Color BuybackDisabled = new Color(0.45f, 0.45f, 0.50f, 1f);

        public void Initialize()
        {
            respawnVM = new RobotRespawnStatusViewModel();
            respawnVM.Initialize();
            logisticsVM = new GlobalLogisticsStatusViewModel();
            logisticsVM.Initialize();

            BuildUI();
            SetVisible(false);
        }

        void OnDestroy()
        {
            respawnVM?.Dispose();
            logisticsVM?.Dispose();
        }

        private void BuildUI()
        {
            // 独立 Canvas — sortingOrder 极高，覆盖所有 HUD
            overlayCanvas = UIFactory.CreateCanvas("DeathOverlayCanvas", 20000);
            overlayCanvas.transform.SetParent(transform, false);
            canvasGroup = UIFactory.EnsureCanvasGroup(overlayCanvas.gameObject);
            canvasGroup.alpha = 0f;

            var root = overlayCanvas.transform;

            // ── 全屏半透明灰色遮罩 ──
            grayOverlay = UIFactory.CreateFullScreenImage(root, "GrayOverlay", DeathGray);
            grayOverlay.raycastTarget = true; // 阻断后层交互

            // ── 复活白闪 ──
            reviveFlash = UIFactory.CreateFullScreenImage(root, "ReviveFlash", Color.clear);
            reviveFlash.raycastTarget = false;
            reviveFlash.gameObject.SetActive(false);

            // ── 中央信息面板 ──
            var panelGO = new GameObject("InfoPanel");
            panelGO.transform.SetParent(root, false);
            infoPanel = panelGO.AddComponent<RectTransform>();
            infoPanel.anchorMin = new Vector2(0.30f, 0.30f);
            infoPanel.anchorMax = new Vector2(0.70f, 0.65f);
            infoPanel.offsetMin = Vector2.zero;
            infoPanel.offsetMax = Vector2.zero;

            // 面板背景
            var panelBg = panelGO.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.04f, 0.08f, 0.75f);
            UIFactory.ApplyRoundedCorners(panelBg, 64, 16);
            panelBg.raycastTarget = false;

            // 面板边框
            var borderImg = UIFactory.CreateImage(infoPanel, "Border",
                new Color(0.30f, 0.55f, 0.85f, 0.35f));
            UIFactory.SetFullStretch(borderImg.rectTransform);
            UIFactory.ApplyRoundedCorners(borderImg, 64, 16);

            // ── "阵亡" 标题 ──
            deathTitle = UIFactory.CreateText(infoPanel, "Title", "阵  亡", 52,
                TextAlignmentOptions.Center, new Color(0.95f, 0.25f, 0.20f, 1f),
                FontStyles.Bold);
            var titleRt = deathTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0.05f, 0.70f);
            titleRt.anchorMax = new Vector2(0.95f, 0.95f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            // ── 倒计时文字 ──
            timerText = UIFactory.CreateText(infoPanel, "Timer", "等待复活中...", 28,
                TextAlignmentOptions.Center, UIColors.Silver);
            var timerRt = timerText.rectTransform;
            timerRt.anchorMin = new Vector2(0.10f, 0.52f);
            timerRt.anchorMax = new Vector2(0.90f, 0.70f);
            timerRt.offsetMin = Vector2.zero;
            timerRt.offsetMax = Vector2.zero;

            // ── 进度条背景 ──
            progressBg = UIFactory.CreateImage(infoPanel, "ProgressBg", ProgressBgColor);
            var pbgRt = progressBg.rectTransform;
            pbgRt.anchorMin = new Vector2(0.08f, 0.38f);
            pbgRt.anchorMax = new Vector2(0.92f, 0.50f);
            pbgRt.offsetMin = Vector2.zero;
            pbgRt.offsetMax = Vector2.zero;
            UIFactory.ApplyRoundedCorners(progressBg, 32, 6);

            // ── 进度条填充 ──
            progressFill = UIFactory.CreateImage(progressBg.transform, "ProgressFill", ProgressFillColor);
            var pfRt = progressFill.rectTransform;
            pfRt.anchorMin = Vector2.zero;
            pfRt.anchorMax = new Vector2(0f, 1f); // 从左到右填充
            pfRt.offsetMin = Vector2.zero;
            pfRt.offsetMax = Vector2.zero;
            UIFactory.ApplyRoundedCorners(progressFill, 32, 6);

            // ── 进度百分比文字 ──
            progressText = UIFactory.CreateText(progressBg.transform, "ProgressPct", "0%", 22,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(progressText.rectTransform);

            // ── 买活按钮区 ──
            var buyGO = new GameObject("BuybackPanel");
            buyGO.transform.SetParent(infoPanel, false);
            buybackPanel = buyGO.AddComponent<RectTransform>();
            buybackPanel.anchorMin = new Vector2(0.15f, 0.05f);
            buybackPanel.anchorMax = new Vector2(0.85f, 0.32f);
            buybackPanel.offsetMin = Vector2.zero;
            buybackPanel.offsetMax = Vector2.zero;

            // 买活按钮
            buybackButton = UIFactory.CreateRoundedButton(buybackPanel, "BuybackBtn",
                "", BuybackGold, 26, new Color(0.10f, 0.08f, 0.02f, 1f));
            var btnRt = buybackButton.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.10f, 0.35f);
            btnRt.anchorMax = new Vector2(0.90f, 0.95f);
            btnRt.offsetMin = Vector2.zero;
            btnRt.offsetMax = Vector2.zero;
            buybackButton.onClick.AddListener(OnBuybackClicked);

            // 按钮文字（手动创建以便动态更新）
            buybackLabel = UIFactory.CreateText(btnRt, "Label", "⚡ 金币买活", 24,
                TextAlignmentOptions.Center, new Color(0.10f, 0.08f, 0.02f, 1f),
                FontStyles.Bold);
            UIFactory.SetFullStretch(buybackLabel.rectTransform);
            buybackLabel.raycastTarget = false;

            // 金币信息
            goldInfoText = UIFactory.CreateText(buybackPanel, "GoldInfo",
                "当前金币: --- | 花费: ---", 18,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.8f));
            var goldRt = goldInfoText.rectTransform;
            goldRt.anchorMin = new Vector2(0.05f, 0f);
            goldRt.anchorMax = new Vector2(0.95f, 0.35f);
            goldRt.offsetMin = Vector2.zero;
            goldRt.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (respawnVM == null) return;

            bool isDead = respawnVM.IsPendingRespawn;

            switch (state)
            {
                case State.Alive:
                    if (isDead)
                    {
                        state = State.Dead;
                        fadeInTimer = 0f;
                        buybackRequested = false;
                        buybackCooldown = 0f;
                        SetVisible(true);
                        wmj.Log.I("[DeathOverlay] 机器人阵亡，显示死亡覆盖层", wmj.Log.Tag.UI);
                    }
                    break;

                case State.Dead:
                    UpdateDeathState();
                    // 空格键买活
                    if (Input.GetKeyDown(KeyCode.Space) && !buybackRequested && buybackCooldown <= 0f)
                    {
                        uint gold = logisticsVM?.RemainingEconomy ?? 0;
                        uint cost = respawnVM.GoldCostForRespawn;
                        bool canPay = respawnVM.CanPayForRespawn && gold >= cost && cost > 0;
                        if (canPay)
                            OnBuybackClicked();
                    }
                    if (!isDead)
                    {
                        // 复活了 → 播放白闪 → 隐藏
                        state = State.Reviving;
                        reviveFlashTimer = REVIVE_FLASH_DURATION;
                        reviveFlash.gameObject.SetActive(true);
                        reviveFlash.color = new Color(1f, 1f, 1f, 0.8f);
                        wmj.Log.I("[DeathOverlay] 已复活，播放复活特效", wmj.Log.Tag.UI);
                    }
                    break;

                case State.Reviving:
                    reviveFlashTimer -= Time.deltaTime;
                    float flashAlpha = Mathf.Clamp01(reviveFlashTimer / REVIVE_FLASH_DURATION) * 0.8f;
                    reviveFlash.color = new Color(1f, 1f, 1f, flashAlpha);
                    canvasGroup.alpha = flashAlpha / 0.8f;
                    if (reviveFlashTimer <= 0f)
                    {
                        state = State.Alive;
                        SetVisible(false);
                        reviveFlash.gameObject.SetActive(false);
                    }
                    break;
            }
        }

        private void UpdateDeathState()
        {
            // 渐现动画
            fadeInTimer += Time.deltaTime;
            float fadeT = Mathf.Clamp01(fadeInTimer / FADE_IN_DURATION);
            canvasGroup.alpha = fadeT;

            // 复活进度
            uint total = respawnVM.TotalRespawnProgress;
            uint current = respawnVM.CurrentRespawnProgress;
            float pct = total > 0 ? Mathf.Clamp01((float)current / total) : 0f;
            uint remaining = total > current ? total - current : 0;

            // 更新进度条
            progressFill.rectTransform.anchorMax = new Vector2(pct, 1f);
            progressText.text = $"{Mathf.RoundToInt(pct * 100)}%";
            timerText.text = remaining > 0
                ? $"复活倒计时  {remaining}s / {total}s"
                : "即将复活...";

            // 进度条颜色：接近满时变绿
            progressFill.color = pct > 0.8f
                ? Color.Lerp(ProgressFillColor, new Color(0.2f, 0.9f, 0.3f, 0.9f), (pct - 0.8f) / 0.2f)
                : ProgressFillColor;

            // 买活按钮状态
            UpdateBuybackUI();

            // 买活冷却
            if (buybackCooldown > 0f) buybackCooldown -= Time.deltaTime;
        }

        private void UpdateBuybackUI()
        {
            uint gold = logisticsVM?.RemainingEconomy ?? 0;
            uint cost = respawnVM.GoldCostForRespawn;
            bool canPay = respawnVM.CanPayForRespawn && gold >= cost;

            // 已经请求过买活 → 显示等待
            if (buybackRequested)
            {
                buybackLabel.text = "⏳ 买活请求已发送...";
                var btnImg = buybackButton.GetComponent<Image>();
                btnImg.color = BuybackDisabled;
                buybackButton.interactable = false;
                goldInfoText.text = $"当前金币: {gold}";
                return;
            }

            // 可以买活
            if (canPay && cost > 0)
            {
                buybackLabel.text = $"⚡ 金币买活 ({cost}金) [空格]";
                buybackLabel.color = new Color(0.10f, 0.08f, 0.02f, 1f);
                var btnImg = buybackButton.GetComponent<Image>();
                btnImg.color = BuybackGold;
                buybackButton.interactable = buybackCooldown <= 0f;
                goldInfoText.text = $"当前金币: {gold}  |  花费: {cost}金";
                goldInfoText.color = UIColors.WithAlpha(UIColors.Silver, 0.8f);
            }
            else
            {
                // 金币不足
                buybackLabel.text = $"金币不足 (需{cost}金)";
                buybackLabel.color = UIColors.WithAlpha(UIColors.Silver, 0.6f);
                var btnImg = buybackButton.GetComponent<Image>();
                btnImg.color = BuybackDisabled;
                buybackButton.interactable = false;
                goldInfoText.text = $"当前金币: {gold}  |  花费: {cost}金";
                goldInfoText.color = UIColors.WithAlpha(new Color(0.95f, 0.3f, 0.2f), 0.8f);
            }
        }

        private void OnBuybackClicked()
        {
            if (buybackRequested || buybackCooldown > 0f) return;
            if (NetworkManager.Instance == null) return;

            // 发送买活指令：CommonCommand(cmd_type=102, param=0)
            var cmd = new CommonCommand();
            cmd.CmdType = 102; // 买活指令
            cmd.Param = 0;

            byte[] payload = cmd.ToByteArray();
            NetworkManager.Instance.SendMqttMessage("CommonCommand", payload);

            buybackRequested = true;
            buybackCooldown = 2f; // 2 秒冷却，防止重复点击
            wmj.Log.I("[DeathOverlay] 已发送买活请求", wmj.Log.Tag.UI);
        }

        private void SetVisible(bool visible)
        {
            if (overlayCanvas != null)
                overlayCanvas.gameObject.SetActive(visible);
        }

        public void Shutdown()
        {
            respawnVM?.Dispose();
            logisticsVM?.Dispose();
            respawnVM = null;
            logisticsVM = null;
            if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
        }
    }
}
