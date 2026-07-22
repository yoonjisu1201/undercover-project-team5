using Unity.Netcode;
using UnityEngine;

public class ArrestCandidateInteractable : InteractableBase
{
    public override string InteractionText => "검거 후보로 지정?";

    // 이미 다른 대상으로 투표가 진행 중이면 새 후보를 지정할 수 없다.
    public override bool CanInteract => ArrestVoteManager.Instance != null
        && ArrestVoteManager.Instance.CurrentVoteState == ArrestVoteState.Idle;

    // NPC는 계속 움직이므로 조준 판정 반경을 넉넉하게 잡는다.
    [SerializeField, Min(1f)] private float _aimRadiusMultiplier = 3f;
    public override float AimRadiusMultiplier => _aimRadiusMultiplier;

    // 서버 재검사 시 네트워크 지연으로 인한 위치 오차를 흡수하기 위한 여유 거리.
    [SerializeField, Min(0f)] private float _rangeTolerance = 0.5f;

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract || !IsSpawned)
        {
            return;
        }

        // 확인 패널을 여는 동안 NPC가 멀어져서 재검사에 실패하지 않도록, 상호작용 시점에 바로 멈춰둔다.
        RequestPauseForConfirmationRpc();
        FindFirstObjectByType<ArrestVoteUI>()?.RequestOpenStartVotePanel(this);
    }

    // 확인 패널에서 [아니요]를 누르거나 패널을 닫았을 때 ArrestVoteUI가 호출하는 진입점.
    public void CancelPendingConfirmation()
    {
        RequestResumeAfterCancelRpc();
    }

    // 확인 패널에서 [네]를 눌렀을 때 ArrestVoteUI가 호출하는 진입점.
    public void ConfirmArrestCandidate()
    {
        RequestSelectArrestCandidateRpc();
    }

    //--- 서버에서 검거 후보 지정 요청을 재검증하는 Rpc 관련 코드 ---//
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPauseForConfirmationRpc(RpcParams rpcParams = default)
    {
        // 확인 패널이 실제로 열릴 수 없는 상황(투표 진행 중, 횟수 소진)이면 멈추지도 않는다.
        if (!IsSpawned || !CanInteract || ArrestVoteManager.Instance.RemainingVoteAttempts <= 0)
        {
            return;
        }

        if (!TryGetInteractionCollider(rpcParams.Receive.SenderClientId, out SphereCollider interactionCollider))
        {
            return;
        }

        if (!IsWithinInteractionRange(interactionCollider))
        {
            return;
        }

        GetComponent<NpcMovement>()?.Pause();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestResumeAfterCancelRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned)
        {
            return;
        }

        GetComponent<NpcMovement>()?.Resume();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSelectArrestCandidateRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned)
        {
            return;
        }

        if (!TryGetInteractionCollider(rpcParams.Receive.SenderClientId, out SphereCollider interactionCollider))
        {
            return;
        }

        if (!IsWithinInteractionRange(interactionCollider))
        {
            return;
        }

        // 후보 지정이 성공했을 때만 같은 흐름에서 바로 투표를 시작해서, 후보 미지정 상태로 투표가 시작되는 경쟁 상태를 막는다.
        // 투표 시작패널에서 [네] 클릭후 바로 부결처리가 되는 원인
        if (ArrestVoteManager.Instance != null && ArrestVoteManager.Instance.TrySetArrestCandidate(NetworkObject))
        {
            ArrestVoteManager.Instance.RequestStartVoteServerRpc();
        }
    }

    private bool TryGetInteractionCollider(ulong senderClientId, out SphereCollider interactionCollider)
    {
        interactionCollider = null;

        if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
        {
            return false;
        }

        NetworkObject playerObject = senderClient.PlayerObject;

        if (playerObject == null)
        {
            return false;
        }

        interactionCollider = playerObject.GetComponent<SphereCollider>();
        return interactionCollider != null;
    }

    // 정확한 콜라이더 겹침 대신 거리 + 여유값으로 판정해, 네트워크 지연으로 인한 근소한 위치 차이를 흡수한다.
    private bool IsWithinInteractionRange(SphereCollider interactionCollider)
    {
        float maxDistance = interactionCollider.radius + _rangeTolerance;
        float distance = Vector3.Distance(transform.position, interactionCollider.transform.position);
        return distance <= maxDistance;
    }
}
