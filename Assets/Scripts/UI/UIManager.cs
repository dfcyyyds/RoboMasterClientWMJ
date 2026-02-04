using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 全局 UI 管理器 - 管理弹窗生命周期和层级
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        private static UIManager _instance;
        public static UIManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[UIManager]");
                    _instance = go.AddComponent<UIManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // 弹窗层级管理
        private readonly Dictionary<string, GameObject> _activePanels = new Dictionary<string, GameObject>();
        private int _nextSortingOrder = 10000;

        /// <summary>
        /// 注册一个弹窗
        /// </summary>
        public void RegisterPanel(string panelId, GameObject panel)
        {
            if (_activePanels.ContainsKey(panelId))
            {
                wmj.DebugTools.Warn($"[UIManager] 面板 {panelId} 已存在，将被替换", wmj.DebugTools.LogCategory.UI);
                UnregisterPanel(panelId);
            }

            _activePanels[panelId] = panel;

            // 设置层级
            var canvas = panel.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.sortingOrder = _nextSortingOrder++;
            }

            wmj.DebugTools.Debug($"[UIManager] 注册面板: {panelId}", wmj.DebugTools.LogCategory.UI);
        }

        /// <summary>
        /// 注销一个弹窗
        /// </summary>
        public void UnregisterPanel(string panelId)
        {
            if (_activePanels.TryGetValue(panelId, out var panel))
            {
                _activePanels.Remove(panelId);
                if (panel != null)
                {
                    Destroy(panel);
                }
                wmj.DebugTools.Debug($"[UIManager] 注销面板: {panelId}", wmj.DebugTools.LogCategory.UI);
            }
        }

        /// <summary>
        /// 获取一个弹窗
        /// </summary>
        public GameObject GetPanel(string panelId)
        {
            _activePanels.TryGetValue(panelId, out var panel);
            return panel;
        }

        /// <summary>
        /// 检查弹窗是否存在
        /// </summary>
        public bool HasPanel(string panelId)
        {
            return _activePanels.ContainsKey(panelId) && _activePanels[panelId] != null;
        }

        /// <summary>
        /// 关闭所有弹窗
        /// </summary>
        public void CloseAllPanels()
        {
            var panelIds = new List<string>(_activePanels.Keys);
            foreach (var id in panelIds)
            {
                UnregisterPanel(id);
            }
        }

        /// <summary>
        /// 获取当前活跃弹窗数量
        /// </summary>
        public int ActivePanelCount => _activePanels.Count;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// 弹窗基类 - 提供通用的显示/隐藏/销毁逻辑
    /// </summary>
    public abstract class PopupPanelBase : MonoBehaviour
    {
        protected abstract string PanelId { get; }

        protected virtual void Awake()
        {
            UIManager.Instance.RegisterPanel(PanelId, gameObject);
        }

        protected virtual void OnDestroy()
        {
            // Remove from UIManager if still registered
            if (UIManager.Instance.HasPanel(PanelId))
            {
                UIManager.Instance.UnregisterPanel(PanelId);
            }
        }

        /// <summary>
        /// Close and destroy panel
        /// </summary>
        public virtual void Close()
        {
            UIManager.Instance.UnregisterPanel(PanelId);
        }
    }
}
