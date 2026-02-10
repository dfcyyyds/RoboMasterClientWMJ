using UnityEngine;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 射击聚焦（开镜）效果 — 放大中心画面，类似 FPS 开镜
    /// 通过修改摄像机 FOV 实现
    /// </summary>
    public class AimZoomHUD : MonoBehaviour
    {
        private Camera mainCamera;
        private float defaultFOV;
        private float targetFOV;
        private bool isAiming;

        void Awake()
        {
            gameObject.AddComponent<RectTransform>();
        }

        void Start()
        {
            mainCamera = Camera.main;
            if (mainCamera != null)
                defaultFOV = mainCamera.fieldOfView;
            else
                defaultFOV = 60f;
            targetFOV = defaultFOV;
        }

        void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
                defaultFOV = mainCamera.fieldOfView;
            }

            var settings = UILayoutManager.Settings;
            float desired = isAiming ? defaultFOV / settings.aimZoomFactor : defaultFOV;

            if (!Mathf.Approximately(mainCamera.fieldOfView, desired))
            {
                mainCamera.fieldOfView = Mathf.Lerp(
                    mainCamera.fieldOfView, desired,
                    Time.deltaTime * settings.aimZoomSpeed);
            }
        }

        /// <summary>设置开镜状态</summary>
        public void SetAiming(bool aiming)
        {
            isAiming = aiming;
        }
    }
}
