using UnityEngine;
using UnityEngine.UI;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 射击聚焦（开镜）效果 — 数字变焦
    /// 原理：修改视频 RawImage 的 uvRect 使画面中心放大，模拟 FPS 开镜效果。
    /// 逻辑：射击时自动开镜，停火后延迟关镜，全程丝滑过渡。
    /// 设置：是否启用、倍率、过渡速度、关镜延迟，均可在设置面板调节。
    ///
    /// 之所以不用 Camera.main.fieldOfView：
    ///   本项目视频画面通过 RawImage + ScreenSpaceOverlay Canvas 显示，
    ///   3D Camera 不渲染任何可见内容，改 FOV 无效。
    /// </summary>
    public class AimZoomHUD : MonoBehaviour
    {
        private RawImage videoRawImage;
        private Rect defaultUVRect;
        private bool foundVideo;

        // ─── 状态 ───
        private bool isZoomed;
        private float idleTimer;
        private uint lastProjectilesFired;
        private float currentZoom = 1f;     // 1 = 无缩放, >1 = 放大

        // ─── 开镜暗角遮罩 ───
        private Image vignetteOverlay;
        private CanvasGroup vignetteGroup;
        private const float VIGNETTE_ALPHA = 0.25f;

        void Awake()
        {
            gameObject.AddComponent<RectTransform>();
        }

        void Start()
        {
            TryFindVideoRawImage();
            BuildVignette();
        }

        /// <summary>查找场景中的视频 RawImage</summary>
        private void TryFindVideoRawImage()
        {
            // 优先通过 VideoTextureView 组件查找
            var vtv = Object.FindAnyObjectByType<VideoTextureView>();
            if (vtv != null && vtv.TargetRawImage != null)
            {
                videoRawImage = vtv.TargetRawImage;
                defaultUVRect = videoRawImage.uvRect;
                foundVideo = true;
                wmj.Log.I($"[AimZoom] 找到视频 RawImage: uvRect={defaultUVRect}", wmj.Log.Tag.UI);
                return;
            }

            // 后备：搜索名称包含 "Video" 的 RawImage
            var allRaw = Object.FindObjectsByType<RawImage>(FindObjectsSortMode.None);
            foreach (var ri in allRaw)
            {
                if (ri.gameObject.name.Contains("Video") || ri.gameObject.name.Contains("video"))
                {
                    videoRawImage = ri;
                    defaultUVRect = ri.uvRect;
                    foundVideo = true;
                    wmj.Log.I($"[AimZoom] 通过名称找到视频 RawImage: {ri.gameObject.name}", wmj.Log.Tag.UI);
                    return;
                }
            }

            wmj.Log.W("[AimZoom] 未找到视频 RawImage，开镜功能不可用", wmj.Log.Tag.UI);
        }

        /// <summary>构建开镜暗角遮罩（边缘微暗，增强聚焦感）</summary>
        private void BuildVignette()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var vigGo = new GameObject("AimVignette");
            vigGo.transform.SetParent(canvas.transform, false);
            vignetteOverlay = vigGo.AddComponent<Image>();
            // 使用径向渐变纹理模拟暗角
            vignetteOverlay.sprite = CreateVignetteSprite();
            vignetteOverlay.type = Image.Type.Simple;
            vignetteOverlay.preserveAspect = false;
            vignetteOverlay.color = new Color(0f, 0f, 0f, 1f);
            vignetteOverlay.raycastTarget = false;
            UIFactory.SetFullStretch(vignetteOverlay.rectTransform);

            vignetteGroup = UIFactory.EnsureCanvasGroup(vigGo);
            vignetteGroup.alpha = 0f;
            vignetteGroup.blocksRaycasts = false;
            vignetteGroup.interactable = false;
        }

        void Update()
        {
            // 延迟查找（视频可能在 HUD 之后初始化）
            if (!foundVideo)
            {
                TryFindVideoRawImage();
                if (!foundVideo) return;
            }

            var s = UILayoutManager.Settings;

            // 功能关闭 → 恢复默认
            if (!s.aimZoomEnabled)
            {
                if (currentZoom > 1.001f)
                {
                    currentZoom = Mathf.Lerp(currentZoom, 1f, Time.deltaTime * s.aimZoomSpeed);
                    if (currentZoom < 1.001f) currentZoom = 1f;
                    ApplyZoom(currentZoom);
                }
                isZoomed = false;
                return;
            }

            // 空闲计时 → 自动关镜
            if (isZoomed)
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= s.aimZoomCloseDelay)
                    isZoomed = false;
            }

            // 目标缩放值
            float targetZoom = isZoomed ? Mathf.Max(s.aimZoomFactor, 1.01f) : 1f;

            // 平滑过渡
            if (!Mathf.Approximately(currentZoom, targetZoom))
            {
                currentZoom = Mathf.Lerp(currentZoom, targetZoom, Time.deltaTime * s.aimZoomSpeed);

                // 接近目标时吸附
                if (Mathf.Abs(currentZoom - targetZoom) < 0.005f)
                    currentZoom = targetZoom;

                ApplyZoom(currentZoom);
            }
        }

        /// <summary>应用缩放到视频 RawImage 的 uvRect</summary>
        private void ApplyZoom(float zoom)
        {
            if (videoRawImage == null) return;

            // 计算居中裁剪的 UV 区域
            // 默认 uvRect = {0, 1, 1, -1}（Y 翻转的视频纹理）
            float defW = defaultUVRect.width;   //  1
            float defH = defaultUVRect.height;  // -1
            float defX = defaultUVRect.x;       //  0
            float defY = defaultUVRect.y;       //  1

            float newW = defW / zoom;
            float newH = defH / zoom;
            float newX = defX + (defW - newW) * 0.5f;
            float newY = defY + (defH - newH) * 0.5f;

            videoRawImage.uvRect = new Rect(newX, newY, newW, newH);

            // 暗角强度跟随缩放比例
            if (vignetteGroup != null)
            {
                float t = Mathf.InverseLerp(1f, 3f, zoom);
                vignetteGroup.alpha = t * VIGNETTE_ALPHA;
            }
        }

        // ─── 外部接口 ───

        /// <summary>
        /// 由 BattleHUD 每帧传入 TotalProjectilesFired。
        /// 数值增加即表示正在射击 → 进入开镜态并重置空闲计时。
        /// </summary>
        public void NotifyShooting(uint totalProjectilesFired)
        {
            if (totalProjectilesFired > lastProjectilesFired)
            {
                isZoomed = true;
                idleTimer = 0f;
            }
            lastProjectilesFired = totalProjectilesFired;
        }

        /// <summary>强制设置开镜状态（兼容外部调用）</summary>
        public void SetAiming(bool aiming)
        {
            if (aiming) { isZoomed = true; idleTimer = 0f; }
            else { isZoomed = false; }
        }

        void OnDestroy()
        {
            // 确保销毁时恢复默认 uvRect
            if (videoRawImage != null && foundVideo)
                videoRawImage.uvRect = defaultUVRect;
        }

        // ─── 暗角纹理生成 ───

        private static Sprite CreateVignetteSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size * 0.5f;
            float maxDist = center * 1.0f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // 从中心透明到边缘不透明
                    float t = Mathf.Clamp01((dist - maxDist * 0.4f) / (maxDist * 0.6f));
                    t = t * t; // 平方曲线，边缘更明显
                    tex.SetPixel(x, y, new Color(0, 0, 0, t));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f));
        }
    }
}
