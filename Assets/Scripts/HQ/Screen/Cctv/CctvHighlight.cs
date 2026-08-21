using System.Collections.Generic;
using EPOOutline;
using UnityEngine;

// CCTV 화면에서 외곽선과 이름 표시의 대상이 되는 오브젝트.
// 아이템(ItemBase)과 필드 미션 장치(MissionInteractable)가 함께 구현한다.
public interface ICctvHighlightTarget
{
    // 화면 사각형을 잡을 때 쓰는 월드 바운즈.
    Bounds CctvBounds { get; }

    // 커서를 올렸을 때 띄울 이름.
    string CctvDisplayName { get; }

    // 인벤토리에 들어간 아이템처럼 월드에 없는 동안은 제외한다.
    bool IsVisibleOnCctv { get; }
}

// CCTV 전용 외곽선의 공용 설정과 대상 목록을 한곳에서 관리한다.
public static class CctvHighlight
{
    // EPO 아웃라인 레이어(Unity 레이어와 무관한 EPO 내부 0~7 값).
    // 이 레이어는 CCTV 오버레이 카메라의 Outliner에서만 켜져 있어서 1인칭 카메라에는 그려지지 않는다.
    public const int OutlineLayer = 5;
    public const long OutlineMask = 1L << OutlineLayer;

    private static readonly List<ICctvHighlightTarget> Targets = new();

    // CCTV 화면에서 커서 아래 대상을 찾을 때 순회한다.
    public static IReadOnlyList<ICctvHighlightTarget> RegisteredTargets => Targets;

    public static void Register(ICctvHighlightTarget target)
    {
        if (target != null && !Targets.Contains(target))
        {
            Targets.Add(target);
        }
    }

    public static void Unregister(ICctvHighlightTarget target)
    {
        Targets.Remove(target);
    }

    // 아이템과 미션 장치가 같은 모양의 외곽선을 쓰도록 생성을 한곳에 둔다.
    // 1인칭 외곽선(InteractableBase가 잡는 Outlinable)과 섞이지 않도록 반드시 base.Awake() 뒤에 부른다.
    public static Outlinable CreateOutline(Transform owner, int unityLayer, Renderer[] renderers)
    {
        GameObject outlineObject = new GameObject("CctvOutline");
        outlineObject.transform.SetParent(owner, false);
        outlineObject.layer = unityLayer;

        Outlinable outline = outlineObject.AddComponent<Outlinable>();
        outline.OutlineLayer = OutlineLayer;
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

    // 활성화된 렌더러들을 합친 월드 바운즈.
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
