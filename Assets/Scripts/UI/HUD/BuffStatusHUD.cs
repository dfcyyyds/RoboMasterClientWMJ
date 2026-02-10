using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// BUFF 状态栏 — 左侧固定宽度双列（BUFF / DEBUFF）
    /// 按剩余时间排序（最短在上），超出可见数量显示提示
    /// 按 Tab 打开完整 BUFF 列表面板
    /// 新 BUFF/DEBUFF 获得时弹出醒目通知
    /// </summary>
    public class BuffStatusHUD : MonoBehaviour
    {
        // ─── BUFF 定义 ───
        private static readonly Dictionary<uint, BuffDef> BuffDefs = new Dictionary<uint, BuffDef>
        {
            { 0, new BuffDef("攻击加成", "攻", false) },
            { 1, new BuffDef("防御加成", "防", false) },
            { 2, new BuffDef("回血",    "回", false) },
            { 3, new BuffDef("冷却加速", "冷", false) },
            { 4, new BuffDef("伤害惩罚", "罚", true) },
            { 5, new BuffDef("移速加成", "速", false) },
            { 6, new BuffDef("能量增益", "能", false) },
        };

        private struct BuffDef
        {
            public string name, icon;
            public bool isDebuff;
            public BuffDef(string n, string i, bool d) { name = n; icon = i; isDebuff = d; }
        }

        private class ActiveBuff
        {
            public uint buffType;
            public int level;
            public float maxTime, leftTime;
            public bool isDebuff;
            public GameObject barGo;
            public Image timerFill;
            public TextMeshProUGUI iconText;
            public TextMeshProUGUI infoText;
            public TextMeshProUGUI timeText;
            public Image flashOverlay;
            public float flashTimer;
        }

        private readonly Dictionary<uint, ActiveBuff> activeBuffs = new Dictionary<uint, ActiveBuff>();
        private RectTransform rootRt;
        private RectTransform buffColumn, debuffColumn;
        private TextMeshProUGUI buffHeader, debuffHeader;
        private TextMeshProUGUI buffOverflowHint, debuffOverflowHint;
        private NotificationHUD notifications;

        // 完整面板
        private GameObject fullPanel;
        private RectTransform fullPanelContent;
        private bool fullPanelOpen;

        // 颜色常量
        private static readonly Color BuffBlue = new Color(0.22f, 0.55f, 0.95f, 1f);
        private static readonly Color DebuffRed = new Color(0.92f, 0.22f, 0.22f, 1f);
        private static readonly Color BuffBgBlue = new Color(0.08f, 0.12f, 0.28f, 0.88f);
        private static readonly Color DebuffBgRed = new Color(0.28f, 0.06f, 0.06f, 0.88f);
        // 弹窗颜色 — 亮蓝/亮红背景 + 对应边框
        private static readonly Color PopupBuffBg = new Color(0.12f, 0.32f, 0.72f, 0.92f);
        private static readonly Color PopupBuffBorder = new Color(0.45f, 0.70f, 0.98f, 0.80f);
        private static readonly Color PopupDebuffBg = new Color(0.72f, 0.10f, 0.10f, 0.92f);
        private static readonly Color PopupDebuffBorder = new Color(0.50f, 0.08f, 0.08f, 0.90f);

        private int maxVisible;
        private float colWidth;
        private const float BAR_HEIGHT = 72f;  // 加高条目确保文字不重叠不撕裂
        private const float BAR_GAP = 5f;

        void Awake()
        {
            var s = UILayoutManager.Settings;
            maxVisible = Mathf.Max(s.buffMaxVisible, 6);  // 每列至少显示6条
            colWidth = s.buffColumnWidth;

            rootRt = gameObject.AddComponent<RectTransform>();
            // 左侧固定宽度双列 — 下移避开齿轮按钮，确保至少6条可见
            rootRt.anchorMin = new Vector2(0f, 0.06f);
            rootRt.anchorMax = new Vector2(0f, 0.88f);
            rootRt.pivot = new Vector2(0f, 0.5f);
            rootRt.anchoredPosition = new Vector2(8, 0);
            rootRt.sizeDelta = new Vector2(colWidth * 2 + 12, 0);

            BuildColumns();
        }

        private void BuildColumns()
        {
            int fSize = Mathf.Max(UILayoutManager.Settings.buffFontSize, 22);

            // ── BUFF 列（左） ──
            var buffGo = new GameObject("BuffCol");
            buffGo.transform.SetParent(rootRt, false);
            buffColumn = buffGo.AddComponent<RectTransform>();
            buffColumn.anchorMin = new Vector2(0, 0);
            buffColumn.anchorMax = new Vector2(0, 1);
            buffColumn.pivot = new Vector2(0, 1);
            buffColumn.anchoredPosition = new Vector2(0, 0);
            buffColumn.sizeDelta = new Vector2(colWidth, 0);

            buffHeader = UIFactory.CreateText(buffColumn, "Hdr", "BUFF", fSize + 2,
                TextAlignmentOptions.Left, BuffBlue, FontStyles.Bold);
            buffHeader.rectTransform.anchorMin = new Vector2(0, 1);
            buffHeader.rectTransform.anchorMax = new Vector2(1, 1);
            buffHeader.rectTransform.pivot = new Vector2(0, 1);
            buffHeader.rectTransform.anchoredPosition = new Vector2(4, 0);
            buffHeader.rectTransform.sizeDelta = new Vector2(colWidth, 28);
            buffHeader.gameObject.SetActive(false);

            buffOverflowHint = UIFactory.CreateText(buffColumn, "Overflow", "",
                fSize - 4, TextAlignmentOptions.Left,
                UIColors.WithAlpha(UIColors.Silver, 0.7f));
            buffOverflowHint.rectTransform.anchorMin = new Vector2(0, 1);
            buffOverflowHint.rectTransform.anchorMax = new Vector2(1, 1);
            buffOverflowHint.rectTransform.pivot = new Vector2(0, 1);
            buffOverflowHint.rectTransform.sizeDelta = new Vector2(colWidth, 22);
            buffOverflowHint.gameObject.SetActive(false);

            // ── DEBUFF 列（右） ──
            var debuffGo = new GameObject("DebuffCol");
            debuffGo.transform.SetParent(rootRt, false);
            debuffColumn = debuffGo.AddComponent<RectTransform>();
            debuffColumn.anchorMin = new Vector2(0, 0);
            debuffColumn.anchorMax = new Vector2(0, 1);
            debuffColumn.pivot = new Vector2(0, 1);
            debuffColumn.anchoredPosition = new Vector2(colWidth + 8, 0);
            debuffColumn.sizeDelta = new Vector2(colWidth, 0);

            debuffHeader = UIFactory.CreateText(debuffColumn, "Hdr", "DEBUFF", fSize + 2,
                TextAlignmentOptions.Left, DebuffRed, FontStyles.Bold);
            debuffHeader.rectTransform.anchorMin = new Vector2(0, 1);
            debuffHeader.rectTransform.anchorMax = new Vector2(1, 1);
            debuffHeader.rectTransform.pivot = new Vector2(0, 1);
            debuffHeader.rectTransform.anchoredPosition = new Vector2(4, 0);
            debuffHeader.rectTransform.sizeDelta = new Vector2(colWidth, 28);
            debuffHeader.gameObject.SetActive(false);

            debuffOverflowHint = UIFactory.CreateText(debuffColumn, "Overflow", "",
                fSize - 4, TextAlignmentOptions.Left,
                UIColors.WithAlpha(UIColors.Silver, 0.7f));
            debuffOverflowHint.rectTransform.anchorMin = new Vector2(0, 1);
            debuffOverflowHint.rectTransform.anchorMax = new Vector2(1, 1);
            debuffOverflowHint.rectTransform.pivot = new Vector2(0, 1);
            debuffOverflowHint.rectTransform.sizeDelta = new Vector2(colWidth, 22);
            debuffOverflowHint.gameObject.SetActive(false);
        }

        void Update()
        {
            var toRemove = new List<uint>();
            bool needRebuild = false;
            foreach (var kv in activeBuffs)
            {
                var b = kv.Value;
                b.leftTime -= Time.deltaTime;
                if (b.leftTime <= 0f) { toRemove.Add(kv.Key); continue; }

                if (b.timerFill && b.maxTime > 0)
                    b.timerFill.fillAmount = b.leftTime / b.maxTime;
                if (b.timeText)
                    b.timeText.text = $"{Mathf.CeilToInt(b.leftTime)}s";

                if (b.flashTimer > 0 && b.flashOverlay)
                {
                    b.flashTimer -= Time.deltaTime;
                    b.flashOverlay.color = new Color(1, 1, 1, Mathf.Clamp01(b.flashTimer / 0.4f) * 0.55f);
                    if (b.flashTimer <= 0) b.flashOverlay.gameObject.SetActive(false);
                }
            }
            foreach (var t in toRemove) { RemoveBuff(t); needRebuild = true; }

            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame)
                ToggleFullPanel();
        }

        public void SetNotificationHUD(NotificationHUD n) => notifications = n;

        public void UpdateBuff(uint buffType, int level, uint maxTime, uint leftTime, string msgParams)
        {
            if (leftTime == 0 || maxTime == 0)
            {
                if (activeBuffs.ContainsKey(buffType)) RemoveBuff(buffType);
                return;
            }

            if (activeBuffs.TryGetValue(buffType, out var existing))
            {
                existing.level = level;
                existing.maxTime = maxTime;
                existing.leftTime = leftTime;
                if (existing.infoText)
                {
                    var d = GetDef(buffType);
                    string lv = level > 1 ? $" Lv{level}" : "";
                    existing.infoText.text = $"{d.name}{lv}";
                }
                // 确保图标文字始终显示
                if (existing.iconText)
                {
                    var d = GetDef(buffType);
                    existing.iconText.text = d.icon;
                }
            }
            else
            {
                AddBuff(buffType, level, maxTime, leftTime);
                // 醒目弹窗通知
                var d = GetDef(buffType);
                string lv = level > 1 ? $" Lv{level}" : "";
                string tag = d.isDebuff ? "减益" : "增益";
                Color popupBg = d.isDebuff ? PopupDebuffBg : PopupBuffBg;
                Color popupBorder = d.isDebuff ? PopupDebuffBorder : PopupBuffBorder;
                // 使用白色大号文字推送通知
                notifications?.PushBuffPopup($"[{tag}]  {d.name}{lv}  [{maxTime}s]",
                    Color.white, popupBg, popupBorder);
            }
            RebuildLayout();
        }

        // ─── 内部 ───

        private BuffDef GetDef(uint t) =>
            BuffDefs.TryGetValue(t, out var d) ? d : new BuffDef($"效果{t}", "?", false);

        private void AddBuff(uint buffType, int level, uint maxTime, uint leftTime)
        {
            var def = GetDef(buffType);
            int fSize = Mathf.Max(UILayoutManager.Settings.buffFontSize, 22);
            bool isDb = def.isDebuff;
            Color theme = isDb ? DebuffRed : BuffBlue;
            Color bg = isDb ? DebuffBgRed : BuffBgBlue;
            RectTransform parent = isDb ? debuffColumn : buffColumn;

            var bar = new GameObject($"Buff_{buffType}");
            bar.transform.SetParent(parent, false);
            var barRt = bar.AddComponent<RectTransform>();
            barRt.sizeDelta = new Vector2(colWidth, BAR_HEIGHT);

            var barBg = bar.AddComponent<Image>();
            barBg.color = bg;
            UIFactory.ApplyRoundedCorners(barBg, 32, 8);
            barBg.raycastTarget = false;

            // 左侧色条
            var accent = UIFactory.CreateImage(barRt, "Acc", theme);
            accent.rectTransform.anchorMin = new Vector2(0, 0.06f);
            accent.rectTransform.anchorMax = new Vector2(0.02f, 0.94f);
            accent.rectTransform.offsetMin = Vector2.zero;
            accent.rectTransform.offsetMax = Vector2.zero;

            // 图标缩写 — 使用单独文本确保稳定显示
            var iconTxt = UIFactory.CreateText(barRt, "Icon", def.icon,
                fSize + 2, TextAlignmentOptions.Center, theme, FontStyles.Bold);
            iconTxt.rectTransform.anchorMin = new Vector2(0.03f, 0.08f);
            iconTxt.rectTransform.anchorMax = new Vector2(0.16f, 0.92f);
            iconTxt.rectTransform.offsetMin = Vector2.zero;
            iconTxt.rectTransform.offsetMax = Vector2.zero;
            iconTxt.enableWordWrapping = false;
            iconTxt.overflowMode = TextOverflowModes.Overflow;

            // 名称
            string lv = level > 1 ? $" Lv{level}" : "";
            var info = UIFactory.CreateText(barRt, "Info", $"{def.name}{lv}",
                fSize - 2, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            info.rectTransform.anchorMin = new Vector2(0.17f, 0.50f);
            info.rectTransform.anchorMax = new Vector2(0.72f, 0.95f);
            info.rectTransform.offsetMin = Vector2.zero;
            info.rectTransform.offsetMax = Vector2.zero;

            // 进度条
            var timerBg = UIFactory.CreateImage(barRt, "TBg",
                new Color(0.03f, 0.03f, 0.06f, 0.9f));
            UIFactory.ApplyRoundedCorners(timerBg, 32, 4);
            timerBg.rectTransform.anchorMin = new Vector2(0.17f, 0.10f);
            timerBg.rectTransform.anchorMax = new Vector2(0.72f, 0.40f);
            timerBg.rectTransform.offsetMin = Vector2.zero;
            timerBg.rectTransform.offsetMax = Vector2.zero;

            var timerFill = UIFactory.CreateImage(timerBg.transform, "TFill",
                UIColors.WithAlpha(theme, 0.75f));
            UIFactory.ApplyRoundedCorners(timerFill, 32, 4);
            timerFill.rectTransform.anchorMin = Vector2.zero;
            timerFill.rectTransform.anchorMax = Vector2.one;
            timerFill.rectTransform.offsetMin = Vector2.zero;
            timerFill.rectTransform.offsetMax = Vector2.zero;
            timerFill.type = Image.Type.Filled;
            timerFill.fillMethod = Image.FillMethod.Horizontal;
            timerFill.fillOrigin = 0;
            timerFill.fillAmount = maxTime > 0 ? (float)leftTime / maxTime : 1f;

            // 倒计时
            var timeTxt = UIFactory.CreateText(barRt, "Time", $"{leftTime}s",
                fSize, TextAlignmentOptions.Right, Color.white, FontStyles.Bold);
            timeTxt.rectTransform.anchorMin = new Vector2(0.74f, 0.08f);
            timeTxt.rectTransform.anchorMax = new Vector2(0.98f, 0.92f);
            timeTxt.rectTransform.offsetMin = Vector2.zero;
            timeTxt.rectTransform.offsetMax = Vector2.zero;

            // 闪光
            var flash = UIFactory.CreateImage(barRt, "Flash", new Color(1, 1, 1, 0.55f));
            UIFactory.ApplyRoundedCorners(flash, 32, 8);
            UIFactory.SetFullStretch(flash.rectTransform);
            flash.raycastTarget = false;

            activeBuffs[buffType] = new ActiveBuff
            {
                buffType = buffType,
                level = level,
                maxTime = maxTime,
                leftTime = leftTime,
                isDebuff = isDb,
                barGo = bar,
                timerFill = timerFill,
                iconText = iconTxt,
                infoText = info,
                timeText = timeTxt,
                flashOverlay = flash,
                flashTimer = 0.4f
            };
        }

        private void RemoveBuff(uint buffType)
        {
            if (!activeBuffs.TryGetValue(buffType, out var ab)) return;
            var d = GetDef(buffType);
            Color nc = d.isDebuff ? DebuffRed : BuffBlue;
            Color popBg = d.isDebuff ? PopupDebuffBg : PopupBuffBg;
            Color popBorder = d.isDebuff ? PopupDebuffBorder : PopupBuffBorder;
            notifications?.PushBuffPopup($"{d.name} 已结束", UIColors.WithAlpha(nc, 0.9f), popBg, popBorder);
            if (ab.barGo) Destroy(ab.barGo);
            activeBuffs.Remove(buffType);
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            LayoutColumn(buffColumn, false, buffHeader, buffOverflowHint);
            LayoutColumn(debuffColumn, true, debuffHeader, debuffOverflowHint);
        }

        /// <summary>按剩余时间排序布局，超出 maxVisible 的隐藏并显示提示</summary>
        private void LayoutColumn(RectTransform column, bool isDebuff,
            TextMeshProUGUI header, TextMeshProUGUI overflowHint)
        {
            var sorted = activeBuffs.Values
                .Where(b => b.isDebuff == isDebuff)
                .OrderBy(b => b.leftTime)
                .ToList();

            bool hasItems = sorted.Count > 0;
            if (header) header.gameObject.SetActive(hasItems);

            float y = hasItems ? -32f : 0f;
            int shown = 0;
            int hidden = 0;

            foreach (var b in sorted)
            {
                if (shown < maxVisible)
                {
                    b.barGo.SetActive(true);
                    var rt = b.barGo.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 1);
                    rt.anchoredPosition = new Vector2(0, y);
                    rt.sizeDelta = new Vector2(0, BAR_HEIGHT);
                    y -= BAR_HEIGHT + BAR_GAP;
                    shown++;
                }
                else
                {
                    b.barGo.SetActive(false);
                    hidden++;
                }
            }

            if (overflowHint)
            {
                if (hidden > 0)
                {
                    overflowHint.gameObject.SetActive(true);
                    overflowHint.text = $"  +{hidden} 更多 (Tab 展开)";
                    overflowHint.rectTransform.anchoredPosition = new Vector2(0, y - 2);
                }
                else
                {
                    overflowHint.gameObject.SetActive(false);
                }
            }
        }

        // ─── 完整面板 (Tab) ───

        private void ToggleFullPanel()
        {
            if (fullPanelOpen) { CloseFullPanel(); return; }
            if (activeBuffs.Count == 0) return;
            OpenFullPanel();
        }

        private void OpenFullPanel()
        {
            fullPanelOpen = true;
            if (fullPanel != null) { fullPanel.SetActive(true); RefreshFullPanel(); return; }

            // 独立 Canvas 挂到根级，避免受 BuffStatusHUD 的 RectTransform 裁剪
            var canvas = UIFactory.CreateCanvas("BuffFullCanvas", 22000);
            fullPanel = canvas.gameObject;

            // 半透明遮罩
            var overlay = UIFactory.CreateFullScreenImage(canvas.transform, "Overlay",
                new Color(0, 0, 0, 0.4f));
            overlay.raycastTarget = true;
            var ob = overlay.gameObject.AddComponent<Button>();
            ob.transition = Selectable.Transition.None;
            ob.onClick.AddListener(CloseFullPanel);

            // 面板 — 更宽更高，充分显示 BUFF 信息
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(canvas.transform, false);
            panel.anchorMin = new Vector2(0.20f, 0.08f);
            panel.anchorMax = new Vector2(0.80f, 0.92f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.10f, 0.92f);
            UIFactory.ApplyRoundedCorners(bg, 48, 14);
            bg.raycastTarget = true;

            // 外发光边框
            var glow = UIFactory.CreateImage(panel, "Glow",
                UIColors.WithAlpha(UIColors.BrightBlue, 0.12f));
            UIFactory.ApplyRoundedCorners(glow, 48, 16);
            UIFactory.SetFullStretch(glow.rectTransform);
            glow.rectTransform.offsetMin = new Vector2(-2, -2);
            glow.rectTransform.offsetMax = new Vector2(2, 2);

            // 标题
            var titleBar = UIFactory.CreateImage(panel, "TitleBg",
                new Color(0.06f, 0.08f, 0.16f, 0.9f));
            UIFactory.ApplyRoundedCorners(titleBar, 48, 14);
            titleBar.rectTransform.anchorMin = new Vector2(0, 0.90f);
            titleBar.rectTransform.anchorMax = new Vector2(1, 1f);
            titleBar.rectTransform.offsetMin = Vector2.zero;
            titleBar.rectTransform.offsetMax = Vector2.zero;

            var title = UIFactory.CreateText(titleBar.rectTransform, "Title",
                "BUFF / DEBUFF 状态一览", 26,
                TextAlignmentOptions.Center, UIColors.BrightBlue, FontStyles.Bold);
            UIFactory.SetFullStretch(title.rectTransform);

            // 关闭提示
            var closeHint = UIFactory.CreateText(panel, "CloseHint",
                "按 Tab 或点击空白处关闭", 18,
                TextAlignmentOptions.Center,
                UIColors.WithAlpha(UIColors.Silver, 0.5f));
            closeHint.rectTransform.anchorMin = new Vector2(0, 0);
            closeHint.rectTransform.anchorMax = new Vector2(1, 0.06f);
            closeHint.rectTransform.offsetMin = Vector2.zero;
            closeHint.rectTransform.offsetMax = Vector2.zero;

            // 滚动内容区
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(panel, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.03f, 0.07f);
            scrollRt.anchorMax = new Vector2(0.97f, 0.88f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            var scrollMask = scrollGo.AddComponent<Image>();
            scrollMask.color = new Color(0, 0, 0, 0.01f);
            scrollGo.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            fullPanelContent = contentGo.AddComponent<RectTransform>();
            fullPanelContent.anchorMin = new Vector2(0, 1);
            fullPanelContent.anchorMax = new Vector2(1, 1);
            fullPanelContent.pivot = new Vector2(0.5f, 1);
            fullPanelContent.anchoredPosition = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 4;
            vlg.padding = new RectOffset(4, 4, 4, 12);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.content = fullPanelContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;
            scroll.viewport = scrollRt;

            RefreshFullPanel();
        }

        private void RefreshFullPanel()
        {
            if (fullPanelContent == null) return;
            for (int i = fullPanelContent.childCount - 1; i >= 0; i--)
                Destroy(fullPanelContent.GetChild(i).gameObject);

            int fSize = Mathf.Max(UILayoutManager.Settings.buffFontSize, 22);
            var sorted = activeBuffs.Values.OrderBy(b => b.isDebuff).ThenBy(b => b.leftTime).ToList();

            foreach (var b in sorted)
            {
                var d = GetDef(b.buffType);
                Color theme = b.isDebuff ? DebuffRed : BuffBlue;
                Color rowBg = b.isDebuff ? DebuffBgRed : BuffBgBlue;
                string lv = b.level > 1 ? $" Lv{b.level}" : "";
                string tag = b.isDebuff ? "减益" : "增益";

                var rowGo = new GameObject($"FRow_{b.buffType}");
                rowGo.transform.SetParent(fullPanelContent, false);
                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.preferredHeight = 68;
                rowLe.flexibleWidth = 1;

                var rowBgImg = rowGo.AddComponent<Image>();
                rowBgImg.color = rowBg;
                UIFactory.ApplyRoundedCorners(rowBgImg, 32, 10);
                rowBgImg.raycastTarget = false;

                var rowRt = rowGo.GetComponent<RectTransform>();

                // 左侧色条
                var acc = UIFactory.CreateImage(rowRt, "Acc", theme);
                acc.rectTransform.anchorMin = new Vector2(0, 0.06f);
                acc.rectTransform.anchorMax = new Vector2(0.012f, 0.94f);
                acc.rectTransform.offsetMin = Vector2.zero;
                acc.rectTransform.offsetMax = Vector2.zero;

                // 图标
                var ico = UIFactory.CreateText(rowRt, "Ico", d.icon,
                    fSize + 4, TextAlignmentOptions.Center, theme, FontStyles.Bold);
                ico.rectTransform.anchorMin = new Vector2(0.02f, 0.08f);
                ico.rectTransform.anchorMax = new Vector2(0.07f, 0.92f);
                ico.rectTransform.offsetMin = Vector2.zero;
                ico.rectTransform.offsetMax = Vector2.zero;
                ico.enableWordWrapping = false;
                ico.overflowMode = TextOverflowModes.Overflow;

                // 标签
                var tagTxt = UIFactory.CreateText(rowRt, "Tag", $"[{tag}]",
                    fSize - 2, TextAlignmentOptions.Center,
                    UIColors.WithAlpha(theme, 0.80f), FontStyles.Bold);
                tagTxt.rectTransform.anchorMin = new Vector2(0.08f, 0.08f);
                tagTxt.rectTransform.anchorMax = new Vector2(0.18f, 0.92f);
                tagTxt.rectTransform.offsetMin = Vector2.zero;
                tagTxt.rectTransform.offsetMax = Vector2.zero;
                tagTxt.enableWordWrapping = false;

                // 名称
                var nameTxt = UIFactory.CreateText(rowRt, "Name", $"{d.name}{lv}",
                    fSize + 2, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
                nameTxt.rectTransform.anchorMin = new Vector2(0.19f, 0.08f);
                nameTxt.rectTransform.anchorMax = new Vector2(0.58f, 0.92f);
                nameTxt.rectTransform.offsetMin = Vector2.zero;
                nameTxt.rectTransform.offsetMax = Vector2.zero;
                nameTxt.enableWordWrapping = false;
                nameTxt.overflowMode = TextOverflowModes.Ellipsis;

                // 进度条
                var prgBg = UIFactory.CreateImage(rowRt, "PrgBg",
                    new Color(0.03f, 0.03f, 0.06f, 0.9f));
                UIFactory.ApplyRoundedCorners(prgBg, 32, 4);
                prgBg.rectTransform.anchorMin = new Vector2(0.60f, 0.35f);
                prgBg.rectTransform.anchorMax = new Vector2(0.82f, 0.65f);
                prgBg.rectTransform.offsetMin = Vector2.zero;
                prgBg.rectTransform.offsetMax = Vector2.zero;

                var prgFill = UIFactory.CreateImage(prgBg.transform, "PrgFill",
                    UIColors.WithAlpha(theme, 0.75f));
                UIFactory.ApplyRoundedCorners(prgFill, 32, 4);
                prgFill.rectTransform.anchorMin = Vector2.zero;
                prgFill.rectTransform.anchorMax = Vector2.one;
                prgFill.rectTransform.offsetMin = Vector2.zero;
                prgFill.rectTransform.offsetMax = Vector2.zero;
                prgFill.type = Image.Type.Filled;
                prgFill.fillMethod = Image.FillMethod.Horizontal;
                prgFill.fillOrigin = 0;
                prgFill.fillAmount = b.maxTime > 0 ? b.leftTime / b.maxTime : 1f;

                // 时间
                var timeTxt = UIFactory.CreateText(rowRt, "Time",
                    $"{Mathf.CeilToInt(b.leftTime)}s / {b.maxTime}s",
                    fSize, TextAlignmentOptions.Right, Color.white, FontStyles.Bold);
                timeTxt.rectTransform.anchorMin = new Vector2(0.83f, 0.08f);
                timeTxt.rectTransform.anchorMax = new Vector2(0.98f, 0.92f);
                timeTxt.rectTransform.offsetMin = Vector2.zero;
                timeTxt.rectTransform.offsetMax = Vector2.zero;
                timeTxt.enableWordWrapping = false;
            }
        }

        private void CloseFullPanel()
        {
            fullPanelOpen = false;
            if (fullPanel != null) fullPanel.SetActive(false);
        }

        void OnDestroy()
        {
            // fullPanel 是独立根级 Canvas，需手动销毁
            if (fullPanel != null) Destroy(fullPanel);
        }
    }
}
