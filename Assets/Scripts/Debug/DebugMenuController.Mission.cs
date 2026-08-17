using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// [미션] 메뉴의 현재 미션 완료와 전력 레버 조작을 처리합니다.
public sealed partial class DebugMenuController
{
    [Header("Mission")]
    [SerializeField] private GameObject _missionPanel;
    [SerializeField] private Button _breakerPowerButton;

    public void OnMissionMenuClick()
    {
        ToggleRootSubMenu(_missionPanel);
        RefreshBreakerPowerButton();
    }

    // 지금 열어 두고 보고 있는 미션 패널의 미션을 완료 처리합니다.
    public void OnCompleteCurrentMissionClick()
    {
        MissionInteractable target = MissionInteractable.ActiveInteractable;
        if (target == null)
        {
            ShowStatus("열려 있는 미션 패널이 없습니다. 미션 기기를 먼저 여세요.");
            return;
        }

        if (target.IsCompleted)
        {
            ShowStatus($"'{target.name}'은 이미 완료된 미션입니다.");
            return;
        }

        int index = Array.IndexOf(GetOrderedMissions(), target);
        if (index < 0)
        {
            ShowStatus("현재 미션을 목록에서 찾지 못했습니다.");
            return;
        }

        CompleteMission(index);
        ShowStatus($"'{target.name}' 미션을 완료 처리했습니다.");
    }

    // B 역할의 전력 레버를 대신 올리고 내려, 혼자서도 배터리 미션 흐름을 확인할 수 있게 합니다.
    public void OnToggleBreakerPowerClick()
    {
        BreakerCircuitState circuitState = FindFirstObjectByType<BreakerCircuitState>();
        if (circuitState == null)
        {
            ShowStatus("브레이커 회로를 찾지 못했습니다. 미션 기계가 스폰된 뒤에 사용하세요.");
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

    private void CompleteMission(int index)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestCompleteMissionRpc(index);
    }

    // 서버에서 해당 미션을 완료 처리합니다. 완료 보상 단서 생성까지 기존 흐름을 그대로 씁니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCompleteMissionRpc(int index)
    {
        MissionInteractable[] missions = GetOrderedMissions();
        if (index < 0 || index >= missions.Length)
        {
            Debug.LogWarning($"[DebugMenu] 미션 {index + 1}을 찾지 못했습니다.");
            return;
        }

        MissionInteractable target = missions[index];
        if (target.IsCompleted)
        {
            Debug.LogWarning($"[DebugMenu] 미션 {index + 1}은 이미 완료된 상태입니다.");
            return;
        }

        target.ServerCompleteFromGameplay(target.transform.position + target.transform.forward * 3f);
    }

    // 텔레포트 메뉴와 완료 메뉴의 번호가 어긋나지 않도록 정렬 기준을 한 곳에서 관리합니다.
    private static MissionInteractable[] GetOrderedMissions()
    {
        return FindObjectsByType<MissionInteractable>(FindObjectsSortMode.None)
            .OrderBy(mission => mission.name, StringComparer.Ordinal)
            .ThenBy(mission => mission.GetInstanceID())
            .ToArray();
    }
}
