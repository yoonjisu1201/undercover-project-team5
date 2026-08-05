using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 로컬 플레이어의 디버그 무적 상태(체력 감소 없음) 토글과 체력 즉시 회복을 처리합니다.
public sealed partial class DebugMenuController
{
    [Header("Health")]
    [SerializeField] private Button _invincibleButton;

    // 디버그 메뉴 버튼에서 호출. 서버에 자신의 무적 상태 토글을 요청한다.
    public void OnToggleInvincibleClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestToggleInvincibleRpc();
    }

    // 요청자 본인의 PlayerHealth를 서버에서 직접 찾아 무적 상태를 반전시킨다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleInvincibleRpc(RpcParams rpcParams = default)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient senderClient) ||
            senderClient.PlayerObject == null ||
            !senderClient.PlayerObject.TryGetComponent(out PlayerHealth health))
        {
            return;
        }

        bool newState = !health.IsDebugInvincible;
        health.SetDebugInvincible(newState);

        ApplyInvincibleStateRpc(newState, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    // 요청한 본인에게만 결과를 반영한다 (다른 플레이어의 무적 여부는 남에게 보일 필요 없음).
    [Rpc(SendTo.SpecifiedInParams)]
    private void ApplyInvincibleStateRpc(bool invincible, RpcParams rpcParams = default)
    {
        SetOnOffButtonColor(_invincibleButton, invincible);
        ShowStatus(invincible ? "무적 상태를 켰습니다." : "무적 상태를 껐습니다.");
    }

    // 다른 경로로 무적이 해제된 경우까지 포함해 버튼 색을 현재 상태와 맞춥니다. 무적이면 초록, 아니면 빨강입니다.
    private void RefreshInvincibleButton()
    {
        Player localPlayer = GetLocalPlayer();
        if (localPlayer == null || !localPlayer.TryGetComponent(out PlayerHealth health))
        {
            return;
        }

        SetOnOffButtonColor(_invincibleButton, health.IsDebugInvincible);
    }

    // 디버그 메뉴 버튼에서 호출. 서버에 자신의 체력을 최대치로 채워달라고 요청한다.
    public void OnHealFullClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestHealFullRpc();
    }

    // 요청자 본인의 PlayerHealth를 서버에서 직접 찾아 체력을 최대치로 채운다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestHealFullRpc(RpcParams rpcParams = default)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient senderClient) ||
            senderClient.PlayerObject == null ||
            !senderClient.PlayerObject.TryGetComponent(out PlayerHealth health))
        {
            return;
        }

        health.ResetForNewRound();
        NotifyHealFullRpc(RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    // 요청한 본인에게만 결과를 알린다.
    [Rpc(SendTo.SpecifiedInParams)]
    private void NotifyHealFullRpc(RpcParams rpcParams = default)
    {
        ShowStatus("체력을 회복했습니다.");
    }
}
