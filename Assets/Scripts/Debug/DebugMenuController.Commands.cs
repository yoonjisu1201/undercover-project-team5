#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 게임 명령, 방해요소, 지역 해방, RPC 요청을 처리합니다.
public sealed partial class DebugMenuController
{
    // 요청한 클라이언트 플레이어의 인벤토리를 서버에서 비웁니다.
    public void OnClearInventoryClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestClearInventoryRpc();
        ShowStatus("인벤토리를 비웠습니다.");
    }

    // 필드 시야를 가리는 안개 방해요소를 요청합니다.
    public void OnFogInterferenceClick() => StartInterference(InterferenceEffectType.FieldVision);
    // 플레이어 화면에 글리치 방해요소를 요청합니다.
    public void OnGlitchInterferenceClick() => StartInterference(InterferenceEffectType.Glitch);

    // 현재 열린 미니게임을 완료하며 CCTV 수리 화면은 전체 연결만 복구하고 닫습니다.
    public void OnCompleteCurrentGameClick()
    {
        MiniGameUIController controller = FindObjectsByType<MiniGameUIController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.gameObject.activeInHierarchy);
        if (controller == null)
        {
            ShowStatus("현재 열린 미니게임 UI가 없습니다.");
            return;
        }

        if (controller.TryGetComponent(out CCTVSignalRepairGame _))
        {
            SetAllCctvConnected();
            controller.CloseWithoutCompletion();
            ShowStatus("CCTV 1~5를 모두 연결 상태로 변경했습니다.");
            return;
        }

        controller.MarkCompletionReady();
        controller.Close();
        ShowStatus("현재 미니게임을 완료 처리했습니다.");
    }

    // 해당 지역을 모든 클라이언트에서 해방합니다.
    public void OnUnlockRegionAClick() => UnlockRegion("A");
    public void OnUnlockRegionBClick() => UnlockRegion("B");
    public void OnUnlockRegionCClick() => UnlockRegion("C");
    public void OnUnlockRegionDClick() => UnlockRegion("D");
    public void OnUnlockRegionEClick() => UnlockRegion("E");
    public void OnUnlockRegionFClick() => UnlockRegion("F");

    // 현재 라운드의 범인 검거 성공을 서버에 보고합니다.
    public void OnArrestCriminalClick()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
        {
            ShowStatus("RoundManager를 찾지 못했습니다.");
            return;
        }

        manager.ReportArrestServerRpc();
        ShowStatus("범인 검거 성공을 보고했습니다.");
    }

    // 진행 중인 라운드를 성공 처리해 다음 라운드 흐름으로 넘깁니다.
    public void OnNextRoundClick()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
        {
            ShowStatus("RoundManager를 찾지 못했습니다.");
            return;
        }

        if (manager.CurrentState != RoundState.InRound)
        {
            ShowStatus($"현재 상태({manager.CurrentState})에서는 다음 라운드로 이동할 수 없습니다.");
            return;
        }

        manager.ReportArrestServerRpc();
        ShowStatus("현재 라운드를 클리어했습니다. 결과 대기 후 다음 라운드로 이동합니다.");
    }

    // 서버에 웨이팅룸 씬 이동을 요청하고 디버그 메뉴를 닫습니다.
    public void OnGoToWaitingRoomClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        SetMenuVisible(false);
        RequestWaitingRoomRpc();
    }

    // 선택한 방해효과를 서버 권한으로 시작합니다.
    private void StartInterference(InterferenceEffectType effectType)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestInterferenceRpc(effectType);
        ShowStatus($"{effectType} 방해요소를 요청했습니다.");
    }

    // 지역 ID를 서버로 전달해 모든 클라이언트가 동일하게 해방하도록 합니다.
    private void UnlockRegion(string regionId)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestUnlockRegionRpc(regionId);
    }

    // 로컬 지역 상태를 해방하고 스폰 가능 지역 정보를 갱신합니다.
    private void ApplyRegionUnlock(string regionId)
    {
        MapRegion region = FindObjectsByType<MapRegion>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => string.Equals(candidate.RegionId, regionId, StringComparison.OrdinalIgnoreCase));
        if (region == null)
        {
            ShowStatus($"지역 {regionId}를 찾지 못했습니다.");
            return;
        }

        region.Unlock();
        FindFirstObjectByType<MapRegionController>()?.RefreshSpawnAreas();
        ShowStatus($"지역 {regionId}를 해방했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // RPC를 보낸 클라이언트의 플레이어를 찾아 서버 인벤토리를 비웁니다.
    private void RequestClearInventoryRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) &&
            client.PlayerObject != null &&
            client.PlayerObject.TryGetComponent(out Player player) &&
            player.PlayerInventory != null)
        {
            player.PlayerInventory.ClearAllItemsOnServer();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 클라이언트가 선택한 방해효과를 서버의 효과 매니저에 요청합니다.
    private void RequestInterferenceRpc(InterferenceEffectType effectType)
    {
        InterferenceEffectManager.Instance?.TryStartEffect(effectType);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 지역 해방 요청을 서버에서 받아 전체 클라이언트 RPC로 전달합니다.
    private void RequestUnlockRegionRpc(string regionId)
    {
        ApplyRegionUnlockRpc(regionId);
    }

    [Rpc(SendTo.Everyone)]
    // 서버를 포함한 모든 클라이언트에 지역 해방 상태를 적용합니다.
    private void ApplyRegionUnlockRpc(string regionId)
    {
        ApplyRegionUnlock(regionId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 네트워크 씬 매니저를 통해 전원을 웨이팅룸으로 이동시킵니다.
    private void RequestWaitingRoomRpc()
    {
        if (NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.LoadScene(WaitingRoomSceneName, LoadSceneMode.Single);
        }
    }
}
#endif
