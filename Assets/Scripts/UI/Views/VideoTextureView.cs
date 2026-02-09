using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// 简易视频纹理视图：将 VideoStreamService 的纹理挂到 RawImage 或 TMP_Sprite
public class VideoTextureView : MonoBehaviour
{
    public RawImage TargetRawImage;
    public Renderer TargetRenderer; // 如需贴到材质
    private Texture lastApplied;

    // 使用 LateUpdate 确保在 VideoStreamService.Update() 之后执行
    // 这样可以获取到当前帧的最新纹理
    void LateUpdate()
    {
        var svc = VideoStreamService.Instance;
        if (svc == null) return;
        var tex = svc.CurrentTexture;
        if (tex == null) return;
        if (tex == lastApplied) return; // 避免重复赋值
        if (TargetRawImage != null)
        {
            TargetRawImage.texture = tex;
        }
        if (TargetRenderer != null)
        {
            TargetRenderer.material.mainTexture = tex;
        }
        lastApplied = tex;
    }
}
