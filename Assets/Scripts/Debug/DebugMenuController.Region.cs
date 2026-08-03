using Unity.Netcode;
using UnityEngine;

// 사용할 필드 지역 선택과 상태 동기화를 처리합니다.
public sealed partial class DebugMenuController
{
    public void OnUnlockRegionAClick() => SelectRegion("A");
    public void OnUnlockRegionBClick() => SelectRegion("B");
    public void OnUnlockRegionCClick() => SelectRegion("C");
    public void OnUnlockRegionDClick() => SelectRegion("D");
    public void OnUnlockRegionEClick() => SelectRegion("E");
    public void OnUnlockRegionFClick() => SelectRegion("F");

    private void SelectRegion(string regionId)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestSelectRegionRpc(regionId);
    }

    private void ApplyRegionSelection(string regionId)
    {
        MapRegionController regionController = FindFirstObjectByType<MapRegionController>();
        if (regionController == null || !regionController.SelectRegion(regionId))
        {
            ShowStatus($"지역 {regionId}를 찾지 못했습니다.");
            return;
        }

        RefreshRegionButtonColors();
        ShowStatus($"지역 {regionId}를 선택했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSelectRegionRpc(string regionId) => ApplyRegionSelectionRpc(regionId);

    [Rpc(SendTo.Everyone)]
    private void ApplyRegionSelectionRpc(string regionId) => ApplyRegionSelection(regionId);
}
