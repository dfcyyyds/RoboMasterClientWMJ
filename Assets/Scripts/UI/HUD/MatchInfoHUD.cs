using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;
using UI.ViewModels;
using System.ComponentModel;
using System.Collections.Generic;

namespace UI.HUD
{
    /// <summary>
    /// 对局信息 HUD — 屏幕顶部中央
    /// 动态布局：仅显示用户开启的信息项，面板自动伸缩
    /// 风格：深蓝半透明玻璃面板 + 微光边框 + 棱角倾斜标签 + 彩色强调线
    /// </summary>
    public class MatchInfoHUD : MonoBehaviour
    {
        // ─── 数据源 ───
        private GameStatusViewModel gameVM;
        private GlobalLogisticsStatusViewModel logisticsVM;

        // ─── 根级 ───
        private RectTransform rootRt;
        private Image panelGlow;
        private Image panelBg;
        private Image topAccent;

        // ─── 分段定义 ───
        private struct Section
        {
            public GameObject go;
            public RectTransform rt;
            public float width;
        }

        private Section stageSection;
        private Section timerSection;
        private Section scoreSection;
        private Section roundSection;
        private Section economySection;

        // ─── 文本引用 ───
        private TextMeshProUGUI stageText;
        private Image stageBadgeBg;
        private TextMeshProUGUI timerText;
        private TextMeshProUGUI timerLabel;
        private TextMeshProUGUI redScoreText;
        private TextMeshProUGUI blueScoreText;
        private TextMeshProUGUI scoreSep;
        private TextMeshProUGUI roundText;
        private TextMeshProUGUI roundLabel;
        private TextMeshProUGUI economyText;
        private TextMeshProUGUI economyLabel;
        private GameObject pauseIndicator;
        private TextMeshProUGUI pauseText;

        // ─── 动态分割线列表 ───
        private readonly List<Image> dividers = new List<Image>();

        // ─── 颜色 ───
        private static readonly Color PanelBgColor = new Color(0.02f, 0.03f, 0.08f, 0.78f);
        private static readonly Color GlowColor = new Color(0.10f, 0.40f, 0.80f, 0.12f);
        private static readonly Color AccentCyan = new Color(0f, 0.90f, 1f, 1f);
        private static readonly Color AccentGold = new Color(1f, 0.85f, 0.30f, 1f);
        private static readonly Color StageActive = new Color(0.10f, 0.50f, 0.90f, 0.80f);
        private static readonly Color StageIdle = new Color(0.08f, 0.12f, 0.22f, 0.65f);
        private static readonly Color TimerWarn = new Color(1f, 0.35f, 0.18f, 1f);
        private static readonly Color DivColor = new Color(0.30f, 0.60f, 0.95f, 0.18f);
        private static readonly Color ScorePanelBg = new Color(0.04f, 0.06f, 0.14f, 0.55f);

        // ─── 布局常量 ───
        private const float PANEL_H = 48f;
        private const float PAD = 14f;          // 左右内边距
        private const float GAP = 3f;           // 分割线两侧间距
        private const float DIV_W = 2f;         // 分割线宽度
        private const float W_STAGE = 130f;
        private const float W_TIMER = 115f;
        private const float W_SCORE = 170f;
        private const float W_ROUND = 95f;
        private const float W_ECONOMY = 115f;

        // ─── 状态 ───
        private bool renderPending;
        private float blinkTimer;
        private bool blinkOn = true;
        private readonly bool[] lastVis = new bool[5];

        // ════════════════════ 生命周期 ════════════════════

        void Awake() => BuildUI();

        void Start()
        {
            gameVM = new GameStatusViewModel();
            gameVM.Initialize();
            gameVM.PropertyChanged += OnVM;

            logisticsVM = new GlobalLogisticsStatusViewModel();
            logisticsVM.Initialize();
            logisticsVM.PropertyChanged += OnVM;

            // 初始化可见性缓存并首次布局
            CacheVisibility();
            RecalcLayout();
            renderPending = true;
        }

        void OnDestroy()
        {
            if (gameVM != null) { gameVM.PropertyChanged -= OnVM; gameVM.Dispose(); }
            if (logisticsVM != null) { logisticsVM.PropertyChanged -= OnVM; logisticsVM.Dispose(); }
        }

        void Update()
        {
            // 检测可见性变更
            if (VisibilityDirty()) RecalcLayout();

            if (renderPending) { RenderAll(); renderPending = false; }

            // 倒计时闪烁
            if (gameVM != null && gameVM.CurrentStage == 4 && gameVM.StageCountdownSec <= 30)
            {
                blinkTimer += Time.deltaTime;
                if (blinkTimer >= 0.5f) { blinkTimer = 0f; blinkOn = !blinkOn; }
                if (timerText) timerText.alpha = blinkOn ? 1f : 0.35f;
            }
            else
            {
                if (timerText) timerText.alpha = 1f;
                blinkOn = true; blinkTimer = 0f;
            }
        }

        private void OnVM(object sender, PropertyChangedEventArgs e) => renderPending = true;

        // ════════════════════ UI 构建 ════════════════════

        private void BuildUI()
        {
            // 根节点 — 顶部居中
            rootRt = gameObject.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 1f);
            rootRt.anchoredPosition = new Vector2(0, -4);
            rootRt.sizeDelta = new Vector2(600, PANEL_H); // RecalcLayout 会修正宽度

            // ── 外发光 ──
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(rootRt, false);
            panelGlow = glowGo.AddComponent<Image>();
            panelGlow.color = GlowColor;
            UIFactory.ApplyRoundedCorners(panelGlow, 64, 16);
            panelGlow.raycastTarget = false;
            var glowRt = glowGo.GetComponent<RectTransform>();
            UIFactory.SetFullStretch(glowRt);
            glowRt.offsetMin = new Vector2(-3, -3);
            glowRt.offsetMax = new Vector2(3, 3);

            // ── 主面板背景 ──
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(rootRt, false);
            panelBg = bgGo.AddComponent<Image>();
            panelBg.color = PanelBgColor;
            UIFactory.ApplyRoundedCorners(panelBg, 64, 12);
            panelBg.raycastTarget = false;
            UIFactory.SetFullStretch(bgGo.GetComponent<RectTransform>());

            // ── 顶部强调线 ──
            topAccent = UIFactory.CreateImage(rootRt, "TopLine",
                new Color(0.20f, 0.60f, 0.95f, 0.40f));
            topAccent.rectTransform.anchorMin = new Vector2(0.06f, 0.90f);
            topAccent.rectTransform.anchorMax = new Vector2(0.94f, 0.96f);
            topAccent.rectTransform.offsetMin = Vector2.zero;
            topAccent.rectTransform.offsetMax = Vector2.zero;
            topAccent.raycastTarget = false;

            // ── 各信息分段 ──
            BuildStage();
            BuildTimer();
            BuildScore();
            BuildRound();
            BuildEconomy();
            BuildPause();
        }

        // ──────── 阶段 ────────
        private void BuildStage()
        {
            var go = new GameObject("Stage");
            go.transform.SetParent(rootRt, false);
            var rt = go.AddComponent<RectTransform>();

            stageBadgeBg = go.AddComponent<Image>();
            stageBadgeBg.color = StageIdle;
            UIFactory.ApplyRoundedCorners(stageBadgeBg, 32, 8);
            stageBadgeBg.raycastTarget = false;
            UIFactory.ApplySkew(rt, 4f);

            stageText = UIFactory.CreateText(rt, "Txt", "准备阶段", 20,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(stageText.rectTransform);
            stageText.rectTransform.offsetMin = new Vector2(8, 3);
            stageText.rectTransform.offsetMax = new Vector2(-8, -3);
            stageText.enableAutoSizing = true;
            stageText.fontSizeMin = 14;
            stageText.fontSizeMax = 22;

            stageSection = new Section { go = go, rt = rt, width = W_STAGE };
        }

        // ──────── 倒计时 ────────
        private void BuildTimer()
        {
            var go = new GameObject("Timer");
            go.transform.SetParent(rootRt, false);
            var rt = go.AddComponent<RectTransform>();

            timerLabel = UIFactory.CreateText(rt, "Lbl", "剩余", 11,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.55f));
            timerLabel.rectTransform.anchorMin = new Vector2(0f, 0.62f);
            timerLabel.rectTransform.anchorMax = new Vector2(1f, 0.95f);
            timerLabel.rectTransform.offsetMin = Vector2.zero;
            timerLabel.rectTransform.offsetMax = Vector2.zero;

            timerText = UIFactory.CreateText(rt, "Val", "00:00", 26,
                TextAlignmentOptions.Center, AccentCyan, FontStyles.Bold);
            timerText.rectTransform.anchorMin = new Vector2(0f, 0.02f);
            timerText.rectTransform.anchorMax = new Vector2(1f, 0.64f);
            timerText.rectTransform.offsetMin = Vector2.zero;
            timerText.rectTransform.offsetMax = Vector2.zero;
            timerText.enableAutoSizing = true;
            timerText.fontSizeMin = 16;
            timerText.fontSizeMax = 28;

            timerSection = new Section { go = go, rt = rt, width = W_TIMER };
        }

        // ──────── 比分（中心） ────────
        private void BuildScore()
        {
            var go = new GameObject("Score");
            go.transform.SetParent(rootRt, false);
            var rt = go.AddComponent<RectTransform>();

            // 比分底板
            var bg = go.AddComponent<Image>();
            bg.color = ScorePanelBg;
            UIFactory.ApplyRoundedCorners(bg, 32, 6);
            bg.raycastTarget = false;

            // 红方色条
            var redBar = UIFactory.CreateImage(rt, "RedBar",
                UIColors.WithAlpha(UIColors.TeamRed, 0.55f));
            redBar.rectTransform.anchorMin = new Vector2(0f, 0.08f);
            redBar.rectTransform.anchorMax = new Vector2(0.015f, 0.92f);
            redBar.rectTransform.offsetMin = Vector2.zero;
            redBar.rectTransform.offsetMax = Vector2.zero;
            redBar.raycastTarget = false;

            // 蓝方色条
            var blueBar = UIFactory.CreateImage(rt, "BlueBar",
                UIColors.WithAlpha(UIColors.TeamBlue, 0.55f));
            blueBar.rectTransform.anchorMin = new Vector2(0.985f, 0.08f);
            blueBar.rectTransform.anchorMax = new Vector2(1f, 0.92f);
            blueBar.rectTransform.offsetMin = Vector2.zero;
            blueBar.rectTransform.offsetMax = Vector2.zero;
            blueBar.raycastTarget = false;

            // 红方分数
            redScoreText = UIFactory.CreateText(rt, "Red", "0", 30,
                TextAlignmentOptions.Right, UIColors.TeamRed, FontStyles.Bold);
            redScoreText.rectTransform.anchorMin = new Vector2(0.02f, 0.05f);
            redScoreText.rectTransform.anchorMax = new Vector2(0.40f, 0.95f);
            redScoreText.rectTransform.offsetMin = Vector2.zero;
            redScoreText.rectTransform.offsetMax = Vector2.zero;
            redScoreText.enableAutoSizing = true;
            redScoreText.fontSizeMin = 20;
            redScoreText.fontSizeMax = 32;

            // 分隔符
            scoreSep = UIFactory.CreateText(rt, "Sep", ":", 24,
                TextAlignmentOptions.Center,
                UIColors.WithAlpha(UIColors.Silver, 0.45f), FontStyles.Bold);
            scoreSep.rectTransform.anchorMin = new Vector2(0.40f, 0.05f);
            scoreSep.rectTransform.anchorMax = new Vector2(0.60f, 0.95f);
            scoreSep.rectTransform.offsetMin = Vector2.zero;
            scoreSep.rectTransform.offsetMax = Vector2.zero;

            // 蓝方分数
            blueScoreText = UIFactory.CreateText(rt, "Blue", "0", 30,
                TextAlignmentOptions.Left, UIColors.TeamBlue, FontStyles.Bold);
            blueScoreText.rectTransform.anchorMin = new Vector2(0.60f, 0.05f);
            blueScoreText.rectTransform.anchorMax = new Vector2(0.98f, 0.95f);
            blueScoreText.rectTransform.offsetMin = Vector2.zero;
            blueScoreText.rectTransform.offsetMax = Vector2.zero;
            blueScoreText.enableAutoSizing = true;
            blueScoreText.fontSizeMin = 20;
            blueScoreText.fontSizeMax = 32;

            scoreSection = new Section { go = go, rt = rt, width = W_SCORE };
        }

        // ──────── 轮次 ────────
        private void BuildRound()
        {
            var go = new GameObject("Round");
            go.transform.SetParent(rootRt, false);
            var rt = go.AddComponent<RectTransform>();

            roundLabel = UIFactory.CreateText(rt, "Lbl", "轮次", 11,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.55f));
            roundLabel.rectTransform.anchorMin = new Vector2(0f, 0.62f);
            roundLabel.rectTransform.anchorMax = new Vector2(1f, 0.95f);
            roundLabel.rectTransform.offsetMin = Vector2.zero;
            roundLabel.rectTransform.offsetMax = Vector2.zero;

            roundText = UIFactory.CreateText(rt, "Val", "1/3", 22,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            roundText.rectTransform.anchorMin = new Vector2(0f, 0.02f);
            roundText.rectTransform.anchorMax = new Vector2(1f, 0.64f);
            roundText.rectTransform.offsetMin = Vector2.zero;
            roundText.rectTransform.offsetMax = Vector2.zero;
            roundText.enableAutoSizing = true;
            roundText.fontSizeMin = 14;
            roundText.fontSizeMax = 24;

            roundSection = new Section { go = go, rt = rt, width = W_ROUND };
        }

        // ──────── 经济 ────────
        private void BuildEconomy()
        {
            var go = new GameObject("Economy");
            go.transform.SetParent(rootRt, false);
            var rt = go.AddComponent<RectTransform>();

            economyLabel = UIFactory.CreateText(rt, "Lbl", "经济", 11,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.55f));
            economyLabel.rectTransform.anchorMin = new Vector2(0f, 0.62f);
            economyLabel.rectTransform.anchorMax = new Vector2(1f, 0.95f);
            economyLabel.rectTransform.offsetMin = Vector2.zero;
            economyLabel.rectTransform.offsetMax = Vector2.zero;

            economyText = UIFactory.CreateText(rt, "Val", "0", 22,
                TextAlignmentOptions.Center, AccentGold, FontStyles.Bold);
            economyText.rectTransform.anchorMin = new Vector2(0f, 0.02f);
            economyText.rectTransform.anchorMax = new Vector2(1f, 0.64f);
            economyText.rectTransform.offsetMin = Vector2.zero;
            economyText.rectTransform.offsetMax = Vector2.zero;
            economyText.enableAutoSizing = true;
            economyText.fontSizeMin = 14;
            economyText.fontSizeMax = 24;

            economySection = new Section { go = go, rt = rt, width = W_ECONOMY };
        }

        // ──────── 暂停指示 ────────
        private void BuildPause()
        {
            pauseIndicator = new GameObject("Pause");
            pauseIndicator.transform.SetParent(rootRt, false);
            var rt = pauseIndicator.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -2);
            rt.sizeDelta = new Vector2(150, 26);

            var bg = pauseIndicator.AddComponent<Image>();
            bg.color = new Color(0.85f, 0.20f, 0.12f, 0.88f);
            UIFactory.ApplyRoundedCorners(bg, 32, 7);
            bg.raycastTarget = false;
            UIFactory.ApplySkew(rt, 4f);

            pauseText = UIFactory.CreateText(rt, "Txt", "⏸  比赛暂停", 16,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(pauseText.rectTransform);
            pauseText.enableAutoSizing = true;
            pauseText.fontSizeMin = 12;
            pauseText.fontSizeMax = 18;

            pauseIndicator.SetActive(false);
        }

        // ════════════════════ 动态布局 ════════════════════

        private void CacheVisibility()
        {
            var s = UILayoutManager.Settings;
            lastVis[0] = s.showMatchStage;
            lastVis[1] = s.showMatchTimer;
            lastVis[2] = s.showMatchScore;
            lastVis[3] = s.showMatchRound;
            lastVis[4] = s.showMatchEconomy;
        }

        private bool VisibilityDirty()
        {
            var s = UILayoutManager.Settings;
            bool dirty = s.showMatchStage != lastVis[0]
                      || s.showMatchTimer != lastVis[1]
                      || s.showMatchScore != lastVis[2]
                      || s.showMatchRound != lastVis[3]
                      || s.showMatchEconomy != lastVis[4];
            if (dirty) CacheVisibility();
            return dirty;
        }

        /// <summary>
        /// 根据当前可见项重新计算面板宽度并重排各分段位置。
        /// 隐藏的分段不占空间，面板自动收缩。
        /// </summary>
        private void RecalcLayout()
        {
            var s = UILayoutManager.Settings;

            // 销毁旧分割线
            foreach (var d in dividers) { if (d) Object.Destroy(d.gameObject); }
            dividers.Clear();

            // 收集可见分段
            stageSection.go.SetActive(s.showMatchStage);
            timerSection.go.SetActive(s.showMatchTimer);
            scoreSection.go.SetActive(s.showMatchScore);
            roundSection.go.SetActive(s.showMatchRound);
            economySection.go.SetActive(s.showMatchEconomy);

            var vis = new List<Section>();
            if (s.showMatchStage) vis.Add(stageSection);
            if (s.showMatchTimer) vis.Add(timerSection);
            if (s.showMatchScore) vis.Add(scoreSection);
            if (s.showMatchRound) vis.Add(roundSection);
            if (s.showMatchEconomy) vis.Add(economySection);

            // 无可见项 → 完全隐藏
            bool any = vis.Count > 0;
            if (panelBg) panelBg.gameObject.SetActive(any);
            if (panelGlow) panelGlow.gameObject.SetActive(any);
            if (topAccent) topAccent.gameObject.SetActive(any);

            if (!any) { rootRt.sizeDelta = new Vector2(0, PANEL_H); return; }

            // 计算总宽度
            float totalW = PAD * 2f;
            for (int i = 0; i < vis.Count; i++)
            {
                totalW += vis[i].width;
                if (i < vis.Count - 1)
                    totalW += GAP + DIV_W + GAP;
            }
            rootRt.sizeDelta = new Vector2(totalW, PANEL_H);

            // 排列各分段
            float x = PAD;
            for (int i = 0; i < vis.Count; i++)
            {
                var sec = vis[i];
                sec.rt.anchorMin = new Vector2(0f, 0.08f);
                sec.rt.anchorMax = new Vector2(0f, 0.92f);
                sec.rt.pivot = new Vector2(0f, 0.5f);
                sec.rt.anchoredPosition = new Vector2(x, 0);
                sec.rt.sizeDelta = new Vector2(sec.width, 0);
                x += sec.width;

                // 添加分割线
                if (i < vis.Count - 1)
                {
                    x += GAP;
                    var div = UIFactory.CreateImage(rootRt, $"D{i}", DivColor);
                    div.rectTransform.anchorMin = new Vector2(0f, 0.15f);
                    div.rectTransform.anchorMax = new Vector2(0f, 0.85f);
                    div.rectTransform.pivot = new Vector2(0f, 0.5f);
                    div.rectTransform.anchoredPosition = new Vector2(x, 0);
                    div.rectTransform.sizeDelta = new Vector2(DIV_W, 0);
                    div.raycastTarget = false;
                    dividers.Add(div);
                    x += DIV_W + GAP;
                }
            }
        }

        // ════════════════════ 数据渲染 ════════════════════

        private void RenderAll()
        {
            if (gameVM == null) return;

            // 阶段
            if (stageText)
            {
                stageText.text = StageName(gameVM.CurrentStage);
                if (stageBadgeBg)
                    stageBadgeBg.color = gameVM.CurrentStage == 4 ? StageActive : StageIdle;
            }

            // 倒计时
            if (timerText)
            {
                int sec = gameVM.StageCountdownSec;
                timerText.text = $"{sec / 60:D2}:{sec % 60:D2}";
                timerText.color = (gameVM.CurrentStage == 4 && sec <= 60) ? TimerWarn : AccentCyan;
            }

            // 比分
            if (redScoreText) redScoreText.text = gameVM.RedScore.ToString();
            if (blueScoreText) blueScoreText.text = gameVM.BlueScore.ToString();

            // 轮次
            if (roundText)
            {
                uint total = gameVM.TotalRounds > 0 ? gameVM.TotalRounds : 3;
                roundText.text = $"{gameVM.CurrentRound}/{total}";
            }

            // 经济
            if (logisticsVM != null && economyText)
                economyText.text = logisticsVM.RemainingEconomy.ToString();

            // 暂停
            if (pauseIndicator) pauseIndicator.SetActive(gameVM.IsPaused);
        }

        private static string StageName(uint s) => s switch
        {
            0 => "未开始",
            1 => "准备阶段",
            2 => "自检阶段",
            3 => "倒计时",
            4 => "比赛中",
            5 => "已结束",
            _ => $"阶段{s}"
        };
    }
}
