using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UI.Core
{
    /// <summary>
    /// HUD 布局持久化 — 保存/加载各 UI 元素的自定义位置
    /// 存储于 StreamingAssets/Config/ui_layout.json
    /// </summary>
    [Serializable]
    public class UILayoutData
    {
        public List<UIElementLayout> elements = new List<UIElementLayout>();
        public HUDSettings hudSettings = new HUDSettings();
    }

    [Serializable]
    public class UIElementLayout
    {
        public string id;           // 元素唯一标识（如 "HealthBar", "CrosshairRing"）
        public float anchorX;       // 中心锚点 X (0~1)
        public float anchorY;       // 中心锚点 Y (0~1)
        public float width;         // 宽度 (像素, 基于 1920x1080)
        public float height;        // 高度
        public float scale;         // 缩放
        public bool visible;        // 是否可见
    }

    [Serializable]
    public class HUDSettings
    {
        // 通知
        public float notificationDuration = 2f;        // 增益/惩罚通知显示时长
        public int maxNotifications = 5;               // 同时显示最大通知数

        // 开镜
        public float aimZoomFactor = 1.5f;             // 射击聚焦倍率
        public float aimZoomSpeed = 8f;                // 聚焦速度

        // 受击提示
        public float hitFlashDuration = 0.3f;          // 受击闪烁持续时间
        public float lowHealthThreshold = 0.6f;        // 血量低于此比例开始红色提示

        // 准星环
        public float crosshairRingRadius = 140f;       // 准星环半径 (px)
        public float crosshairRingThickness = 2.5f;    // 准星环线宽
        public float crosshairDotSize = 8f;
        public float crosshairLineLength = 40f;

        // 血条
        public float healthBarWidth = 650f;
        public float healthBarHeight = 28f;

        // 每模块字体大小
        public int crosshairFontSize = 36;
        public int healthBarFontSize = 30;
        public int notificationFontSize = 32;
        public int buffFontSize = 26;
        public float textOpacity = 1.0f;

        /// <summary>返回所有设置的默认值（用于重置）</summary>
        public static HUDSettings Defaults() => new HUDSettings();
    }

    public static class UILayoutManager
    {
        private static UILayoutData _data;
        private static string _path;

        public static UILayoutData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        public static HUDSettings Settings => Data.hudSettings;

        public static void Load()
        {
            _path = Path.Combine(Application.streamingAssetsPath, "Config/ui_layout.json");
            if (File.Exists(_path))
            {
                try
                {
                    string json = File.ReadAllText(_path);
                    _data = JsonUtility.FromJson<UILayoutData>(json);
                    wmj.Log.I($"[UILayout] 已加载布局配置: {_path}", wmj.Log.Tag.UI);
                }
                catch (Exception e)
                {
                    wmj.Log.W($"[UILayout] 加载布局失败，使用默认值: {e.Message}", wmj.Log.Tag.UI);
                    _data = new UILayoutData();
                }
            }
            else
            {
                _data = new UILayoutData();
                Save(); // 创建默认配置
            }
        }

        public static void Save()
        {
            try
            {
                if (_data == null) _data = new UILayoutData();

                if (_path == null)
                    _path = Path.Combine(Application.streamingAssetsPath, "Config/ui_layout.json");

                string dir = Path.GetDirectoryName(_path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(_path, json);
                wmj.Log.I($"[UILayout] 布局已保存: {_path}", wmj.Log.Tag.UI);
            }
            catch (Exception e)
            {
                wmj.Log.E($"[UILayout] 保存布局失败: {e.Message}", wmj.Log.Tag.UI);
            }
        }

        /// <summary>
        /// 获取或创建元素布局
        /// </summary>
        public static UIElementLayout GetElement(string id, float defaultX = 0.5f, float defaultY = 0.5f,
            float defaultW = 200f, float defaultH = 50f, float defaultScale = 1f)
        {
            var data = Data;
            var el = data.elements.Find(e => e.id == id);
            if (el == null)
            {
                el = new UIElementLayout
                {
                    id = id,
                    anchorX = defaultX,
                    anchorY = defaultY,
                    width = defaultW,
                    height = defaultH,
                    scale = defaultScale,
                    visible = true
                };
                data.elements.Add(el);
            }
            return el;
        }

        /// <summary>
        /// 将布局数据应用到 RectTransform
        /// </summary>
        public static void ApplyLayout(RectTransform rt, UIElementLayout layout)
        {
            if (rt == null || layout == null) return;
            rt.anchorMin = new Vector2(layout.anchorX, layout.anchorY);
            rt.anchorMax = new Vector2(layout.anchorX, layout.anchorY);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(layout.width, layout.height);
            rt.localScale = Vector3.one * layout.scale;
        }

        /// <summary>
        /// 从 RectTransform 保存布局
        /// </summary>
        public static void SaveFromTransform(RectTransform rt, UIElementLayout layout)
        {
            if (rt == null || layout == null) return;
            layout.anchorX = rt.anchorMin.x;
            layout.anchorY = rt.anchorMin.y;
            layout.width = rt.sizeDelta.x;
            layout.height = rt.sizeDelta.y;
            layout.scale = rt.localScale.x;
        }
    }
}
