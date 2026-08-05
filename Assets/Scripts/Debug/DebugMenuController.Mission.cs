using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// [미션 완료] 하위의 미니게임별 완료와 [미션 혼자하기] 하위의 단독 진행 명령을 처리합니다.
public sealed partial class DebugMenuController
{
    [Header("Mission")]
    [SerializeField] private GameObject _missionPanel;
    [SerializeField] private GameObject _missionCompletePanel;
    [SerializeField] private GameObject _missionSoloPanel;
    [SerializeField] private Button _breakerPowerButton;

    // 미션 루트 메뉴를 열 때는 하위 메뉴를 접은 상태에서 시작합니다.
    public void OnMissionMenuClick()
    {
        ToggleRootSubMenu(_missionPanel);
        _missionCompletePanel?.SetActive(false);
        _missionSoloPanel?.SetActive(false);
    }

    public void OnMissionCompleteMenuClick() => ToggleNestedSubMenu(_missionCompletePanel, _missionSoloPanel);

    public void OnMissionSoloMenuClick()
    {
        ToggleNestedSubMenu(_missionSoloPanel, _missionCompletePanel);
        RefreshBreakerPowerButton();
    }

    // 텔레포트 메뉴의 미니게임 번호와 같은 순서로 완료 처리합니다.
    public void OnCompleteMiniGame1Click() => CompleteMiniGame(0);
    public void OnCompleteMiniGame2Click() => CompleteMiniGame(1);
    public void OnCompleteMiniGame3Click() => CompleteMiniGame(2);
    public void OnCompleteMiniGame4Click() => CompleteMiniGame(3);
    public void OnCompleteMiniGame5Click() => CompleteMiniGame(4);
    public void OnCompleteMiniGame6Click() => CompleteMiniGame(5);
    public void OnCompleteMiniGame7Click() => CompleteMiniGame(6);
    public void OnCompleteMiniGame8Click() => CompleteMiniGame(7);

    // B 역할의 전력 레버를 대신 올리고 내려, 혼자서도 배터리 미니게임 흐름을 확인할 수 있게 합니다.
    public void OnToggleBreakerPowerClick()
    {
        BreakerCircuitState circuitState = FindFirstObjectByType<BreakerCircuitState>();
        if (circuitState == null)
        {
            ShowStatus("브레이커 회로를 찾지 못했습니다. 미니게임 기계가 스폰된 뒤에 사용하세요.");
            return;
        }

        bool nextPowerOn = !circuitState.PowerOn;
        circuitState.SetPower(nextPowerOn);
        ShowStatus(nextPowerOn ? "전력 레버를 올렸습니다. (전원 ON)" : "전력 레버를 내렸습니다. (전원 OFF)");
    }

    // 다른 사람이 실제 레버를 조작한 경우까지 포함해 버튼 색을 현재 전원 상태와 맞춥니다.
    private void RefreshBreakerPowerButton()
    {
        if (_breakerPowerButton == null)
        {
            return;
        }

        BreakerCircuitState circuitState = FindFirstObjectByType<BreakerCircuitState>();
        SetOnOffButtonColor(_breakerPowerButton, circuitState != null && circuitState.PowerOn);
    }

    private void CompleteMiniGame(int index)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestCompleteMiniGameRpc(index);
    }

    // 서버에서 해당 미니게임을 완료 처리합니다. 완료 보상 단서 생성까지 기존 흐름을 그대로 씁니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCompleteMiniGameRpc(int index)
    {
        MiniGameInteractable[] miniGames = GetOrderedMiniGames();
        if (index < 0 || index >= miniGames.Length)
        {
            Debug.LogWarning($"[DebugMenu] 미니게임 {index + 1}을 찾지 못했습니다.");
            return;
        }

        MiniGameInteractable target = miniGames[index];
        if (target.IsCompleted)
        {
            Debug.LogWarning($"[DebugMenu] 미니게임 {index + 1}은 이미 완료된 상태입니다.");
            return;
        }

        target.ServerCompleteFromGameplay(target.transform.position + target.transform.forward * 3f);
    }

    // 텔레포트 메뉴와 완료 메뉴의 번호가 어긋나지 않도록 정렬 기준을 한 곳에서 관리합니다.
    private static MiniGameInteractable[] GetOrderedMiniGames()
    {
        return FindObjectsByType<MiniGameInteractable>(FindObjectsSortMode.None)
            .OrderBy(miniGame => miniGame.name, StringComparer.Ordinal)
            .ThenBy(miniGame => miniGame.GetInstanceID())
            .ToArray();
    }
}
