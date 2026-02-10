using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 增益/惩罚通知 HUD — 在视野上方显示通知，自动淡出
    /// </summary>
    public class NotificationHUD : MonoBehaviour
    {
        private struct NotifEntry
        {
            public TextMeshProUGUI text;
            public float expireTime;
            public RectTransform rt;
            public bool isPopup;
        }

        private readonly List<NotifEntry> entries = new List<NotifEntry>();
        private RectTransform rootRt;
        private int maxNotifs;

        void Awake()
        {
            maxNotifs = UILayoutManager.Settings.maxNotifications;

            rootRt = gameObject.AddComponent<RectTransform>();
            // 屏幕顶部居中
            rootRt.anchorMin = new Vector2(0.3f, 0.82f);
            rootRt.anchorMax = new Vector2(0.7f, 0.95f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
        }

        void Update()
        {
            float now = Time.time;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                float remaining = e.expireTime - now;
                if (remaining <= 0)
                {
                    if (e.rt) Destroy(e.rt.gameObject);
                    entries.RemoveAt(i);
                    continue;
                }
                // 最后 0.5s 淡出
                if (remaining < 0.5f && e.text)
                {
                    Color c = e.text.color;
                    c.a = remaining / 0.5f;
                    e.text.color = c;
                }
            }
            // 重新排列位置
            LayoutEntries();
        }

        /// <summary>推送一条通知</summary>
        public void Push(string message, Color color, float duration = -1)
        {
            if (duration <= 0) duration = UILayoutManager.Settings.notificationDuration;

            // 超出上限时移除最早的
            while (entries.Count >= maxNotifs && entries.Count > 0)
            {
                if (entries[0].rt) Destroy(entries[0].rt.gameObject);
                entries.RemoveAt(0);
            }

            var s = UILayoutManager.Settings;
            var txt = UIFactory.CreateText(rootRt, $"Notif_{entries.Count}", message, s.notificationFontSize,
                TextAlignmentOptions.Center, color, FontStyles.Bold);
            txt.textWrappingMode = TextWrappingModes.Normal;

            // 背景
            var bgGo = new GameObject("NotifBg");
            bgGo.transform.SetParent(rootRt, false);
            var bg = bgGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = UIColors.WithAlpha(UIColors.PanelBg, 0.7f);
            UIFactory.ApplyRoundedCorners(bg, 32, 6);
            bg.raycastTarget = false;

            // 通知边框
            var notifBorder = UIFactory.CreateImage(bgGo.transform, "NotifBorder",
                UIColors.WithAlpha(UIColors.LightBlueBorder, 0.35f));
            UIFactory.ApplyRoundedCorners(notifBorder, 32, 6);
            UIFactory.SetFullStretch(notifBorder.rectTransform);
            notifBorder.raycastTarget = false;

            var bgRt = bgGo.GetComponent<RectTransform>();

            // 把文字移到背景下
            txt.transform.SetParent(bgGo.transform, false);
            UIFactory.SetFullStretch(txt.rectTransform);
            txt.rectTransform.offsetMin = new Vector2(12, 4);
            txt.rectTransform.offsetMax = new Vector2(-12, -4);

            entries.Add(new NotifEntry
            {
                text = txt,
                expireTime = Time.time + duration,
                rt = bgRt
            });

            LayoutEntries();
        }

        /// <summary>推送醒目的 BUFF/DEBUFF 弹窗通知（大号白色文字 + 自定义背景色 + 自定义边框色）</summary>
        public void PushBuffPopup(string message, Color textColor, Color bgColor, Color borderColor, float duration = -1)
        {
            if (duration <= 0) duration = UILayoutManager.Settings.notificationDuration + 0.5f;

            while (entries.Count >= maxNotifs && entries.Count > 0)
            {
                if (entries[0].rt) Destroy(entries[0].rt.gameObject);
                entries.RemoveAt(0);
            }

            int fontSize = Mathf.Max(UILayoutManager.Settings.notificationFontSize + 4, 34);
            var txt = UIFactory.CreateText(rootRt, $"BuffPopup_{entries.Count}", message,
                fontSize, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.alpha = 1f;

            var bgGo = new GameObject("BuffPopupBg");
            bgGo.transform.SetParent(rootRt, false);
            var bg = bgGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = bgColor;
            UIFactory.ApplyRoundedCorners(bg, 48, 10);
            bg.raycastTarget = false;

            // 边框 — 使用传入的边框色
            var border = UIFactory.CreateImage(bgGo.transform, "Border", borderColor);
            UIFactory.ApplyRoundedCorners(border, 48, 10);
            UIFactory.SetFullStretch(border.rectTransform);
            border.rectTransform.offsetMin = new Vector2(-2, -2);
            border.rectTransform.offsetMax = new Vector2(2, 2);
            border.raycastTarget = false;

            var bgRt = bgGo.GetComponent<RectTransform>();

            txt.transform.SetParent(bgGo.transform, false);
            UIFactory.SetFullStretch(txt.rectTransform);
            txt.rectTransform.offsetMin = new Vector2(18, 6);
            txt.rectTransform.offsetMax = new Vector2(-18, -6);

            entries.Add(new NotifEntry
            {
                text = txt,
                expireTime = Time.time + duration,
                rt = bgRt,
                isPopup = true
            });

            LayoutEntries();
        }

        private void LayoutEntries()
        {
            float y = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.rt == null) continue;
                float h = e.isPopup ? 62f : 48f;
                float xMin = e.isPopup ? 0.05f : 0.1f;
                float xMax = e.isPopup ? 0.95f : 0.9f;
                e.rt.anchorMin = new Vector2(xMin, 1f);
                e.rt.anchorMax = new Vector2(xMax, 1f);
                e.rt.pivot = new Vector2(0.5f, 1f);
                e.rt.anchoredPosition = new Vector2(0, -y);
                e.rt.sizeDelta = new Vector2(0, h);
                y += h + 4f;
            }
        }
    }
}
