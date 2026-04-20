using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace UI.Core
{
    /// <summary>
    /// EventSystem 运行时健康监护组件
    /// 持续验证 EventSystem 和 InputSystemUIInputModule 的输入处理能力
    /// 异常时自动执行 disable→configure→enable 修复流程
    /// </summary>
    public class EventSystemGuard : MonoBehaviour
    {
        private InputSystemUIInputModule _module;
        private float _nextCheckTime;
        private int _repairCount;
        private bool _initialCheckDone;

        private const float CHECK_INTERVAL = 3f;
        private const float INITIAL_CHECK_DELAY = 0.5f;
        private const int MAX_REPAIRS = 5;

        void Start()
        {
            _module = GetComponent<InputSystemUIInputModule>();
            _nextCheckTime = Time.unscaledTime + INITIAL_CHECK_DELAY;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextCheckTime) return;
            _nextCheckTime = Time.unscaledTime + CHECK_INTERVAL;

            // 初次诊断 — 输出详细信息
            if (!_initialCheckDone)
            {
                _initialCheckDone = true;
                RunInitialDiagnostic();
            }

            // 检查 1: EventSystem.current 应该是我们
            var currentES = EventSystem.current;
            if (currentES == null)
            {
                Debug.LogWarning("[EventSystemGuard] EventSystem.current 为 null");
                var es = GetComponent<EventSystem>();
                if (es != null && es.isActiveAndEnabled)
                {
                    // 尝试通过 disable/enable 恢复 current 状态
                    es.enabled = false;
                    es.enabled = true;
                    Debug.Log("[EventSystemGuard] 已重新激活 EventSystem");
                }
                return;
            }

            if (currentES.gameObject != gameObject)
            {
                // 另一个 EventSystem 获得了 current — 如果它正常就不干预
                if (currentES.isActiveAndEnabled)
                {
                    var otherModule = currentES.GetComponent<InputSystemUIInputModule>();
                    if (otherModule != null && otherModule.isActiveAndEnabled && otherModule.actionsAsset != null)
                        return; // 另一个正常，不干预
                }

                // 另一个不正常，夺回 current
                var es = GetComponent<EventSystem>();
                if (es != null)
                {
                    es.enabled = false;
                    es.enabled = true;
                    Debug.Log("[EventSystemGuard] 已抢回 EventSystem.current");
                }
                return;
            }

            // 检查 2: InputModule 应该正常工作
            if (_module == null)
            {
                _module = GetComponent<InputSystemUIInputModule>();
            }

            if (_module == null || !_module.isActiveAndEnabled || _module.actionsAsset == null)
            {
                if (_repairCount < MAX_REPAIRS)
                {
                    RepairModule();
                }
                return;
            }

            // 检查 3: UI ActionMap 应该启用
            try
            {
                var uiMap = _module.actionsAsset.FindActionMap("UI");
                if (uiMap != null && !uiMap.enabled)
                {
                    uiMap.Enable();
                    Debug.Log("[EventSystemGuard] 检测到 UI ActionMap 被禁用，已重新启用");
                }
            }
            catch { }
        }

        private void RunInitialDiagnostic()
        {
            var es = EventSystem.current;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[EventSystemGuard] ═══ 初始诊断 ═══");
            sb.AppendLine($"  EventSystem.current = {es?.name ?? "NULL"}");
            sb.AppendLine($"  isActiveAndEnabled = {es?.isActiveAndEnabled}");

            if (_module != null)
            {
                sb.AppendLine($"  InputModule.enabled = {_module.enabled}");
                sb.AppendLine($"  InputModule.isActiveAndEnabled = {_module.isActiveAndEnabled}");
                sb.AppendLine($"  actionsAsset = {_module.actionsAsset?.name ?? "NULL"}");
                sb.AppendLine($"  point = {_module.point != null}");
                sb.AppendLine($"  leftClick = {_module.leftClick != null}");
                sb.AppendLine($"  scrollWheel = {_module.scrollWheel != null}");
                sb.AppendLine($"  move = {_module.move != null}");

                if (_module.actionsAsset != null)
                {
                    var uiMap = _module.actionsAsset.FindActionMap("UI");
                    if (uiMap != null)
                    {
                        sb.AppendLine($"  UI ActionMap: enabled={uiMap.enabled}, actions={uiMap.actions.Count}");
                        foreach (var action in uiMap.actions)
                        {
                            sb.AppendLine($"    {action.name}: enabled={action.enabled}, " +
                                $"type={action.type}, controls={action.controls.Count}");
                        }
                    }
                    else
                    {
                        sb.AppendLine("  UI ActionMap: NOT FOUND");
                    }
                }
            }
            else
            {
                sb.AppendLine("  InputModule: NULL");
            }

            Debug.Log(sb.ToString());

            // 如果初始诊断发现异常，立即修复
            if (_module == null || !_module.isActiveAndEnabled || _module.actionsAsset == null)
            {
                Debug.LogWarning("[EventSystemGuard] 初始诊断发现异常，执行修复");
                RepairModule();
            }
        }

        private void RepairModule()
        {
            _repairCount++;
            Debug.LogWarning($"[EventSystemGuard] 修复 InputModule (第{_repairCount}次/{MAX_REPAIRS})");

            if (_module == null)
            {
                _module = GetComponent<InputSystemUIInputModule>();
                if (_module == null)
                {
                    _module = gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }

            // 执行 disable → configure → enable 修复流程
            _module.enabled = false;

            if (_module.actionsAsset == null)
            {
                var asset = FindAsset();
                if (asset != null)
                {
                    _module.actionsAsset = asset;

                    // 绑定各个动作引用
                    var uiMap = asset.FindActionMap("UI");
                    if (uiMap != null)
                    {
                        BindAction(uiMap, "Point", r => _module.point = r);
                        BindAction(uiMap, "Click", r => _module.leftClick = r);
                        BindAction(uiMap, "ScrollWheel", r => _module.scrollWheel = r);
                        BindAction(uiMap, "Navigate", r => _module.move = r);
                        BindAction(uiMap, "Submit", r => _module.submit = r);
                        BindAction(uiMap, "Cancel", r => _module.cancel = r);
                        BindAction(uiMap, "RightClick", r => _module.rightClick = r);
                        BindAction(uiMap, "MiddleClick", r => _module.middleClick = r);
                    }
                }
            }

            // 确保 UI ActionMap 启用
            if (_module.actionsAsset != null)
            {
                var uiMap = _module.actionsAsset.FindActionMap("UI");
                if (uiMap != null) uiMap.Enable();
            }

            _module.enabled = true;

            Debug.Log($"[EventSystemGuard] 修复完成: isActiveAndEnabled={_module.isActiveAndEnabled}, " +
                $"actionsAsset={_module.actionsAsset?.name ?? "NULL"}");
        }

        private static InputActionAsset FindAsset()
        {
            try
            {
                var asset = InputSystem.actions;
                if (asset != null) return asset;
            }
            catch { }

            try
            {
                var found = Resources.FindObjectsOfTypeAll<InputActionAsset>();
                if (found != null)
                {
                    foreach (var a in found)
                        if (a.FindActionMap("UI") != null) return a;
                    if (found.Length > 0) return found[0];
                }
            }
            catch { }

            return null;
        }

        private static void BindAction(InputActionMap map, string actionName,
            System.Action<InputActionReference> setter)
        {
            var action = map.FindAction(actionName);
            if (action != null) setter(InputActionReference.Create(action));
        }
    }
}
