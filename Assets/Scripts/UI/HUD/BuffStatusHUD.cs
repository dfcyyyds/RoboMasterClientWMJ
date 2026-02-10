using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// BUFF 状态栏 — 屏幕左侧竖直排列
    /// 上栏 BUFF（蓝色），下栏 DEBUFF（红色）
    /// 新 BUFF 出现时白色闪光 + 通知
    /// </summary>
    public class BuffStatusHUD : MonoBehaviour
    {
        // ─── BUFF 定义 ───
        private static readonly Dictionary<uint, BuffDef> BuffDefs = new Dictionary<uint, BuffDef>
        {
            { 0, new BuffDef("攻击加成", "攻", false) },
            { 1, new BuffDef("防御加成", "防", false) },
            { 2, new BuffDef("回血",     "回", false) },
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

        // ─── 活跃 BUFF ───
        private class ActiveBuff
        {
            public uint buffType;
            public int level;
            public float maxTime, leftTime;
            public bool isDebuff;
            public GameObject barGo;
            public Image timerFill;
            public TextMeshProUGUI infoText;
            public TextMeshProUGUI timeText;
            public Image flashOverlay;
            public float flashTimer;
        }

        private readonly Dictionary<uint, ActiveBuff> activeBuffs = new Dictionary<uint, ActiveBuff>();
        private RectTransform rootRt;
        private RectTransform buffSection, debuffSection;
        private TextMeshProUGUI buffHeader, debuffHeader;
        private NotificationHUD notifications;

        // 颜色常量
        private static readonly Color BuffBlue = new Color(0.22f, 0.55f, 0.95f, 1f);
        private static readonly Color DebuffRed = new Color(0.92f, 0.22f, 0.22f, 1f);
        private static readonly Color BuffBgBlue = new Color(0.08f, 0.12f, 0.28f, 0.88f);
        private static readonly Color DebuffBgRed = new Color(0.28f, 0.06f, 0.06f, 0.88f);

        void Awake()
        {
            rootRt = gameObject.AddComponent<RectTransform>();
            // 左侧竖直条
            rootRt.anchorMin = new Vector2(0.008f, 0.20f);
            rootRt.anchorMax = new Vector2(0.155f, 0.82f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            BuildSections();
        }

        private void BuildSections()
        {
            int fSize = Mathf.Max(UILayoutManager.Settings.buffFontSize, 22);

            // ── BUFF 区域（上半） ──
            var buffGo = new GameObject("BuffSection");
            buffGo.transform.SetParent(rootRt, false);
            buffSection = buffGo.AddComponent<RectTransform>();
            buffSection.anchorMin = new Vector2(0, 0.52f);
            buffSection.anchorMax = new Vector2(1, 1f);
            buffSection.offsetMin = Vector2.zero;
            buffSection.offsetMax = Vector2.zero;

            buffHeader = UIFactory.CreateText(buffSection, "BuffHdr", "BUFF", fSize + 4,
                TextAlignmentOptions.Left, BuffBlue, FontStyles.Bold);
            buffHeader.rectTransform.anchorMin = new Vector2(0.06f, 0.86f);
            buffHeader.rectTransform.anchorMax = new Vector2(1f, 1f);
            buffHeader.rectTransform.offsetMin = Vector2.zero;
            buffHeader.rectTransform.offsetMax = Vector2.zero;
            buffHeader.gameObject.SetActive(false);

            // ── DEBUFF 区域（下半） ──
            var debuffGo = new GameObject("DebuffSection");
            debuffGo.transform.SetParent(rootRt, false);
            debuffSection = debuffGo.AddComponent<RectTransform>();
            debuffSection.anchorMin = new Vector2(0, 0f);
            debuffSection.anchorMax = new Vector2(1, 0.48f);
            debuffSection.offsetMin = Vector2.zero;
            debuffSection.offsetMax = Vector2.zero;

            debuffHeader = UIFactory.CreateText(debuffSection, "DebuffHdr", "DEBUFF", fSize + 4,
                TextAlignmentOptions.Left, DebuffRed, FontStyles.Bold);
            debuffHeader.rectTransform.anchorMin = new Vector2(0.06f, 0.86f);
            debuffHeader.rectTransform.anchorMax = new Vector2(1f, 1f);
            debuffHeader.rectTransform.offsetMin = Vector2.zero;
            debuffHeader.rectTransform.offsetMax = Vector2.zero;
            debuffHeader.gameObject.SetActive(false);
        }

        void Update()
        {
            var toRemove = new List<uint>();
            foreach (var kv in activeBuffs)
            {
                var b = kv.Value;
                b.leftTime -= Time.deltaTime;
                if (b.leftTime <= 0f) { toRemove.Add(kv.Key); continue; }

                if (b.timerFill && b.maxTime > 0)
                    b.timerFill.fillAmount = b.leftTime / b.maxTime;
                if (b.timeText)
                    b.timeText.text = $"{Mathf.CeilToInt(b.leftTime)}s";

                // 新增闪光动画
                if (b.flashTimer > 0 && b.flashOverlay)
                {
                    b.flashTimer -= Time.deltaTime;
                    b.flashOverlay.color = new Color(1, 1, 1, Mathf.Clamp01(b.flashTimer / 0.4f) * 0.55f);
                    if (b.flashTimer <= 0) b.flashOverlay.gameObject.SetActive(false);
                }
            }
            foreach (var t in toRemove) RemoveBuff(t);
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
                    existing.infoText.text = $"{d.icon} {d.name}{lv}";
                }
            }
            else
            {
                AddBuff(buffType, level, maxTime, leftTime);
                var d = GetDef(buffType);
                string lv = level > 1 ? $" Lv{level}" : "";
                string tag = d.isDebuff ? "[减益]" : "[增益]";
                notifications?.Push($"{tag} {d.name}{lv}  [{maxTime}s]", Color.white);
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
            RectTransform parent = isDb ? debuffSection : buffSection;

            // 条形容器
            var bar = new GameObject($"Buff_{buffType}");
            bar.transform.SetParent(parent, false);
            var barRt = bar.AddComponent<RectTransform>();
            barRt.sizeDelta = new Vector2(0, 46);

            // 背景
            var barBg = bar.AddComponent<Image>();
            barBg.color = bg;
            UIFactory.ApplyRoundedCorners(barBg, 32, 8);
            barBg.raycastTarget = false;

            // 左侧色条标识
            var accent = UIFactory.CreateImage(barRt, "Acc", theme);
            accent.rectTransform.anchorMin = new Vector2(0, 0.08f);
            accent.rectTransform.anchorMax = new Vector2(0.025f, 0.92f);
            accent.rectTransform.offsetMin = Vector2.zero;
            accent.rectTransform.offsetMax = Vector2.zero;

            // 图标 + 名称 + 等级
            string lv = level > 1 ? $" Lv{level}" : "";
            var info = UIFactory.CreateText(barRt, "Info", $"{def.icon} {def.name}{lv}",
                fSize, TextAlignmentOptions.Left, Color.white, FontStyles.Bold);
            info.rectTransform.anchorMin = new Vector2(0.045f, 0.48f);
            info.rectTransform.anchorMax = new Vector2(0.72f, 1f);
            info.rectTransform.offsetMin = Vector2.zero;
            info.rectTransform.offsetMax = Vector2.zero;

            // 计时进度条背景
            var timerBg = UIFactory.CreateImage(barRt, "TBg",
                new Color(0.03f, 0.03f, 0.06f, 0.9f));
            UIFactory.ApplyRoundedCorners(timerBg, 32, 4);
            timerBg.rectTransform.anchorMin = new Vector2(0.045f, 0.10f);
            timerBg.rectTransform.anchorMax = new Vector2(0.76f, 0.40f);
            timerBg.rectTransform.offsetMin = Vector2.zero;
            timerBg.rectTransform.offsetMax = Vector2.zero;

            // 计时进度条填充
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

            // 倒计时文字
            var timeTxt = UIFactory.CreateText(barRt, "Time", $"{leftTime}s",
                fSize, TextAlignmentOptions.Right, Color.white, FontStyles.Bold);
            timeTxt.rectTransform.anchorMin = new Vector2(0.78f, 0.08f);
            timeTxt.rectTransform.anchorMax = new Vector2(0.98f, 0.92f);
            timeTxt.rectTransform.offsetMin = Vector2.zero;
            timeTxt.rectTransform.offsetMax = Vector2.zero;

            // 闪光层
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
            notifications?.Push($"{d.name} 已结束", UIColors.WithAlpha(nc, 0.8f));
            if (ab.barGo) Destroy(ab.barGo);
            activeBuffs.Remove(buffType);
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            LayoutSection(buffSection, false);
            LayoutSection(debuffSection, true);

            bool hasBuff = false, hasDebuff = false;
            foreach (var kv in activeBuffs)
            {
                if (kv.Value.isDebuff) hasDebuff = true;
                else hasBuff = true;
            }
            if (buffHeader) buffHeader.gameObject.SetActive(hasBuff);
            if (debuffHeader) debuffHeader.gameObject.SetActive(hasDebuff);
        }

        private void LayoutSection(RectTransform section, bool isDebuff)
        {
            float y = -32f;   // 标题下方起始
            float barH = 46f;
            float gap = 5f;

            foreach (var kv in activeBuffs)
            {
                if (kv.Value.isDebuff != isDebuff) continue;
                var rt = kv.Value.barGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.02f, 1f);
                rt.anchorMax = new Vector2(0.98f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0, y);
                rt.sizeDelta = new Vector2(0, barH);
                y -= barH + gap;
            }
        }
    }
}
