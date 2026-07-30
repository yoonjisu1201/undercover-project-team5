#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 미니게임, 방해요소, 라운드와 세션 이동 명령을 처리합니다.
public sealed partial class DebugMenuController
{
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

    // 라운드 제한시간만 정지하거나 다시 흐르게 합니다.
    public void OnToggleRoundTimeStopClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestToggleRoundTimeStopRpc();
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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 클라이언트가 선택한 방해효과를 서버의 효과 매니저에 요청합니다.
    private void RequestInterferenceRpc(InterferenceEffectType effectType)
    {
        InterferenceEffectManager.Instance?.TryStartEffect(effectType);
    }

    // 서버의 라운드 시간 정지 상태를 토글하고 모든 디버그 메뉴에 버튼 상태를 반영합니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleRoundTimeStopRpc()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null || !manager.SetDebugTimeStopped(!manager.IsDebugTimeStopped))
        {
            Debug.LogWarning("[DebugMenu] 현재 상태에서는 라운드 시간을 정지할 수 없습니다.");
            return;
        }

        ApplyRoundTimeStopStateRpc(manager.IsDebugTimeStopped);
    }

    [Rpc(SendTo.Everyone)]
    private void ApplyRoundTimeStopStateRpc(bool stopped)
    {
        SetToggleButtonState(_timeStopButton, stopped);
        ShowStatus(stopped ? "라운드 시간을 정지했습니다." : "라운드 시간을 다시 시작했습니다.");
    }

    // 서버에서 자동 해제된 경우까지 포함해 Time Stop 버튼 색상을 현재 상태와 맞춥니다.
    private void RefreshRoundTimeStopButton()
    {
        SetToggleButtonState(
            _timeStopButton,
            RoundManager.Instance != null && RoundManager.Instance.IsDebugTimeStopped);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 플레이어 역할을 초기화한 뒤 전원을 웨이팅룸으로 이동시킵니다.
    private void RequestWaitingRoomRpc()
    {
        ResetPlayerRolesForWaitingRoom();
        NetworkManager.SceneManager?.LoadScene(WaitingRoomSceneName, LoadSceneMode.Single);
    }

    // 씬 전환 후에도 유지되는 Player 오브젝트에서 이전 게임의 역할 정보를 제거합니다.
    private void ResetPlayerRolesForWaitingRoom()
    {
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject != null &&
                client.PlayerObject.TryGetComponent(out Player player))
            {
                player.PlayerRole = Role.Field;
            }
        }
    }
}
#endif
