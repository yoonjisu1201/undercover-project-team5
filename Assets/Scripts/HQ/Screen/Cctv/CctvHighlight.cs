using EPOOutline;
using UnityEngine;

// CCTV 전용 외곽선을 만드는 공용 도구.
// 아이템(ItemBase)은 예전부터 자체 구현을 갖고 있고, 이후에 추가된 대상(미션 장치 등)이 이것을 쓴다.
// 외곽선 모양이 아이템과 같아야 하므로 파라미터는 ItemBase.CreateCctvOutline과 맞춰 둔다.
public static class CctvHighlight
{
    public static Outlinable CreateOutline(Transform owner, int unityLayer, Renderer[] renderers)
    {
        GameObject outlineObject = new GameObject("CctvOutline");
        outlineObject.transform.SetParent(owner, false);
        outlineObject.layer = unityLayer;

        Outlinable outline = outlineObject.AddComponent<Outlinable>();
        outline.OutlineLayer = ItemBase.CctvOutlineLayer;
        outline.DrawingMode = OutlinableDrawingMode.Normal;

        // Single은 깊이 비교가 Always라 벽 뒤 대상까지 비친다.
        // FrontBack으로 앞면(보이는 부분)만 그리고 뒷면(가려진 부분)은 꺼서 가려지도록 한다.
        outline.RenderStyle = RenderStyle.FrontBack;
        outline.OutlineParameters.Enabled = false;
        outline.BackParameters.Enabled = false;
        outline.FrontParameters.Enabled = true;
        outline.FrontParameters.Color = Color.yellow;
        outline.FrontParameters.DilateShift = 1f;
        outline.FrontParameters.BlurShift = 1f;

        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer != null)
            {
                outline.AddRenderer(targetRenderer);
            }
        }

        return outline;
    }

    // 활성화된 렌더러들을 합친 월드 바운즈. CCTV 조준 표시가 화면 사각형을 잡을 때 쓴다.
    public static Bounds GetWorldBounds(Renderer[] renderers)
    {
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null || !targetRenderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = targetRenderer.bounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(targetRenderer.bounds);
        }

        return bounds;
    }
}
