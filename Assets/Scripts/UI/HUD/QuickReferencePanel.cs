using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 操作手现场速查面板（按 H 打开/关闭）
    ///
    /// - 内容由 StreamingAssets/Config/quick_reference.json 渲染而来；
    /// - 风格与 SettingsPanel 一致（深色面板 + 蓝色强调 + 标题栏 + 帮助栏）；
    /// - 仅显示，不接管输入：除 H 切换、PgUp/PgDn 翻页、W/S↑↓ 滚动外，不消费其他按键；
    /// - sortingOrder 高于 SettingsPanel(31100)，作为最上层 overlay。
    /// </summary>
    [DefaultExecutionOrder(-1480)]
    public class QuickReferencePanel : MonoBehaviour
    {
        public static QuickReferencePanel Instance { get; private set; }

        // ─── 配置数据结构 (Unity JsonUtility 兼容) ───
        [Serializable]
        private class QuickRefSection
        {
            public string name;
            public string color;
            public List<string> items;
        }
        [Serializable]
        private class QuickRefData
        {
            public string title;
            public string subtitle;
            public string footer;
            public List<QuickRefSection> sections;
        }

        // ─── 颜色（与 SettingsPanel 同源） ───
        private static readonly Color PanelBg     = new Color(0.04f, 0.05f, 0.10f, 0.96f);
        private static readonly Color ContentBg   = new Color(0.05f, 0.06f, 0.12f, 0.90f);
        private static readonly Color TitleBarBg  = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color RowEven     = new Color(0.07f, 0.08f, 0.14f, 0.55f);
        private static readonly Color RowOdd      = new Color(0.05f, 0.06f, 0.11f, 0.40f);
        private static readonly Color Accent      = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color HintColor   = new Color(0.55f, 0.65f, 0.78f, 0.60f);
        private static readonly Color SectionBg   = new Color(0.10f, 0.16f, 0.28f, 0.85f);

        // ─── UI 引用 ───
        private Canvas canvas;
        private GameObject panelRoot;
        private ScrollRect scroll;
        private bool isOpen;
        private QuickRefData data;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // H 键切换 — 仅在 SettingsPanel 未捕获按键时响应（避免在改键监听里冲突）
            if (kb.hKey.wasPressedThisFrame && !IsSettingsCapturingInput())
            {
                Toggle();
                return;
            }

            if (!isOpen || scroll == null) return;

            // Esc 也允许关闭
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            // 滚动支持：W/S、↑/↓、PgUp/PgDn
            float step = 0f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)         step += 0.015f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)       step -= 0.015f;
            if (kb.pageUpKey.wasPressedThisFrame)                     step += 0.18f;
            if (kb.pageDownKey.wasPressedThisFrame)                   step -= 0.18f;
            if (kb.homeKey.wasPressedThisFrame)
                scroll.verticalNormalizedPosition = 1f;
            else if (kb.endKey.wasPressedThisFrame)
                scroll.verticalNormalizedPosition = 0f;
            else if (Mathf.Abs(step) > 0.0001f)
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + step);
        }

        private bool IsSettingsCapturingInput()
        {
            // SettingsPanel 在文字编辑或改键监听期间会消费几乎所有按键；
            // 这里通过反射式接口避免硬耦合：只检查 Instance 是否存在并打开。
            var sp = SettingsPanel.Instance;
            if (sp == null) return false;
            // 简化：只要设置面板打开，就把 H 让给设置面板（用户可能在文字框里输入 h）
            return sp.gameObject.activeInHierarchy && sp.GetType()
                .GetField("isOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(sp) is bool open && open;
        }

        public void Toggle()
        {
            if (isOpen) Hide();
            else Show();
        }

        public void Show()
        {
            if (panelRoot == null)
            {
                LoadData();
                BuildUI();
            }
            else
            {
                // 每次打开都重新加载，便于现场热更 JSON
                LoadData();
                Rebuild();
            }
            panelRoot.SetActive(true);
            isOpen = true;
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            wmj.Log.I("[QuickReference] 打开速查面板", wmj.Log.Tag.UI);
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            isOpen = false;
        }

        // ═══════════════════ 数据加载 ═══════════════════

        private void LoadData()
        {
            string persistPath = Path.Combine(Application.persistentDataPath, "Config/quick_reference.json");
            string streamPath  = Path.Combine(Application.streamingAssetsPath, "Config/quick_reference.json");
            string path = File.Exists(persistPath) ? persistPath
                        : File.Exists(streamPath)  ? streamPath
                        : null;

            if (path == null)
            {
                wmj.Log.W("[QuickReference] 未找到 quick_reference.json，使用内置占位内容", wmj.Log.Tag.UI);
                data = new QuickRefData
                {
                    title = "操作手速查",
                    subtitle = "按 [H] 关闭",
                    footer = "未找到 quick_reference.json",
                    sections = new List<QuickRefSection>
                    {
                        new QuickRefSection {
                            name = "提示", color = "#3FA9F5",
                            items = new List<string> {
                                "请将 quick_reference.json 放入 StreamingAssets/Config/ 目录"
                            }
                        }
                    }
                };
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrEmpty(json) && json[0] == '\uFEFF') json = json.Substring(1);
                data = JsonUtility.FromJson<QuickRefData>(json);
                if (data == null || data.sections == null)
                    throw new Exception("JSON 反序列化结果为空");
                wmj.Log.I($"[QuickReference] 已加载: {path} ({data.sections.Count} 个分组)", wmj.Log.Tag.UI);
            }
            catch (Exception e)
            {
                wmj.Log.W($"[QuickReference] 加载失败: {e.Message}", wmj.Log.Tag.UI);
                data = new QuickRefData
                {
                    title = "操作手速查",
                    subtitle = "(加载失败)",
                    footer = e.Message,
                    sections = new List<QuickRefSection>()
                };
            }
        }

        // ═══════════════════ UI 构建 ═══════════════════

        private void BuildUI()
        {
            canvas = UIFactory.CreateCanvas("QuickReferenceCanvas", 32000);
            DontDestroyOnLoad(canvas.gameObject);
            panelRoot = canvas.gameObject;

            // 半透明遮罩（不阻挡射线，避免抢占鼠标）
            var overlay = UIFactory.CreateFullScreenImage(panelRoot.transform, "Overlay",
                new Color(0f, 0f, 0f, 0.55f));
            overlay.raycastTarget = false;

            // 居中面板
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(panelRoot.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.18f, 0.08f);
            panelRt.anchorMax = new Vector2(0.82f, 0.92f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            var panelBg = panelGo.AddComponent<Image>();
            panelBg.color = PanelBg;
            panelBg.raycastTarget = false;

            BuildTitleBar(panelRt);
            BuildContentArea(panelRt);
            BuildFooter(panelRt);

            Rebuild();
        }

        private TextMeshProUGUI titleText;
        private TextMeshProUGUI subtitleText;
        private TextMeshProUGUI footerText;
        private RectTransform scrollContent;

        private void BuildTitleBar(RectTransform panel)
        {
            var barGo = new GameObject("TitleBar");
            barGo.transform.SetParent(panel, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0.92f);
            barRt.anchorMax = new Vector2(1, 1f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;
            var barBg = barGo.AddComponent<Image>();
            barBg.color = TitleBarBg;
            barBg.raycastTarget = false;

            // 底部分隔线
            var div = UIFactory.CreateImage(barRt, "TitleDiv",
                UIColors.WithAlpha(Accent, 0.35f));
            div.rectTransform.anchorMin = new Vector2(0.01f, 0f);
            div.rectTransform.anchorMax = new Vector2(0.99f, 0.04f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;

            titleText = UIFactory.CreateText(barGo.transform, "Title", "",
                26, TextAlignmentOptions.Left, Accent, FontStyles.Bold);
            titleText.rectTransform.anchorMin = new Vector2(0.025f, 0f);
            titleText.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            titleText.rectTransform.offsetMin = Vector2.zero;
            titleText.rectTransform.offsetMax = Vector2.zero;

            subtitleText = UIFactory.CreateText(barGo.transform, "Subtitle", "",
                15, TextAlignmentOptions.Right, HintColor);
            subtitleText.rectTransform.anchorMin = new Vector2(0.45f, 0f);
            subtitleText.rectTransform.anchorMax = new Vector2(0.975f, 1f);
            subtitleText.rectTransform.offsetMin = Vector2.zero;
            subtitleText.rectTransform.offsetMax = Vector2.zero;
            subtitleText.richText = true;
        }

        private void BuildContentArea(RectTransform panel)
        {
            var areaGo = new GameObject("ContentArea");
            areaGo.transform.SetParent(panel, false);
            var areaRt = areaGo.AddComponent<RectTransform>();
            areaRt.anchorMin = new Vector2(0, 0.05f);
            areaRt.anchorMax = new Vector2(1, 0.92f);
            areaRt.offsetMin = new Vector2(4, 0);
            areaRt.offsetMax = new Vector2(-4, 0);
            var areaBg = areaGo.AddComponent<Image>();
            areaBg.color = ContentBg;
            areaBg.raycastTarget = false;

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(areaRt, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(viewportRt);
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = Color.white;
            viewportImg.raycastTarget = false;
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            scrollContent = contentGo.AddComponent<RectTransform>();
            scrollContent.anchorMin = new Vector2(0, 1);
            scrollContent.anchorMax = new Vector2(1, 1);
            scrollContent.pivot = new Vector2(0.5f, 1);
            scrollContent.offsetMin = Vector2.zero;
            scrollContent.offsetMax = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 10, 14);
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scroll = areaGo.AddComponent<ScrollRect>();
            scroll.viewport = viewportRt;
            scroll.content = scrollContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        private void BuildFooter(RectTransform panel)
        {
            var barGo = new GameObject("Footer");
            barGo.transform.SetParent(panel, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0);
            barRt.anchorMax = new Vector2(1, 0.05f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;
            var barBg = barGo.AddComponent<Image>();
            barBg.color = TitleBarBg;
            barBg.raycastTarget = false;

            footerText = UIFactory.CreateText(barGo.transform, "FooterText", "",
                14, TextAlignmentOptions.Center, HintColor);
            footerText.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            footerText.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            footerText.rectTransform.offsetMin = Vector2.zero;
            footerText.rectTransform.offsetMax = Vector2.zero;
            footerText.richText = true;
        }

        // ═══════════════════ 内容渲染（每次 Show 都会重建） ═══════════════════

        private void Rebuild()
        {
            if (scrollContent == null) return;

            // 清空旧内容
            for (int i = scrollContent.childCount - 1; i >= 0; i--)
                Destroy(scrollContent.GetChild(i).gameObject);

            if (titleText != null)    titleText.text    = data?.title    ?? "操作手速查";
            if (subtitleText != null) subtitleText.text = data?.subtitle ?? "";
            if (footerText != null)   footerText.text   = data?.footer   ?? "";

            if (data == null || data.sections == null) return;

            int rowParity = 0;
            foreach (var section in data.sections)
            {
                if (section == null) continue;
                BuildSectionHeader(section);
                if (section.items == null) continue;
                foreach (var item in section.items)
                {
                    BuildItemRow(item, rowParity++);
                }
                // 段落间留一个小空行
                BuildSpacer(8f);
            }
        }

        private void BuildSectionHeader(QuickRefSection section)
        {
            var go = new GameObject("Section_" + section.name);
            go.transform.SetParent(scrollContent, false);
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;

            var bg = go.AddComponent<Image>();
            bg.color = SectionBg;
            bg.raycastTarget = false;

            // 左侧颜色条（按 section.color 上色）
            Color barColor = Accent;
            if (!string.IsNullOrEmpty(section.color) &&
                ColorUtility.TryParseHtmlString(section.color, out var c))
                barColor = c;

            var stripe = UIFactory.CreateImage(rt, "Stripe", barColor);
            stripe.rectTransform.anchorMin = new Vector2(0, 0.10f);
            stripe.rectTransform.anchorMax = new Vector2(0.006f, 0.90f);
            stripe.rectTransform.offsetMin = Vector2.zero;
            stripe.rectTransform.offsetMax = Vector2.zero;

            var label = UIFactory.CreateText(rt, "Label", section.name ?? "",
                20, TextAlignmentOptions.Left, barColor, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildItemRow(string text, int parity)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(scrollContent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;

            var bg = go.AddComponent<Image>();
            bg.color = (parity % 2 == 0) ? RowEven : RowOdd;
            bg.raycastTarget = false;

            var label = UIFactory.CreateText(go.transform, "Text", text ?? "",
                17, TextAlignmentOptions.Left, UIColors.Silver);
            label.rectTransform.anchorMin = new Vector2(0.025f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.985f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.richText = true;
            // 等宽对齐键位文本
            label.font = label.font; // 保持工厂字体
        }

        private void BuildSpacer(float h)
        {
            var go = new GameObject("Spacer");
            go.transform.SetParent(scrollContent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = h;
        }
    }
}
