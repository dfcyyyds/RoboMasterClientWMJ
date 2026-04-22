using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
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

        /// <summary>获取预加载的中文字体 — 供外部手动创建的 TMP 组件使用</summary>
        public static TMP_FontAsset CachedFont
        {
            get
            {
                if (!_fontSearched) PreloadFont();
                return _cachedFont;
            }
        }

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
            }
            else
            {
                // 2) SDF 资产不可用 — 从 TTF 在运行时动态创建
                var ttf = Resources.Load<Font>("Fonts/ZhanKuGaoDuanHei");
                if (ttf == null) ttf = Resources.Load<Font>("Fonts/ChineseFont");
                if (ttf != null)
                {
                    _cachedFont = TMP_FontAsset.CreateFontAsset(ttf);
                    if (_cachedFont != null)
                    {
                        _cachedFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        _cachedFont.TryAddCharacters(BuildPreloadCharset());
                        wmj.Log.I("[UIFactory] 已从 TTF 动态创建并预热中文字体", wmj.Log.Tag.UI);
                    }
                }
            }

            if (_cachedFont == null)
            {
                wmj.Log.W("[UIFactory] 未找到中文字体，将使用 TMP 默认字体", wmj.Log.Tag.UI);
                return;
            }

            // 3) 添加系统默认字体为回退 — 补齐中文字体里没有的符号（如 ⚙、箭头、表情等）
            //    TMP 内置的 LiberationSans SDF 对西文符号覆盖完整
            TryAttachDefaultFallback(_cachedFont);
        }

        /// <summary>构造字体预热字符集 — 包含常用汉字 + 全角标点 + ASCII + 业务符号</summary>
        private static string BuildPreloadCharset()
        {
            var sb = new System.Text.StringBuilder(4096);

            // 业务专用词汇（保证首次出现即可渲染）
            sb.Append("兵种选择英雄工程步兵空中哨兵飞镖雷达红方蓝方确认设置保存通知参数界面自定义")
              .Append("血量热量弹药准星倍率宽度半径时长显示持续最大开镜受击提示重新选择阵营")
              .Append("请先选择已选择可以确认取消返回切换拖拽位置大小缩放可见恢复默认")
              .Append("攻防回冷罚速能加成惩击杀需发获得已结束效果增益减")
              .Append("字体透明度全局模块区域信息闪烁阈值低警告高点线环")
              .Append("系统配置全部重置条高度存储状态栏竖直排列")
              .Append("布局缩略图预览拖拽方块可调整元素实同步关闭单列宽间距")
              .Append("更多展开剩余秒级别中敌人血剩需弹")
              .Append("体射手底盘功率优先爆发冷却控制全自动半散")
              .Append("对局阶段倒计时轮次经济比赛准备自检未开始暂停比分隐藏")
              .Append("启用延迟关镜停止射击")
              .Append("购买弹丸花费金币快捷键绑定冲突按下键位数字")
              .Append("检测硬件网络摄像头分辨率读取失败成功异常错误启动连接断开")
              .Append("体系方案性能高低中等模式推荐普通集成显卡内存处理器")
              .Append("主机从机模式下创建加入房间等待广播扫描");

            // ASCII
            sb.Append("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz")
              .Append("+-*/=()[]{}|\\/<>,.;:!?@#$%^&~`'\" ");

            // 全角中文标点（使用 Unicode 转义避免引号冲突）
            sb.Append("【】「」『』、，。？！：；\u201c\u201d\u2018\u2019…—·《》〈〉（）");

            // 常用符号与箭头（TTF 不含时，会由回退字体补齐）
            sb.Append("⚙×∞↺✕·⏸↑↓←→⚡⏳◉○●▲▼◆★☆♦♠♥♣§※¶°±≈≠≤≥√∑");

            // 扩展常用汉字 — CJK Unified Ideographs 常用区间中的高频字
            //   说明：TryAddCharacters 会静默跳过 TTF 中不存在的字形，
            //   此处只加"常用 3500"内的字（数量可控，不会爆图集）
            string commonCJK =
                "的一是不了人我在有他这为之大来以个中上们到说国和地也子时道出而要于就" +
                "下得可你年生自会那后能对着事其里所去行过家十用发天如然作方成者多日都" +
                "三小军二无同么经法当起与好看学进种将还分此心前面又定见只主没公从" +
                "当最全性运力业现化她通机本由实体明党应理果象各因物期设党政法治济" +
                "社会育科技信息安管委工作员干部门研究工业金数表明动直提让相使等流" +
                "城区内外高下东西南北左右前后开关门窗玩家操作";
            sb.Append(commonCJK);

            return sb.ToString();
        }

        /// <summary>为字体附加系统默认回退 — 补齐缺失符号</summary>
        private static void TryAttachDefaultFallback(TMP_FontAsset font)
        {
            try
            {
                // TMP 内置 LiberationSans SDF 通常覆盖全部 Latin-1 / 符号区
                var fallback = TMP_Settings.defaultFontAsset;
                if (fallback != null && fallback != font && font.fallbackFontAssetTable != null)
                {
                    if (!font.fallbackFontAssetTable.Contains(fallback))
                    {
                        font.fallbackFontAssetTable.Add(fallback);
                        wmj.Log.I("[UIFactory] 已添加 TMP 默认字体回退", wmj.Log.Tag.UI);
                    }
                }
            }
            catch (System.Exception ex)
            {
                wmj.Log.W($"[UIFactory] 添加字体回退失败: {ex.Message}", wmj.Log.Tag.UI);
            }
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

            // 确保场景中存在正确配置的 EventSystem（UI 交互必需）
            EnsureEventSystem();

            return canvas;
        }

        // ─── EventSystem 可靠输入适配 ───

        private const string DynamicEventSystemName = "[EventSystem-Dynamic]";

        /// <summary>
        /// 确保存在正确配置的 EventSystem — 全平台兼容
        /// 核心策略：disable → configure → enable
        /// 确保 InputSystemUIInputModule.OnEnable 在拥有正确 actionsAsset 的状态下运行
        /// </summary>
        public static void EnsureEventSystem()
        {
            // 查找所有 EventSystem（包括被禁用的和待销毁的）
            var allEventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            UnityEngine.EventSystems.EventSystem dynamicES = null;
            foreach (var es in allEventSystems)
            {
                if (es.gameObject.name == DynamicEventSystemName)
                {
                    dynamicES = es;
                    break;
                }
            }

            // 如果已有动态 EventSystem，验证并复用
            if (dynamicES != null)
            {
                ValidateAndRepairModule(dynamicES);
                // 确保它是 current
                if (UnityEngine.EventSystems.EventSystem.current != dynamicES)
                {
                    dynamicES.enabled = false;
                    dynamicES.enabled = true;
                }
                return;
            }

            // 检查场景中的 EventSystem
            var existing = UnityEngine.EventSystems.EventSystem.current;
            if (existing != null)
            {
                var sceneModule = existing.GetComponent<InputSystemUIInputModule>();
                if (sceneModule != null && sceneModule.isActiveAndEnabled && sceneModule.actionsAsset != null)
                {
                    try
                    {
                        var uiMap = sceneModule.actionsAsset.FindActionMap("UI");
                        if (uiMap != null && uiMap.enabled
                            && uiMap.FindAction("Point") != null
                            && uiMap.FindAction("Click") != null)
                        {
                            Debug.Log("[UIFactory] 场景 EventSystem 正常，复用");
                            return;
                        }
                    }
                    catch { }
                }

                // 场景 EventSystem 异常，禁用它而不是销毁（避免时序问题）
                Debug.LogWarning("[UIFactory] 场景 EventSystem 异常，禁用并创建新的");
                existing.enabled = false;
            }

            CreateDynamicEventSystem();
        }

        /// <summary>
        /// 创建全新的动态 EventSystem
        /// 关键流程：AddComponent → 立即 disable → 配置 actionsAsset + 绑定动作 → enable
        /// 解决 AddComponent 时 OnEnable 空跑导致鼠标事件不处理的问题
        /// </summary>
        private static void CreateDynamicEventSystem()
        {
            Debug.Log("[UIFactory] ═══ 创建动态 EventSystem ═══");

            var esGo = new GameObject(DynamicEventSystemName);
            Object.DontDestroyOnLoad(esGo);

            var eventSystem = esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.sendNavigationEvents = true;
            eventSystem.pixelDragThreshold = 5;

            // ① AddComponent 会立即触发 OnEnable（此时 actionsAsset 为空，回调注册为空）
            var uiModule = esGo.AddComponent<InputSystemUIInputModule>();

            // ② 立即禁用 — 触发 OnDisable，清理空白状态的回调
            uiModule.enabled = false;

            // ③ 在禁用状态下完整配置 actionsAsset 和各个动作引用
            var actionsAsset = FindInputActionAsset();
            if (actionsAsset != null)
            {
                uiModule.actionsAsset = actionsAsset;
                Debug.Log($"[UIFactory] actionsAsset = {actionsAsset.name}");

                // 显式绑定各个 UI 动作引用，防止 auto-discovery 失效
                SetModuleActionReferences(uiModule, actionsAsset);

                // 预先启用 UI ActionMap，确保 OnEnable 时所有动作已就绪
                var uiMap = actionsAsset.FindActionMap("UI");
                if (uiMap != null)
                {
                    uiMap.Enable();
                    Debug.Log($"[UIFactory] UI ActionMap 预启用完成 (actions={uiMap.actions.Count})");
                }
            }
            else
            {
                Debug.LogError("[UIFactory] 未找到 InputActionAsset — UI 将无法交互!");
            }

            // ④ 重新启用 — OnEnable 使用完整配置初始化所有回调和事件处理管线
            uiModule.enabled = true;

            // ⑤ 输出诊断信息验证初始化结果
            LogModuleDiagnostics(uiModule, "创建完成");

            // ⑥ 添加运行时健康监护组件
            esGo.AddComponent<EventSystemGuard>();

            Debug.Log($"[UIFactory] EventSystem.current = " +
                $"{UnityEngine.EventSystems.EventSystem.current?.name ?? "NULL"}");
        }

        /// <summary>查找项目中的 InputActionAsset</summary>
        private static UnityEngine.InputSystem.InputActionAsset FindInputActionAsset()
        {
            // 优先使用项目全局 InputActions（Player Settings 中配置）
            try
            {
                var asset = UnityEngine.InputSystem.InputSystem.actions;
                if (asset != null)
                {
                    Debug.Log($"[UIFactory] 从 InputSystem.actions 获取到资产: {asset.name}");
                    return asset;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UIFactory] InputSystem.actions 访问异常: {ex.Message}");
            }

            // 后备 1：搜索已加载的 InputActionAsset
            try
            {
                var found = Resources.FindObjectsOfTypeAll<UnityEngine.InputSystem.InputActionAsset>();
                if (found != null && found.Length > 0)
                {
                    foreach (var a in found)
                    {
                        if (a.FindActionMap("UI") != null)
                        {
                            Debug.Log($"[UIFactory] 从已加载资产中找到含 UI ActionMap 的: {a.name}");
                            return a;
                        }
                    }
                    Debug.Log($"[UIFactory] 使用第一个 InputActionAsset: {found[0].name}");
                    return found[0];
                }
            }
            catch { }

            // 后备 2：从 Resources 加载
            try
            {
                var loaded = Resources.Load<UnityEngine.InputSystem.InputActionAsset>("InputSystem_Actions");
                if (loaded != null)
                {
                    Debug.Log($"[UIFactory] 从 Resources 加载到: {loaded.name}");
                    return loaded;
                }
            }
            catch { }

            Debug.LogWarning("[UIFactory] 未找到任何 InputActionAsset");
            return null;
        }

        /// <summary>
        /// 在模块禁用状态下显式设置各个 UI 动作引用
        /// 使用 InputActionReference.Create 确保每个动作正确映射到模块属性
        /// </summary>
        private static void SetModuleActionReferences(InputSystemUIInputModule module,
            UnityEngine.InputSystem.InputActionAsset asset)
        {
            try
            {
                var uiMap = asset.FindActionMap("UI");
                if (uiMap == null)
                {
                    Debug.LogWarning("[UIFactory] InputActions 中未找到 UI ActionMap");
                    return;
                }

                System.Action<string, System.Action<InputActionReference>> bind =
                    (actionName, setter) =>
                    {
                        var action = uiMap.FindAction(actionName);
                        if (action != null) setter(InputActionReference.Create(action));
                    };

                bind("Point", r => module.point = r);
                bind("Click", r => module.leftClick = r);
                bind("ScrollWheel", r => module.scrollWheel = r);
                bind("Navigate", r => module.move = r);
                bind("Submit", r => module.submit = r);
                bind("Cancel", r => module.cancel = r);
                bind("RightClick", r => module.rightClick = r);
                bind("MiddleClick", r => module.middleClick = r);

                Debug.Log("[UIFactory] UI Action 引用绑定完成");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UIFactory] 绑定 UI Actions 异常: {ex.Message}");
            }
        }

        /// <summary>验证并修复已存在的动态 EventSystem</summary>
        private static void ValidateAndRepairModule(UnityEngine.EventSystems.EventSystem es)
        {
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                Debug.LogWarning("[UIFactory] 动态 EventSystem 缺少 InputModule，重建");
                Object.Destroy(es.gameObject);
                CreateDynamicEventSystem();
                return;
            }

            // 模块异常 — 执行 disable→configure→enable 修复
            if (!module.isActiveAndEnabled || module.actionsAsset == null)
            {
                Debug.LogWarning("[UIFactory] InputModule 异常，执行修复流程");
                module.enabled = false;

                if (module.actionsAsset == null)
                {
                    var asset = FindInputActionAsset();
                    if (asset != null)
                    {
                        module.actionsAsset = asset;
                        SetModuleActionReferences(module, asset);
                    }
                }

                if (module.actionsAsset != null)
                {
                    var uiMap = module.actionsAsset.FindActionMap("UI");
                    if (uiMap != null) uiMap.Enable();
                }

                module.enabled = true;
                LogModuleDiagnostics(module, "修复完成");
                return;
            }

            // 检查 UI ActionMap 是否启用
            try
            {
                var uiMap = module.actionsAsset.FindActionMap("UI");
                if (uiMap != null && !uiMap.enabled)
                {
                    uiMap.Enable();
                    Debug.Log("[UIFactory] 已重新启用 UI ActionMap");
                }
            }
            catch { }
        }

        /// <summary>输出 InputModule 详细诊断信息</summary>
        private static void LogModuleDiagnostics(InputSystemUIInputModule module, string context)
        {
            if (module == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[UIFactory] ─── InputModule 诊断 ({context}) ───");
            sb.AppendLine($"  isActiveAndEnabled = {module.isActiveAndEnabled}");
            sb.AppendLine($"  actionsAsset = {module.actionsAsset?.name ?? "NULL"}");
            sb.Append($"  point={module.point != null} leftClick={module.leftClick != null}");
            sb.AppendLine($" scrollWheel={module.scrollWheel != null} move={module.move != null}");

            if (module.actionsAsset != null)
            {
                try
                {
                    var uiMap = module.actionsAsset.FindActionMap("UI");
                    if (uiMap != null)
                    {
                        sb.AppendLine($"  UI ActionMap: enabled={uiMap.enabled}, actions={uiMap.actions.Count}");
                        foreach (var action in uiMap.actions)
                            sb.AppendLine($"    {action.name}: enabled={action.enabled}, bindings={action.bindings.Count}");
                    }
                }
                catch { }
            }

            Debug.Log(sb.ToString());
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

        // ─── TMP InputField ───

        /// <summary>创建 TMP 文本输入框，用于设置面板中的参数编辑</summary>
        public static TMP_InputField CreateInputField(Transform parent, string name,
            string initialText, int fontSize = 20, TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // 背景
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.14f, 0.90f);
            ApplyRoundedCorners(bg, 32, 8);
            bg.raycastTarget = true;

            // 文本区域
            var textAreaGo = new GameObject("TextArea");
            textAreaGo.transform.SetParent(go.transform, false);
            var textAreaRt = textAreaGo.AddComponent<RectTransform>();
            textAreaRt.anchorMin = new Vector2(0.02f, 0f);
            textAreaRt.anchorMax = new Vector2(0.98f, 1f);
            textAreaRt.offsetMin = Vector2.zero;
            textAreaRt.offsetMax = Vector2.zero;
            textAreaGo.AddComponent<RectMask2D>();

            // 文本内容
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = fontSize;
            textTmp.color = UIColors.White;
            textTmp.alignment = TextAlignmentOptions.Left;
            textTmp.textWrappingMode = TextWrappingModes.NoWrap;
            textTmp.overflowMode = TextOverflowModes.ScrollRect;
            textTmp.richText = false;
            if (!_fontSearched) PreloadFont();
            if (_cachedFont != null) textTmp.font = _cachedFont;
            var textRt = textGo.GetComponent<RectTransform>();
            SetFullStretch(textRt);

            // 占位符文本
            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(textAreaGo.transform, false);
            var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderTmp.text = "输入...";
            placeholderTmp.fontSize = fontSize;
            placeholderTmp.color = new Color(0.5f, 0.5f, 0.6f, 0.5f);
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.alignment = TextAlignmentOptions.Left;
            placeholderTmp.textWrappingMode = TextWrappingModes.NoWrap;
            if (_cachedFont != null) placeholderTmp.font = _cachedFont;
            var phRt = placeholderGo.GetComponent<RectTransform>();
            SetFullStretch(phRt);

            // InputField 组件
            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textViewport = textAreaRt;
            inputField.textComponent = textTmp;
            inputField.placeholder = placeholderTmp;
            inputField.text = initialText ?? "";
            inputField.contentType = contentType;
            inputField.pointSize = fontSize;
            inputField.caretColor = UIColors.WithAlpha(UIColors.BrightBlue, 0.9f);
            inputField.selectionColor = UIColors.WithAlpha(UIColors.BrightBlue, 0.3f);

            // 聚焦时高亮边框
            var colors = inputField.colors;
            colors.normalColor = new Color(0.06f, 0.07f, 0.14f, 0.90f);
            colors.highlightedColor = new Color(0.10f, 0.12f, 0.22f, 0.95f);
            colors.selectedColor = new Color(0.10f, 0.14f, 0.28f, 0.95f);
            colors.fadeDuration = 0.1f;
            inputField.colors = colors;

            return inputField;
        }
    }
}
