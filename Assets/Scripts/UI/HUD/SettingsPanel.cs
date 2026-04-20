using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UI.Core;
using UI.RobotSelection;
using System.Collections;
using System.Collections.Generic;

namespace UI.HUD
{
    /// <summary>
    /// 设置面板（全键盘交互版）
    /// 完全通过键盘操作，不依赖鼠标/EventSystem，兼容 Linux 和 Windows
    ///
    /// 快捷键总览：
    ///   F10         打开/关闭设置面板
    ///   Esc         关闭面板 / 取消编辑 / 返回侧边栏
    ///   1~9,0,-,=   直接选择侧边栏页面 1~12
    ///   W/↑  S/↓    侧边栏/内容行 上下导航
    ///   A/←  D/→    滑块 减/增  |  整数 -1/+1
    ///   Space/Enter  切换开关  |  开始/确认文字编辑
    ///   R           重置当前行到默认值
    ///   Tab         侧边栏 ↔ 内容区 焦点切换
    ///   Ctrl+S      保存并预览
    ///   Ctrl+R      全部重置
    ///   F1          重新选择兵种
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        // ─── 焦点模式 ───
        private enum FocusMode { Sidebar, Content, TextEditing }
        private FocusMode focusMode = FocusMode.Sidebar;

        // ─── 行类型 ───
        private enum RowType { Slider, Toggle, TextInput, IntInput, KeyBinding, Button }

        // ─── 行数据 ───
        private class RowData
        {
            public RowType type;
            public GameObject go;
            public Image rowBg;
            public TextMeshProUGUI hintText;
            // Slider
            public Slider slider;
            public float defaultValue, sliderMin, sliderMax;
            public System.Action<float> onSliderChange;
            public TextMeshProUGUI valueText;
            public string fmt, unit;
            // Toggle
            public bool toggleValue;
            public System.Action<bool> onToggleChange;
            public Image toggleBg;
            public TextMeshProUGUI toggleLabel;
            // TextInput
            public string textValue;
            public System.Action<string> onTextChange;
            public TextMeshProUGUI textDisplay;
            // IntInput
            public int intValue, intMin, intMax, intDefault;
            public System.Action<int> onIntChange;
            public TextMeshProUGUI intDisplay;
            public TextMeshProUGUI rangeText;
            // KeyBinding
            public AmmoKeyBinding binding;
            public int bindingIndex;
            public TextMeshProUGUI keyDisplay;
            // Button
            public System.Action onButtonClick;
        }

        // ─── UI 状态 ───
        private Canvas gearCanvas;
        private Canvas settingsCanvas;
        private GameObject panelRoot;
        private bool isOpen;

        // ─── 侧边栏 ───
        private readonly List<SidebarItem> sidebarItems = new List<SidebarItem>();
        private int activeSidebarIndex = -1;
        private RectTransform contentArea;

        // ─── 内容行 ───
        private readonly List<RowData> rows = new List<RowData>();
        private int focusedRowIndex = -1;
        private int rowBuildIndex;

        // ─── 文字编辑状态 ───
        private string editBuffer = "";
        private string editOriginal = "";
        private float cursorBlinkTimer;
        private bool cursorVisible;

        // ─── 布局编辑器 ───
        private RectTransform minimapRoot;
        private readonly List<LayoutHandle> layoutHandles = new List<LayoutHandle>();
        private int layoutFocusIndex = -1;

        // ─── 实时预览 ───
        private Coroutine previewCoroutine;
        private const float PREVIEW_DELAY = 0.6f;

        // ─── 快捷键监听 ───
        private bool isListeningForKey;

        // ─── 帮助栏 ───
        private TextMeshProUGUI helpBarText;

        // ─── 按键重复 ───
        private float keyRepeatTimer;
        private Key lastHeldKey = Key.None;
        private const float KEY_REPEAT_INITIAL = 0.35f;
        private const float KEY_REPEAT_INTERVAL = 0.06f;

        // ─── 滚动 ───
        private ScrollRect currentScrollRect;

        // ─── 数据结构 ───
        private class SidebarItem
        {
            public Image bg;
            public Image accent;
            public TextMeshProUGUI label;
            public TextMeshProUGUI keyHint;
            public string pageId;
        }

        private class LayoutHandle
        {
            public string id;
            public RectTransform rt;
            public UIElementLayout layout;
            public Image bg;
        }

        // ─── 颜色常量 ───
        private static readonly Color PanelBg       = new Color(0.04f, 0.05f, 0.10f, 0.96f);
        private static readonly Color SidebarBg      = new Color(0.03f, 0.04f, 0.08f, 0.98f);
        private static readonly Color ContentBg      = new Color(0.05f, 0.06f, 0.12f, 0.90f);
        private static readonly Color TitleBarBg     = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color RowEven        = new Color(0.07f, 0.08f, 0.14f, 0.60f);
        private static readonly Color RowOdd         = new Color(0.05f, 0.06f, 0.11f, 0.45f);
        private static readonly Color RowFocused     = new Color(0.15f, 0.25f, 0.45f, 0.80f);
        private static readonly Color SliderTrack    = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        private static readonly Color SliderFill     = new Color(0.22f, 0.55f, 0.95f, 0.70f);
        private static readonly Color SliderHandle   = new Color(0.85f, 0.90f, 0.98f, 1f);
        private static readonly Color Accent         = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color SidebarActive  = new Color(0.12f, 0.18f, 0.32f, 0.85f);
        private static readonly Color BtnSave        = new Color(0.16f, 0.50f, 0.88f, 0.80f);
        private static readonly Color BtnReset       = new Color(0.80f, 0.25f, 0.20f, 0.65f);
        private static readonly Color BtnReselect    = new Color(0.75f, 0.60f, 0.18f, 0.70f);
        private static readonly Color BtnClose       = new Color(0.35f, 0.35f, 0.40f, 0.60f);
        private static readonly Color GearNormal     = new Color(0.20f, 0.25f, 0.35f, 0.70f);
        private static readonly Color MinimapBg      = new Color(0.06f, 0.07f, 0.12f, 0.80f);
        private static readonly Color HintColor      = new Color(0.55f, 0.65f, 0.78f, 0.55f);
        private static readonly Color EditingBg      = new Color(0.10f, 0.14f, 0.28f, 0.95f);
        private static readonly Color LayoutSelected = new Color(0.40f, 0.75f, 1.0f, 0.60f);

        // ─── 菜单定义 ───
        private static readonly string[] MenuIds = {
            "matchinfo", "notify", "aim", "hit", "crosshair", "health",
            "buff", "font", "shortcut", "economy", "network", "layout"
        };
        private static readonly string[] MenuLabels = {
            "对局信息", "通知设置", "开镜设置", "受击提示", "准星设置", "血条设置",
            "BUFF设置", "字体大小", "快捷键", "经济管控", "网络配置", "UI 布局"
        };
        private static readonly string[] MenuKeyHints = {
            "1", "2", "3", "4", "5", "6",
            "7", "8", "9", "0", "-", "="
        };

        // ═══════════════════ 生命周期 ═══════════════════

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildGearButton();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f10Key.wasPressedThisFrame)
            {
                ToggleSettings();
                return;
            }

            if (!isOpen) return;

            // 快捷键监听模式（改键）
            if (isListeningForKey)
            {
                HandleKeyListeningInput(kb);
                return;
            }

            // 文字编辑模式
            if (focusMode == FocusMode.TextEditing)
            {
                HandleTextEditingInput(kb);
                return;
            }

            // Esc 处理
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (focusMode == FocusMode.Content)
                {
                    SetFocusMode(FocusMode.Sidebar);
                    return;
                }
                else
                {
                    HidePanel();
                    return;
                }
            }

            // Ctrl+S 保存
            if ((kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.sKey.wasPressedThisFrame)
            {
                OnSaveClicked();
                return;
            }

            // Ctrl+R 重置
            if ((kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.rKey.wasPressedThisFrame)
            {
                OnResetAllClicked();
                return;
            }

            // F1 重新选择兵种
            if (kb.f1Key.wasPressedThisFrame) { OnReselectClicked(); return; }

            // Tab 切换焦点区域
            if (kb.tabKey.wasPressedThisFrame)
            {
                if (focusMode == FocusMode.Sidebar && rows.Count > 0)
                    SetFocusMode(FocusMode.Content);
                else
                    SetFocusMode(FocusMode.Sidebar);
                return;
            }

            // 数字键直接选页面（无修饰键时）
            if (!kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed &&
                !kb.leftShiftKey.isPressed && !kb.rightShiftKey.isPressed)
            {
                if (focusMode == FocusMode.Sidebar)
                {
                    int pageIdx = GetPageIndexFromKey(kb);
                    if (pageIdx >= 0 && pageIdx < MenuIds.Length)
                    {
                        SelectSidebarByIndex(pageIdx);
                        SetFocusMode(FocusMode.Content);
                        return;
                    }
                }
            }

            if (focusMode == FocusMode.Sidebar)
                HandleSidebarInput(kb);
            else if (focusMode == FocusMode.Content)
                HandleContentInput(kb);
        }

        // ═══════════════════ 页面快捷键映射 ═══════════════════

        private int GetPageIndexFromKey(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame) return 0;
            if (kb.digit2Key.wasPressedThisFrame) return 1;
            if (kb.digit3Key.wasPressedThisFrame) return 2;
            if (kb.digit4Key.wasPressedThisFrame) return 3;
            if (kb.digit5Key.wasPressedThisFrame) return 4;
            if (kb.digit6Key.wasPressedThisFrame) return 5;
            if (kb.digit7Key.wasPressedThisFrame) return 6;
            if (kb.digit8Key.wasPressedThisFrame) return 7;
            if (kb.digit9Key.wasPressedThisFrame) return 8;
            if (kb.digit0Key.wasPressedThisFrame) return 9;
            if (kb.minusKey.wasPressedThisFrame) return 10;
            if (kb.equalsKey.wasPressedThisFrame) return 11;
            return -1;
        }

        // ═══════════════════ 侧边栏键盘处理 ═══════════════════

        private void HandleSidebarInput(Keyboard kb)
        {
            if (sidebarItems.Count == 0) return;

            bool up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            bool down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                      || kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;

            if (up)
                SelectSidebarByIndex((activeSidebarIndex - 1 + sidebarItems.Count) % sidebarItems.Count);
            else if (down)
                SelectSidebarByIndex((activeSidebarIndex + 1) % sidebarItems.Count);
            else if (enter && rows.Count > 0)
                SetFocusMode(FocusMode.Content);
        }

        // ═══════════════════ 内容区键盘处理 ═══════════════════

        private void HandleContentInput(Keyboard kb)
        {
            if (rows.Count == 0) return;

            // 布局页面特殊处理
            if (activeSidebarIndex >= 0 && activeSidebarIndex < sidebarItems.Count
                && sidebarItems[activeSidebarIndex].pageId == "layout")
            {
                HandleLayoutInput(kb);
                return;
            }

            // 上下导航
            bool up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            bool down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;

            if (up) { NavigateRow(-1); return; }
            if (down) { NavigateRow(1); return; }

            if (focusedRowIndex < 0 || focusedRowIndex >= rows.Count) return;
            var row = rows[focusedRowIndex];

            switch (row.type)
            {
                case RowType.Slider:
                    HandleSliderRowInput(kb, row);
                    break;
                case RowType.Toggle:
                    HandleToggleRowInput(kb, row);
                    break;
                case RowType.IntInput:
                    HandleIntInputRowInput(kb, row);
                    break;
                case RowType.TextInput:
                    HandleTextInputRowInput(kb, row);
                    break;
                case RowType.KeyBinding:
                    HandleKeyBindingRowInput(kb, row);
                    break;
                case RowType.Button:
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                        || kb.spaceKey.wasPressedThisFrame)
                        row.onButtonClick?.Invoke();
                    break;
            }
        }

        private void HandleSliderRowInput(Keyboard kb, RowData row)
        {
            if (row.slider == null) return;

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            float range = row.sliderMax - row.sliderMin;
            float step = shift ? range * 0.01f : range * 0.05f;

            bool left = CheckKeyWithRepeat(kb, Key.A, Key.LeftArrow);
            bool right = CheckKeyWithRepeat(kb, Key.D, Key.RightArrow);

            if (left)
            {
                row.slider.value = Mathf.Clamp(row.slider.value - step, row.sliderMin, row.sliderMax);
                row.onSliderChange?.Invoke(row.slider.value);
                UpdateSliderValueText(row);
                ScheduleLivePreview();
            }
            else if (right)
            {
                row.slider.value = Mathf.Clamp(row.slider.value + step, row.sliderMin, row.sliderMax);
                row.onSliderChange?.Invoke(row.slider.value);
                UpdateSliderValueText(row);
                ScheduleLivePreview();
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                row.slider.value = row.defaultValue;
                row.onSliderChange?.Invoke(row.defaultValue);
                UpdateSliderValueText(row);
                ScheduleLivePreview();
            }

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                editBuffer = row.slider.value.ToString(row.fmt);
                editOriginal = editBuffer;
                SetFocusMode(FocusMode.TextEditing);
            }
        }

        private void HandleToggleRowInput(Keyboard kb, RowData row)
        {
            bool toggle = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame
                       || kb.numpadEnterKey.wasPressedThisFrame
                       || kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
                       || kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;

            if (toggle)
            {
                row.toggleValue = !row.toggleValue;
                UpdateToggleVisual(row.toggleBg, row.toggleLabel, row.toggleValue);
                row.onToggleChange?.Invoke(row.toggleValue);
                ScheduleLivePreview();
            }
        }

        private void HandleIntInputRowInput(Keyboard kb, RowData row)
        {
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            int step = shift ? 1 : Mathf.Max(1, (row.intMax - row.intMin) / 20);

            bool left = CheckKeyWithRepeat(kb, Key.A, Key.LeftArrow);
            bool right = CheckKeyWithRepeat(kb, Key.D, Key.RightArrow);

            if (left)
            {
                row.intValue = Mathf.Clamp(row.intValue - step, row.intMin, row.intMax);
                row.onIntChange?.Invoke(row.intValue);
                if (row.intDisplay) row.intDisplay.text = row.intValue.ToString();
                ScheduleLivePreview();
            }
            else if (right)
            {
                row.intValue = Mathf.Clamp(row.intValue + step, row.intMin, row.intMax);
                row.onIntChange?.Invoke(row.intValue);
                if (row.intDisplay) row.intDisplay.text = row.intValue.ToString();
                ScheduleLivePreview();
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                row.intValue = row.intDefault;
                row.onIntChange?.Invoke(row.intValue);
                if (row.intDisplay) row.intDisplay.text = row.intValue.ToString();
                ScheduleLivePreview();
            }

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                editBuffer = row.intValue.ToString();
                editOriginal = editBuffer;
                SetFocusMode(FocusMode.TextEditing);
            }
        }

        private void HandleTextInputRowInput(Keyboard kb, RowData row)
        {
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                editBuffer = row.textValue ?? "";
                editOriginal = editBuffer;
                SetFocusMode(FocusMode.TextEditing);
            }
        }

        private void HandleKeyBindingRowInput(Keyboard kb, RowData row)
        {
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                || kb.spaceKey.wasPressedThisFrame)
            {
                if (!isListeningForKey)
                {
                    isListeningForKey = true;
                    if (row.keyDisplay) { row.keyDisplay.text = "按下新键..."; row.keyDisplay.color = UIColors.Orange; }
                }
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                var defaults = HUDSettings.DefaultAmmoKeyBindings();
                if (row.bindingIndex < defaults.Count)
                {
                    row.binding.keyCode = defaults[row.bindingIndex].keyCode;
                    if (row.keyDisplay)
                    {
                        row.keyDisplay.text = FormatKeyName(((KeyCode)row.binding.keyCode).ToString());
                        row.keyDisplay.color = UIColors.White;
                    }
                }
            }
        }

        // ═══════════════════ 按键监听处理 ═══════════════════

        private void HandleKeyListeningInput(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                isListeningForKey = false;
                if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
                {
                    var row = rows[focusedRowIndex];
                    if (row.keyDisplay)
                    {
                        row.keyDisplay.text = FormatKeyName(((KeyCode)row.binding.keyCode).ToString());
                        row.keyDisplay.color = UIColors.White;
                    }
                }
                return;
            }

            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || kc == KeyCode.Escape) continue;
                try
                {
                    if (Input.GetKeyDown(kc))
                    {
                        isListeningForKey = false;
                        if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
                        {
                            var row = rows[focusedRowIndex];
                            if (row.type == RowType.KeyBinding)
                            {
                                var s = UILayoutManager.Settings;
                                bool conflict = false;
                                if (s.ammoKeyBindings != null)
                                {
                                    for (int i = 0; i < s.ammoKeyBindings.Count; i++)
                                    {
                                        if (i != row.bindingIndex && s.ammoKeyBindings[i].keyCode == (int)kc)
                                        {
                                            conflict = true;
                                            break;
                                        }
                                    }
                                }

                                if (conflict)
                                {
                                    if (row.keyDisplay)
                                    {
                                        row.keyDisplay.text = FormatKeyName(kc.ToString()) + " (冲突!)";
                                        row.keyDisplay.color = Color.red;
                                    }
                                    StartCoroutine(ResetKeyLabelAfterDelay(row));
                                }
                                else
                                {
                                    row.binding.keyCode = (int)kc;
                                    if (row.keyDisplay)
                                    {
                                        row.keyDisplay.text = FormatKeyName(kc.ToString());
                                        row.keyDisplay.color = UIColors.White;
                                    }
                                }
                            }
                        }
                        return;
                    }
                }
                catch { continue; }
            }
        }

        // ═══════════════════ 文字编辑处理 ═══════════════════

        private void HandleTextEditingInput(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                editBuffer = editOriginal;
                FinishTextEditing(false);
                return;
            }

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                FinishTextEditing(true);
                return;
            }

            if (CheckKeyWithRepeat(kb, Key.Backspace, Key.None))
            {
                if (editBuffer.Length > 0)
                    editBuffer = editBuffer.Substring(0, editBuffer.Length - 1);
            }

            if (kb.deleteKey.wasPressedThisFrame)
                editBuffer = "";

            // 字符输入
            AppendCharFromKey(kb, Key.A, 'a', 'A'); AppendCharFromKey(kb, Key.B, 'b', 'B');
            AppendCharFromKey(kb, Key.C, 'c', 'C'); AppendCharFromKey(kb, Key.D, 'd', 'D');
            AppendCharFromKey(kb, Key.E, 'e', 'E'); AppendCharFromKey(kb, Key.F, 'f', 'F');
            AppendCharFromKey(kb, Key.G, 'g', 'G'); AppendCharFromKey(kb, Key.H, 'h', 'H');
            AppendCharFromKey(kb, Key.I, 'i', 'I'); AppendCharFromKey(kb, Key.J, 'j', 'J');
            AppendCharFromKey(kb, Key.K, 'k', 'K'); AppendCharFromKey(kb, Key.L, 'l', 'L');
            AppendCharFromKey(kb, Key.M, 'm', 'M'); AppendCharFromKey(kb, Key.N, 'n', 'N');
            AppendCharFromKey(kb, Key.O, 'o', 'O'); AppendCharFromKey(kb, Key.P, 'p', 'P');
            AppendCharFromKey(kb, Key.Q, 'q', 'Q'); AppendCharFromKey(kb, Key.R, 'r', 'R');
            AppendCharFromKey(kb, Key.S, 's', 'S'); AppendCharFromKey(kb, Key.T, 't', 'T');
            AppendCharFromKey(kb, Key.U, 'u', 'U'); AppendCharFromKey(kb, Key.V, 'v', 'V');
            AppendCharFromKey(kb, Key.W, 'w', 'W'); AppendCharFromKey(kb, Key.X, 'x', 'X');
            AppendCharFromKey(kb, Key.Y, 'y', 'Y'); AppendCharFromKey(kb, Key.Z, 'z', 'Z');
            AppendCharFromKey(kb, Key.Digit0, '0', ')');
            AppendCharFromKey(kb, Key.Digit1, '1', '!');
            AppendCharFromKey(kb, Key.Digit2, '2', '@');
            AppendCharFromKey(kb, Key.Digit3, '3', '#');
            AppendCharFromKey(kb, Key.Digit4, '4', '$');
            AppendCharFromKey(kb, Key.Digit5, '5', '%');
            AppendCharFromKey(kb, Key.Digit6, '6', '^');
            AppendCharFromKey(kb, Key.Digit7, '7', '&');
            AppendCharFromKey(kb, Key.Digit8, '8', '*');
            AppendCharFromKey(kb, Key.Digit9, '9', '(');
            AppendCharFromKey(kb, Key.Period, '.', '>');
            AppendCharFromKey(kb, Key.Minus, '-', '_');
            AppendCharFromKey(kb, Key.Slash, '/', '?');
            AppendCharFromKey(kb, Key.Semicolon, ';', ':');
            AppendCharFromKey(kb, Key.Quote, '\'', '"');
            AppendCharFromKey(kb, Key.Comma, ',', '<');
            AppendCharFromKey(kb, Key.Space, ' ', ' ');
            AppendCharFromKey(kb, Key.Numpad0, '0', '0');
            AppendCharFromKey(kb, Key.Numpad1, '1', '1');
            AppendCharFromKey(kb, Key.Numpad2, '2', '2');
            AppendCharFromKey(kb, Key.Numpad3, '3', '3');
            AppendCharFromKey(kb, Key.Numpad4, '4', '4');
            AppendCharFromKey(kb, Key.Numpad5, '5', '5');
            AppendCharFromKey(kb, Key.Numpad6, '6', '6');
            AppendCharFromKey(kb, Key.Numpad7, '7', '7');
            AppendCharFromKey(kb, Key.Numpad8, '8', '8');
            AppendCharFromKey(kb, Key.Numpad9, '9', '9');
            AppendCharFromKey(kb, Key.NumpadPeriod, '.', '.');

            UpdateTextEditDisplay();
            cursorBlinkTimer += Time.unscaledDeltaTime;
            if (cursorBlinkTimer > 0.5f) { cursorBlinkTimer = 0; cursorVisible = !cursorVisible; }
        }

        private void AppendCharFromKey(Keyboard kb, Key key, char lower, char upper)
        {
            if (kb[key].wasPressedThisFrame)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                editBuffer += shift ? upper : lower;
            }
        }

        private void UpdateTextEditDisplay()
        {
            if (focusedRowIndex < 0 || focusedRowIndex >= rows.Count) return;
            var row = rows[focusedRowIndex];
            string displayText = editBuffer + (cursorVisible ? "|" : " ");

            switch (row.type)
            {
                case RowType.TextInput:
                    if (row.textDisplay) row.textDisplay.text = displayText;
                    break;
                case RowType.IntInput:
                    if (row.intDisplay) row.intDisplay.text = displayText;
                    break;
                case RowType.Slider:
                    if (row.valueText) row.valueText.text = displayText;
                    break;
            }
        }

        private void FinishTextEditing(bool confirm)
        {
            if (focusedRowIndex < 0 || focusedRowIndex >= rows.Count)
            {
                SetFocusMode(FocusMode.Content);
                return;
            }

            var row = rows[focusedRowIndex];

            if (confirm)
            {
                switch (row.type)
                {
                    case RowType.TextInput:
                        row.textValue = editBuffer;
                        row.onTextChange?.Invoke(editBuffer);
                        if (row.textDisplay) row.textDisplay.text = editBuffer;
                        break;
                    case RowType.IntInput:
                        if (int.TryParse(editBuffer, out int iv))
                        {
                            iv = Mathf.Clamp(iv, row.intMin, row.intMax);
                            row.intValue = iv;
                            row.onIntChange?.Invoke(iv);
                            if (row.intDisplay) row.intDisplay.text = iv.ToString();
                        }
                        else
                        {
                            if (row.intDisplay) row.intDisplay.text = row.intValue.ToString();
                        }
                        break;
                    case RowType.Slider:
                        if (float.TryParse(editBuffer, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float fv))
                        {
                            fv = Mathf.Clamp(fv, row.sliderMin, row.sliderMax);
                            row.slider.value = fv;
                            row.onSliderChange?.Invoke(fv);
                            UpdateSliderValueText(row);
                        }
                        else
                        {
                            UpdateSliderValueText(row);
                        }
                        break;
                }
                ScheduleLivePreview();
            }
            else
            {
                switch (row.type)
                {
                    case RowType.TextInput:
                        if (row.textDisplay) row.textDisplay.text = row.textValue ?? "";
                        break;
                    case RowType.IntInput:
                        if (row.intDisplay) row.intDisplay.text = row.intValue.ToString();
                        break;
                    case RowType.Slider:
                        UpdateSliderValueText(row);
                        break;
                }
            }

            SetFocusMode(FocusMode.Content);
        }

        // ═══════════════════ 按键重复 ═══════════════════

        private bool CheckKeyWithRepeat(Keyboard kb, Key key1, Key key2)
        {
            bool pressed1 = key1 != Key.None && kb[key1].wasPressedThisFrame;
            bool pressed2 = key2 != Key.None && kb[key2].wasPressedThisFrame;
            bool held1 = key1 != Key.None && kb[key1].isPressed;
            bool held2 = key2 != Key.None && kb[key2].isPressed;

            if (pressed1 || pressed2)
            {
                lastHeldKey = pressed1 ? key1 : key2;
                keyRepeatTimer = KEY_REPEAT_INITIAL;
                return true;
            }

            if ((held1 || held2) && (lastHeldKey == key1 || lastHeldKey == key2))
            {
                keyRepeatTimer -= Time.unscaledDeltaTime;
                if (keyRepeatTimer <= 0)
                {
                    keyRepeatTimer = KEY_REPEAT_INTERVAL;
                    return true;
                }
            }
            else if (lastHeldKey == key1 || lastHeldKey == key2)
            {
                lastHeldKey = Key.None;
            }

            return false;
        }

        // ═══════════════════ 行导航 ═══════════════════

        private void NavigateRow(int delta)
        {
            if (rows.Count == 0) return;

            int newIdx = focusedRowIndex + delta;
            if (newIdx < 0) newIdx = 0;
            if (newIdx >= rows.Count) newIdx = rows.Count - 1;

            SetFocusedRow(newIdx);
        }

        private void SetFocusedRow(int index)
        {
            if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
            {
                var old = rows[focusedRowIndex];
                if (old.rowBg)
                    old.rowBg.color = (focusedRowIndex % 2 == 0) ? RowEven : RowOdd;
            }

            focusedRowIndex = index;

            if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
            {
                var cur = rows[focusedRowIndex];
                if (cur.rowBg) cur.rowBg.color = RowFocused;
                ScrollToRow(cur.go);
            }

            UpdateHelpBar();
        }

        private void ScrollToRow(GameObject rowGo)
        {
            if (currentScrollRect == null || rowGo == null) return;

            var rowRt = rowGo.GetComponent<RectTransform>();
            var contentRt = currentScrollRect.content;
            var viewportRt = currentScrollRect.viewport;
            if (rowRt == null || contentRt == null || viewportRt == null) return;

            float contentHeight = contentRt.rect.height;
            float viewportHeight = viewportRt.rect.height;
            if (contentHeight <= viewportHeight) return;

            Vector3 localPos = contentRt.InverseTransformPoint(rowRt.position);
            float rowY = -localPos.y;

            float scrollRange = contentHeight - viewportHeight;
            float targetScroll = Mathf.Clamp01((rowY - viewportHeight * 0.3f) / scrollRange);
            currentScrollRect.verticalNormalizedPosition = 1f - targetScroll;
        }

        // ═══════════════════ 焦点模式切换 ═══════════════════

        private void SetFocusMode(FocusMode mode)
        {
            focusMode = mode;

            if (mode == FocusMode.Content && rows.Count > 0)
            {
                if (focusedRowIndex < 0) SetFocusedRow(0);
                else SetFocusedRow(focusedRowIndex);
            }
            else if (mode == FocusMode.Sidebar)
            {
                if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
                {
                    var old = rows[focusedRowIndex];
                    if (old.rowBg)
                        old.rowBg.color = (focusedRowIndex % 2 == 0) ? RowEven : RowOdd;
                }
                focusedRowIndex = -1;
            }
            else if (mode == FocusMode.TextEditing)
            {
                cursorBlinkTimer = 0;
                cursorVisible = true;
                if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
                {
                    var row = rows[focusedRowIndex];
                    if (row.rowBg) row.rowBg.color = EditingBg;
                }
            }

            UpdateHelpBar();
        }

        // ═══════════════════ 帮助栏 ═══════════════════

        private void UpdateHelpBar()
        {
            if (helpBarText == null) return;

            switch (focusMode)
            {
                case FocusMode.Sidebar:
                    helpBarText.text =
                        "<color=#5BC1FA>[W/\u2191][S/\u2193]</color> \u9009\u9875\u9762  " +
                        "<color=#5BC1FA>[Enter/\u2192]</color> \u8fdb\u5165  " +
                        "<color=#5BC1FA>[1~0,-,=]</color> \u5feb\u9009  " +
                        "<color=#5BC1FA>[Tab]</color> \u5207\u6362\u533a\u57df  " +
                        "<color=#5BC1FA>[Ctrl+S]</color> \u4fdd\u5b58  " +
                        "<color=#5BC1FA>[Esc]</color> \u5173\u95ed";
                    break;
                case FocusMode.Content:
                    if (focusedRowIndex >= 0 && focusedRowIndex < rows.Count)
                    {
                        var row = rows[focusedRowIndex];
                        switch (row.type)
                        {
                            case RowType.Slider:
                                helpBarText.text =
                                    "<color=#5BC1FA>[A/\u2190][D/\u2192]</color> \u8c03\u6574  " +
                                    "<color=#5BC1FA>[Shift]</color> \u7cbe\u7ec6  " +
                                    "<color=#5BC1FA>[Enter]</color> \u8f93\u5165\u6570\u503c  " +
                                    "<color=#5BC1FA>[R]</color> \u91cd\u7f6e  " +
                                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de  " +
                                    "<color=#5BC1FA>[Esc]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
                                break;
                            case RowType.Toggle:
                                helpBarText.text =
                                    "<color=#5BC1FA>[Space/Enter]</color> \u5207\u6362  " +
                                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de  " +
                                    "<color=#5BC1FA>[Esc]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
                                break;
                            case RowType.IntInput:
                                helpBarText.text =
                                    "<color=#5BC1FA>[A/\u2190][D/\u2192]</color> \u00b1\u8c03\u6574  " +
                                    "<color=#5BC1FA>[Shift]</color> \u7cbe\u7ec6(\u00b11)  " +
                                    "<color=#5BC1FA>[Enter]</color> \u8f93\u5165  " +
                                    "<color=#5BC1FA>[R]</color> \u91cd\u7f6e  " +
                                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de";
                                break;
                            case RowType.TextInput:
                                helpBarText.text =
                                    "<color=#5BC1FA>[Enter]</color> \u7f16\u8f91  " +
                                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de  " +
                                    "<color=#5BC1FA>[Esc]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
                                break;
                            case RowType.KeyBinding:
                                helpBarText.text =
                                    "<color=#5BC1FA>[Enter/Space]</color> \u6539\u952e  " +
                                    "<color=#5BC1FA>[R]</color> \u91cd\u7f6e  " +
                                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de  " +
                                    "<color=#5BC1FA>[Esc]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
                                break;
                            default:
                                helpBarText.text =
                                    "<color=#5BC1FA>[Enter/Space]</color> \u6267\u884c  " +
                                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de  " +
                                    "<color=#5BC1FA>[Esc]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
                                break;
                        }
                    }
                    else
                    {
                        helpBarText.text =
                            "<color=#5BC1FA>[W/\u2191][S/\u2193]</color> \u9009\u884c  " +
                            "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f  " +
                            "<color=#5BC1FA>[Esc]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
                    }
                    break;
                case FocusMode.TextEditing:
                    helpBarText.text =
                        "<color=#EFE580>\u6b63\u5728\u7f16\u8f91...</color>  " +
                        "<color=#5BC1FA>[Enter]</color> \u786e\u8ba4  " +
                        "<color=#5BC1FA>[Esc]</color> \u53d6\u6d88  " +
                        "<color=#5BC1FA>[Backspace]</color> \u5220\u9664  " +
                        "<color=#5BC1FA>[Delete]</color> \u6e05\u7a7a";
                    break;
            }
        }

        // ═══════════════════ 布局页面键盘 ═══════════════════

        private void HandleLayoutInput(Keyboard kb)
        {
            if (layoutHandles.Count == 0) return;

            bool up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            bool down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;

            if (up || down)
            {
                int delta = up ? -1 : 1;
                int newIdx = layoutFocusIndex + delta;
                if (newIdx < 0) newIdx = layoutHandles.Count - 1;
                if (newIdx >= layoutHandles.Count) newIdx = 0;
                SetLayoutFocus(newIdx);
                return;
            }

            if (layoutFocusIndex < 0 || layoutFocusIndex >= layoutHandles.Count) return;
            var handle = layoutHandles[layoutFocusIndex];

            float moveStep = 0.02f;
            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) moveStep = 0.005f;

            bool moveL = CheckKeyWithRepeat(kb, Key.A, Key.LeftArrow);
            bool moveR = CheckKeyWithRepeat(kb, Key.D, Key.RightArrow);
            bool moveU = kb.eKey.wasPressedThisFrame;
            bool moveD = kb.qKey.wasPressedThisFrame;

            if (moveL || moveR || moveU || moveD)
            {
                float dx = moveL ? -moveStep : (moveR ? moveStep : 0);
                float dy = moveD ? -moveStep : (moveU ? moveStep : 0);
                handle.layout.anchorX = Mathf.Clamp01(handle.layout.anchorX + dx);
                handle.layout.anchorY = Mathf.Clamp01(handle.layout.anchorY + dy);

                float halfW = 0.06f, halfH = 0.04f;
                handle.rt.anchorMin = new Vector2(handle.layout.anchorX - halfW, handle.layout.anchorY - halfH);
                handle.rt.anchorMax = new Vector2(handle.layout.anchorX + halfW, handle.layout.anchorY + halfH);
                handle.rt.offsetMin = Vector2.zero;
                handle.rt.offsetMax = Vector2.zero;

                UILayoutManager.Save();
                if (BattleHUD.Instance != null)
                    BattleHUD.Instance.RebuildHUD();
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                handle.layout.anchorX = 0.5f;
                handle.layout.anchorY = 0.5f;
                float halfW = 0.06f, halfH = 0.04f;
                handle.rt.anchorMin = new Vector2(0.5f - halfW, 0.5f - halfH);
                handle.rt.anchorMax = new Vector2(0.5f + halfW, 0.5f + halfH);
                handle.rt.offsetMin = Vector2.zero;
                handle.rt.offsetMax = Vector2.zero;

                UILayoutManager.Save();
                if (BattleHUD.Instance != null)
                    BattleHUD.Instance.RebuildHUD();
            }
        }

        private void SetLayoutFocus(int index)
        {
            if (layoutFocusIndex >= 0 && layoutFocusIndex < layoutHandles.Count)
            {
                var old = layoutHandles[layoutFocusIndex];
                if (old.bg) old.bg.color = UIColors.WithAlpha(Accent, 0.35f);
            }

            layoutFocusIndex = index;

            if (layoutFocusIndex >= 0 && layoutFocusIndex < layoutHandles.Count)
            {
                var cur = layoutHandles[layoutFocusIndex];
                if (cur.bg) cur.bg.color = LayoutSelected;
            }

            if (helpBarText != null)
            {
                helpBarText.text =
                    "<color=#5BC1FA>[W/\u2191][S/\u2193]</color> \u9009\u62e9\u5143\u7d20  " +
                    "<color=#5BC1FA>[A/\u2190][D/\u2192]</color> \u6c34\u5e73\u79fb\u52a8  " +
                    "<color=#5BC1FA>[Q]</color> \u4e0b\u79fb <color=#5BC1FA>[E]</color> \u4e0a\u79fb  " +
                    "<color=#5BC1FA>[Shift]</color> \u7cbe\u7ec6  " +
                    "<color=#5BC1FA>[R]</color> \u5c45\u4e2d  " +
                    "<color=#5BC1FA>[Tab]</color> \u8fd4\u56de\u4fa7\u8fb9\u680f";
            }
        }

        // ═══════════════════ 齿轮按钮 ═══════════════════

        private void BuildGearButton()
        {
            gearCanvas = UIFactory.CreateCanvas("SettingsGearCanvas", 31000);
            DontDestroyOnLoad(gearCanvas.gameObject);

            var btnGo = new GameObject("GearBtn");
            btnGo.transform.SetParent(gearCanvas.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 1f);
            btnRt.anchorMax = new Vector2(0f, 1f);
            btnRt.pivot = new Vector2(0f, 1f);
            btnRt.anchoredPosition = new Vector2(15, -15);
            btnRt.sizeDelta = new Vector2(120, 32);

            var btnBg = btnGo.AddComponent<Image>();
            btnBg.color = GearNormal;
            btnBg.raycastTarget = false;

            var iconTxt = UIFactory.CreateText(btnGo.transform, "Icon",
                "[设置 F10]", 18,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(iconTxt.rectTransform);
        }

        // ═══════════════════ 开关面板 ═══════════════════

        public void ToggleSettings()
        {
            if (isOpen)
                HidePanel();
            else
                ShowPanel();
            wmj.Log.I("[SettingsPanel] ToggleSettings -> isOpen=" + isOpen, wmj.Log.Tag.UI);
        }

        private void ShowPanel()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
                isOpen = true;
                focusMode = FocusMode.Sidebar;
                UpdateHelpBar();
                return;
            }

            settingsCanvas = UIFactory.CreateCanvas("SettingsCanvas", 31100);
            DontDestroyOnLoad(settingsCanvas.gameObject);
            panelRoot = settingsCanvas.gameObject;

            var overlay = UIFactory.CreateFullScreenImage(panelRoot.transform, "Overlay",
                new Color(0f, 0f, 0f, 0.50f));
            overlay.raycastTarget = false;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(panelRoot.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.075f, 0.06f);
            panelRt.anchorMax = new Vector2(0.925f, 0.94f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;

            var panelBgImg = panelGo.AddComponent<Image>();
            panelBgImg.color = PanelBg;
            panelBgImg.raycastTarget = false;

            BuildTitleBar(panelRt);
            BuildSidebar(panelRt);
            BuildContentArea(panelRt);
            BuildHelpBar(panelRt);

            isOpen = true;
            focusMode = FocusMode.Sidebar;

            if (sidebarItems.Count > 0)
                SelectSidebarByIndex(0);

            UpdateHelpBar();
        }

        private void HidePanel()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            isOpen = false;
            isListeningForKey = false;
            focusMode = FocusMode.Sidebar;
        }

        // ═══════════════════ 标题栏 ═══════════════════

        private void BuildTitleBar(RectTransform panel)
        {
            var barGo = new GameObject("TitleBar");
            barGo.transform.SetParent(panel, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0.93f);
            barRt.anchorMax = new Vector2(1, 1f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            var barBg = barGo.AddComponent<Image>();
            barBg.color = TitleBarBg;
            barBg.raycastTarget = false;

            var div = UIFactory.CreateImage(barRt, "TitleDiv",
                UIColors.WithAlpha(Accent, 0.30f));
            div.rectTransform.anchorMin = new Vector2(0.01f, 0f);
            div.rectTransform.anchorMax = new Vector2(0.99f, 0.03f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;

            var title = UIFactory.CreateText(barGo.transform, "Title",
                "\u2699  \u7cfb\u7edf\u8bbe\u7f6e  \u2014  \u5168\u952e\u76d8\u64cd\u4f5c", 26,
                TextAlignmentOptions.Left, Accent, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            title.rectTransform.anchorMax = new Vector2(0.65f, 1f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;

            var shortcuts = UIFactory.CreateText(barGo.transform, "Shortcuts",
                "<color=#EFE580>[Esc]</color> \u5173\u95ed    <color=#EFE580>[Ctrl+S]</color> \u4fdd\u5b58    <color=#EFE580>[F1]</color> \u91cd\u9009\u5175\u79cd",
                16, TextAlignmentOptions.Right, HintColor);
            shortcuts.rectTransform.anchorMin = new Vector2(0.50f, 0f);
            shortcuts.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            shortcuts.rectTransform.offsetMin = Vector2.zero;
            shortcuts.rectTransform.offsetMax = Vector2.zero;
            shortcuts.richText = true;
        }

        // ═══════════════════ 侧边栏 ═══════════════════

        private void BuildSidebar(RectTransform panel)
        {
            var sidebarGo = new GameObject("Sidebar");
            sidebarGo.transform.SetParent(panel, false);
            var sidebarRt = sidebarGo.AddComponent<RectTransform>();
            sidebarRt.anchorMin = new Vector2(0, 0.06f);
            sidebarRt.anchorMax = new Vector2(0.18f, 0.93f);
            sidebarRt.offsetMin = Vector2.zero;
            sidebarRt.offsetMax = Vector2.zero;

            var sidebarBgImg = sidebarGo.AddComponent<Image>();
            sidebarBgImg.color = SidebarBg;
            sidebarBgImg.raycastTarget = false;

            sidebarItems.Clear();
            activeSidebarIndex = -1;

            float itemHeight = 1f / MenuIds.Length;

            for (int i = 0; i < MenuIds.Length; i++)
            {
                var itemGo = new GameObject("MenuItem_" + MenuIds[i]);
                itemGo.transform.SetParent(sidebarRt, false);
                var itemRt = itemGo.AddComponent<RectTransform>();
                itemRt.anchorMin = new Vector2(0, 1f - (i + 1) * itemHeight);
                itemRt.anchorMax = new Vector2(1, 1f - i * itemHeight);
                itemRt.offsetMin = new Vector2(2, 1);
                itemRt.offsetMax = new Vector2(-2, -1);

                var itemBg = itemGo.AddComponent<Image>();
                itemBg.color = Color.clear;
                itemBg.raycastTarget = false;

                var accentGo = new GameObject("Accent");
                accentGo.transform.SetParent(itemRt, false);
                var accentRt = accentGo.AddComponent<RectTransform>();
                accentRt.anchorMin = new Vector2(0, 0.10f);
                accentRt.anchorMax = new Vector2(0.025f, 0.90f);
                accentRt.offsetMin = Vector2.zero;
                accentRt.offsetMax = Vector2.zero;
                var accentImg = accentGo.AddComponent<Image>();
                accentImg.color = Color.clear;
                accentImg.raycastTarget = false;

                var keyHint = UIFactory.CreateText(itemRt, "KeyHint", MenuKeyHints[i], 16,
                    TextAlignmentOptions.Center, HintColor, FontStyles.Bold);
                keyHint.rectTransform.anchorMin = new Vector2(0.02f, 0.15f);
                keyHint.rectTransform.anchorMax = new Vector2(0.16f, 0.85f);
                keyHint.rectTransform.offsetMin = Vector2.zero;
                keyHint.rectTransform.offsetMax = Vector2.zero;

                var lbl = UIFactory.CreateText(itemRt, "Label", MenuLabels[i], 18,
                    TextAlignmentOptions.Left, UIColors.Silver);
                lbl.rectTransform.anchorMin = new Vector2(0.18f, 0f);
                lbl.rectTransform.anchorMax = new Vector2(0.95f, 1f);
                lbl.rectTransform.offsetMin = Vector2.zero;
                lbl.rectTransform.offsetMax = Vector2.zero;

                sidebarItems.Add(new SidebarItem
                {
                    bg = itemBg,
                    accent = accentImg,
                    label = lbl,
                    keyHint = keyHint,
                    pageId = MenuIds[i]
                });
            }
        }

        // ═══════════════════ 内容区 ═══════════════════

        private void BuildContentArea(RectTransform panel)
        {
            var areaGo = new GameObject("ContentArea");
            areaGo.transform.SetParent(panel, false);
            var areaRt = areaGo.AddComponent<RectTransform>();
            areaRt.anchorMin = new Vector2(0.19f, 0.06f);
            areaRt.anchorMax = new Vector2(1f, 0.93f);
            areaRt.offsetMin = new Vector2(4, 0);
            areaRt.offsetMax = new Vector2(-4, 0);

            var areaBg = areaGo.AddComponent<Image>();
            areaBg.color = ContentBg;
            areaBg.raycastTarget = false;

            contentArea = areaRt;
        }

        // ═══════════════════ 帮助栏 ═══════════════════

        private void BuildHelpBar(RectTransform panel)
        {
            var barGo = new GameObject("HelpBar");
            barGo.transform.SetParent(panel, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0);
            barRt.anchorMax = new Vector2(1, 0.055f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            var barBg = barGo.AddComponent<Image>();
            barBg.color = new Color(0.03f, 0.04f, 0.08f, 0.90f);
            barBg.raycastTarget = false;

            var div = UIFactory.CreateImage(panel, "HelpDiv",
                UIColors.WithAlpha(Accent, 0.20f));
            div.rectTransform.anchorMin = new Vector2(0.01f, 0.055f);
            div.rectTransform.anchorMax = new Vector2(0.99f, 0.058f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;

            helpBarText = UIFactory.CreateText(barGo.transform, "HelpText", "", 16,
                TextAlignmentOptions.Center, UIColors.Silver);
            helpBarText.richText = true;
            UIFactory.SetFullStretch(helpBarText.rectTransform);
            helpBarText.rectTransform.offsetMin = new Vector2(8, 0);
            helpBarText.rectTransform.offsetMax = new Vector2(-8, 0);
        }

        // ═══════════════════ 侧边栏选择 ═══════════════════

        private void SelectSidebarByIndex(int index)
        {
            if (index < 0 || index >= sidebarItems.Count) return;

            if (activeSidebarIndex >= 0 && activeSidebarIndex < sidebarItems.Count)
            {
                var old = sidebarItems[activeSidebarIndex];
                if (old.bg) old.bg.color = Color.clear;
                if (old.accent) old.accent.color = Color.clear;
                if (old.label) old.label.color = UIColors.Silver;
                if (old.keyHint) old.keyHint.color = HintColor;
            }

            activeSidebarIndex = index;
            var item = sidebarItems[index];
            if (item.bg) item.bg.color = SidebarActive;
            if (item.accent) item.accent.color = Accent;
            if (item.label) item.label.color = UIColors.White;
            if (item.keyHint) item.keyHint.color = Accent;

            if (contentArea != null)
            {
                for (int i = contentArea.childCount - 1; i >= 0; i--)
                    Destroy(contentArea.GetChild(i).gameObject);
            }

            rows.Clear();
            focusedRowIndex = -1;
            rowBuildIndex = 0;
            isListeningForKey = false;
            currentScrollRect = null;
            layoutHandles.Clear();
            layoutFocusIndex = -1;

            BuildPage(item.pageId);
            UpdateHelpBar();
        }

        private void BuildPage(string pageId)
        {
            switch (pageId)
            {
                case "matchinfo": BuildMatchInfoPage(); break;
                case "notify":    BuildNotifyPage(); break;
                case "aim":       BuildAimPage(); break;
                case "hit":       BuildHitPage(); break;
                case "crosshair": BuildCrosshairPage(); break;
                case "health":    BuildHealthPage(); break;
                case "buff":      BuildBuffPage(); break;
                case "font":      BuildFontPage(); break;
                case "shortcut":  BuildShortcutPage(); break;
                case "economy":   BuildEconomyPage(); break;
                case "network":   BuildNetworkPage(); break;
                case "layout":    BuildLayoutPage(); break;
            }
        }

        // ═══════════════════ 滚动内容容器 ═══════════════════

        private Transform CreateScrollContent(string pageName)
        {
            var scrollGo = new GameObject("Scroll_" + pageName);
            scrollGo.transform.SetParent(contentArea, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(scrollRt);

            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = Color.clear;
            scrollImg.raycastTarget = false;

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(viewportRt);
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = Color.white;
            viewportImg.raycastTarget = false;
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 3;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.viewport = viewportRt;
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            currentScrollRect = scroll;

            return contentGo.transform;
        }

        // ═══════════════════ 各设置页面 ═══════════════════

        private void BuildMatchInfoPage()
        {
            var c = CreateScrollContent("MatchInfo");
            var s = UILayoutManager.Settings;
            AddSectionHeader(c, "\u5bf9\u5c40\u4fe1\u606f\u663e\u793a");
            AddToggleRow(c, "\u663e\u793a\u9636\u6bb5", s.showMatchStage, v => s.showMatchStage = v);
            AddToggleRow(c, "\u663e\u793a\u5012\u8ba1\u65f6", s.showMatchTimer, v => s.showMatchTimer = v);
            AddToggleRow(c, "\u663e\u793a\u8f6e\u6b21", s.showMatchRound, v => s.showMatchRound = v);
            AddToggleRow(c, "\u663e\u793a\u6bd4\u5206", s.showMatchScore, v => s.showMatchScore = v);
            AddToggleRow(c, "\u663e\u793a\u7ecf\u6d4e", s.showMatchEconomy, v => s.showMatchEconomy = v);
        }

        private void BuildNotifyPage()
        {
            var c = CreateScrollContent("Notify");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u901a\u77e5\u53c2\u6570");
            AddSliderRow(c, "\u901a\u77e5\u65f6\u957f", "\u79d2", s.notificationDuration, d.notificationDuration,
                0.5f, 8f, v => s.notificationDuration = v);
            AddSliderRow(c, "\u6700\u5927\u901a\u77e5\u6570", "", s.maxNotifications, d.maxNotifications,
                1, 10, v => s.maxNotifications = Mathf.RoundToInt(v));
            AddSectionHeader(c, "\u4e8b\u4ef6\u8fc7\u6ee4");
            AddToggleRow(c, "\u961f\u53cb\u9635\u4ea1\u4e8b\u4ef6", s.showTeammateDeathEvents,
                v => s.showTeammateDeathEvents = v);
            AddToggleRow(c, "\u961f\u53cb\u590d\u6d3b\u4e8b\u4ef6", s.showTeammateRespawnEvents,
                v => s.showTeammateRespawnEvents = v);
            AddToggleRow(c, "\u5f39\u836f\u5151\u6362\u4e8b\u4ef6", s.showIndividualAmmoEvents,
                v => s.showIndividualAmmoEvents = v);
            AddToggleRow(c, "\u4e2a\u4f53\u5347\u7ea7\u4e8b\u4ef6", s.showIndividualLevelEvents,
                v => s.showIndividualLevelEvents = v);
            AddToggleRow(c, "\u51fb\u6740\u4e8b\u4ef6", s.showKillFeedEvents,
                v => s.showKillFeedEvents = v);
        }

        private void BuildAimPage()
        {
            var c = CreateScrollContent("Aim");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u5f00\u955c\u53c2\u6570");
            AddToggleRow(c, "\u542f\u7528\u81ea\u52a8\u5f00\u955c", s.aimZoomEnabled, v => s.aimZoomEnabled = v);
            AddSliderRow(c, "\u805a\u7126\u500d\u7387", "x", s.aimZoomFactor, d.aimZoomFactor,
                1f, 4f, v => s.aimZoomFactor = v);
            AddSliderRow(c, "\u805a\u7126\u901f\u5ea6", "", s.aimZoomSpeed, d.aimZoomSpeed,
                1f, 20f, v => s.aimZoomSpeed = v);
            AddSliderRow(c, "\u81ea\u52a8\u5173\u955c\u5ef6\u8fdf", "\u79d2", s.aimZoomCloseDelay, d.aimZoomCloseDelay,
                0.5f, 10f, v => s.aimZoomCloseDelay = v);
        }

        private void BuildHitPage()
        {
            var c = CreateScrollContent("Hit");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u53d7\u51fb\u63d0\u793a");
            AddSliderRow(c, "\u95ea\u70c1\u65f6\u957f", "\u79d2", s.hitFlashDuration, d.hitFlashDuration,
                0.1f, 1f, v => s.hitFlashDuration = v);
            AddSliderRow(c, "\u4f4e\u8840\u91cf\u9608\u503c", "%", s.lowHealthThreshold * 100f,
                d.lowHealthThreshold * 100f, 10f, 90f,
                v => s.lowHealthThreshold = v / 100f);
        }

        private void BuildCrosshairPage()
        {
            var c = CreateScrollContent("Crosshair");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u51c6\u661f\u53c2\u6570");
            AddSliderRow(c, "\u51c6\u661f\u73af\u534a\u5f84", "px", s.crosshairRingRadius, d.crosshairRingRadius,
                20f, 200f, v => s.crosshairRingRadius = v);
            AddSliderRow(c, "\u51c6\u661f\u73af\u7ebf\u5bbd", "px", s.crosshairRingThickness, d.crosshairRingThickness,
                1f, 30f, v => s.crosshairRingThickness = v);
            AddSliderRow(c, "\u4e2d\u5fc3\u70b9\u5927\u5c0f", "px", s.crosshairDotSize, d.crosshairDotSize,
                2f, 20f, v => s.crosshairDotSize = v);
            AddSliderRow(c, "\u5341\u5b57\u7ebf\u957f\u5ea6", "px", s.crosshairLineLength, d.crosshairLineLength,
                10f, 100f, v => s.crosshairLineLength = v);
            AddSliderRow(c, "\u70ed\u91cf\u73af\u95f4\u8ddd", "px", s.crosshairHeatRingGap, d.crosshairHeatRingGap,
                0f, 30f, v => s.crosshairHeatRingGap = v);
            AddSliderRow(c, "\u540a\u5c04\u6a21\u5f0f\u900f\u660e\u5ea6", "", s.deployModeRingOpacity, d.deployModeRingOpacity,
                0.1f, 1f, v => s.deployModeRingOpacity = v);
        }

        private void BuildHealthPage()
        {
            var c = CreateScrollContent("Health");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u8840\u6761\u53c2\u6570");
            AddSliderRow(c, "\u8840\u6761\u5bbd\u5ea6", "px", s.healthBarWidth, d.healthBarWidth,
                200f, 1600f, v => s.healthBarWidth = v);
            AddSliderRow(c, "\u8840\u6761\u9ad8\u5ea6", "px", s.healthBarHeight, d.healthBarHeight,
                12f, 80f, v => s.healthBarHeight = v);
        }

        private void BuildBuffPage()
        {
            var c = CreateScrollContent("Buff");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "BUFF \u72b6\u6001\u680f");
            AddSliderRow(c, "\u5355\u5217\u6700\u5927\u53ef\u89c1\u6570", "", s.buffMaxVisible, d.buffMaxVisible,
                1, 12, v => s.buffMaxVisible = Mathf.RoundToInt(v));
            AddSliderRow(c, "\u680f\u5bbd\u5ea6", "px", s.buffColumnWidth, d.buffColumnWidth,
                100f, 400f, v => s.buffColumnWidth = v);
        }

        private void BuildFontPage()
        {
            var c = CreateScrollContent("Font");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u5b57\u4f53\u5927\u5c0f");
            AddSliderRow(c, "\u51c6\u661f\u5b57\u4f53", "px", s.crosshairFontSize, d.crosshairFontSize,
                16, 60, v => s.crosshairFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "\u8840\u6761\u5b57\u4f53", "px", s.healthBarFontSize, d.healthBarFontSize,
                16, 60, v => s.healthBarFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "\u901a\u77e5\u5b57\u4f53", "px", s.notificationFontSize, d.notificationFontSize,
                16, 60, v => s.notificationFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "BUFF\u5b57\u4f53", "px", s.buffFontSize, d.buffFontSize,
                12, 48, v => s.buffFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "\u5168\u5c40\u6587\u5b57\u900f\u660e\u5ea6", "", s.textOpacity, d.textOpacity,
                0.2f, 1f, v => s.textOpacity = v);
        }

        private void BuildShortcutPage()
        {
            var c = CreateScrollContent("Shortcut");
            var s = UILayoutManager.Settings;
            AddSectionHeader(c, "\u5f39\u836f\u8d2d\u4e70\u5feb\u6377\u952e");
            AddSliderRow(c, "\u8d2d\u4e70\u63d0\u793a\u65f6\u957f", "\u79d2", s.ammoPurchasePopupDuration,
                HUDSettings.Defaults().ammoPurchasePopupDuration, 0.1f, 3f,
                v => s.ammoPurchasePopupDuration = v);

            if (s.ammoKeyBindings == null)
                s.ammoKeyBindings = HUDSettings.DefaultAmmoKeyBindings();

            for (int i = 0; i < s.ammoKeyBindings.Count; i++)
            {
                int idx = i;
                var binding = s.ammoKeyBindings[i];
                AddKeyBindingRow(c, "\u8d2d\u4e70 x" + binding.purchaseDigit.ToString(), binding, idx);
            }

            AddButtonRow(c, "\u21ba \u91cd\u7f6e\u6240\u6709\u5feb\u6377\u952e", () =>
            {
                var ss = UILayoutManager.Settings;
                ss.ammoKeyBindings = HUDSettings.DefaultAmmoKeyBindings();
                ss.ammoPurchasePopupDuration = HUDSettings.Defaults().ammoPurchasePopupDuration;
                SelectSidebarByIndex(activeSidebarIndex);
            });
        }

        private void BuildEconomyPage()
        {
            var c = CreateScrollContent("Economy");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();
            AddSectionHeader(c, "\u81ea\u52a8\u8865\u7ed9");
            AddToggleRow(c, "\u542f\u7528\u81ea\u52a8\u8865\u7ed9", s.autoResupplyEnabled,
                v => s.autoResupplyEnabled = v);
            AddIntInputRow(c, "\u8865\u7ed9\u89e6\u53d1\u9608\u503c", (int)s.autoResupplyThreshold, 20, 500, (int)d.autoResupplyThreshold,
                v => s.autoResupplyThreshold = (uint)v, "\u53d1");
            AddIntInputRow(c, "\u6bcf\u6b21\u8d2d\u4e70\u6279\u6570", (int)s.autoResupplyBatchCount, 1, 20, (int)d.autoResupplyBatchCount,
                v => s.autoResupplyBatchCount = (uint)v, "\u6279");
            AddSectionHeader(c, "\u667a\u80fd\u8865\u7ed9");
            AddToggleRow(c, "\u542f\u7528\u667a\u80fd\u6a21\u5f0f", s.smartResupplyEnabled,
                v => s.smartResupplyEnabled = v);
            AddIntInputRow(c, "\u7d27\u6025\u8865\u7ed9\u9608\u503c", (int)s.emergencyAmmoThreshold, 5, 100, (int)d.emergencyAmmoThreshold,
                v => s.emergencyAmmoThreshold = (uint)v, "\u53d1");
            AddSliderRow(c, "\u6218\u6597\u5f3a\u5ea6\u6743\u91cd", "x",
                s.combatIntensityWeight, d.combatIntensityWeight, 1.0f, 3.0f,
                v => s.combatIntensityWeight = v);
            AddSectionHeader(c, "\u91d1\u5e01\u9884\u7559");
            AddIntInputRow(c, "\u4e70\u6d3b\u91d1\u5e01\u9884\u7559", (int)s.goldReserveForBuyback, 0, 500, (int)d.goldReserveForBuyback,
                v => s.goldReserveForBuyback = (uint)v, "\u91d1");
        }

        private void BuildNetworkPage()
        {
            var c = CreateScrollContent("Network");
            var cfg = ConfigLoader.config;
            var gp = GameParamsConfig.Get;
            if (cfg == null)
            {
                AddSectionHeader(c, "\u26a0 \u914d\u7f6e\u672a\u52a0\u8f7d");
                return;
            }
            AddSectionHeader(c, "\u670d\u52a1\u5668\u8fde\u63a5");
            AddTextInputRow(c, "\u670d\u52a1\u5668 IP \u5730\u5740", cfg.ip ?? "192.168.12.1",
                v => cfg.ip = v);
            AddIntInputRow(c, "\u6570\u636e\u7aef\u53e3 (MQTT)", cfg.dataPort, 1, 65535, cfg.dataPort,
                v => cfg.dataPort = v, "");
            AddIntInputRow(c, "\u89c6\u9891\u7aef\u53e3 (UDP)", cfg.videoPort, 1, 65535, cfg.videoPort,
                v => cfg.videoPort = v, "");
            AddSectionHeader(c, "\u673a\u5668\u4eba\u53c2\u6570");
            AddIntInputRow(c, "\u673a\u5668\u4eba ID", cfg.RobotID, 1, 20, cfg.RobotID,
                v => cfg.RobotID = v, "");
            AddIntInputRow(c, "\u573a\u4e0a\u673a\u5668\u4eba\u6570\u91cf", cfg.RobotNum, 1, 20, cfg.RobotNum,
                v => cfg.RobotNum = v, "");
            AddSectionHeader(c, "\u6027\u80fd\u53c2\u6570");
            AddIntInputRow(c, "\u76ee\u6807\u5e27\u7387 (0=\u4e0d\u9650)", cfg.targetFrameRate, 0, 360, cfg.targetFrameRate,
                v => { cfg.targetFrameRate = v; Application.targetFrameRate = v > 0 ? v : -1; }, "fps");
            AddIntInputRow(c, "\u89e3\u7801\u5bbd\u5ea6", cfg.decoderOutputWidth, 480, 3840, cfg.decoderOutputWidth,
                v => cfg.decoderOutputWidth = v, "px");
            AddIntInputRow(c, "\u89e3\u7801\u9ad8\u5ea6", cfg.decoderOutputHeight, 240, 2160, cfg.decoderOutputHeight,
                v => cfg.decoderOutputHeight = v, "px");
            AddIntInputRow(c, "\u89e3\u7801\u961f\u5217\u5927\u5c0f", cfg.decoderQueueSize, 1, 32, cfg.decoderQueueSize,
                v => cfg.decoderQueueSize = v, "");
            AddIntInputRow(c, "\u6bcf\u5e27\u6700\u5927\u89e3\u7801\u6570", cfg.maxDrainPerUpdate, 1, 16, cfg.maxDrainPerUpdate,
                v => cfg.maxDrainPerUpdate = v, "");
            AddSectionHeader(c, "\u9ad8\u7ea7\u53c2\u6570");
            AddSliderRow(c, "MQTT \u91cd\u8fde\u95f4\u9694", "s",
                cfg.mqttReconnectInterval, 3f, 2f, 30f,
                v => cfg.mqttReconnectInterval = v);
            AddIntInputRow(c, "\u65e5\u5fd7\u7f13\u51b2\u533a\u5927\u5c0f", cfg.logBufferSize, 1, 256, cfg.logBufferSize,
                v => cfg.logBufferSize = v, "");

            AddSectionHeader(c, "\u6bd4\u8d5b\u9632\u51b2\u7a81");
            AddToggleRow(c, "\u6bd4\u8d5b\u6a21\u5f0f\u88ab\u52a8\u89c2\u5bdf\u6a21\u5f0f(\u53ea\u6536\u4e0d\u53d1)", gp.competitionPassiveObserverMode,
                v => gp.competitionPassiveObserverMode = v);
            AddToggleRow(c, "\u6bd4\u8d5b\u6a21\u5f0f\u5141\u8bb8\u53d1\u9001\u4f53\u7cfb\u9009\u62e9\u547d\u4ee4", gp.allowCustomPerformanceSelectionCommandInCompetition,
                v => gp.allowCustomPerformanceSelectionCommandInCompetition = v);

            AddSectionHeader(c, "\u540a\u5c04\u56fe\u4f20\u663e\u793a");
            AddToggleRow(c, "拉伸到全屏显示（v3.2.1: 1024×512 原生）", gp.lobShotStretchTo720x1080,
                v =>
                {
                    gp.lobShotStretchTo720x1080 = v;
                    BattleHUD.Instance?.ApplyLobShotDisplaySettingsFromConfig();
                });
            AddToggleRow(c, "\u62c9\u4f38\u65f6\u4f7f\u7528 SR \u8d85\u5206\u6a21\u578b", gp.lobShotUseSrWhenStretched,
                v =>
                {
                    gp.lobShotUseSrWhenStretched = v;
                    BattleHUD.Instance?.ApplyLobShotDisplaySettingsFromConfig();
                });

            AddButtonRow(c, "\ud83d\udcbe \u4fdd\u5b58\u7f51\u7edc\u914d\u7f6e", () =>
            {
                ConfigLoader.SaveConfig();
                GameParamsConfig.Save();
                wmj.Log.I("[Settings] \u7f51\u7edc\u914d\u7f6e\u5df2\u4fdd\u5b58", wmj.Log.Tag.UI);
            });
        }

        private void BuildLayoutPage()
        {
            layoutHandles.Clear();
            layoutFocusIndex = -1;
            minimapRoot = null;

            var layoutGo = new GameObject("LayoutEditor");
            layoutGo.transform.SetParent(contentArea, false);
            var layoutRt = layoutGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(layoutRt);

            var hint = UIFactory.CreateText(layoutRt, "Hint",
                "\u4f7f\u7528\u952e\u76d8\u64cd\u63a7 HUD \u5143\u7d20\u4f4d\u7f6e  [W/S] \u9009\u5143\u7d20  [A/D] \u6c34\u5e73\u79fb  [Q/E] \u5782\u76f4\u79fb  [R] \u5c45\u4e2d",
                18, TextAlignmentOptions.Center, UIColors.WithAlpha(Accent, 0.7f));
            hint.rectTransform.anchorMin = new Vector2(0, 0.92f);
            hint.rectTransform.anchorMax = new Vector2(1, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var minimapGo = new GameObject("Minimap");
            minimapGo.transform.SetParent(layoutRt, false);
            minimapRoot = minimapGo.AddComponent<RectTransform>();
            minimapRoot.anchorMin = new Vector2(0.05f, 0.05f);
            minimapRoot.anchorMax = new Vector2(0.95f, 0.90f);
            minimapRoot.offsetMin = Vector2.zero;
            minimapRoot.offsetMax = Vector2.zero;

            var mmBg = minimapGo.AddComponent<Image>();
            mmBg.color = MinimapBg;
            mmBg.raycastTarget = false;

            var border = UIFactory.CreateImage(minimapRoot, "Border",
                UIColors.WithAlpha(Accent, 0.25f));
            UIFactory.SetFullStretch(border.rectTransform);
            border.raycastTarget = false;

            var data = UILayoutManager.Data;
            if (data.elements != null && data.elements.Count > 0)
            {
                foreach (var el in data.elements)
                    AddLayoutElement(el);
            }
            else
            {
                string[] defaultIds = { "HealthBar", "CrosshairRing", "BuffPanel", "Notification", "MatchInfo" };
                foreach (var id in defaultIds)
                {
                    var el = UILayoutManager.GetElement(id);
                    AddLayoutElement(el);
                }
            }

            if (layoutHandles.Count > 0)
                SetLayoutFocus(0);
        }

        private void AddLayoutElement(UIElementLayout el)
        {
            if (el == null || minimapRoot == null) return;

            float halfW = 0.06f, halfH = 0.04f;

            var elemGo = new GameObject("LE_" + el.id);
            elemGo.transform.SetParent(minimapRoot, false);
            var elemRt = elemGo.AddComponent<RectTransform>();
            elemRt.anchorMin = new Vector2(el.anchorX - halfW, el.anchorY - halfH);
            elemRt.anchorMax = new Vector2(el.anchorX + halfW, el.anchorY + halfH);
            elemRt.offsetMin = Vector2.zero;
            elemRt.offsetMax = Vector2.zero;

            var elemBg = elemGo.AddComponent<Image>();
            elemBg.color = UIColors.WithAlpha(Accent, 0.35f);
            elemBg.raycastTarget = false;

            UIFactory.CreateText(elemRt, "Label", el.id, 14,
                TextAlignmentOptions.Center, UIColors.White);

            layoutHandles.Add(new LayoutHandle { id = el.id, rt = elemRt, layout = el, bg = elemBg });
        }

        // ═══════════════════ 行组件构建器 ═══════════════════

        private void AddSectionHeader(Transform content, string text)
        {
            var go = new GameObject("Section_" + text);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            le.flexibleWidth = 1;

            var rt = go.GetComponent<RectTransform>();

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.08f, 0.16f, 0.70f);
            bg.raycastTarget = false;

            var div = UIFactory.CreateImage(rt, "Div", UIColors.WithAlpha(Accent, 0.30f));
            div.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            div.rectTransform.anchorMax = new Vector2(0.98f, 0.04f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;

            var label = UIFactory.CreateText(rt, "Label", text, 24,
                TextAlignmentOptions.Left, Accent, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0.025f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.95f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            rowBuildIndex = 0;
        }

        private void AddSliderRow(Transform content, string label, string unit,
            float value, float defaultVal, float min, float max,
            System.Action<float> onChange)
        {
            string fmt = (max - min) > 50 ? "F0" : "F1";
            // 包装回调：每次变更打印详细日志
            var label_ = label; var unit_ = unit; var fmt_ = fmt;
            float lastLogged = value;
            var origChange = onChange;
            onChange = (v) =>
            {
                origChange?.Invoke(v);
                if (Mathf.Abs(v - lastLogged) > 0.0001f)
                {
                    wmj.Log.I($"[Settings] 修改 {label_} = {v.ToString(fmt_)}{unit_}", wmj.Log.Tag.UI);
                    lastLogged = v;
                }
            };

            var go = new GameObject("Slider_" + label);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowBuildIndex % 2 == 0) ? RowEven : RowOdd;
            rowBg.raycastTarget = false;
            rowBuildIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            var lbl = UIFactory.CreateText(rowRt, "Label", label, 19,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.25f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            var sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(rowRt, false);
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0.27f, 0.20f);
            sliderRt.anchorMax = new Vector2(0.62f, 0.80f);
            sliderRt.offsetMin = Vector2.zero;
            sliderRt.offsetMax = Vector2.zero;

            var sliderBg = sliderGo.AddComponent<Image>();
            sliderBg.color = SliderTrack;
            sliderBg.raycastTarget = false;

            var fillAreaGo = new GameObject("FillArea");
            fillAreaGo.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillAreaGo.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.15f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.85f);
            fillAreaRt.offsetMin = new Vector2(4, 0);
            fillAreaRt.offsetMax = new Vector2(-4, 0);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(fillRt);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = SliderFill;
            fillImg.raycastTarget = false;

            var handleAreaGo = new GameObject("HandleArea");
            handleAreaGo.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleAreaGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(handleAreaRt);
            handleAreaRt.offsetMin = new Vector2(8, 0);
            handleAreaRt.offsetMax = new Vector2(-8, 0);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(16, 0);
            handleRt.anchorMin = new Vector2(0, 0.05f);
            handleRt.anchorMax = new Vector2(0, 0.95f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = SliderHandle;
            handleImg.raycastTarget = false;

            var slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.interactable = false;

            var valText = UIFactory.CreateText(rowRt, "Value",
                value.ToString(fmt) + unit, 19,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            valText.rectTransform.anchorMin = new Vector2(0.64f, 0f);
            valText.rectTransform.anchorMax = new Vector2(0.76f, 1f);
            valText.rectTransform.offsetMin = Vector2.zero;
            valText.rectTransform.offsetMax = Vector2.zero;

            var hint = UIFactory.CreateText(rowRt, "Hint", "[\u2190\u2192] [R]\u91cd\u7f6e [Enter]\u8f93\u5165",
                13, TextAlignmentOptions.Right, HintColor);
            hint.rectTransform.anchorMin = new Vector2(0.77f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.99f, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var row = new RowData
            {
                type = RowType.Slider,
                go = go, rowBg = rowBg, hintText = hint,
                slider = slider, defaultValue = defaultVal,
                sliderMin = min, sliderMax = max,
                onSliderChange = onChange, valueText = valText,
                fmt = fmt, unit = unit
            };
            rows.Add(row);
        }

        private void AddToggleRow(Transform content, string label, bool value,
            System.Action<bool> onChange)
        {
            // 包装回调：打印开关状态变化
            var label_ = label;
            var origChange = onChange;
            onChange = (v) =>
            {
                origChange?.Invoke(v);
                wmj.Log.I($"[Settings] 开关 {label_} = {(v ? "开启" : "关闭")}", wmj.Log.Tag.UI);
            };
            var go = new GameObject("Toggle_" + label);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowBuildIndex % 2 == 0) ? RowEven : RowOdd;
            rowBg.raycastTarget = false;
            rowBuildIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            var lbl = UIFactory.CreateText(rowRt, "Label", label, 19,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.50f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            var toggleGo = new GameObject("ToggleDisplay");
            toggleGo.transform.SetParent(rowRt, false);
            var toggleRt = toggleGo.AddComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(0.55f, 0.18f);
            toggleRt.anchorMax = new Vector2(0.72f, 0.82f);
            toggleRt.offsetMin = Vector2.zero;
            toggleRt.offsetMax = Vector2.zero;

            var toggleBg = toggleGo.AddComponent<Image>();
            toggleBg.raycastTarget = false;

            var statusLabel = UIFactory.CreateText(toggleGo.transform, "Status", "", 18,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(statusLabel.rectTransform);

            UpdateToggleVisual(toggleBg, statusLabel, value);

            var hint = UIFactory.CreateText(rowRt, "Hint", "[Space/Enter] \u5207\u6362",
                13, TextAlignmentOptions.Right, HintColor);
            hint.rectTransform.anchorMin = new Vector2(0.77f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.99f, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var row = new RowData
            {
                type = RowType.Toggle,
                go = go, rowBg = rowBg, hintText = hint,
                toggleValue = value, onToggleChange = onChange,
                toggleBg = toggleBg, toggleLabel = statusLabel
            };
            rows.Add(row);
        }

        private void AddIntInputRow(Transform content, string label, int value,
            int min, int max, int defaultVal, System.Action<int> onChange, string unit)
        {
            // 包装回调：每次值变化打印日志
            var label_ = label; var unit_ = unit;
            int lastLogged = value;
            var origChange = onChange;
            onChange = (v) =>
            {
                origChange?.Invoke(v);
                if (v != lastLogged)
                {
                    wmj.Log.I($"[Settings] 修改 {label_} = {v}{unit_}", wmj.Log.Tag.UI);
                    lastLogged = v;
                }
            };
            var go = new GameObject("IntInput_" + label);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowBuildIndex % 2 == 0) ? RowEven : RowOdd;
            rowBg.raycastTarget = false;
            rowBuildIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            var lbl = UIFactory.CreateText(rowRt, "Label", label, 19,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.30f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            string displayStr = string.IsNullOrEmpty(unit) ? value.ToString() : value.ToString() + " " + unit;
            var intDisplay = UIFactory.CreateText(rowRt, "Value", displayStr, 19,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            intDisplay.rectTransform.anchorMin = new Vector2(0.32f, 0f);
            intDisplay.rectTransform.anchorMax = new Vector2(0.50f, 1f);
            intDisplay.rectTransform.offsetMin = Vector2.zero;
            intDisplay.rectTransform.offsetMax = Vector2.zero;

            var rangeText = UIFactory.CreateText(rowRt, "Range", "(" + min + "~" + max + ")", 14,
                TextAlignmentOptions.Left, UIColors.WithAlpha(UIColors.Silver, 0.5f));
            rangeText.rectTransform.anchorMin = new Vector2(0.52f, 0f);
            rangeText.rectTransform.anchorMax = new Vector2(0.66f, 1f);
            rangeText.rectTransform.offsetMin = Vector2.zero;
            rangeText.rectTransform.offsetMax = Vector2.zero;

            var hint = UIFactory.CreateText(rowRt, "Hint", "[\u2190\u2192] [R]\u91cd\u7f6e [Enter]\u8f93\u5165",
                13, TextAlignmentOptions.Right, HintColor);
            hint.rectTransform.anchorMin = new Vector2(0.67f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.99f, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var row = new RowData
            {
                type = RowType.IntInput,
                go = go, rowBg = rowBg, hintText = hint,
                intValue = value, intMin = min, intMax = max,
                intDefault = defaultVal, onIntChange = onChange,
                intDisplay = intDisplay, rangeText = rangeText,
                unit = unit
            };
            rows.Add(row);
        }

        private void AddTextInputRow(Transform content, string label, string value,
            System.Action<string> onChange)
        {
            // 包装回调：每次文本变化打印日志
            var label_ = label;
            string lastLogged = value ?? "";
            var origChange = onChange;
            onChange = (v) =>
            {
                origChange?.Invoke(v);
                if (v != lastLogged)
                {
                    wmj.Log.I($"[Settings] 修改 {label_} = \"{v}\"", wmj.Log.Tag.UI);
                    lastLogged = v;
                }
            };
            var go = new GameObject("TextInput_" + label);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowBuildIndex % 2 == 0) ? RowEven : RowOdd;
            rowBg.raycastTarget = false;
            rowBuildIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            var lbl = UIFactory.CreateText(rowRt, "Label", label, 19,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.28f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            var textBoxGo = new GameObject("TextBox");
            textBoxGo.transform.SetParent(rowRt, false);
            var textBoxRt = textBoxGo.AddComponent<RectTransform>();
            textBoxRt.anchorMin = new Vector2(0.30f, 0.12f);
            textBoxRt.anchorMax = new Vector2(0.72f, 0.88f);
            textBoxRt.offsetMin = Vector2.zero;
            textBoxRt.offsetMax = Vector2.zero;
            var textBoxBg = textBoxGo.AddComponent<Image>();
            textBoxBg.color = new Color(0.06f, 0.07f, 0.14f, 0.90f);
            textBoxBg.raycastTarget = false;

            var textDisplay = UIFactory.CreateText(textBoxGo.transform, "Text", value ?? "", 18,
                TextAlignmentOptions.Left, UIColors.White);
            textDisplay.rectTransform.anchorMin = new Vector2(0.03f, 0f);
            textDisplay.rectTransform.anchorMax = new Vector2(0.97f, 1f);
            textDisplay.rectTransform.offsetMin = Vector2.zero;
            textDisplay.rectTransform.offsetMax = Vector2.zero;

            var hint = UIFactory.CreateText(rowRt, "Hint", "[Enter] \u7f16\u8f91",
                13, TextAlignmentOptions.Right, HintColor);
            hint.rectTransform.anchorMin = new Vector2(0.77f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.99f, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var row = new RowData
            {
                type = RowType.TextInput,
                go = go, rowBg = rowBg, hintText = hint,
                textValue = value ?? "", onTextChange = onChange,
                textDisplay = textDisplay
            };
            rows.Add(row);
        }

        private void AddKeyBindingRow(Transform content, string label,
            AmmoKeyBinding binding, int index)
        {
            var go = new GameObject("Key_" + label);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowBuildIndex % 2 == 0) ? RowEven : RowOdd;
            rowBg.raycastTarget = false;
            rowBuildIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            var lbl = UIFactory.CreateText(rowRt, "Label", label, 19,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.35f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            string keyName = FormatKeyName(((KeyCode)binding.keyCode).ToString());
            var keyDisplay = UIFactory.CreateText(rowRt, "Key", keyName, 20,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            keyDisplay.rectTransform.anchorMin = new Vector2(0.38f, 0f);
            keyDisplay.rectTransform.anchorMax = new Vector2(0.58f, 1f);
            keyDisplay.rectTransform.offsetMin = Vector2.zero;
            keyDisplay.rectTransform.offsetMax = Vector2.zero;

            var hint = UIFactory.CreateText(rowRt, "Hint", "[Enter] \u6539\u952e  [R] \u91cd\u7f6e",
                13, TextAlignmentOptions.Right, HintColor);
            hint.rectTransform.anchorMin = new Vector2(0.60f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.99f, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var row = new RowData
            {
                type = RowType.KeyBinding,
                go = go, rowBg = rowBg, hintText = hint,
                binding = binding, bindingIndex = index,
                keyDisplay = keyDisplay
            };
            rows.Add(row);
        }

        private void AddButtonRow(Transform content, string label, System.Action onClick)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 55;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowBuildIndex % 2 == 0) ? RowEven : RowOdd;
            rowBg.raycastTarget = false;
            rowBuildIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            var lbl = UIFactory.CreateText(rowRt, "Label", label, 20,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            lbl.rectTransform.anchorMin = new Vector2(0.15f, 0.10f);
            lbl.rectTransform.anchorMax = new Vector2(0.70f, 0.90f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            var hint = UIFactory.CreateText(rowRt, "Hint", "[Enter/Space] \u6267\u884c",
                13, TextAlignmentOptions.Right, HintColor);
            hint.rectTransform.anchorMin = new Vector2(0.72f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.99f, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            var row = new RowData
            {
                type = RowType.Button,
                go = go, rowBg = rowBg, hintText = hint,
                onButtonClick = onClick
            };
            rows.Add(row);
        }

        // ═══════════════════ 视觉辅助 ═══════════════════

        private static void UpdateToggleVisual(Image bg, TextMeshProUGUI label, bool isOn)
        {
            if (isOn)
            {
                if (bg) bg.color = UIColors.WithAlpha(new Color(0.2f, 0.6f, 0.9f), 0.65f);
                if (label) { label.text = "\u25cf \u542f\u7528"; label.color = UIColors.White; }
            }
            else
            {
                if (bg) bg.color = new Color(0.15f, 0.15f, 0.20f, 0.50f);
                if (label) { label.text = "\u25cb \u5173\u95ed"; label.color = UIColors.WithAlpha(UIColors.Silver, 0.6f); }
            }
        }

        private void UpdateSliderValueText(RowData row)
        {
            if (row.valueText && row.slider)
                row.valueText.text = row.slider.value.ToString(row.fmt) + row.unit;
        }

        // ═══════════════════ 操作回调 ═══════════════════

        private void OnSaveClicked()
        {
            UILayoutManager.Save();
            try { ConfigLoader.SaveConfig(); } catch (System.Exception e) { wmj.Log.W($"[Settings] ConfigLoader.SaveConfig 失败: {e.Message}", wmj.Log.Tag.UI); }
            try { GameParamsConfig.Save(); } catch (System.Exception e) { wmj.Log.W($"[Settings] GameParamsConfig.Save 失败: {e.Message}", wmj.Log.Tag.UI); }
            if (BattleHUD.Instance != null) BattleHUD.Instance.RebuildHUD();
            wmj.Log.I("[Settings] ✅ 所有设置已保存并应用 (UILayout + params.json + game_params.json)", wmj.Log.Tag.UI);
        }

        private void OnResetAllClicked()
        {
            UILayoutManager.Data.hudSettings = new HUDSettings();
            UILayoutManager.Data.elements.Clear();
            UILayoutManager.Save();

            HidePanel();
            if (panelRoot != null) Destroy(panelRoot);
            panelRoot = null;
            isOpen = false;
            ToggleSettings();
        }

        private void OnReselectClicked()
        {
            HidePanel();
            if (BattleHUD.Instance != null) BattleHUD.Instance.Shutdown();
            RobotSelectionBootstrap.ResetSelection();
            RobotSelectionPanel.Show(result =>
            {
                RobotSelectionBootstrap.ApplySelection(result);
                wmj.Log.I("[Settings] \u91cd\u65b0\u9009\u62e9\u5b8c\u6210: " + result, wmj.Log.Tag.UI);
            });
        }

        // ═══════════════════ 辅助方法 ═══════════════════

        private static string FormatKeyName(string rawName)
        {
            if (rawName.StartsWith("Alpha")) return rawName.Substring(5);
            if (rawName.StartsWith("Keypad")) return "Num" + rawName.Substring(6);
            return rawName;
        }

        private void ScheduleLivePreview()
        {
            if (previewCoroutine != null) StopCoroutine(previewCoroutine);
            previewCoroutine = StartCoroutine(DebouncedPreview());
        }

        private IEnumerator DebouncedPreview()
        {
            yield return new WaitForSeconds(PREVIEW_DELAY);
            previewCoroutine = null;
            UILayoutManager.Save();
            try { ConfigLoader.SaveConfig(); } catch { }
            try { GameParamsConfig.Save(); } catch { }
            if (BattleHUD.Instance != null)
                BattleHUD.Instance.RebuildHUD();
            wmj.Log.D("[Settings] 实时预览已保存到磁盘 (自动持久化)", wmj.Log.Tag.UI);
        }

        private IEnumerator ResetKeyLabelAfterDelay(RowData row)
        {
            yield return new WaitForSeconds(1.5f);
            if (row.keyDisplay)
            {
                row.keyDisplay.text = FormatKeyName(((KeyCode)row.binding.keyCode).ToString());
                row.keyDisplay.color = UIColors.White;
            }
        }
    }
}
