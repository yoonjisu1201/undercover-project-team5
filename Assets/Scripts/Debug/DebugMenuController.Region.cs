#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
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
    public void OnUnlockRegionFClick() => UnlockRegion("F");

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
        MapRegion region = FindObjectsByType<MapRegion>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.RegionId, regionId, StringComparison.OrdinalIgnoreCase));
        if (region == null)
        {
            ShowStatus($"지역 {regionId}를 찾지 못했습니다.");
            return;
        }

        region.Unlock();
        FindFirstObjectByType<MapRegionController>()?.RefreshSpawnAreas();
        RefreshRegionButtonColors();
        ShowStatus($"지역 {regionId}를 해방했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUnlockRegionRpc(string regionId) => ApplyRegionUnlockRpc(regionId);

    [Rpc(SendTo.Everyone)]
    private void ApplyRegionUnlockRpc(string regionId) => ApplyRegionUnlock(regionId);
}
#endif
