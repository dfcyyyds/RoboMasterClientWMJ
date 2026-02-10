using System.Collections.Generic;
using UnityEngine;

namespace UI.Core
{
    /// <summary>
    /// 图标管理器 — 从 Resources/Icons 加载 PNG 图标并缓存为 Sprite
    /// 图标统一 128×128，白色/透明通道，运行时可着色
    /// </summary>
    public static class IconManager
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static bool _initialized;

        /// <summary>预定义图标名称常量</summary>
        public const string ICON_SETTING = "setting";
        public const string ICON_CANCEL = "cancel";
        public const string ICON_WARNING = "warning";
        public const string ICON_FATAL_WARNING = "fatalWarning";
        public const string ICON_INFORM = "inform";
        public const string ICON_PILL = "pill";
        public const string ICON_PULL = "pull";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            PreloadAll();
        }

        private static void PreloadAll()
        {
            string[] names = { ICON_SETTING, ICON_CANCEL, ICON_WARNING,
                               ICON_FATAL_WARNING, ICON_INFORM, ICON_PILL, ICON_PULL };
            foreach (var name in names)
            {
                Load(name);
            }
            wmj.Log.I($"[IconManager] 预加载了 {_cache.Count} 个图标", wmj.Log.Tag.UI);
        }

        /// <summary>
        /// 加载指定名称的图标 Sprite（带缓存）
        /// </summary>
        public static Sprite Load(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;
            if (_cache.TryGetValue(iconName, out var cached)) return cached;

            var tex = Resources.Load<Texture2D>($"Icons/{iconName}");
            if (tex == null)
            {
                wmj.Log.W($"[IconManager] 图标未找到: Icons/{iconName}", wmj.Log.Tag.UI);
                return null;
            }

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            sprite.name = $"Icon_{iconName}";
            _cache[iconName] = sprite;
            return sprite;
        }

        /// <summary>
        /// 获取缓存的图标，如果未加载则尝试加载
        /// </summary>
        public static Sprite Get(string iconName) => Load(iconName);
    }
}
