using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UI.Core
{
    /// <summary>
    /// UI 水平剪切（Skew / Shear）组件 —
    /// 水平边保持水平，垂直边按角度倾斜，形成平行四边形效果。
    /// 挂载到任何带 Graphic（Image/RawImage/Text）的 GameObject 上即可。
    /// 也会递归影响子 Graphic。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UISkew : BaseMeshEffect
    {
        [Tooltip("倾斜角度（度），正值 = 上沿右移（顺时针）")]
        [Range(-30f, 30f)]
        public float skewAngle = 5f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(skewAngle, 0f))
                return;

            var rt = graphic.rectTransform;
            float h = rt.rect.height;
            if (h <= 0f) return;

            // tan(angle) 决定水平偏移量与高度的比值
            float tanA = Mathf.Tan(skewAngle * Mathf.Deg2Rad);

            var verts = new List<UIVertex>();
            vh.GetUIVertexStream(verts);

            float pivotY = rt.rect.yMin; // 底边为基准

            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                // 根据 y 位置偏移 x
                float dy = v.position.y - pivotY;
                v.position.x += dy * tanA;
                verts[i] = v;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
        }
    }
}
