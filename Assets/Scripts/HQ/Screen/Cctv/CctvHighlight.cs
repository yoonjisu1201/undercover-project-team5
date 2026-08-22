using System;
using System.Collections.Generic;
using EPOOutline;
using UnityEngine;

// CCTV 외곽선 대상의 종류. 값이 곧 EPO 아웃라인 레이어(EPO 내부 0~7)다.
// 종류마다 레이어가 다르므로 Outliner의 OutlineLayerMask 비트만 켜고 끄면 종류별로 표시를 나눌 수 있다.
public enum CctvHighlightKind
{
    Item = 5,
    MissionMachine = 6,
    Npc = 7
}

// CCTV 화면에서 외곽선과 이름 표시의 대상이 되는 오브젝트.
public interface ICctvHighlightTarget
{
    CctvHighlightKind CctvKind { get; }

    // 화면 사각형을 잡을 때 쓰는 월드 바운즈.
    Bounds CctvBounds { get; }

    // 커서를 올렸을 때 띄울 이름.
    string CctvDisplayName { get; }

    // 인벤토리에 들어간 아이템처럼 월드에 없는 동안은 제외한다.
    bool IsVisibleOnCctv { get; }
}

// 이름 대신 착용 의상 이미지를 보여주는 대상(NPC)이 추가로 구현한다.
// ICctvHighlightTarget을 늘리지 않도록 별도 인터페이스로 둔다.
public interface ICctvOutfitPreview
{
    // 아직 준비되지 않았으면 null을 돌려도 된다. 다음 프레임에 다시 묻는다.
    IReadOnlyList<Sprite> CctvOutfitThumbnails { get; }
}

// 아이템·미션 장치·NPC의 CCTV 외곽선을 한곳에서 관리한다.
// 외곽선 자체는 EPO가 그리고, 여기서는 어떤 종류를 그릴지와 대상 목록을 들고 있다.
public static class CctvHighlight
{
    // 어떤 종류를 CCTV에 표시할지. 비트를 끄면 그 종류는 외곽선이 그려지지 않는다.
    private static long _enabledMask = MaskOf(CctvHighlightKind.Item)
                                      | MaskOf(CctvHighlightKind.MissionMachine)
                                      | MaskOf(CctvHighlightKind.Npc);

    private static readonly List<ICctvHighlightTarget> Targets = new();

    // 대상별 CCTV 외곽선. 멀어서 점처럼 보이는 대상의 외곽선을 끄는 데 쓴다.
    private static readonly Dictionary<ICctvHighlightTarget, Outlinable> Outlines = new();

    // CCTV 화면에서 커서 아래 대상을 찾을 때 순회한다.
    public static IReadOnlyList<ICctvHighlightTarget> RegisteredTargets => Targets;

    // Outliner에 넣을 마스크. 표시하기로 한 종류의 비트만 켜져 있다.
    public static long EnabledMask => _enabledMask;

    // 1인칭 카메라에서 CCTV 외곽선을 전부 제외할 때 쓴다.
    public static long AllKindsMask => MaskOf(CctvHighlightKind.Item)
                                      | MaskOf(CctvHighlightKind.MissionMachine)
                                      | MaskOf(CctvHighlightKind.Npc);

    // 표시 종류가 바뀌면 알린다. CCTVHub가 Outliner 마스크를 갱신한다.
    public static event Action EnabledKindsChanged;

    // CCTV 오버레이 카메라가 그리는 레이어. 이 레이어가 아닌 오브젝트는 CCTV 화면에 절대 나오지 않는다.
    public static int CameraLayerMask => (1 << Layers.Item) | (1 << Layers.NotInMinimap);

    public static bool IsOnCameraLayer(int layer) => (CameraLayerMask & (1 << layer)) != 0;

    public static long MaskOf(CctvHighlightKind kind) => 1L << (int)kind;

    public static bool IsKindEnabled(CctvHighlightKind kind) => (_enabledMask & MaskOf(kind)) != 0L;

    // 특정 종류만 CCTV에 표시하고 싶을 때 쓴다. 예: 미션 장치만 켜기.
    public static void SetKindEnabled(CctvHighlightKind kind, bool isEnabled)
    {
        long mask = MaskOf(kind);
        long updated = isEnabled ? _enabledMask | mask : _enabledMask & ~mask;

        if (updated == _enabledMask)
        {
            return;
        }

        _enabledMask = updated;
        EnabledKindsChanged?.Invoke();
    }

    // 등록은 CreateOutline이 맡는다. 외곽선 없이 목록에만 있는 대상이 생기지 않도록 외부에 열지 않는다.
    private static void Register(ICctvHighlightTarget target)
    {
        if (target != null && !Targets.Contains(target))
        {
            Targets.Add(target);
        }
    }

    public static void Unregister(ICctvHighlightTarget target)
    {
        Targets.Remove(target);
        Outlines.Remove(target);
    }

    public static bool IsOutlineEnabled(ICctvHighlightTarget target)
    {
        return Outlines.TryGetValue(target, out Outlinable outline) && outline != null && outline.enabled;
    }

    // 멀어서 작게 그려진 대상은 외곽선을 끈다. 외곽선이 없으면 조준·이름 표시도 따라서 걸러진다.
    public static void SetOutlineEnabled(ICctvHighlightTarget target, bool isEnabled)
    {
        if (Outlines.TryGetValue(target, out Outlinable outline) && outline != null)
        {
            outline.enabled = isEnabled;
        }
    }

    // 대상 등록과 외곽선 생성을 함께 한다. 둘로 나누면 외곽선만 있고 목록에는 없는 대상이 생긴다.
    // 종류마다 EPO 레이어가 다를 뿐, 외곽선 모양은 모두 같다.
    // 1인칭 외곽선(InteractableBase가 잡는 Outlinable)과 섞이지 않도록 반드시 base.Awake() 뒤에 부른다.
    public static Outlinable CreateOutline(
        ICctvHighlightTarget target,
        Transform owner,
        int unityLayer,
        Renderer[] renderers,
        CctvHighlightKind kind)
    {
        GameObject outlineObject = new GameObject("CctvOutline");
        outlineObject.transform.SetParent(owner, false);
        outlineObject.layer = unityLayer;

        Outlinable outline = outlineObject.AddComponent<Outlinable>();
        outline.OutlineLayer = (int)kind;
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

        Register(target);
        Outlines[target] = outline;

        return outline;
    }

    // 활성화된 렌더러들을 합친 월드 바운즈. CCTV 조준 표시가 화면 사각형을 잡을 때 쓴다.
    // 활성 렌더러가 하나도 없으면 default(Bounds)는 월드 원점이 되어 엉뚱한 곳에 조준이 잡힌다.
    // 그래서 대상 위치 기준의 작은 바운즈로 대체한다.
    public static Bounds GetWorldBounds(Renderer[] renderers, Vector3 fallbackPosition)
    {
        // NPC처럼 렌더러를 나중에 모으는 대상이 있어, 아직 준비되지 않은 경우를 막아 둔다.
        if (renderers == null)
        {
            return new Bounds(fallbackPosition, Vector3.one * 0.1f);
        }

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

        return hasBounds ? bounds : new Bounds(fallbackPosition, Vector3.one * 0.1f);
    }
}
