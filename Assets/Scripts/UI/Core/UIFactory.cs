using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI.Core
{
    /// <summary>
    /// UI 元素工厂 — 纯代码创建 Canvas / Image / Text / Button 等
    /// </summary>
    public static class UIFactory
    {
        // 字体缓存 — 只搜索一次 Resources
        private static TMP_FontAsset _cachedFont;
        private static bool _fontSearched;

        /// <summary>
        /// 应用启动时预加载字体，避免首次创建 Text 时卡顿
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreloadFont()
        {
            if (_fontSearched) return;
            _fontSearched = true;

            // 1) 优先加载预制 SDF 字体资产
            var loaded = Resources.Load<TMP_FontAsset>("Fonts/ChineseFont SDF");
            if (loaded != null && loaded.atlasTexture != null)
            {
                _cachedFont = loaded;
                wmj.Log.I("[UIFactory] 预加载 SDF 字体成功", wmj.Log.Tag.UI);
                return;
            }

            // 2) SDF 资产不可用 — 从 TTF 在运行时动态创建
            var ttf = Resources.Load<Font>("Fonts/ZhanKuGaoDuanHei");
            if (ttf == null) ttf = Resources.Load<Font>("Fonts/ChineseFont");
            if (ttf != null)
            {
                _cachedFont = TMP_FontAsset.CreateFontAsset(ttf);
                if (_cachedFont != null)
                {
                    _cachedFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    // 预热常用字符
                    _cachedFont.TryAddCharacters(
                        "兵种选择英雄工程步兵空中哨兵飞镖雷达红方蓝方确认设置保存通知参数界面自定义"
                        + "血量热量弹药准星倍率宽度半径时长显示持续最大开镜受击提示重新选择阵营"
                        + "请先选择已选择可以确认取消返回切换拖拽位置大小缩放可见恢复默认"
                        + "攻防回冷罚速能加成惩击杀需发获得已结束效果增益减"
                        + "字体透明度全局模块区域信息闪烁阈值低警告高点线环"
                        + "系统配置全部重置条高度存储状态栏竖直排列"
                        + "布局缩略图预览拖拽方块可调整元素实同步关闭单列宽间距"
                        + "更多展开剩余秒级别中敌人血剩需弹"
                        + "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
                        + "+-*/=()[]{}|\\/<>,.;:!?@#$%^&~`'\""
                        + "⚙×∞↺✕·"
                    );
                    wmj.Log.I("[UIFactory] 已从 TTF 动态创建并预热中文字体", wmj.Log.Tag.UI);
                }
            }

            if (_cachedFont == null)
                wmj.Log.W("[UIFactory] 未找到中文字体，将使用 TMP 默认字体", wmj.Log.Tag.UI);
        }

        // ─── Canvas ───
        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            // 确保场景中存在 EventSystem（UI 交互必需）
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            return canvas;
        }

        // ─── Image ───
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image CreateFullScreenImage(Transform parent, string name, Color color)
        {
            var img = CreateImage(parent, name, color);
            SetFullStretch(img.rectTransform);
            return img;
        }

        // ─── TMP Text ───
        public static TextMeshProUGUI CreateText(Transform parent, string name, string text,
            int fontSize = 24, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            Color? color = null, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color ?? UIColors.White;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

            // 使用预加载的中文字体（由 PreloadFont 在启动时初始化）
            if (!_fontSearched) PreloadFont();
            if (_cachedFont != null)
                tmp.font = _cachedFont;

            return tmp;
        }

        // ─── Button ───
        public static Button CreateButton(Transform parent, string name, string label,
            Color bgColor, int fontSize = 24, Color? textColor = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            if (!string.IsNullOrEmpty(label))
            {
                CreateText(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center,
                    textColor ?? UIColors.White, FontStyles.Bold);
                var lblRt = go.transform.GetChild(0).GetComponent<RectTransform>();
                SetFullStretch(lblRt);
            }
            return btn;
        }

        // ─── Slider (HP bar, heat bar) ───
        public static Slider CreateSlider(Transform parent, string name,
            Color bgColor, Color fillColor, float height = 12f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var slider = go.AddComponent<Slider>();
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            var bg = CreateImage(go.transform, "Background", bgColor);
            SetFullStretch(bg.rectTransform);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            SetFullStretch(fillAreaRt);

            var fill = CreateImage(fillArea.transform, "Fill", fillColor);
            SetFullStretch(fill.rectTransform);

            slider.fillRect = fill.rectTransform;

            return slider;
        }

        // ─── RectTransform helpers ───
        public static void SetFullStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void SetAnchoredSize(RectTransform rt, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
        }

        /// <summary>创建 CanvasGroup 便于淡入淡出</summary>
        public static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        // ─── 圆角 ───

        /// <summary>为 Image 应用圆角 9-sliced 精灵</summary>
        public static void ApplyRoundedCorners(Image img, int texSize = 64, int radius = 12)
        {
            img.sprite = UIShapeHelper.GetRoundedRectSprite(texSize, radius);
            img.type = Image.Type.Sliced;
        }

        // ─── 平行四边形倾斜（Skew）───

        /// <summary>
        /// 对 RectTransform 施加水平剪切（skew），水平边保持水平，垂直边倾斜。
        /// skewAngle 为正值表示顺时针倾斜（即上沿右移）。
        /// 实现方式：通过挂载 UISkew 组件修改 Mesh 顶点。
        /// </summary>
        public static void ApplySkew(RectTransform rt, float skewAngle = 5f)
        {
            if (Mathf.Approximately(skewAngle, 0f)) return;
            var skew = rt.gameObject.GetComponent<UISkew>();
            if (skew == null) skew = rt.gameObject.AddComponent<UISkew>();
            skew.skewAngle = skewAngle;
        }

        /// <summary>创建圆角容器背景 Image（不倾斜，亮蓝色半透明 + 圆角）</summary>
        public static Image CreateContainerBg(Transform parent, string name,
            float bgAlpha = 0.18f)
        {
            var bg = CreateImage(parent, name, UIColors.WithAlpha(UIColors.BrightBlue, bgAlpha));
            ApplyRoundedCorners(bg);
            bg.raycastTarget = true;
            return bg;
        }

        /// <summary>创建容器边框（淡蓝色）</summary>
        public static Image CreateContainerBorder(Transform parent, string name, float alpha = 0.4f)
        {
            var border = CreateImage(parent, name,
                UIColors.WithAlpha(UIColors.LightBlueBorder, alpha));
            ApplyRoundedCorners(border);
            border.raycastTarget = false;
            return border;
        }

        /// <summary>创建圆角 + skew 倾斜按钮（用于选项类 UI）</summary>
        public static Button CreateSkewedButton(Transform parent, string name, string label,
            Color bgColor, float skewAngle = 5f, int fontSize = 24, Color? textColor = null)
        {
            var btn = CreateButton(parent, name, label, bgColor, fontSize, textColor);
            var img = btn.GetComponent<Image>();
            ApplyRoundedCorners(img);
            img.raycastTarget = true;
            ApplySkew(btn.GetComponent<RectTransform>(), skewAngle);
            btn.transition = Selectable.Transition.None;
            return btn;
        }

        /// <summary>创建圆角容器按钮（不倾斜）</summary>
        public static Button CreateRoundedButton(Transform parent, string name, string label,
            Color bgColor, int fontSize = 24, Color? textColor = null)
        {
            var btn = CreateButton(parent, name, label, bgColor, fontSize, textColor);
            var img = btn.GetComponent<Image>();
            ApplyRoundedCorners(img);
            img.raycastTarget = true;
            btn.transition = Selectable.Transition.None;
            return btn;
        }
    }
}
