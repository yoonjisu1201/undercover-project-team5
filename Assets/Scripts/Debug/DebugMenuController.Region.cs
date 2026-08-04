using Unity.Netcode;
using UnityEngine;

// 필드 지역 해방과 상태 동기화를 처리합니다.
public sealed partial class DebugMenuController
{
    public void OnUnlockRegionAClick() => UnlockRegion("A");
    public void OnUnlockRegionBClick() => UnlockRegion("B");
    public void OnUnlockRegionCClick() => UnlockRegion("C");
    public void OnUnlockRegionDClick() => UnlockRegion("D");
    public void OnUnlockRegionEClick() => UnlockRegion("E");

    private void UnlockRegion(string regionId)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestUnlockRegionRpc(regionId);
    }

    // 로컬 지역을 해방하고 공용 스폰 지역 캐시를 갱신합니다.
    private void ApplyRegionUnlock(string regionId)
    {
        MapRegionController regionController = FindFirstObjectByType<MapRegionController>();
        if (regionController == null || !regionController.SetActiveRegion(regionId))
        {
            ShowStatus($"지역 {regionId}를 찾지 못했습니다.");
            return;
        }

        RefreshRegionButtonColors();
        ShowStatus($"지역 {regionId}를 선택했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUnlockRegionRpc(string regionId) => ApplyRegionUnlockRpc(regionId);

    [Rpc(SendTo.Everyone)]
    private void ApplyRegionUnlockRpc(string regionId) => ApplyRegionUnlock(regionId);
}
